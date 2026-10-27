/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-26
 * 描述：盘盈盘亏明细实体（wms_inventory_adjust_lines）——数量与成本估算快照
 */

using System.ComponentModel.DataAnnotations;

using Microsoft.EntityFrameworkCore;

using Qlx.Abstractions.Domain;

namespace Qlx.Modules.Wms.Domain.Adjust;

/// <summary>
/// 盘盈盘亏明细行（表 wms_inventory_adjust_lines）。<see cref="UnitCost"/> 为建单时成本估算快照
/// （盘盈取当前均价 → 参考成本 → 0；盘亏仅作金额估算，入队时单价恒为 0）。
/// </summary>
[Index(nameof(TenantId), nameof(DocId), nameof(LineNo), IsUnique = true)]
[Index(nameof(TenantId), nameof(ItemId))]
public sealed class InventoryAdjustLine : AuditableEntity, ITenantEntity
{
    /// <summary>所属租户 Id。</summary>
    public long TenantId { get; internal set; }

    /// <summary>盘盈盘亏单 Id。</summary>
    public long DocId { get; internal set; }

    /// <summary>行号（单内唯一）。</summary>
    public int LineNo { get; internal set; }

    /// <summary>商品 Id。</summary>
    public long ItemId { get; internal set; }

    /// <summary>批次号（启用批次商品必填）。</summary>
    [MaxLength(64)]
    public string? BatchNo { get; internal set; }

    /// <summary>调整数量（&gt;0，方向由单头类型决定）。</summary>
    public decimal Qty { get; internal set; }

    /// <summary>成本估算单价。</summary>
    public decimal? UnitCost { get; internal set; }

    /// <summary>行备注。</summary>
    [MaxLength(256)]
    public string? Remark { get; internal set; }

    /// <summary>EF 物化用无参构造。</summary>
    public InventoryAdjustLine() { }

    /// <summary>创建盘盈盘亏明细行。</summary>
    /// <param name="tenantId">租户 Id。</param>
    /// <param name="lineNo">行号。</param>
    /// <param name="itemId">商品 Id。</param>
    /// <param name="batchNo">批次号。</param>
    /// <param name="qty">调整数量（&gt;0）。</param>
    /// <param name="unitCost">成本估算单价。</param>
    /// <param name="remark">行备注。</param>
    public InventoryAdjustLine(
        long tenantId,
        int lineNo,
        long itemId,
        string? batchNo,
        decimal qty,
        decimal? unitCost,
        string? remark)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(tenantId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(lineNo);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(itemId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(qty);
        ArgumentOutOfRangeException.ThrowIfNegative(unitCost ?? 0m);
        TenantId = tenantId;
        LineNo = lineNo;
        ItemId = itemId;
        BatchNo = batchNo;
        Qty = qty;
        UnitCost = unitCost;
        Remark = remark;
    }
}
