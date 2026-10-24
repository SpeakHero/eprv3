/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-22 21:40:00 +08:00 中国标准时间 星期四
 * 描述：任务执行存储抽象（job_executions / job_logs / job_definitions 读写），
 *       由 Support 模块基于 SupportTenantDbContext 实现，BuildingBlocks 调度引擎仅依赖本抽象
 */

using Qlx.Abstractions.Jobs;

namespace Qlx.BuildingBlocks.Jobs;

/// <summary>
/// 任务执行快照（跨层只读传输，字段对应 job_executions 行）。
/// </summary>
/// <param name="ExecutionId">执行 Id。</param>
/// <param name="TenantId">租户 Id（long 雪花）。</param>
/// <param name="TaskCode">任务编码。</param>
/// <param name="Mode">执行模式。</param>
/// <param name="Status">当前状态。</param>
/// <param name="RetryCount">已重试次数。</param>
/// <param name="InitiatorUserId">发起人用户标识（可空：系统调度）。</param>
/// <param name="ParametersJson">任务参数 JSON。</param>
/// <param name="ResultJson">执行结果 JSON。</param>
/// <param name="Error">错误信息。</param>
/// <param name="DurationMs">执行时长（毫秒）。</param>
/// <param name="TimeoutSeconds">超时秒数。</param>
/// <param name="RetryPolicyJson">重试策略 JSON（null=引擎默认策略）。</param>
/// <param name="HangfireJobId">Hangfire 作业 Id（ASYNC 模式）。</param>
/// <param name="QueuedAt">登记时间。</param>
/// <param name="StartedAt">开始执行时间。</param>
/// <param name="FinishedAt">终态时间。</param>
public sealed record JobExecutionSnapshot(
    Guid ExecutionId,
    long TenantId,
    string TaskCode,
    ExecutionMode Mode,
    JobExecutionStatus Status,
    int RetryCount,
    string? InitiatorUserId,
    string? ParametersJson,
    string? ResultJson,
    string? Error,
    long? DurationMs,
    int TimeoutSeconds,
    string? RetryPolicyJson,
    string? HangfireJobId,
    DateTimeOffset QueuedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? FinishedAt);

/// <summary>
/// 任务执行登记数据（写入 job_executions）。
/// </summary>
/// <param name="ExecutionId">执行 Id。</param>
/// <param name="TenantId">租户 Id。</param>
/// <param name="TaskCode">任务编码。</param>
/// <param name="Mode">执行模式。</param>
/// <param name="Status">初始状态（PENDING）。</param>
/// <param name="RetryCount">已重试次数。</param>
/// <param name="TriggerSource">触发来源（手动 / 调度 / 依赖）。</param>
/// <param name="InitiatorUserId">发起人用户标识（可空：系统调度）。</param>
/// <param name="IdempotencyKey">幂等键（可空）。</param>
/// <param name="ParametersJson">任务参数 JSON。</param>
/// <param name="TimeoutSeconds">超时秒数。</param>
/// <param name="RetryPolicyJson">重试策略 JSON（null=引擎默认策略）。</param>
public sealed record JobExecutionCreation(
    Guid ExecutionId,
    long TenantId,
    string TaskCode,
    ExecutionMode Mode,
    JobExecutionStatus Status,
    int RetryCount,
    string TriggerSource,
    string? InitiatorUserId,
    string? IdempotencyKey,
    string? ParametersJson,
    int TimeoutSeconds,
    string? RetryPolicyJson);

/// <summary>
/// 任务日志草稿（批量写入 job_logs，只增不改）。
/// </summary>
/// <param name="ExecutionId">执行 Id。</param>
/// <param name="TenantId">租户 Id。</param>
/// <param name="TaskCode">任务编码。</param>
/// <param name="Level">日志级别。</param>
/// <param name="Message">日志消息。</param>
/// <param name="Detail">详情（可空）。</param>
/// <param name="StackTrace">堆栈（可空）。</param>
/// <param name="LogAt">记录时间（UTC）。</param>
public sealed record JobLogDraft(
    Guid ExecutionId,
    long TenantId,
    string TaskCode,
    JobLogLevel Level,
    string Message,
    string? Detail,
    string? StackTrace,
    DateTimeOffset LogAt);

/// <summary>
/// 到期任务调度候选（来自 job_definitions，含租户）。
/// </summary>
/// <param name="DefinitionId">定义行 Id。</param>
/// <param name="TenantId">租户 Id。</param>
/// <param name="TaskCode">任务编码。</param>
/// <param name="EffectiveCron">生效 CRON（含租户级覆盖）。</param>
/// <param name="DefaultMode">默认执行模式。</param>
/// <param name="TimeoutSeconds">超时秒数。</param>
/// <param name="PreviousNextRunAt">被声明的上次「下次执行时间」（乐观锁判据）。</param>
public sealed record JobDispatchCandidate(
    long DefinitionId,
    long TenantId,
    string TaskCode,
    string EffectiveCron,
    ExecutionMode DefaultMode,
    int TimeoutSeconds,
    DateTimeOffset PreviousNextRunAt);

/// <summary>
/// 任务定义快照（调度与提交时读取）。
/// </summary>
/// <param name="TaskCode">任务编码。</param>
/// <param name="TenantId">租户 Id。</param>
/// <param name="Name">显示名。</param>
/// <param name="ModuleCode">所属模块编码。</param>
/// <param name="DefaultMode">默认执行模式。</param>
/// <param name="EffectiveCron">生效 CRON（含覆盖）。</param>
/// <param name="Enabled">租户级启用（定义启用且配置未停用）。</param>
/// <param name="Paused">熔断暂停。</param>
/// <param name="TimeoutSeconds">超时秒数。</param>
/// <param name="RetryPolicy">重试策略。</param>
public sealed record JobDefinitionSnapshot(
    string TaskCode,
    long TenantId,
    string Name,
    string ModuleCode,
    ExecutionMode DefaultMode,
    string EffectiveCron,
    bool Enabled,
    bool Paused,
    int TimeoutSeconds,
    RetryPolicy RetryPolicy);

/// <summary>
/// 任务执行存储抽象。Support 模块实现（job_executions / job_logs / job_definitions），
/// 全部方法携带显式租户 Id——后台 Worker 与调度扫描无 HTTP 租户上下文，禁止依赖环境租户。
/// </summary>
public interface IJobExecutionStore
{
    /// <summary>登记一次任务执行（status=PENDING）。</summary>
    /// <param name="creation">登记数据。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task CreateAsync(JobExecutionCreation creation, CancellationToken cancellationToken);

    /// <summary>按执行 Id 加载快照；不存在返回 null。</summary>
    /// <param name="tenantId">租户 Id。</param>
    /// <param name="executionId">执行 Id。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task<JobExecutionSnapshot?> GetAsync(long tenantId, Guid executionId, CancellationToken cancellationToken);

    /// <summary>按幂等键查询 5 分钟窗口内的既有执行（防重）；无命中返回 null。</summary>
    /// <param name="tenantId">租户 Id。</param>
    /// <param name="taskCode">任务编码。</param>
    /// <param name="idempotencyKey">幂等键。</param>
    /// <param name="windowStart">窗口起点（UTC）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task<JobExecutionSnapshot?> GetByIdempotencyKeyAsync(
        long tenantId, string taskCode, string idempotencyKey, DateTimeOffset windowStart,
        CancellationToken cancellationToken);

    /// <summary>按状态机条件转移执行状态（原子 SQL：仅当前状态等于 from 时更新）。</summary>
    /// <param name="tenantId">租户 Id。</param>
    /// <param name="executionId">执行 Id。</param>
    /// <param name="from">期望当前状态。</param>
    /// <param name="targetStatus">目标状态。</param>
    /// <param name="timestamp">转移时间（UTC）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>转移成功返回 true；当前状态不符返回 false。</returns>
    Task<bool> TryTransitionAsync(
        long tenantId, Guid executionId, JobExecutionStatus from, JobExecutionStatus targetStatus,
        DateTimeOffset timestamp, CancellationToken cancellationToken);

    /// <summary>原子落终态：条件转移 + 结果 / 错误 / 时长一并写入。</summary>
    /// <param name="tenantId">租户 Id。</param>
    /// <param name="executionId">执行 Id。</param>
    /// <param name="from">期望当前状态（RUNNING）。</param>
    /// <param name="terminal">终态。</param>
    /// <param name="resultJson">结果 JSON（可空）。</param>
    /// <param name="errorMessage">错误信息（可空）。</param>
    /// <param name="durationMs">执行时长（毫秒）。</param>
    /// <param name="finishedAt">终态时间（UTC）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>落库成功返回 true；当前状态不符（已被取消等）返回 false。</returns>
    Task<bool> CompleteAsync(
        long tenantId, Guid executionId, JobExecutionStatus from, JobExecutionStatus terminal,
        string? resultJson, string? errorMessage, long durationMs, DateTimeOffset finishedAt,
        CancellationToken cancellationToken);

    /// <summary>批量追加执行日志（job_logs 只增不改；单次 SaveChanges 批量插入，禁止逐条提交）。</summary>
    /// <param name="logs">日志草稿集合。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task AppendLogsAsync(IReadOnlyList<JobLogDraft> logs, CancellationToken cancellationToken);

    /// <summary>保存 Hangfire 作业 Id（ASYNC 模式取消与状态联动用）。</summary>
    /// <param name="tenantId">租户 Id。</param>
    /// <param name="executionId">执行 Id。</param>
    /// <param name="hangfireJobId">Hangfire 作业 Id。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task SaveHangfireJobIdAsync(
        long tenantId, Guid executionId, string hangfireJobId, CancellationToken cancellationToken);

    /// <summary>统计指定任务最近连续失败次数（从最新一次执行回溯连续 FAILED 的条数，用于熔断）。</summary>
    /// <param name="tenantId">租户 Id。</param>
    /// <param name="taskCode">任务编码。</param>
    /// <param name="lookback">回溯条数上限。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task<int> CountLeadingFailuresAsync(
        long tenantId, string taskCode, int lookback, CancellationToken cancellationToken);

    /// <summary>暂停（熔断）指定任务定义。</summary>
    /// <param name="tenantId">租户 Id。</param>
    /// <param name="taskCode">任务编码。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task PauseDefinitionAsync(long tenantId, string taskCode, CancellationToken cancellationToken);

    /// <summary>读取任务定义快照（含租户级配置覆盖）；未种子化返回 null。</summary>
    /// <param name="tenantId">租户 Id。</param>
    /// <param name="taskCode">任务编码。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task<JobDefinitionSnapshot?> GetDefinitionAsync(
        long tenantId, string taskCode, CancellationToken cancellationToken);

    /// <summary>扫描全部活跃租户的到期任务定义（已启用、未熔断暂停且下次执行时间已到期，含配置覆盖）。</summary>
    /// <param name="now">当前时间（UTC）。</param>
    /// <param name="limit">单轮扫描条数上限。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>到期候选列表。</returns>
    Task<IReadOnlyList<JobDispatchCandidate>> GetDueDefinitionsAsync(
        DateTimeOffset now, int limit, CancellationToken cancellationToken);

    /// <summary>
    /// 乐观锁认领到期任务：仅当 job_definitions.next_run_at 仍等于 <paramref name="previousNextRunAt"/>
    /// 时将其推进到 <paramref name="nextRunAt"/>（多实例部署下的分布式认领，等价于
    /// <c>tenant:job:dispatch:{code}</c> 锁的持久化形态）。
    /// </summary>
    /// <param name="tenantId">租户 Id（构建租户上下文用）。</param>
    /// <param name="definitionId">定义行 Id。</param>
    /// <param name="previousNextRunAt">上次「下次执行时间」。</param>
    /// <param name="nextRunAt">推进后的「下次执行时间」。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>认领成功返回 true；已被其他实例认领返回 false。</returns>
    Task<bool> TryClaimDueJobAsync(
        long tenantId, long definitionId, DateTimeOffset previousNextRunAt, DateTimeOffset nextRunAt,
        CancellationToken cancellationToken);
}
