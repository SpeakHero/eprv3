/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-26
 * 描述：成本调整单头实体（stk_cost_adjusts）——单号由后端生成（N-1），旧均价与差额由后端从台账取值（V-1），
 *       状态机 Draft → Pending → Approved / Rejected，仅草稿可改删
 */

using System.ComponentModel.DataAnnotations;

using Microsoft.EntityFrameworkCore;

using Qlx.Abstractions.Domain;
using Qlx.Abstractions.Exceptions;

namespace Qlx.Modules.Stk.Domain.Inventory;

/// <summary>成本调整状态。</summary>
public enum CostAdjustStatus
{
    /// <summary>草稿（可改删）。</summary>
    Draft = 1,

    /// <summary>待审批。</summary>
    Pending = 2,

    /// <summary>已通过（台账已落新均价）。</summary>
    Approved = 3,

    /// <summary>已驳回。</summary>
    Rejected = 4,
}

/// <summary>
/// 成本调整单头（表 stk_cost_adjusts）。调整原因取 1 价格差错 / 2 盘盈亏联动 / 3 其他；
/// 仓库为空表示全部仓库（明细行各自携带仓库）；状态机非法流转统一 409 <c>STK_INVALID_STATE</c>。
/// </summary>
[Index(nameof(TenantId), nameof(AdjNo), IsUnique = true)]
public sealed class CostAdjust : AuditableEntity, ITenantEntity
{
    /// <summary>所属租户 Id。</summary>
    public long TenantId { get; internal set; }

    /// <summary>成本调整单号（租户内唯一，后端生成后不可修改）。</summary>
    [MaxLength(32)]
    public string AdjNo { get; internal set; } = string.Empty;

    /// <summary>调整原因（1 价格差错 / 2 盘盈亏联动 / 3 其他）。</summary>
    public int ReasonCode { get; private set; }

    /// <summary>仓库 Id（为空表示全部仓库；原始引用，不联表取名称）。</summary>
    public long? WarehouseId { get; private set; }

    /// <summary>差额合计（2 位小数，明细差额之和）。</summary>
    [Precision(18, 2)]
    public decimal TotalDiffAmount { get; private set; }

    /// <summary>状态。</summary>
    public CostAdjustStatus Status { get; private set; } = CostAdjustStatus.Draft;

    /// <summary>生效时间（仅审批通过并落账后有值）。</summary>
    public DateTimeOffset? AppliedAt { get; private set; }

    /// <summary>备注。</summary>
    [MaxLength(256)]
    public string? Remark { get; private set; }

    /// <summary>EF 物化用无参构造。</summary>
    public CostAdjust() { }

    /// <summary>新建成本调整草稿。</summary>
    /// <param name="tenantId">租户 Id。</param>
    /// <param name="adjNo">成本调整单号（后端生成）。</param>
    /// <param name="reasonCode">调整原因（1/2/3）。</param>
    /// <param name="warehouseId">仓库 Id（空表示全部仓库）。</param>
    /// <param name="totalDiffAmount">差额合计。</param>
    /// <param name="remark">备注。</param>
    public CostAdjust(
        long tenantId, string adjNo, int reasonCode, long? warehouseId, decimal totalDiffAmount, string? remark)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(adjNo);
        ValidateReasonCode(reasonCode);
        if (warehouseId is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(warehouseId), "仓库 Id 必须大于 0");
        }

        TenantId = tenantId;
        AdjNo = adjNo;
        ReasonCode = reasonCode;
        WarehouseId = warehouseId;
        TotalDiffAmount = totalDiffAmount;
        Remark = remark;
    }

    /// <summary>修改草稿（仅草稿可改）。</summary>
    /// <param name="reasonCode">调整原因（1/2/3）。</param>
    /// <param name="warehouseId">仓库 Id（空表示全部仓库）。</param>
    /// <param name="totalDiffAmount">差额合计。</param>
    /// <param name="remark">备注。</param>
    public void UpdateDraft(int reasonCode, long? warehouseId, decimal totalDiffAmount, string? remark)
    {
        EnsureDraft();
        ValidateReasonCode(reasonCode);
        if (warehouseId is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(warehouseId), "仓库 Id 必须大于 0");
        }

        ReasonCode = reasonCode;
        WarehouseId = warehouseId;
        TotalDiffAmount = totalDiffAmount;
        Remark = remark;
    }

    /// <summary>提交审批（Draft → Pending）。</summary>
    public void Submit()
    {
        EnsureDraft();
        Status = CostAdjustStatus.Pending;
    }

    /// <summary>标记审批通过（Pending → Approved）。</summary>
    /// <param name="appliedAt">生效时间。</param>
    public void MarkApproved(DateTimeOffset appliedAt)
    {
        EnsurePending();
        Status = CostAdjustStatus.Approved;
        AppliedAt = appliedAt;
    }

    /// <summary>标记审批驳回（Pending → Rejected）。</summary>
    public void MarkRejected()
    {
        EnsurePending();
        Status = CostAdjustStatus.Rejected;
    }

    /// <summary>断言当前为草稿状态（否则 409 <c>STK_INVALID_STATE</c>）。</summary>
    /// <exception cref="ConflictException">非草稿状态时抛出。</exception>
    public void EnsureDraft()
    {
        if (Status != CostAdjustStatus.Draft)
        {
            throw new ConflictException("仅草稿可操作", "STK_INVALID_STATE");
        }
    }

    private void EnsurePending()
    {
        if (Status != CostAdjustStatus.Pending)
        {
            throw new ConflictException("仅待审批可操作", "STK_INVALID_STATE");
        }
    }

    private static void ValidateReasonCode(int reasonCode)
    {
        if (reasonCode is < 1 or > 3)
        {
            throw new ConflictException("调整原因必须为 1/2/3", "STK_INVALID_REASON");
        }
    }
}
