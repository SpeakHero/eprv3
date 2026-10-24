/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-22 15:35:00 +08:00 中国标准时间 星期三
 * 修改时间：2026-10-22 15:35:00 +08:00 中国标准时间 星期三
 * 描述：审批响应 / 详情 record
 */

namespace Qlx.Abstractions.Approvals;

/// <summary>
/// 审批实例详情。
/// </summary>
public sealed record ApprovalInstanceInfo(
    string InstanceId,
    string ApprovalDefinitionCode,
    string BizType,
    string BizId,
    string BizNo,
    string Title,
    string InitiatorUserId,
    ApprovalStatus Status,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt,
    IReadOnlyList<ApprovalTaskInfo> Tasks);

/// <summary>
/// 审批任务（待办 / 已办）。
/// </summary>
public sealed record ApprovalTaskInfo(
    string TaskId,
    string InstanceId,
    string NodeCode,
    string NodeName,
    string AssigneeUserId,
    ApprovalTaskStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CompletedAt,
    string? Comment = null);

/// <summary>
/// 审批任务状态。
/// </summary>
public enum ApprovalTaskStatus
{
    /// <summary>待处理。</summary>
    Pending = 0,

    /// <summary>已通过。</summary>
    Approved = 1,

    /// <summary>已驳回。</summary>
    Rejected = 2,

    /// <summary>已转交。</summary>
    Transferred = 3,

    /// <summary>已加签。</summary>
    AddSigned = 4,

    /// <summary>已撤销。</summary>
    Canceled = 5
}

/// <summary>
/// 审批委托：用户 A 把审批权限委托给用户 B 在指定时段内处理。
/// </summary>
public sealed record ApprovalDelegateInfo(
    string DelegateId,
    string DelegatorUserId,
    string DelegateeUserId,
    DateTimeOffset StartAt,
    DateTimeOffset EndAt,
    string? Reason = null);

/// <summary>
/// 审批完成终态事件（IntegrationEvent 子类，历史事实）。
/// </summary>
public sealed record ApprovalCompletedEvent(
    string ApprovalInstanceId,
    string ApprovalDefinitionCode,
    string BizType,
    string BizId,
    ApprovalStatus FinalStatus,
    string InitiatorUserId,
    DateTimeOffset CompletedAt) : Qlx.Abstractions.Events.IntegrationEvent;
