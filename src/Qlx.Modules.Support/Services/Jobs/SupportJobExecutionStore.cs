/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-22 22:55:00 +08:00 中国标准时间 星期四
 * 描述：IJobExecutionStore 的 Support 模块实现（job_definitions / job_executions / job_logs）。
 *       全部方法携带显式租户 Id 按需构建租户上下文（调度扫描与 Hangfire Worker 无 HTTP 租户上下文）。
 *       状态转移 / 认领使用 ExecuteUpdate 原子 SQL（绕过审计拦截器属有意为之：仅状态机字段推进，无业务审计诉求）
 */

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Qlx.Abstractions.Jobs;
using Qlx.Abstractions.Serialization;
using Qlx.Abstractions.Tenancy;
using Qlx.BuildingBlocks.Jobs;
using Qlx.BuildingBlocks.MultiTenancy;
using Qlx.Modules.Support.Domain;
using Qlx.Modules.Support.Options;

namespace Qlx.Modules.Support.Services.Jobs;

/// <summary>
/// 任务执行存储（Support 模块实现）。调度引擎（BuildingBlocks）仅依赖
/// <see cref="IJobExecutionStore"/> 抽象；本实现按显式租户 Id 构建独立
/// <see cref="SupportTenantDbContext"/>，活跃租户枚举复用门户侧
/// <see cref="ITenantIsolationAuditRepository.GetActiveTenantIdsAsync"/>。
/// </summary>
public sealed partial class SupportJobExecutionStore(
    IServiceProvider serviceProvider,
    ITenantDatabaseResolver resolver,
    SupportJobDefinitionRegistry registry,
    IOptions<SupportJobsOptions> jobsOptions,
    ILogger<SupportJobExecutionStore> logger) : IJobExecutionStore
{
    private readonly IServiceProvider _serviceProvider = serviceProvider;
    private readonly ITenantDatabaseResolver _resolver = resolver;
    private readonly SupportJobDefinitionRegistry _registry = registry;
    private readonly SupportJobsOptions _jobsOptions = jobsOptions.Value;
    private readonly ILogger<SupportJobExecutionStore> _logger = logger;

    /// <summary>快照投影（EF 单查询映射，禁 N+1）。</summary>
    private static readonly System.Linq.Expressions.Expression<Func<JobExecutionEntity, JobExecutionSnapshot>>
        s_snapshotSelector = e => new JobExecutionSnapshot(
            e.ExecutionId, e.TenantId, e.TaskCode, e.Mode, e.Status, e.RetryCount,
            e.InitiatorUserId, e.ParametersJson, e.ResultJson, e.Error, e.DurationMs,
            e.TimeoutSeconds, e.RetryPolicyJson, e.HangfireJobId, e.QueuedAt, e.StartedAt, e.FinishedAt);

    /// <inheritdoc />
    public async Task CreateAsync(JobExecutionCreation creation, CancellationToken cancellationToken)
    {
        await using var db = CreateContext(creation.TenantId);
        db.JobExecutions.Add(new JobExecutionEntity
        {
            TenantId = creation.TenantId,
            ExecutionId = creation.ExecutionId,
            TaskCode = creation.TaskCode,
            Mode = creation.Mode,
            Status = creation.Status,
            RetryCount = creation.RetryCount,
            TriggerSource = creation.TriggerSource,
            InitiatorUserId = creation.InitiatorUserId,
            IdempotencyKey = creation.IdempotencyKey,
            ParametersJson = creation.ParametersJson,
            TimeoutSeconds = creation.TimeoutSeconds,
            RetryPolicyJson = creation.RetryPolicyJson,
            QueuedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<JobExecutionSnapshot?> GetAsync(
        long tenantId, Guid executionId, CancellationToken cancellationToken)
    {
        await using var db = CreateContext(tenantId);
        return await db.JobExecutions
            .Where(e => e.ExecutionId == executionId)
            .Select(s_snapshotSelector)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<JobExecutionSnapshot?> GetByIdempotencyKeyAsync(
        long tenantId, string taskCode, string idempotencyKey, DateTimeOffset windowStart,
        CancellationToken cancellationToken)
    {
        await using var db = CreateContext(tenantId);
        return await db.JobExecutions
            .Where(e => e.TaskCode == taskCode
                && e.IdempotencyKey == idempotencyKey
                && e.QueuedAt >= windowStart)
            .OrderByDescending(e => e.QueuedAt)
            .Select(s_snapshotSelector)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<bool> TryTransitionAsync(
        long tenantId, Guid executionId, JobExecutionStatus from, JobExecutionStatus targetStatus,
        DateTimeOffset timestamp, CancellationToken cancellationToken)
    {
        // 状态机守卫：非法转移（含终态 → RUNNING 复活、QUEUED → SUCCESS 跳级）直接拒绝，
        // 保证「已被取消 / 终态任务跳过执行」的幂等语义（JobExecutionRunner.ExecuteAsync 依赖本行为）
        if (!JobStateMachine.CanTransition(from, targetStatus))
        {
            return false;
        }

        await using var db = CreateContext(tenantId);
        var rows = await db.JobExecutions
            .Where(e => e.ExecutionId == executionId && e.Status == from)
            .ExecuteUpdateAsync(setter => setter
                    .SetProperty(e => e.Status, targetStatus)
                    // 仅进入 RUNNING 时写 StartedAt，其余转移保留原值（终态转移不得清空开始时间）
                    .SetProperty(e => e.StartedAt, e => targetStatus == JobExecutionStatus.Running
                        ? (DateTimeOffset?)timestamp
                        : e.StartedAt)
                    .SetProperty(e => e.FinishedAt, e => JobStateMachine.IsTerminal(targetStatus)
                        ? (DateTimeOffset?)timestamp
                        : e.FinishedAt),
                cancellationToken)
            .ConfigureAwait(false);
        return rows > 0;
    }

    /// <inheritdoc />
    public async Task<bool> CompleteAsync(
        long tenantId, Guid executionId, JobExecutionStatus from, JobExecutionStatus terminal,
        string? resultJson, string? errorMessage, long durationMs, DateTimeOffset finishedAt,
        CancellationToken cancellationToken)
    {
        await using var db = CreateContext(tenantId);
        var rows = await db.JobExecutions
            .Where(e => e.ExecutionId == executionId && e.Status == from)
            .ExecuteUpdateAsync(setter => setter
                    .SetProperty(e => e.Status, terminal)
                    .SetProperty(e => e.ResultJson, resultJson)
                    .SetProperty(e => e.Error, errorMessage)
                    .SetProperty(e => e.DurationMs, durationMs)
                    .SetProperty(e => e.FinishedAt, finishedAt),
                cancellationToken)
            .ConfigureAwait(false);
        return rows > 0;
    }

    /// <inheritdoc />
    public async Task AppendLogsAsync(IReadOnlyList<JobLogDraft> logs, CancellationToken cancellationToken)
    {
        if (logs.Count == 0)
        {
            return;
        }

        // 日志按租户分组批量写入（同批次理论同租户；跨租户批次按租户拆分上下文，各一次 SaveChanges）
        foreach (var group in logs.GroupBy(static l => l.TenantId))
        {
            await using var db = CreateContext(group.Key);
            db.JobLogs.AddRange(group.Select(l => new JobLogEntity(
                l.TenantId, l.ExecutionId, l.TaskCode, l.Level, l.Message, l.Detail, l.StackTrace, l.LogAt)));
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async Task SaveHangfireJobIdAsync(
        long tenantId, Guid executionId, string hangfireJobId, CancellationToken cancellationToken)
    {
        await using var db = CreateContext(tenantId);
        await db.JobExecutions
            .Where(e => e.ExecutionId == executionId)
            .ExecuteUpdateAsync(setter => setter.SetProperty(e => e.HangfireJobId, hangfireJobId), cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<int> CountLeadingFailuresAsync(
        long tenantId, string taskCode, int lookback, CancellationToken cancellationToken)
    {
        await using var db = CreateContext(tenantId);
        var recentStatuses = await db.JobExecutions
            .Where(e => e.TaskCode == taskCode)
            .OrderByDescending(e => e.QueuedAt)
            .Take(lookback)
            .Select(e => (JobExecutionStatus?)e.Status)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var leading = 0;
        foreach (var status in recentStatuses)
        {
            if (status != JobExecutionStatus.Failed)
            {
                break;
            }

            leading++;
        }

        return leading;
    }

    /// <inheritdoc />
    public async Task PauseDefinitionAsync(long tenantId, string taskCode, CancellationToken cancellationToken)
    {
        await using var db = CreateContext(tenantId);
        await db.JobDefinitions
            .Where(d => d.TaskCode == taskCode)
            .ExecuteUpdateAsync(setter => setter
                    .SetProperty(d => d.Paused, true)
                    .SetProperty(d => d.UpdatedAt, DateTimeOffset.UtcNow),
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<JobDefinitionSnapshot?> GetDefinitionAsync(
        long tenantId, string taskCode, CancellationToken cancellationToken)
    {
        var snapshot = await QueryDefinitionAsync(tenantId, taskCode, cancellationToken).ConfigureAwait(false);
        if (snapshot is not null)
        {
            return snapshot;
        }

        // 懒种子兜底：存量租户早于服务任务（ServiceJobs）声明前已种子化，首次提交新任务码时
        // 触发一次非破坏性刷新（补齐缺失定义）后重查；仍未命中才视为未注册（与审批流 / 消息模板懒种子同模式）
        await _registry.EnsureTenantSeededAsync(tenantId, cancellationToken).ConfigureAwait(false);
        return await QueryDefinitionAsync(tenantId, taskCode, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>按任务码查询定义快照（含租户级配置覆盖）；未种子化返回 null。</summary>
    private async Task<JobDefinitionSnapshot?> QueryDefinitionAsync(
        long tenantId, string taskCode, CancellationToken cancellationToken)
    {
        await using var db = CreateContext(tenantId);
        var definition = await db.JobDefinitions
            .Where(d => d.TaskCode == taskCode)
            .Select(d => new { d.TaskCode, d.Name, d.ModuleCode, d.DefaultMode, d.DefaultCron, d.Enabled, d.Paused, d.DefaultTimeoutSeconds })
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        if (definition is null)
        {
            return null;
        }

        var config = await db.JobConfigs
            .Where(c => c.TaskCode == taskCode)
            .Select(c => new { c.CronOverride, c.TimeoutSecondsOverride, c.Enabled })
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return new JobDefinitionSnapshot(
            definition.TaskCode,
            tenantId,
            definition.Name,
            definition.ModuleCode,
            definition.DefaultMode,
            config?.CronOverride ?? definition.DefaultCron,
            definition.Enabled && (config?.Enabled ?? true),
            definition.Paused,
            config?.TimeoutSecondsOverride ?? definition.DefaultTimeoutSeconds,
            new RetryPolicy());
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<JobDispatchCandidate>> GetDueDefinitionsAsync(
        DateTimeOffset now, int limit, CancellationToken cancellationToken)
    {
        var auditRepository = _serviceProvider.GetService<ITenantIsolationAuditRepository>();
        if (auditRepository is null)
        {
            // 门户侧租户巡检仓储未注册（纯单测宿主）：无活跃租户来源，直接空扫描
            return [];
        }

        var tenantIds = await auditRepository.GetActiveTenantIdsAsync(cancellationToken).ConfigureAwait(false);
        var candidates = new List<JobDispatchCandidate>();
        foreach (var tenantId in tenantIds)
        {
            if (candidates.Count >= limit)
            {
                break;
            }

            try
            {
                await CollectDueCandidatesAsync(tenantId, now, limit - candidates.Count, candidates, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // 单租户扫描失败不阻断整体调度（下一轮重试）
                LogTenantScanFailed(tenantId, ex);
            }
        }

        return candidates;
    }

    /// <summary>收集单租户到期任务候选（含懒种子与租户级配置覆盖）。</summary>
    /// <param name="tenantId">租户 Id。</param>
    /// <param name="now">当前时间（UTC）。</param>
    /// <param name="remaining">剩余候选容量。</param>
    /// <param name="candidates">候选累积列表。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    private async Task CollectDueCandidatesAsync(
        long tenantId, DateTimeOffset now, int remaining,
        List<JobDispatchCandidate> candidates, CancellationToken cancellationToken)
    {
        await using var db = CreateContext(tenantId);

        // 懒种子：新租户零定义时先按 Manifest / 许可种子（下一轮扫描即可调度）
        if (!await db.JobDefinitions.AnyAsync(cancellationToken).ConfigureAwait(false))
        {
            await _registry.EnsureTenantSeededAsync(tenantId, cancellationToken).ConfigureAwait(false);
        }

        var due = await db.JobDefinitions
            .Where(d => d.Enabled && !d.Paused && d.NextRunAt != null && d.NextRunAt <= now)
            .OrderBy(d => d.NextRunAt)
            .Take(remaining)
            .Select(d => new
            {
                d.Id,
                d.TaskCode,
                d.DefaultCron,
                d.DefaultMode,
                d.DefaultTimeoutSeconds,
                d.NextRunAt,
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        if (due.Count == 0)
        {
            return;
        }

        // 租户级配置覆盖（单查询批量拉取，禁 N+1）
        var dueCodes = due.Select(static d => d.TaskCode).ToList();
        var configs = await db.JobConfigs
            .Where(c => dueCodes.Contains(c.TaskCode))
            .ToDictionaryAsync(static c => c.TaskCode, cancellationToken)
            .ConfigureAwait(false);

        foreach (var d in due)
        {
            _ = configs.TryGetValue(d.TaskCode, out var config);
            candidates.Add(new JobDispatchCandidate(
                d.Id,
                tenantId,
                d.TaskCode,
                config?.CronOverride ?? d.DefaultCron,
                d.DefaultMode,
                config?.TimeoutSecondsOverride ?? d.DefaultTimeoutSeconds,
                d.NextRunAt!.Value));
        }
    }

    /// <inheritdoc />
    public async Task<bool> TryClaimDueJobAsync(
        long tenantId, long definitionId, DateTimeOffset previousNextRunAt, DateTimeOffset nextRunAt,
        CancellationToken cancellationToken)
    {
        await using var db = CreateContext(tenantId);
        var rows = await db.JobDefinitions
            .Where(d => d.Id == definitionId && d.NextRunAt == previousNextRunAt)
            .ExecuteUpdateAsync(setter => setter
                    .SetProperty(d => d.NextRunAt, nextRunAt)
                    .SetProperty(d => d.UpdatedAt, DateTimeOffset.UtcNow),
                cancellationToken)
            .ConfigureAwait(false);
        return rows > 0;
    }

    /// <summary>按显式租户 Id 构建租户上下文（复用注册表的上下文工厂逻辑）。</summary>
    /// <param name="tenantId">租户 Id。</param>
    /// <returns>租户上下文。</returns>
    private SupportTenantDbContext CreateContext(long tenantId)
    {
        var builder = new DbContextOptionsBuilder<SupportTenantDbContext>();
        var interceptors = _serviceProvider.GetServices<Microsoft.EntityFrameworkCore.Diagnostics.IInterceptor>();
        builder.AddInterceptors(interceptors);
        return new SupportTenantDbContext(_resolver, tenantId, builder.Options);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "租户 {TenantId} 到期任务扫描失败（下一轮重试）")]
    private partial void LogTenantScanFailed(long tenantId, Exception exception);
}
