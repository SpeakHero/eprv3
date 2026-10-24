/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-23 04:40:00 +08:00 中国标准时间 星期五
 * 描述：JobExecutionRunner 测试——ASYNC 成功终态+通知、失败重试退避、重试额度耗尽、
 *       超时终止（含 RetryOnTimeout 重试一次）、终态幂等跳过、连续失败熔断
 */

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Qlx.Abstractions.Exceptions;
using Qlx.Abstractions.Jobs;
using Qlx.Abstractions.Messages;
using Qlx.BuildingBlocks.Jobs;
using Qlx.BuildingBlocks.Tests.Files;
using System.Text.Json;

namespace Qlx.BuildingBlocks.Tests.Jobs;

/// <summary>
/// <see cref="JobExecutionRunner"/> ASYNC 执行链路单元测试（内存存储 + 假入队器 + 记录型消息服务）。
/// </summary>
public sealed class JobExecutionRunnerTests
{
    private const long TenantId = 42L;
    private const string TaskCode = "test.runner.job";

    /// <summary>运行器测试夹具（持有被测对象与全部捕获替身）。</summary>
    private sealed record RunnerFixture(
        JobExecutionRunner Runner,
        InMemoryJobExecutionStore Store,
        FakeEnqueuer Enqueuer,
        RecordingMessageService Messages,
        ServiceProvider Provider) : IDisposable
    {
        public void Dispose() => Provider.Dispose();
    }

    private static RunnerFixture BuildRunner(QlxJobsOptions? options = null, params IJobHandler[] handlers)
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
            Options.Create(options ?? new QlxJobsOptions()),
            NullLogger<JobExecutionRunner>.Instance);
        return new RunnerFixture(runner, store, enqueuer, messages, provider);
    }

    /// <summary>登记一个 QUEUED 态执行（模拟调度器提交后的落库状态）。</summary>
    private static async Task<Guid> SeedQueuedExecutionAsync(
        InMemoryJobExecutionStore store,
        string taskCode,
        int retryCount = 0,
        int timeoutSeconds = 300,
        string? parametersJson = null)
    {
        var executionId = Guid.NewGuid();
        await store.CreateAsync(
            new JobExecutionCreation(
                executionId, TenantId, taskCode, ExecutionMode.Async, JobExecutionStatus.Pending,
                retryCount, JobSubmitMetadata.TriggerManual, null, null, parametersJson, timeoutSeconds, null),
            TestContext.Current.CancellationToken);
        await store.TryTransitionAsync(
            TenantId, executionId, JobExecutionStatus.Pending, JobExecutionStatus.Queued,
            DateTimeOffset.UtcNow, TestContext.Current.CancellationToken);
        return executionId;
    }

    [Fact]
    public async Task ExecuteAsyncSuccessMarksTerminalAndPushesCompletedEvent()
    {
        using var fixture = BuildRunner(new QlxJobsOptions(), ScriptedJobHandler.Success(TaskCode));
        var executionId = await SeedQueuedExecutionAsync(fixture.Store, TaskCode);

        await fixture.Runner.ExecuteAsync(executionId, TenantId, TaskCode, CancellationToken.None);

        var execution = fixture.Store.Executions[executionId];
        Assert.Equal(JobExecutionStatus.Success, execution.Status);
        Assert.NotNull(execution.StartedAt);
        Assert.NotNull(execution.FinishedAt);
        Assert.Contains(fixture.Messages.Pushes, p => p.Subject == "TASK_COMPLETED");
        Assert.Contains(fixture.Store.Logs, l => l.ExecutionId == executionId && l.Level == JobLogLevel.Info);
        Assert.Empty(fixture.Enqueuer.Calls);
    }

    [Fact]
    public async Task ExecuteAsyncFailureWithinBudgetSchedulesRetryWithBackoffDelay()
    {
        using var fixture = BuildRunner(
            new QlxJobsOptions(),
            ScriptedJobHandler.Throwing(TaskCode, new ConflictException("boom")));
        var executionId = await SeedQueuedExecutionAsync(fixture.Store, TaskCode);

        await fixture.Runner.ExecuteAsync(executionId, TenantId, TaskCode, CancellationToken.None);

        // 原执行落 FAILED 并持久化错误信息
        var original = fixture.Store.Executions[executionId];
        Assert.Equal(JobExecutionStatus.Failed, original.Status);
        Assert.Equal("boom", original.Error);

        // 默认策略（Exponential / Initial=60s）：新建 1 次重试执行并按 60s 延迟入队
        var retry = Assert.Single(fixture.Store.Executions.Values, e => e.ExecutionId != executionId);
        Assert.Equal(1, retry.RetryCount);
        Assert.Equal(JobExecutionStatus.Queued, retry.Status);
        var call = Assert.Single(fixture.Enqueuer.Calls);
        Assert.Equal(retry.ExecutionId, call.ExecutionId);
        Assert.Equal(TimeSpan.FromSeconds(60), call.Delay);
        Assert.Contains(fixture.Messages.Pushes, p => p.Subject == "TASK_RETRYING");
    }

    [Fact]
    public async Task ExecuteAsyncRetryBudgetExhaustedPushesTerminalFailureWithoutRetry()
    {
        using var fixture = BuildRunner(
            new QlxJobsOptions(),
            ScriptedJobHandler.Throwing(TaskCode, new ConflictException("boom")));
        var executionId = await SeedQueuedExecutionAsync(fixture.Store, TaskCode, retryCount: 3);

        await fixture.Runner.ExecuteAsync(executionId, TenantId, TaskCode, CancellationToken.None);

        Assert.Equal(JobExecutionStatus.Failed, fixture.Store.Executions[executionId].Status);
        Assert.Single(fixture.Store.Executions);
        Assert.Empty(fixture.Enqueuer.Calls);
        Assert.Contains(fixture.Messages.Pushes, p => p.Subject == "TASK_FAILED");
    }

    [Fact]
    public async Task ExecuteAsyncTimeoutWithRetryDisabledMarksTimeoutWithoutRequeue()
    {
        using var fixture = BuildRunner(
            new QlxJobsOptions { RetryOnTimeout = false },
            ScriptedJobHandler.BlockingUntilCanceled(TaskCode));
        var executionId = await SeedQueuedExecutionAsync(fixture.Store, TaskCode, timeoutSeconds: 1);

        await fixture.Runner.ExecuteAsync(executionId, TenantId, TaskCode, CancellationToken.None);

        var execution = fixture.Store.Executions[executionId];
        Assert.Equal(JobExecutionStatus.Timeout, execution.Status);
        Assert.Contains(fixture.Messages.Pushes, p => p.Subject == "TASK_TIMEOUT");
        Assert.Empty(fixture.Enqueuer.Calls);
    }

    [Fact]
    public async Task ExecuteAsyncTimeoutWithRetryEnabledRequeuesOnceWithSameParameters()
    {
        using var fixture = BuildRunner(
            new QlxJobsOptions { RetryOnTimeout = true },
            ScriptedJobHandler.BlockingUntilCanceled(TaskCode));
        var executionId = await SeedQueuedExecutionAsync(
            fixture.Store, TaskCode, timeoutSeconds: 1, parametersJson: "{\"batch\":1}");

        await fixture.Runner.ExecuteAsync(executionId, TenantId, TaskCode, CancellationToken.None);

        // 原执行落 TIMEOUT；重试执行继承参数并立即重新入队（RetryOnTimeout 仅一次）
        Assert.Equal(JobExecutionStatus.Timeout, fixture.Store.Executions[executionId].Status);
        var retry = Assert.Single(fixture.Store.Executions.Values, e => e.ExecutionId != executionId);
        Assert.Equal(1, retry.RetryCount);
        Assert.Equal(JobExecutionStatus.Queued, retry.Status);
        Assert.Equal("{\"batch\":1}", retry.ParametersJson);
        var call = Assert.Single(fixture.Enqueuer.Calls);
        Assert.Null(call.Delay);
    }

    [Fact]
    public async Task ExecuteAsyncTerminalExecutionSkipsIdempotently()
    {
        // 已终态（用户取消后 Hangfire 仍可能投递）：状态机守卫拒绝复活执行
        using var fixture = BuildRunner(
            new QlxJobsOptions(),
            ScriptedJobHandler.Success(TaskCode));
        var executionId = Guid.NewGuid();
        fixture.Store.SeedExecution(new JobExecutionSnapshot(
            executionId, TenantId, TaskCode, ExecutionMode.Async, JobExecutionStatus.Canceled,
            0, null, null, null, null, null, 300, null, "job-1",
            DateTimeOffset.UtcNow, null, DateTimeOffset.UtcNow));

        await fixture.Runner.ExecuteAsync(executionId, TenantId, TaskCode, CancellationToken.None);

        var execution = fixture.Store.Executions[executionId];
        Assert.Equal(JobExecutionStatus.Canceled, execution.Status);
        Assert.Empty(fixture.Messages.Pushes);
        Assert.DoesNotContain(fixture.Store.Logs, l => l.ExecutionId == executionId);
    }

    [Fact]
    public async Task ExecuteDirectAsyncUnwrapsPersistedJsonParametersAndAppliesRuntimeOverrides()
    {
        JobContext? captured = null;
        var handler = new ScriptedJobHandler(TaskCode, context =>
        {
            captured = context;
            return Task.FromResult(new CompletedResult(null, null, null, 0, IsSuccess: true));
        });
        using var fixture = BuildRunner(new QlxJobsOptions(), handler);

        const string parametersJson = """{"text":"hello","count":42,"ratio":1.5,"flag":true,"missing":null,"nested":{"a":1}}""";
        var executionId = Guid.NewGuid();
        await fixture.Store.CreateAsync(
            new JobExecutionCreation(
                executionId, TenantId, TaskCode, ExecutionMode.Sync, JobExecutionStatus.Pending,
                0, JobSubmitMetadata.TriggerManual, null, null, parametersJson, 300, null),
            TestContext.Current.CancellationToken);
        var snapshot = await fixture.Store.GetAsync(TenantId, executionId, TestContext.Current.CancellationToken);

        await fixture.Runner.ExecuteDirectAsync(
            snapshot!, handler, TestContext.Current.CancellationToken,
            new Dictionary<string, object?> { ["count"] = 99L, ["extra"] = "runtime" });

        Assert.NotNull(captured);
        Assert.Equal("hello", Assert.IsType<string>(captured.Parameters["text"]));
        // 整数 JSON 数值必须还原为 long（处理器按 is long 读取），且运行期参数优先于持久化参数
        Assert.Equal(99L, Assert.IsType<long>(captured.Parameters["count"]));
        Assert.Equal(1.5, Assert.IsType<double>(captured.Parameters["ratio"]));
        Assert.True(Assert.IsType<bool>(captured.Parameters["flag"]));
        Assert.Null(captured.Parameters["missing"]);
        // Object / Array 保持 JsonElement 原样，由处理器自行反序列化
        Assert.Equal(JsonValueKind.Object, Assert.IsType<JsonElement>(captured.Parameters["nested"]).ValueKind);
        Assert.Equal("runtime", Assert.IsType<string>(captured.Parameters["extra"]));
    }

    [Fact]
    public async Task ExecuteAsyncConsecutiveFailuresTripsCircuitBreakerAndPushesSystemAlert()
    {
        using var fixture = BuildRunner(
            new QlxJobsOptions { FailureThreshold = 2 },
            ScriptedJobHandler.Throwing(TaskCode, new ConflictException("boom")));
        fixture.Store.SeedDefinition(new JobDefinitionSnapshot(
            TaskCode, TenantId, "测试任务", "sup", ExecutionMode.Async, "0 0 3 * * *",
            Enabled: true, Paused: false, 300, new RetryPolicy()));

        // 第 1 次失败：连续失败 1 次 < 阈值，不熔断（RetryCount 已达上限避免重试干扰统计）
        var first = await SeedQueuedExecutionAsync(fixture.Store, TaskCode, retryCount: 3);
        await fixture.Runner.ExecuteAsync(first, TenantId, TaskCode, CancellationToken.None);
        Assert.False(fixture.Store.Definitions[(TenantId, TaskCode)].Paused);
        Assert.DoesNotContain(fixture.Messages.Pushes, p => p.Subject == "SYSTEM_ALERT");

        // 第 2 次失败：连续失败 2 次 ≥ 阈值 → 暂停定义 + SYSTEM_ALERT（Urgent）
        var second = await SeedQueuedExecutionAsync(fixture.Store, TaskCode, retryCount: 3);
        await fixture.Runner.ExecuteAsync(second, TenantId, TaskCode, CancellationToken.None);

        Assert.True(fixture.Store.Definitions[(TenantId, TaskCode)].Paused);
        var alert = Assert.Single(fixture.Messages.Pushes, p => p.Subject == "SYSTEM_ALERT");
        Assert.Equal(MessagePriority.Urgent, alert.Priority);
    }
}
