/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-26 21:00:00 +08:00 中国标准时间 星期一
 * 描述：Wms 审批挂起请求实体（wms_approval_requests）——「审批通过后才生效」的期望变更载体 + 事件幂等键
 */

using System.ComponentModel.DataAnnotations;

using Microsoft.EntityFrameworkCore;

namespace Qlx.Modules.Wms.Domain.Numbering;

/// <summary>Wms 审批挂起请求状态。</summary>
public enum WmsApprovalRequestStatus
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
/// Wms 审批挂起请求（表 wms_approval_requests）。与 Stk 同构：
/// 大额出库/调拨/盘盈盘亏/仓库停用先序列化期望变更为 PayloadJson 挂起，
/// 审批通过事件到达后由 <c>WmsApprovalApplier</c> 按 BizType 分发生效。
/// <para><see cref="ApprovalInstanceId"/> 为事件幂等键（同一实例只处理一次），
/// 发起审批前为 null，唯一索引以 <c>approval_instance_id IS NOT NULL</c> 过滤。</para>
/// </summary>
[Index(nameof(TenantId), nameof(BizType))]
public sealed class WmsApprovalRequest : Qlx.Abstractions.Domain.AuditableEntity, Qlx.Abstractions.Domain.ITenantEntity
{
    /// <summary>所属租户 Id。</summary>
    public long TenantId { get; internal set; }

    /// <summary>审批实例 Id（租户内唯一；发起审批前为 null）。</summary>
    [MaxLength(64)]
    public string? ApprovalInstanceId { get; internal set; }

    /// <summary>业务类型（wms.outbound.large / wms.transfer / wms.inventory.adjust / wms.warehouse.disable）。</summary>
    [MaxLength(64)]
    public string BizType { get; internal set; } = string.Empty;

    /// <summary>业务 Id（作业单据 Id，发起审批时作为 BizId 传入）。</summary>
    public long BizId { get; internal set; }

    /// <summary>期望变更载荷 JSON。</summary>
    public string? PayloadJson { get; internal set; }

    /// <summary>请求状态。</summary>
    public WmsApprovalRequestStatus Status { get; private set; } = WmsApprovalRequestStatus.Pending;

    /// <summary>生效时间（仅审批通过并应用成功后有值）。</summary>
    public DateTimeOffset? AppliedAt { get; private set; }

    /// <summary>EF 物化用无参构造。</summary>
    public WmsApprovalRequest() { }

    /// <summary>创建审批挂起请求。</summary>
    /// <param name="tenantId">租户 Id。</param>
    /// <param name="bizType">业务类型。</param>
    /// <param name="bizId">业务单据 Id。</param>
    /// <param name="payloadJson">期望变更载荷 JSON。</param>
    public WmsApprovalRequest(long tenantId, string bizType, long bizId, string? payloadJson)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(bizType);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(bizId);
        TenantId = tenantId;
        BizType = bizType;
        BizId = bizId;
        PayloadJson = payloadJson;
    }

    /// <summary>是否为待审批状态。</summary>
    public bool IsPending => Status == WmsApprovalRequestStatus.Pending;

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
        Status = WmsApprovalRequestStatus.Approved;
        AppliedAt = appliedAt;
    }

    /// <summary>标记审批驳回。</summary>
    public void MarkRejected() => Status = WmsApprovalRequestStatus.Rejected;

    /// <summary>标记发起人撤回。</summary>
    public void MarkWithdrawn() => Status = WmsApprovalRequestStatus.Withdrawn;
}
