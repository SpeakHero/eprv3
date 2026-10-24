/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-22 23:10:00 +08:00 中国标准时间 星期四
 * 描述：后台任务中心模块对外契约（触发 / 取消 / 状态 / 历史 / 定义），record + init 不可变
 */

using Qlx.Abstractions.Jobs;

namespace Qlx.Modules.Support.Services.Jobs;

/// <summary>任务触发入参（API 层）。Mode 取值 sync / async（不区分大小写）。</summary>
/// <param name="TaskCode">任务编码。</param>
/// <param name="Mode">执行模式（缺省 sync）。</param>
/// <param name="Parameters">任务参数（可空）。</param>
/// <param name="IdempotencyKey">幂等键（可空；窗口内防重）。</param>
/// <param name="TimeoutSeconds">超时秒数覆盖（可空）。</param>
public sealed record JobTriggerPayload(
    string TaskCode,
    string? Mode = null,
    IReadOnlyDictionary<string, object?>? Parameters = null,
    string? IdempotencyKey = null,
    int? TimeoutSeconds = null);

/// <summary>任务提交结果（对应 <see cref="TaskSubmitResponse"/>，枚举字符串化便于前端展示）。</summary>
/// <param name="ExecutionId">执行 Id。</param>
/// <param name="Mode">执行模式（sync / async）。</param>
/// <param name="Status">当前状态（pending / queued / running / success / failed / timeout / canceled）。</param>
/// <param name="CreatedAt">登记时间（UTC）。</param>
/// <param name="CompletedResult">同步执行完成结果（ASYNC 提交为 null）。</param>
public sealed record JobSubmitResult(
    Guid ExecutionId,
    string Mode,
    string Status,
    DateTimeOffset CreatedAt,
    CompletedResult? CompletedResult);

/// <summary>任务执行历史项。</summary>
/// <param name="ExecutionId">执行 Id。</param>
/// <param name="TaskCode">任务编码。</param>
/// <param name="Mode">执行模式。</param>
/// <param name="Status">状态。</param>
/// <param name="RetryCount">已重试次数。</param>
/// <param name="TriggerSource">触发来源（manual / schedule / retry）。</param>
/// <param name="InitiatorUserId">发起人（可空：系统调度）。</param>
/// <param name="QueuedAt">登记时间（UTC）。</param>
/// <param name="StartedAt">开始时间（UTC，可空）。</param>
/// <param name="FinishedAt">终态时间（UTC，可空）。</param>
/// <param name="DurationMs">执行时长（毫秒，可空）。</param>
/// <param name="Error">错误信息（可空）。</param>
public sealed record JobExecutionItem(
    Guid ExecutionId,
    string TaskCode,
    string Mode,
    string Status,
    int RetryCount,
    string TriggerSource,
    string? InitiatorUserId,
    DateTimeOffset QueuedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? FinishedAt,
    long? DurationMs,
    string? Error);

/// <summary>执行历史分页结果。</summary>
/// <param name="Items">当前页数据。</param>
/// <param name="TotalCount">总条数。</param>
/// <param name="PageIndex">页码（1 起）。</param>
/// <param name="PageSize">页大小。</param>
public sealed record JobExecutionPage(
    IReadOnlyList<JobExecutionItem> Items,
    int TotalCount,
    int PageIndex,
    int PageSize);

/// <summary>任务定义项（含租户级配置覆盖后的生效值）。</summary>
/// <param name="TaskCode">任务编码。</param>
/// <param name="Name">显示名。</param>
/// <param name="ModuleCode">所属模块编码。</param>
/// <param name="DefaultCron">默认 CRON。</param>
/// <param name="CronOverride">租户级 CRON 覆盖（可空）。</param>
/// <param name="EffectiveCron">生效 CRON。</param>
/// <param name="DefaultMode">默认执行模式。</param>
/// <param name="Enabled">是否启用（定义启用且租户配置未停用）。</param>
/// <param name="Paused">熔断暂停。</param>
/// <param name="TimeoutSeconds">生效超时秒数。</param>
/// <param name="NextRunAt">下次调度时间（UTC，可空）。</param>
public sealed record JobDefinitionItem(
    string TaskCode,
    string Name,
    string ModuleCode,
    string DefaultCron,
    string? CronOverride,
    string EffectiveCron,
    string DefaultMode,
    bool Enabled,
    bool Paused,
    int TimeoutSeconds,
    DateTimeOffset? NextRunAt);

/// <summary>任务定义启停入参。启用时同时解除熔断暂停。</summary>
/// <param name="Enabled">是否启用。</param>
public sealed record JobTogglePayload(bool Enabled);
