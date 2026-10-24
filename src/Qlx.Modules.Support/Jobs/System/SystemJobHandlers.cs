/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-22 23:35:00 +08:00 中国标准时间 星期四
 * 描述：后台任务中心系统级任务处理器（6 个）：
 *       job.execution.clean（真实清理）/ tenant.isolation.audit（真实巡检+告警）/
 *       tenant.migrate.shared_to_isolated（候选推荐，永不自动升级）/
 *       message.archive.run / audit.archive / session.clean（能力未落地，观测模式输出统计）
 */

using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Qlx.Abstractions.Jobs;
using Qlx.Abstractions.Messages;
using Qlx.Abstractions.Serialization;
using Qlx.Abstractions.Tenancy;
using Qlx.BuildingBlocks.Jobs;
using Qlx.BuildingBlocks.MultiTenancy;
using Qlx.BuildingBlocks.MultiTenancy.Isolation;
using Qlx.Modules.Support.Domain;
using Qlx.Modules.Support.Options;

// 命名空间取 SystemJobs（而非 System）：避免与全局 System 命名空间冲突，
// 否则本命名空间内 System.Diagnostics / 源生成器分部声明均会解析失败
namespace Qlx.Modules.Support.Jobs.SystemJobs;

/// <summary>
/// 能力未落地任务的观测模式基类：不落 FAILED（避免调度周期性告警风暴），
/// 输出统计日志并以成功结果返回「能力待接入」说明，2D / 2F 落地后替换为真实实现。
/// </summary>
public abstract partial class ObservationJobHandlerBase(ILogger logger) : IJobHandler
{
    private readonly ILogger _logger = logger;

    /// <inheritdoc />
    public abstract string TaskCode { get; }

    /// <summary>能力名称（日志与结果展示）。</summary>
    protected abstract string CapabilityName { get; }

    /// <summary>待接入的落地阶段（2D / 2F）。</summary>
    protected abstract string TargetPhase { get; }

    /// <inheritdoc />
    public async Task<CompletedResult> ExecuteAsync(JobContext context, CancellationToken cancellationToken)
    {
        LogObservation(context.TaskCode, CapabilityName, TargetPhase, context.ExecutionId);
        await Task.CompletedTask.ConfigureAwait(false);
        return new CompletedResult(
            new
            {
                Capability = CapabilityName,
                Phase = TargetPhase,
                context.ExecutionId,
                Note = $"能力 {CapabilityName} 的持久存储在 {TargetPhase} 落地，当前为观测模式（仅输出统计日志）。",
            },
            null, null, ExecutionDurationMs: 0, IsSuccess: true);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "任务 {TaskCode}（{Capability}）以观测模式执行：能力在 {Phase} 落地，本次仅输出统计（executionId={ExecutionId}）")]
    private partial void LogObservation(string taskCode, string capability, string phase, Guid executionId);
}

/// <summary>
/// 消息归档任务（message.archive.run，2D 真实实现）：将已读且创建时间早于保留期的消息批量归档。
/// 状态机红线：仅 Read → Archived（未读消息保持原状态，等用户处理）；
/// 批量走 ExecuteUpdate（【豁免点】集合级过期归档，无逐行业务审计诉求，绕过审计拦截器属有意为之）。
/// </summary>
/// <param name="db">租户上下文。</param>
/// <param name="messagesOptions">信息中心模块配置（归档保留期）。</param>
/// <param name="logger">日志器。</param>
public sealed partial class MessageArchiveRunHandler(
    SupportTenantDbContext db,
    IOptions<SupportMessagesOptions> messagesOptions,
    ILogger<MessageArchiveRunHandler> logger) : IJobHandler
{
    private readonly SupportTenantDbContext _db = db;
    private readonly SupportMessagesOptions _messagesOptions = messagesOptions.Value;
    private readonly ILogger<MessageArchiveRunHandler> _logger = logger;

    /// <inheritdoc />
    public string TaskCode => JobTaskCodes.MessageArchiveRun;

    /// <inheritdoc />
    public async Task<CompletedResult> ExecuteAsync(JobContext context, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var cutoff = now - TimeSpan.FromDays(_messagesOptions.ArchiveRetentionDays);

        var archived = await _db.Messages
            .Where(m => m.Status == MessageStatus.Read && m.CreatedAt < cutoff)
            .ExecuteUpdateAsync(s => s
                .SetProperty(m => m.Status, MessageStatus.Archived)
                .SetProperty(m => m.ArchivedAt, now)
                .SetProperty(m => m.UpdatedAt, now), cancellationToken)
            .ConfigureAwait(false);

        LogArchived(_db.CurrentTenantId, archived, cutoff, _messagesOptions.ArchiveRetentionDays);
        return new CompletedResult(
            new { Archived = archived, Cutoff = cutoff, RetentionDays = _messagesOptions.ArchiveRetentionDays },
            null, null, ExecutionDurationMs: 0, IsSuccess: true);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "租户 {TenantId} 消息归档完成：归档 {Archived} 条（截止 {Cutoff:O}，保留期 {RetentionDays} 天）")]
    private partial void LogArchived(long tenantId, int archived, DateTimeOffset cutoff, int retentionDays);
}

/// <summary>审计归档任务（audit.archive）。审计域归档存储 2F 管控域落地后接入。</summary>
public sealed class AuditArchiveHandler(ILogger<AuditArchiveHandler> logger)
    : ObservationJobHandlerBase(logger)
{
    /// <inheritdoc />
    public override string TaskCode => JobTaskCodes.AuditArchive;

    /// <inheritdoc />
    protected override string CapabilityName => "审计归档";

    /// <inheritdoc />
    protected override string TargetPhase => "2F 管控域";
}

/// <summary>
/// 会话清理任务（session.clean）。登录令牌黑名单走缓存 TTL 自动过期（无持久会话表），
/// 当前无可清理持久数据，输出统计日志；引入持久会话存储后替换为真实清理。
/// </summary>
public sealed class SessionCleanHandler(ILogger<SessionCleanHandler> logger)
    : ObservationJobHandlerBase(logger)
{
    /// <inheritdoc />
    public override string TaskCode => JobTaskCodes.SessionClean;

    /// <inheritdoc />
    protected override string CapabilityName => "会话清理";

    /// <inheritdoc />
    protected override string TargetPhase => "持久会话存储引入后";
}

/// <summary>
/// 任务执行历史清理（job.execution.clean）：删除保留期外的 job_executions 行；
/// job_logs 只增不改、永久保留（方案 §4.12）。清理采用 ExecuteDelete 批量 SQL
/// （绕过审计拦截器属有意为之：历史数据批量过期清除，无逐行业务审计诉求）。
/// </summary>
/// <param name="db">租户上下文。</param>
/// <param name="jobsOptions">任务中心模块配置（保留期）。</param>
/// <param name="logger">日志器。</param>
public sealed partial class JobExecutionCleanHandler(
    SupportTenantDbContext db,
    IOptions<SupportJobsOptions> jobsOptions,
    ILogger<JobExecutionCleanHandler> logger) : IJobHandler
{
    private readonly SupportTenantDbContext _db = db;
    private readonly SupportJobsOptions _jobsOptions = jobsOptions.Value;
    private readonly ILogger<JobExecutionCleanHandler> _logger = logger;

    /// <inheritdoc />
    public string TaskCode => JobTaskCodes.JobExecutionClean;

    /// <inheritdoc />
    public async Task<CompletedResult> ExecuteAsync(JobContext context, CancellationToken cancellationToken)
    {
        // 参数 beforeTime 优先（运维手工指定清理截止）；缺省按保留期推算
        var before = context.Parameters.TryGetValue(JobTaskCodes.ParamKeyBeforeTime, out var rawValue)
            && rawValue is string beforeText
            && DateTimeOffset.TryParse(beforeText, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
                ? parsed.ToUniversalTime()
                : DateTimeOffset.UtcNow - TimeSpan.FromDays(_jobsOptions.ExecutionRetentionDays);

        // 只清理终态执行：RUNNING / QUEUED / PENDING 一律保留（防误删在途任务）
        var deleted = await _db.JobExecutions
            .Where(e => e.QueuedAt < before
                && (e.Status == JobExecutionStatus.Success
                    || e.Status == JobExecutionStatus.Failed
                    || e.Status == JobExecutionStatus.Timeout
                    || e.Status == JobExecutionStatus.Canceled))
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);

        LogCleaned(_db.CurrentTenantId, deleted, before);
        return new CompletedResult(
            new { DeletedExecutions = deleted, Before = before, LogsPreserved = true },
            null, null, ExecutionDurationMs: 0, IsSuccess: true);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "租户 {TenantId} 执行历史清理完成：删除 {Deleted} 条（截止 {Before:O}；job_logs 永久保留）")]
    private partial void LogCleaned(long tenantId, int deleted, DateTimeOffset before);
}

/// <summary>
/// 租户隔离巡检任务（tenant.isolation.audit）：对全部活跃租户（或参数指定租户）执行
/// <see cref="TenantIsolationAuditor.RunAsync"/>，报告经 <see cref="ITenantIsolationAuditRepository"/> 落门户库，
/// 存在严重发现时推 SYSTEM_ALERT。巡检结果随 CompletedResult 返回。
/// </summary>
/// <param name="serviceProvider">服务提供者（可选解析巡检仓储）。</param>
/// <param name="auditor">隔离巡检器。</param>
/// <param name="messageService">消息推送（SYSTEM_ALERT）。</param>
/// <param name="logger">日志器。</param>
public sealed partial class TenantIsolationAuditJobHandler(
    IServiceProvider serviceProvider,
    TenantIsolationAuditor auditor,
    IMessageService messageService,
    ILogger<TenantIsolationAuditJobHandler> logger) : IJobHandler
{
    private readonly IServiceProvider _serviceProvider = serviceProvider;
    private readonly TenantIsolationAuditor _auditor = auditor;
    private readonly IMessageService _messageService = messageService;
    private readonly ILogger<TenantIsolationAuditJobHandler> _logger = logger;

    /// <inheritdoc />
    public string TaskCode => JobTaskCodes.TenantIsolationAudit;

    /// <inheritdoc />
    public async Task<CompletedResult> ExecuteAsync(JobContext context, CancellationToken cancellationToken)
    {
        var repository = _serviceProvider.GetService<ITenantIsolationAuditRepository>();
        if (repository is null)
        {
            // 门户侧巡检仓储未注册（单测宿主）：巡检无法落库，按失败返回（注册缺失属装配问题）
            return new CompletedResult(null, null, null, 0, IsSuccess: false, "巡检仓储未注册（ITenantIsolationAuditRepository），无法执行租户隔离巡检。");
        }

        // 参数 tenantId 指定单租户巡检；缺省巡检全部活跃租户
        var targetTenants = context.Parameters.TryGetValue(JobTaskCodes.ParamKeyTenantId, out var rawTenantId)
            && rawTenantId is long singleTenantId
            ? [singleTenantId]
            : await repository.GetActiveTenantIdsAsync(cancellationToken).ConfigureAwait(false);

        var criticalTenants = new List<long>();
        var healthyCount = 0;
        foreach (var tenantId in targetTenants)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var report = await _auditor.RunAsync(tenantId, cancellationToken).ConfigureAwait(false);
                var findingsJson = JsonSerializer.Serialize(report.Findings, QlxJsonDefaults.Default);
                await repository.SaveReportAsync(
                    tenantId, report.IsHealthy, report.HasCritical, report.Findings.Count,
                    findingsJson, report.ToReadableText(), cancellationToken).ConfigureAwait(false);

                if (report.IsHealthy)
                {
                    healthyCount++;
                }

                if (report.HasCritical)
                {
                    criticalTenants.Add(tenantId);
                    LogCriticalFound(tenantId, report.Findings.Count);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // 单租户巡检失败不阻断其余租户（下一轮调度重试）
                LogTenantAuditFailed(tenantId, ex);
            }
        }

        if (criticalTenants.Count > 0)
        {
            // 系统告警推送（通知链路 D21；推送失败不阻断任务成功）
            try
            {
                await _messageService.PushAsync(new PushRequest(
                    JobSubmitMetadata.SystemUserId,
                    "SYSTEM_ALERT",
                    $"租户隔离巡检发现严重风险：租户 [{string.Join(',', criticalTenants)}]，请立即核查巡检报告。",
                    MessageChannel.InApp,
                    MessagePriority.Urgent,
                    new MessageBizRef("job_execution", context.ExecutionId.ToString("N"))),
                    cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogAlertPushFailed(ex);
            }
        }

        LogAuditSummary(targetTenants.Count, healthyCount, criticalTenants.Count);
        return new CompletedResult(
            new { Audited = targetTenants.Count, Healthy = healthyCount, CriticalTenants = criticalTenants },
            null, null, ExecutionDurationMs: 0, IsSuccess: true);
    }

    [LoggerMessage(Level = LogLevel.Critical, Message = "租户 {TenantId} 隔离巡检发现严重风险（共 {FindingCount} 条发现）")]
    private partial void LogCriticalFound(long tenantId, int findingCount);

    [LoggerMessage(Level = LogLevel.Error, Message = "租户 {TenantId} 隔离巡检执行失败（下一轮调度重试）")]
    private partial void LogTenantAuditFailed(long tenantId, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "隔离巡检系统告警推送失败（不阻断任务）")]
    private partial void LogAlertPushFailed(Exception exception);

    [LoggerMessage(Level = LogLevel.Information, Message = "租户隔离巡检完成：巡检 {Total} 租户，健康 {Healthy}，严重 {Critical}")]
    private partial void LogAuditSummary(int total, int healthy, int critical);
}

/// <summary>
/// 租户共享库→独立库升级推荐任务（tenant.migrate.shared_to_isolated）。
/// 红线：独立库升级仅由门户管理员人工决定（永不自动切换），本任务只做候选识别与推荐推送，
/// 不登记升级任务、不触碰 TenantUpgradePipeline 流水线。
/// </summary>
/// <param name="serviceProvider">服务提供者（可选解析巡检仓储枚举活跃租户）。</param>
/// <param name="resolver">租户数据库路由解析器（连接信息含共享库标记）。</param>
/// <param name="messageService">消息推送（推荐通知）。</param>
/// <param name="logger">日志器。</param>
public sealed partial class TenantMigrateRecommendHandler(
    IServiceProvider serviceProvider,
    ITenantDatabaseResolver resolver,
    IMessageService messageService,
    ILogger<TenantMigrateRecommendHandler> logger) : IJobHandler
{
    private readonly IServiceProvider _serviceProvider = serviceProvider;
    private readonly ITenantDatabaseResolver _resolver = resolver;
    private readonly IMessageService _messageService = messageService;
    private readonly ILogger<TenantMigrateRecommendHandler> _logger = logger;

    /// <inheritdoc />
    public string TaskCode => JobTaskCodes.TenantMigrateSharedToIsolated;

    /// <inheritdoc />
    public async Task<CompletedResult> ExecuteAsync(JobContext context, CancellationToken cancellationToken)
    {
        // 参数 tenantId 指定单租户判定；缺省枚举仓储活跃租户（缺仓储时降级为仅日志）
        IReadOnlyList<long> targetTenants;
        if (context.Parameters.TryGetValue(JobTaskCodes.ParamKeyTenantId, out var rawTenantId)
            && rawTenantId is long singleTenantId)
        {
            targetTenants = [singleTenantId];
        }
        else
        {
            targetTenants = await ResolveActiveTenantsAsync(cancellationToken).ConfigureAwait(false);
        }

        var sharedCandidates = new List<long>();
        foreach (var tenantId in targetTenants)
        {
            try
            {
                if (_resolver.Resolve(tenantId).IsShared)
                {
                    sharedCandidates.Add(tenantId);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // 单租户路由解析失败不阻断其余租户
                LogResolveFailed(tenantId, ex);
            }
        }

        var operatorName = context.Parameters.TryGetValue(JobTaskCodes.ParamKeyOperatorName, out var rawOperator)
            && rawOperator is string operatorText
                ? operatorText
                : JobSubmitMetadata.SystemUserId;
        if (sharedCandidates.Count > 0)
        {
            try
            {
                await _messageService.PushAsync(new PushRequest(
                    operatorName,
                    "TENANT_UPGRADE_RECOMMEND",
                    $"识别到 {sharedCandidates.Count} 个共享库租户（{string.Join(',', sharedCandidates)}），" +
                    "如需升级独立库请由门户管理员在租户管理页人工发起（系统永不自动切换）。",
                    MessageChannel.InApp,
                    MessagePriority.Normal,
                    new MessageBizRef("job_execution", context.ExecutionId.ToString("N"))),
                    cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogRecommendPushFailed(ex);
            }
        }

        LogRecommendSummary(targetTenants.Count, sharedCandidates.Count);
        return new CompletedResult(
            new { Checked = targetTenants.Count, SharedCandidates = sharedCandidates, AutoUpgraded = false },
            null, null, ExecutionDurationMs: 0, IsSuccess: true);
    }

    /// <summary>解析活跃租户清单（仓储可选：未注册时返回空集，仅记录跳过）。</summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>活跃租户 Id 列表。</returns>
    private async Task<IReadOnlyList<long>> ResolveActiveTenantsAsync(CancellationToken cancellationToken)
    {
        // 仓储经上下文可选解析（巡检仓储由 Portal 注册；纯单测宿主未注册时返回空集）
        var repository = _serviceProvider.GetService<ITenantIsolationAuditRepository>();
        if (repository is null)
        {
            return [];
        }

        return await repository.GetActiveTenantIdsAsync(cancellationToken).ConfigureAwait(false);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "租户 {TenantId} 数据库路由解析失败（升级推荐跳过该租户）")]
    private partial void LogResolveFailed(long tenantId, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "独立库升级推荐推送失败（不阻断任务）")]
    private partial void LogRecommendPushFailed(Exception exception);

    [LoggerMessage(Level = LogLevel.Information, Message = "独立库升级推荐完成：检查 {CheckedCount} 租户，共享库候选 {SharedCount}（未自动升级）")]
    private partial void LogRecommendSummary(int checkedCount, int sharedCount);
}
