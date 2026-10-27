/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-26
 * 描述：调拨明细实体（wms_stock_transfer_lines）——调拨/发货/收货数量与成本快照、差异说明
 */

using System.ComponentModel.DataAnnotations;

using Microsoft.EntityFrameworkCore;

using Qlx.Abstractions.Domain;

namespace Qlx.Modules.Wms.Domain.Transfer;

/// <summary>调拨明细（表 wms_stock_transfer_lines）。</summary>
[Index(nameof(TenantId), nameof(TransferId), nameof(LineNo), IsUnique = true)]
[Index(nameof(TenantId), nameof(ItemId))]
public sealed class StockTransferLine : AuditableEntity, ITenantEntity
{
    /// <summary>所属租户 Id。</summary>
    public long TenantId { get; internal set; }

    /// <summary>所属调拨单 Id（由 EF 关系回填）。</summary>
    public long TransferId { get; internal set; }

    /// <summary>行号（同单内唯一）。</summary>
    public int LineNo { get; internal set; }

    /// <summary>商品 Id。</summary>
    public long ItemId { get; internal set; }

    /// <summary>调拨数量。</summary>
    public decimal Qty { get; internal set; }

    /// <summary>发货数量。</summary>
    public decimal ShipQty { get; internal set; }

    /// <summary>收货数量。</summary>
    public decimal ReceiveQty { get; internal set; }

    /// <summary>批次号（启用批次商品必填）。</summary>
    [MaxLength(64)]
    public string? BatchNo { get; internal set; }

    /// <summary>发货成本快照（调出仓当时均价，A-13）。</summary>
    public decimal? UnitCost { get; internal set; }

    /// <summary>收货差异说明（收货数量少于发货数量时必填）。</summary>
    [MaxLength(256)]
    public string? DiffNote { get; internal set; }

    /// <summary>EF 物化用无参构造。</summary>
    public StockTransferLine() { }

    /// <summary>创建明细行。</summary>
    /// <param name="tenantId">租户 Id。</param>
    /// <param name="lineNo">行号。</param>
    /// <param name="itemId">商品 Id。</param>
    /// <param name="qty">调拨数量（&gt;0）。</param>
    /// <param name="batchNo">批次号。</param>
    public StockTransferLine(long tenantId, int lineNo, long itemId, decimal qty, string? batchNo)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(tenantId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(lineNo);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(itemId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(qty);
        TenantId = tenantId;
        LineNo = lineNo;
        ItemId = itemId;
        Qty = qty;
        BatchNo = batchNo;
    }

    /// <summary>登记发货结果与成本快照。</summary>
    /// <param name="shipQty">发货数量（&gt;=0 且 ≤ 调拨数量）。</param>
    /// <param name="unitCost">成本快照。</param>
    public void RecordShip(decimal shipQty, decimal unitCost)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(shipQty);
        if (shipQty > Qty)
        {
            throw new ArgumentOutOfRangeException(nameof(shipQty), "发货数量不能超过调拨数量");
        }

        ArgumentOutOfRangeException.ThrowIfNegative(unitCost);
        ShipQty = shipQty;
        UnitCost = unitCost;
    }

    /// <summary>登记收货结果与差异说明。</summary>
    /// <param name="receiveQty">收货数量（&gt;=0 且 ≤ 发货数量）。</param>
    /// <param name="diffNote">差异说明（收货数量少于发货数量时必填）。</param>
    public void RecordReceive(decimal receiveQty, string? diffNote)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(receiveQty);
        if (receiveQty > ShipQty)
        {
            throw new ArgumentOutOfRangeException(nameof(receiveQty), "收货数量不能超过发货数量");
        }

        ReceiveQty = receiveQty;
        DiffNote = diffNote;
    }
}
