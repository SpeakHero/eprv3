/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-26
 * 描述：盘盈盘亏单实体（wms_inventory_adjust_docs）——手工建单或盘点完成自动生成，提交审批后由 Applier 落地
 */

using System.ComponentModel.DataAnnotations;

using Microsoft.EntityFrameworkCore;

using Qlx.Abstractions.Domain;

namespace Qlx.Modules.Wms.Domain.Adjust;

/// <summary>盘盈盘亏类型数值枚举。</summary>
public enum AdjustType
{
    /// <summary>盘盈（入库）。</summary>
    Gain = 1,

    /// <summary>盘亏（出库）。</summary>
    Loss = 2,
}

/// <summary>
/// 盘盈盘亏单（表 wms_inventory_adjust_docs）。单号由后端发号器生成（N-1），前端不传码；
/// 仓库/商品只存原始 Id（跨模块不做 Join，B-2 / A-4）。
/// </summary>
[Index(nameof(TenantId), nameof(AdjustNo), IsUnique = true)]
[Index(nameof(TenantId), nameof(Status))]
[Index(nameof(TenantId), nameof(WarehouseId))]
[Index(nameof(TenantId), nameof(TakeId))]
public sealed class InventoryAdjustDoc : AuditableEntity, ITenantEntity
{
    /// <summary>所属租户 Id。</summary>
    public long TenantId { get; internal set; }

    /// <summary>盘盈盘亏单号（租户内唯一，不可修改）。</summary>
    [MaxLength(32)]
    public string AdjustNo { get; internal set; } = string.Empty;

    /// <summary>来源盘点单 Id（自动生成时有值）。</summary>
    public long? TakeId { get; internal set; }

    /// <summary>类型数值枚举（1 盘盈 / 2 盘亏）。</summary>
    public int Type { get; internal set; }

    /// <summary>仓库 Id。</summary>
    public long WarehouseId { get; internal set; }

    /// <summary>盘盈盘亏原因。</summary>
    [MaxLength(256)]
    public string Reason { get; internal set; } = string.Empty;

    /// <summary>金额估算（Σ 数量 × 当前均价，业务口径原始值）。</summary>
    public decimal TotalAmount { get; internal set; }

    /// <summary>备注。</summary>
    [MaxLength(256)]
    public string? Remark { get; internal set; }

    /// <summary>单据状态。</summary>
    public AdjustDocStatus Status { get; private set; } = AdjustDocStatus.Draft;

    /// <summary>明细行。</summary>
    public List<InventoryAdjustLine> Lines { get; private set; } = [];

    /// <summary>EF 物化用无参构造。</summary>
    public InventoryAdjustDoc() { }

    /// <summary>创建盘盈盘亏单（草稿态）。</summary>
    /// <param name="tenantId">租户 Id。</param>
    /// <param name="adjustNo">单号（后端生成）。</param>
    /// <param name="takeId">来源盘点单 Id。</param>
    /// <param name="type">类型数值枚举。</param>
    /// <param name="warehouseId">仓库 Id。</param>
    /// <param name="reason">原因。</param>
    /// <param name="remark">备注。</param>
    public InventoryAdjustDoc(
        long tenantId,
        string adjustNo,
        long? takeId,
        int type,
        long warehouseId,
        string reason,
        string? remark)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(adjustNo);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(warehouseId);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        if (type is not (1 or 2))
        {
            throw new ArgumentOutOfRangeException(nameof(type), "盘盈盘亏类型必须为 1 或 2");
        }

        TenantId = tenantId;
        AdjustNo = adjustNo;
        TakeId = takeId;
        Type = type;
        WarehouseId = warehouseId;
        Reason = reason;
        Remark = remark;
    }

    /// <summary>追加明细行（明细从属单据，Id 由 EF 在保存时回填）。</summary>
    /// <param name="line">明细行。</param>
    public void AddLine(InventoryAdjustLine line)
    {
        ArgumentNullException.ThrowIfNull(line);
        Lines.Add(line);
        TotalAmount = Math.Round(
            Lines.Sum(static item => Math.Round(item.Qty * (item.UnitCost ?? 0m), 2,
                MidpointRounding.AwayFromZero)),
            2,
            MidpointRounding.AwayFromZero);
    }

    /// <summary>提交审批：草稿 → 待审批。</summary>
    public void Submit()
    {
        if (Status != AdjustDocStatus.Draft)
        {
            throw new InvalidOperationException("仅草稿状态的盘盈盘亏单可提交审批");
        }

        Status = AdjustDocStatus.PendingApproval;
    }

    /// <summary>审批通过：待审批 → 已通过。</summary>
    public void Approve()
    {
        if (Status != AdjustDocStatus.PendingApproval)
        {
            throw new InvalidOperationException("仅待审批的盘盈盘亏单可审批通过");
        }

        Status = AdjustDocStatus.Approved;
    }

    /// <summary>审批驳回：待审批 → 草稿。</summary>
    public void Reject()
    {
        if (Status != AdjustDocStatus.PendingApproval)
        {
            throw new InvalidOperationException("仅待审批的盘盈盘亏单可驳回");
        }

        Status = AdjustDocStatus.Draft;
    }
}
