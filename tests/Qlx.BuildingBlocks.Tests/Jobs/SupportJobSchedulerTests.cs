/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-23 04:40:00 +08:00 中国标准时间 星期五
 * 描述：SupportJobScheduler 测试（方案 D20 统一提交五步）——404 / 停用 / 熔断 / 幂等防重 /
 *       SYNC 成功与业务异常透传 / ASYNC 入队+作业 Id 落库 / 取消语义 / 租户上下文守卫
 */

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Qlx.Abstractions.Exceptions;
using Qlx.Abstractions.Jobs;
using Qlx.BuildingBlocks.Ids;
using Qlx.BuildingBlocks.Jobs;
using Qlx.BuildingBlocks.Tests.Files;
using Qlx.Modules.Support.Options;
using Qlx.Modules.Support.Services.Jobs;

namespace Qlx.BuildingBlocks.Tests.Jobs;

/// <summary>
/// <see cref="SupportJobScheduler"/> 单元测试。真实调度器 + 真实 JobHandlerFactory / JobExecutionRunner，
/// 仅存储 / 入队 / 消息链路使用内存替身（真实 Store 依赖 SqlServer 提供程序，SQLite 不可挂载）。
/// </summary>
public sealed class SupportJobSchedulerTests
{
    private const long TenantId = 42L;
    private const string TaskCode = "test.scheduler.job";

    /// <summary>调度器测试夹具。</summary>
    private sealed record SchedulerFixture(
        SupportJobScheduler Scheduler,
        InMemoryJobExecutionStore Store,
        FakeEnqueuer Enqueuer,
        RecordingMessageService Messages,
        ServiceProvider Provider) : IDisposable
    {
        public void Dispose() => Provider.Dispose();
    }

    private static SchedulerFixture BuildScheduler(params IJobHandler[] handlers)
    {
        var services = new ServiceCollection();
        foreach (var handler in handlers)
        {
            services.AddSingleton(handler);
        }

        var provider = services.BuildServiceProvider();
        var store = new InMemoryJobExecutionStore();
        var enqueuer = new FakeEnqueuer();
        var messages = new RecordingMessageService();
        var runner = new JobExecutionRunner(
            provider, store, enqueuer, messages,
            Options.Create(new QlxJobsOptions()),
            NullLogger<JobExecutionRunner>.Instance);
        var scheduler = new SupportJobScheduler(
            new JobHandlerFactory(provider),
            store,
            enqueuer,
            runner,
            new TestTenantContext(TenantId),
            Options.Create(new SupportJobsOptions()),
            NullLogger<SupportJobScheduler>.Instance);
        return new SchedulerFixture(scheduler, store, enqueuer, messages, provider);
    }

    private static void SeedDefinition(
        InMemoryJobExecutionStore store,
        bool enabled = true,
        bool paused = false,
        ExecutionMode defaultMode = ExecutionMode.Sync)
        => store.SeedDefinition(new JobDefinitionSnapshot(
            TaskCode, TenantId, "测试任务", "sup", defaultMode, "0 0 3 * * *",
            enabled, paused, 300, new RetryPolicy()));

    private static TaskSubmitRequest BuildRequest(
        ExecutionMode mode = ExecutionMode.Sync,
        string? idempotencyKey = null,
        IReadOnlyDictionary<string, object?>? parameters = null)
        => new(TaskCode, GuidIdMapper.ToGuid(TenantId), mode, InitiatorUserId: "user-1",
            Parameters: parameters, IdempotencyKey: idempotencyKey);

    [Fact]
    public async Task SubmitAsyncUnknownHandlerThrowsNotFound()
    {
        using var fixture = BuildScheduler();

        await Assert.ThrowsAsync<NotFoundException>(
            () => fixture.Scheduler.SubmitAsync(BuildRequest(), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task SubmitAsyncMissingDefinitionThrowsNotFound()
    {
        using var fixture = BuildScheduler(ScriptedJobHandler.Success(TaskCode));

        await Assert.ThrowsAsync<NotFoundException>(
            () => fixture.Scheduler.SubmitAsync(BuildRequest(), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task SubmitAsyncDisabledDefinitionThrowsForbidden()
    {
        using var fixture = BuildScheduler(ScriptedJobHandler.Success(TaskCode));
        SeedDefinition(fixture.Store, enabled: false);

        await Assert.ThrowsAsync<ForbiddenException>(
            () => fixture.Scheduler.SubmitAsync(BuildRequest(), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task SubmitAsyncPausedDefinitionThrowsConflictWithJobPausedCode()
    {
        using var fixture = BuildScheduler(ScriptedJobHandler.Success(TaskCode));
        SeedDefinition(fixture.Store, paused: true);

        var ex = await Assert.ThrowsAsync<ConflictException>(
            () => fixture.Scheduler.SubmitAsync(BuildRequest(), TestContext.Current.CancellationToken));

        Assert.Equal("JOB_PAUSED", ex.ErrorCode);
    }

    [Fact]
    public async Task SubmitAsyncIdempotencyKeyWithinWindowReturnsExistingExecution()
    {
        using var fixture = BuildScheduler(ScriptedJobHandler.Success(TaskCode));
        SeedDefinition(fixture.Store);
        var existingId = Guid.NewGuid();
        await fixture.Store.CreateAsync(
            new JobExecutionCreation(
                existingId, TenantId, TaskCode, ExecutionMode.Async, JobExecutionStatus.Pending,
                0, JobSubmitMetadata.TriggerManual, null, "case-1", null, 300, null),
            TestContext.Current.CancellationToken);

        var response = await fixture.Scheduler.SubmitAsync(
            BuildRequest(mode: ExecutionMode.Async, idempotencyKey: "case-1"),
            TestContext.Current.CancellationToken);

        Assert.Equal(existingId, response.ExecutionId);
        Assert.Single(fixture.Store.Executions);
        Assert.Empty(fixture.Enqueuer.Calls);
    }

    [Fact]
    public async Task SubmitAsyncSyncSuccessReturnsCompletedResult()
    {
        using var fixture = BuildScheduler(
            ScriptedJobHandler.Success(TaskCode, payload: "raw-result"));
        SeedDefinition(fixture.Store);

        var response = await fixture.Scheduler.SubmitAsync(
            BuildRequest(), TestContext.Current.CancellationToken);

        Assert.Equal(ExecutionMode.Sync, response.Mode);
        Assert.Equal(JobExecutionStatus.Success, response.InitialStatus);
        Assert.NotNull(response.CompletedResult);
        var completed = response.CompletedResult!;
        Assert.True(completed.IsSuccess);
        Assert.Equal("raw-result", completed.ResultJson);
        var execution = Assert.Single(fixture.Store.Executions.Values);
        Assert.Equal(JobExecutionStatus.Success, execution.Status);
        Assert.NotNull(execution.StartedAt);
        Assert.NotNull(execution.FinishedAt);
    }

    [Fact]
    public async Task SubmitAsyncSyncBusinessExceptionPropagatesAndPersistsFailure()
    {
        using var fixture = BuildScheduler(
            ScriptedJobHandler.Throwing(TaskCode, new ConflictException("上游校验失败")));
        SeedDefinition(fixture.Store);

        await Assert.ThrowsAsync<ConflictException>(
            () => fixture.Scheduler.SubmitAsync(BuildRequest(), TestContext.Current.CancellationToken));

        var execution = Assert.Single(fixture.Store.Executions.Values);
        Assert.Equal(JobExecutionStatus.Failed, execution.Status);
        Assert.Equal("上游校验失败", execution.Error);
        Assert.Contains(fixture.Store.Logs, l => l.Level == JobLogLevel.Error && l.ExecutionId == execution.ExecutionId);
    }

    [Fact]
    public async Task SubmitAsyncSyncUnexpectedExceptionReturnsFailedResponseWithoutThrowing()
    {
        using var fixture = BuildScheduler(
            ScriptedJobHandler.Throwing(TaskCode, new InvalidOperationException("boom")));
        SeedDefinition(fixture.Store);

        var response = await fixture.Scheduler.SubmitAsync(
            BuildRequest(), TestContext.Current.CancellationToken);

        Assert.Equal(JobExecutionStatus.Failed, response.InitialStatus);
        Assert.False(response.CompletedResult!.IsSuccess);
        Assert.Contains("boom", response.CompletedResult.ErrorMessage);
    }

    [Fact]
    public async Task SubmitAsyncAsyncReturnsQueuedEnqueuesAndSavesHangfireJobId()
    {
        using var fixture = BuildScheduler(ScriptedJobHandler.Success(TaskCode));
        SeedDefinition(fixture.Store, defaultMode: ExecutionMode.Async);

        var response = await fixture.Scheduler.SubmitAsync(
            BuildRequest(
                mode: ExecutionMode.Async,
                parameters: new Dictionary<string, object?> { [JobSubmitMetadata.TriggerSourceParameterKey] = JobSubmitMetadata.TriggerSchedule }),
            TestContext.Current.CancellationToken);

        Assert.Equal(ExecutionMode.Async, response.Mode);
        Assert.Equal(JobExecutionStatus.Queued, response.InitialStatus);
        Assert.Null(response.CompletedResult);
        var call = Assert.Single(fixture.Enqueuer.Calls);
        Assert.Equal(response.ExecutionId, call.ExecutionId);
        Assert.Equal(TenantId, call.TenantId);
        var execution = fixture.Store.Executions[response.ExecutionId];
        Assert.Equal(JobExecutionStatus.Queued, execution.Status);
        Assert.Equal("job-1", execution.HangfireJobId);
        // 触发来源保留键随参数一并落库（供执行历史溯源）
        Assert.Contains(JobSubmitMetadata.TriggerSourceParameterKey, execution.ParametersJson);
    }

    [Fact]
    public async Task CancelAsyncTerminalExecutionThrowsJobTerminal()
    {
        using var fixture = BuildScheduler();
        var executionId = Guid.NewGuid();
        fixture.Store.SeedExecution(new JobExecutionSnapshot(
            executionId, TenantId, TaskCode, ExecutionMode.Sync, JobExecutionStatus.Success,
            0, null, null, null, null, null, 300, null, null,
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));

        var ex = await Assert.ThrowsAsync<ConflictException>(
            () => fixture.Scheduler.CancelAsync(executionId, TestContext.Current.CancellationToken));

        Assert.Equal("JOB_TERMINAL", ex.ErrorCode);
    }

    [Fact]
    public async Task CancelAsyncQueuedExecutionDeletesJobAndMarksCanceled()
    {
        using var fixture = BuildScheduler();
        var executionId = Guid.NewGuid();
        await fixture.Store.CreateAsync(
            new JobExecutionCreation(
                executionId, TenantId, TaskCode, ExecutionMode.Async, JobExecutionStatus.Pending,
                0, JobSubmitMetadata.TriggerManual, null, null, null, 300, null),
            TestContext.Current.CancellationToken);
        await fixture.Store.TryTransitionAsync(
            TenantId, executionId, JobExecutionStatus.Pending, JobExecutionStatus.Queued,
            DateTimeOffset.UtcNow, TestContext.Current.CancellationToken);
        await fixture.Store.SaveHangfireJobIdAsync(
            TenantId, executionId, "job-9", TestContext.Current.CancellationToken);

        await fixture.Scheduler.CancelAsync(executionId, TestContext.Current.CancellationToken);

        Assert.Equal(JobExecutionStatus.Canceled, fixture.Store.Executions[executionId].Status);
        Assert.Contains("job-9", fixture.Enqueuer.DeletedJobIds);
    }

    [Fact]
    public async Task CancelAsyncWithoutTenantContextThrowsUnauthorized()
    {
        using var provider = new ServiceCollection().BuildServiceProvider();
        var scheduler = new SupportJobScheduler(
            new JobHandlerFactory(provider),
            new InMemoryJobExecutionStore(),
            new FakeEnqueuer(),
            new JobExecutionRunner(
                provider, new InMemoryJobExecutionStore(), new FakeEnqueuer(), new RecordingMessageService(),
                Options.Create(new QlxJobsOptions()), NullLogger<JobExecutionRunner>.Instance),
            new TestTenantContext(null),
            Options.Create(new SupportJobsOptions()),
            NullLogger<SupportJobScheduler>.Instance);

        await Assert.ThrowsAsync<UnauthorizedException>(
            () => scheduler.CancelAsync(Guid.NewGuid(), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GetStatusAsyncUnknownExecutionReturnsNull()
    {
        using var fixture = BuildScheduler();

        var status = await fixture.Scheduler.GetStatusAsync(Guid.NewGuid(), TestContext.Current.CancellationToken);

        Assert.Null(status);
    }
}
