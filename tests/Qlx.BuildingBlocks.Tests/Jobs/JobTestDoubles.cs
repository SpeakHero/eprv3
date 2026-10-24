/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-23 04:40:00 +08:00 中国标准时间 星期五
 * 描述：后台任务中心共享测试替身——内存执行存储 / 假入队器 / 脚本化处理器 / 记录型消息服务，
 *       供 JobExecutionRunnerTests 与 SupportJobSchedulerTests 复用（同程序集 internal 可见）
 */

using Qlx.Abstractions.Jobs;
using Qlx.Abstractions.Messages;
using Qlx.BuildingBlocks.Jobs;
using Qlx.BuildingBlocks.MultiTenancy;

namespace Qlx.BuildingBlocks.Tests.Jobs;

/// <summary>
/// 内存版 <see cref="IJobExecutionStore"/>：行为与 SupportJobExecutionStore 对齐
/// （状态转移按 from 匹配 + 状态机守卫、幂等键窗口、连续失败统计、定义熔断暂停）。
/// </summary>
internal sealed class InMemoryJobExecutionStore : IJobExecutionStore
{
    private readonly object _gate = new();
    private readonly Dictionary<Guid, JobExecutionSnapshot> _executions = [];
    private readonly List<JobLogDraft> _logs = [];
    private readonly Dictionary<(long TenantId, string TaskCode), JobDefinitionSnapshot> _definitions = [];
    private readonly Dictionary<long, ((long TenantId, string TaskCode) Key, DateTimeOffset? NextRunAt)> _definitionRows = [];
    private long _definitionIdSequence;

    /// <summary>执行快照只读视图（断言用）。</summary>
    public IReadOnlyDictionary<Guid, JobExecutionSnapshot> Executions
    {
        get
        {
            lock (_gate)
            {
                return new Dictionary<Guid, JobExecutionSnapshot>(_executions);
            }
        }
    }

    /// <summary>日志只读视图（断言用）。</summary>
    public IReadOnlyList<JobLogDraft> Logs
    {
        get
        {
            lock (_gate)
            {
                return [.. _logs];
            }
        }
    }

    /// <summary>定义只读视图（断言用）。</summary>
    public IReadOnlyDictionary<(long TenantId, string TaskCode), JobDefinitionSnapshot> Definitions
    {
        get
        {
            lock (_gate)
            {
                return new Dictionary<(long, string), JobDefinitionSnapshot>(_definitions);
            }
        }
    }

    /// <summary>种子任务定义（调度器测试用）。</summary>
    /// <param name="snapshot">定义快照。</param>
    public void SeedDefinition(JobDefinitionSnapshot snapshot)
    {
        lock (_gate)
        {
            var id = ++_definitionIdSequence;
            _definitions[(snapshot.TenantId, snapshot.TaskCode)] = snapshot;
            _definitionRows[id] = ((snapshot.TenantId, snapshot.TaskCode), DateTimeOffset.UtcNow);
        }
    }

    /// <summary>直接种子一条执行（终态 / 任意状态构造用）。</summary>
    /// <param name="snapshot">执行快照。</param>
    public void SeedExecution(JobExecutionSnapshot snapshot)
    {
        lock (_gate)
        {
            _executions[snapshot.ExecutionId] = snapshot;
        }
    }

    /// <inheritdoc />
    public Task CreateAsync(JobExecutionCreation creation, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            _executions[creation.ExecutionId] = new JobExecutionSnapshot(
                creation.ExecutionId, creation.TenantId, creation.TaskCode, creation.Mode, creation.Status,
                creation.RetryCount, creation.InitiatorUserId, creation.ParametersJson,
                ResultJson: null, Error: null, DurationMs: null,
                creation.TimeoutSeconds, creation.RetryPolicyJson, HangfireJobId: null,
                DateTimeOffset.UtcNow, StartedAt: null, FinishedAt: null);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<JobExecutionSnapshot?> GetAsync(long tenantId, Guid executionId, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            return Task.FromResult(_executions.TryGetValue(executionId, out var snapshot) ? snapshot : null);
        }
    }

    /// <inheritdoc />
    public Task<JobExecutionSnapshot?> GetByIdempotencyKeyAsync(
        long tenantId, string taskCode, string idempotencyKey, DateTimeOffset windowStart,
        CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            var hit = _executions.Values
                .Where(e => e.TenantId == tenantId
                    && e.TaskCode == taskCode
                    && e.QueuedAt >= windowStart)
                .OrderByDescending(static e => e.QueuedAt)
                .FirstOrDefault();
            return Task.FromResult(hit);
        }
    }

    /// <inheritdoc />
    public Task<bool> TryTransitionAsync(
        long tenantId, Guid executionId, JobExecutionStatus from, JobExecutionStatus targetStatus,
        DateTimeOffset timestamp, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (!JobStateMachine.CanTransition(from, targetStatus)
                || !_executions.TryGetValue(executionId, out var current)
                || current.Status != from)
            {
                return Task.FromResult(false);
            }

            _executions[executionId] = current with
            {
                Status = targetStatus,
                StartedAt = targetStatus == JobExecutionStatus.Running ? timestamp : current.StartedAt,
                FinishedAt = JobStateMachine.IsTerminal(targetStatus) ? timestamp : current.FinishedAt,
            };
            return Task.FromResult(true);
        }
    }

    /// <inheritdoc />
    public Task<bool> CompleteAsync(
        long tenantId, Guid executionId, JobExecutionStatus from, JobExecutionStatus terminal,
        string? resultJson, string? errorMessage, long durationMs, DateTimeOffset finishedAt,
        CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (!JobStateMachine.CanTransition(from, terminal)
                || !_executions.TryGetValue(executionId, out var current)
                || current.Status != from)
            {
                return Task.FromResult(false);
            }

            _executions[executionId] = current with
            {
                Status = terminal,
                ResultJson = resultJson,
                Error = errorMessage,
                DurationMs = durationMs,
                FinishedAt = finishedAt,
            };
            return Task.FromResult(true);
        }
    }

    /// <inheritdoc />
    public Task AppendLogsAsync(IReadOnlyList<JobLogDraft> logs, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            _logs.AddRange(logs);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task SaveHangfireJobIdAsync(
        long tenantId, Guid executionId, string hangfireJobId, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (_executions.TryGetValue(executionId, out var current))
            {
                _executions[executionId] = current with { HangfireJobId = hangfireJobId };
            }
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<int> CountLeadingFailuresAsync(long tenantId, string taskCode, int lookback, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            var leading = 0;
            foreach (var status in _executions.Values
                .Where(e => e.TenantId == tenantId && e.TaskCode == taskCode)
                .OrderByDescending(static e => e.QueuedAt)
                .Take(lookback)
                .Select(static e => e.Status))
            {
                if (status != JobExecutionStatus.Failed)
                {
                    break;
                }

                leading++;
            }

            return Task.FromResult(leading);
        }
    }

    /// <inheritdoc />
    public Task PauseDefinitionAsync(long tenantId, string taskCode, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            var key = (tenantId, taskCode);
            if (_definitions.TryGetValue(key, out var snapshot))
            {
                _definitions[key] = snapshot with { Paused = true };
            }
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<JobDefinitionSnapshot?> GetDefinitionAsync(long tenantId, string taskCode, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            return Task.FromResult(_definitions.TryGetValue((tenantId, taskCode), out var snapshot) ? snapshot : null);
        }
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<JobDispatchCandidate>> GetDueDefinitionsAsync(
        DateTimeOffset now, int limit, CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<JobDispatchCandidate>>([]);

    /// <inheritdoc />
    public Task<bool> TryClaimDueJobAsync(
        long tenantId, long definitionId, DateTimeOffset previousNextRunAt, DateTimeOffset nextRunAt,
        CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (!_definitionRows.TryGetValue(definitionId, out var row)
                || row.NextRunAt != previousNextRunAt)
            {
                return Task.FromResult(false);
            }

            _definitionRows[definitionId] = (row.Key, nextRunAt);
            return Task.FromResult(true);
        }
    }
}

/// <summary>
/// 假入队器：记录 Enqueue / EnqueueIn / Delete 调用，返回递增作业 Id。
/// </summary>
internal sealed class FakeEnqueuer : IJobBackgroundEnqueuer
{
    /// <summary>入队调用记录。</summary>
    /// <param name="ExecutionId">执行 Id。</param>
    /// <param name="TenantId">租户 Id。</param>
    /// <param name="TaskCode">任务编码。</param>
    /// <param name="Delay">延迟时长（立即入队为 null）。</param>
    public sealed record EnqueueCall(Guid ExecutionId, long TenantId, string TaskCode, TimeSpan? Delay);

    /// <summary>全部入队调用（按发生顺序）。</summary>
    public List<EnqueueCall> Calls { get; } = [];

    /// <summary>被删除的 Hangfire 作业 Id 列表。</summary>
    public List<string> DeletedJobIds { get; } = [];

    private int _sequence;

    /// <inheritdoc />
    public string Enqueue(Guid executionId, long tenantId, string taskCode)
    {
        Calls.Add(new EnqueueCall(executionId, tenantId, taskCode, null));
        return $"job-{Interlocked.Increment(ref _sequence)}";
    }

    /// <inheritdoc />
    public string EnqueueIn(TimeSpan delay, Guid executionId, long tenantId, string taskCode)
    {
        Calls.Add(new EnqueueCall(executionId, tenantId, taskCode, delay));
        return $"job-{Interlocked.Increment(ref _sequence)}";
    }

    /// <inheritdoc />
    public bool Delete(string hangfireJobId)
    {
        DeletedJobIds.Add(hangfireJobId);
        return true;
    }
}

/// <summary>
/// 脚本化任务处理器：按注入委托决定执行行为（成功 / 抛异常 / 阻塞到取消）。
/// </summary>
/// <param name="taskCode">任务编码。</param>
/// <param name="behavior">执行行为委托。</param>
internal sealed class ScriptedJobHandler(string taskCode, Func<JobContext, Task<CompletedResult>> behavior) : IJobHandler
{
    /// <inheritdoc />
    public string TaskCode { get; } = taskCode;

    /// <inheritdoc />
    public Task<CompletedResult> ExecuteAsync(JobContext context, CancellationToken cancellationToken)
        => behavior(context);

    /// <summary>构造恒成功的处理器。</summary>
    /// <param name="taskCode">任务编码。</param>
    /// <param name="payload">结果载荷（写入 CompletedResult.ResultJson）。</param>
    /// <returns>处理器实例。</returns>
    public static ScriptedJobHandler Success(string taskCode, object? payload = null)
        => new(taskCode, _ => Task.FromResult(new CompletedResult(payload, null, null, 0, IsSuccess: true)));

    /// <summary>构造恒抛指定异常的处理器。</summary>
    /// <param name="taskCode">任务编码。</param>
    /// <param name="exception">要抛出的异常。</param>
    /// <returns>处理器实例。</returns>
    public static ScriptedJobHandler Throwing(string taskCode, Exception exception)
        => new(taskCode, _ => Task.FromException<CompletedResult>(exception));

    /// <summary>构造阻塞直到取消令牌触发的处理器（超时测试用）。</summary>
    /// <param name="taskCode">任务编码。</param>
    /// <returns>处理器实例。</returns>
    public static ScriptedJobHandler BlockingUntilCanceled(string taskCode)
        => new(taskCode, async context =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, context.CancellationToken).ConfigureAwait(false);
            return new CompletedResult(null, null, null, 0, IsSuccess: true);
        });
}

/// <summary>
/// 记录型消息服务：仅记录 PushAsync 调用（任务中心通知链路断言用），其余方法空实现。
/// </summary>
internal sealed class RecordingMessageService : IMessageService
{
    /// <summary>全部推送请求（按发生顺序）。</summary>
    public List<PushRequest> Pushes { get; } = [];

    /// <inheritdoc />
    public Task SendTemplateAsync(SendTemplateRequest request, CancellationToken cancellationToken)
        => Task.CompletedTask;

    /// <inheritdoc />
    public Task PushAsync(PushRequest request, CancellationToken cancellationToken)
    {
        Pushes.Add(request);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task MarkReadAsync(string messageInboxId, CancellationToken cancellationToken)
        => Task.CompletedTask;

    /// <inheritdoc />
    public Task MarkAllReadAsync(string userId, CancellationToken cancellationToken)
        => Task.CompletedTask;

    /// <inheritdoc />
    public Task<int> GetUnreadCountAsync(string userId, CancellationToken cancellationToken)
        => Task.FromResult(0);

    /// <inheritdoc />
    public Task<IReadOnlyList<MessageInfo>> GetInboxAsync(MessageQuery request, CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<MessageInfo>>([]);

    /// <inheritdoc />
    public Task<IReadOnlyList<MessageInfo>> GetSentListAsync(MessageQuery request, CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<MessageInfo>>([]);
}

/// <summary>租户连接解析桩（测试不触达真实数据库路由；SQLite 预配时不会命中本桩）。</summary>
internal sealed class JobStubTenantDatabaseResolver : ITenantDatabaseResolver
{
    /// <inheritdoc />
    public TenantConnectionInfo Resolve(long tenantId)
        => new(tenantId, "stub", 0, "stub", string.Empty, string.Empty);

    /// <inheritdoc />
    public Task PreloadAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <inheritdoc />
    public void Invalidate(long tenantId)
    {
    }
}
