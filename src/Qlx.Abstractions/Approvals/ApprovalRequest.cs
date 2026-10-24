/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-22 15:35:00 +08:00 中国标准时间 星期三
 * 修改时间：2026-10-22 15:35:00 +08:00 中国标准时间 星期三
 * 描述：审批请求 DTO record
 */

namespace Qlx.Abstractions.Approvals;

/// <summary>
/// 发起审批请求。
/// </summary>
public sealed record StartApprovalRequest(
    string ApprovalDefinitionCode,
    string BizType,
    string BizId,
    string BizNo,
    string InitiatorUserId,
    string Title,
    string? Description = null,
    IReadOnlyDictionary<string, object?>? BizData = null,
    string? FormJson = null);

/// <summary>
/// 审批通过请求。
/// </summary>
public sealed record ApproveRequest(
    string InstanceId,
    string ApproverUserId,
    string? Comment = null);

/// <summary>
/// 审批驳回请求。
/// </summary>
public sealed record RejectRequest(
    string InstanceId,
    string ApproverUserId,
    string Reason);

/// <summary>
/// 撤回审批请求。
/// </summary>
public sealed record WithdrawRequest(
    string InstanceId,
    string InitiatorUserId,
    string? Reason = null);

/// <summary>
/// 加签请求：当前审批人增加临时审批人（加签人须先审批，原审批人才能继续）。
/// </summary>
public sealed record AddSignerRequest(
    string InstanceId,
    string TaskId,
    string ApproverUserId,
    string AddSignerUserId,
    string? Reason = null,
    bool IsBeforeCurrent = true);

/// <summary>
/// 转交请求：当前审批人将任务转给他人处理。
/// </summary>
public sealed record TransferRequest(
    string InstanceId,
    string TaskId,
    string FromUserId,
    string ToUserId,
    string? Reason = null);

/// <summary>
/// 催办请求。
/// </summary>
public sealed record UrgeRequest(
    string InstanceId,
    string RequestorUserId,
    string? Message = null);
