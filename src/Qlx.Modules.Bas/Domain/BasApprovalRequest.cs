/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-25 12:00:00 +08:00 中国标准时间 星期日
 * 描述：审批挂起请求实体（bas_approval_requests）——「审批通过后才生效」的期望变更载体 + 事件幂等键
 */

using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace Qlx.Modules.Bas.Domain;

/// <summary>审批挂起请求状态。</summary>
public enum ApprovalRequestStatus
{
    /// <summary>待审批（尚未收到终态事件）。</summary>
    Pending = 1,

    /// <summary>审批通过（变更已生效）。</summary>
    Approved = 2,

    /// <summary>审批驳回（变更未生效）。</summary>
    Rejected = 3,

    /// <summary>发起人撤回（变更未生效）。</summary>
    Withdrawn = 4,
}

/// <summary>
/// 审批挂起请求（表 bas_approval_requests）。
/// <para>业务变更先序列化为 PayloadJson 挂起，审批通过事件到达后才落库生效；
/// ApprovalInstanceId 为事件幂等键（同一实例只处理一次）。</para>
/// </summary>
[Index(nameof(TenantId), nameof(BizType))]
public sealed class BasApprovalRequest : Qlx.Abstractions.Domain.AuditableEntity, Qlx.Abstractions.Domain.ITenantEntity
{
    /// <summary>所属租户 Id。</summary>
    public long TenantId { get; internal set; }

    /// <summary>审批实例 Id（租户内唯一；发起审批前为 null）。</summary>
    [MaxLength(64)]
    public string? ApprovalInstanceId { get; internal set; }

    /// <summary>业务类型（bas.org.adjust / bas.position.overstaff）。</summary>
    [MaxLength(64)]
    public string BizType { get; internal set; } = string.Empty;

    /// <summary>业务 Id（本请求行 Id，发起审批时作为 BizId 传入）。</summary>
    public long BizId { get; internal set; }

    /// <summary>期望变更载荷 JSON。</summary>
    public string? PayloadJson { get; internal set; }

    /// <summary>请求状态。</summary>
    public ApprovalRequestStatus Status { get; private set; } = ApprovalRequestStatus.Pending;

    /// <summary>生效时间（仅审批通过并应用成功后有值）。</summary>
    public DateTimeOffset? AppliedAt { get; private set; }

    public BasApprovalRequest() { }

    public BasApprovalRequest(long tenantId, string bizType, string? payloadJson)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(bizType);
        TenantId = tenantId;
        BizType = bizType;
        PayloadJson = payloadJson;
    }

    /// <summary>是否为待审批状态。</summary>
    public bool IsPending => Status == ApprovalRequestStatus.Pending;

    /// <summary>回写审批实例 Id。</summary>
    /// <param name="instanceId">审批实例 Id。</param>
    public void BindInstance(string instanceId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceId);
        ApprovalInstanceId = instanceId;
    }

    /// <summary>标记审批通过且变更已生效。</summary>
    /// <param name="appliedAt">生效时间。</param>
    public void MarkApplied(DateTimeOffset appliedAt)
    {
        Status = ApprovalRequestStatus.Approved;
        AppliedAt = appliedAt;
    }

    /// <summary>标记审批驳回。</summary>
    public void MarkRejected() => Status = ApprovalRequestStatus.Rejected;

    /// <summary>标记发起人撤回。</summary>
    public void MarkWithdrawn() => Status = ApprovalRequestStatus.Withdrawn;
}
