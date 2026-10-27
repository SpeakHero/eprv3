/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-26
 * 描述：在途库存行实体（wms_in_transit_stocks）——发货登记生成，收货关闭；明细行唯一
 */

using System.ComponentModel.DataAnnotations;

using Microsoft.EntityFrameworkCore;

using Qlx.Abstractions.Domain;

namespace Qlx.Modules.Wms.Domain.Transfer;

/// <summary>
/// 在途库存行（表 wms_in_transit_stocks）。同一调拨明细行仅一条在途记录（租户内唯一索引）。
/// 超期判定由前端按 <see cref="ExpectArrive"/> 计算（B-2）。
/// </summary>
[Index(nameof(TenantId), nameof(TransferLineId), IsUnique = true)]
[Index(nameof(TenantId), nameof(Status))]
[Index(nameof(TenantId), nameof(ItemId))]
public sealed class InTransitStock : AuditableEntity, ITenantEntity
{
    /// <summary>所属租户 Id。</summary>
    public long TenantId { get; internal set; }

    /// <summary>调拨单 Id。</summary>
    public long TransferId { get; internal set; }

    /// <summary>调拨明细 Id。</summary>
    public long TransferLineId { get; internal set; }

    /// <summary>调拨单号（冗余存储，便于在途列表扁平下发）。</summary>
    [MaxLength(32)]
    public string TransferNo { get; internal set; } = string.Empty;

    /// <summary>商品 Id。</summary>
    public long ItemId { get; internal set; }

    /// <summary>调出仓库 Id。</summary>
    public long FromWarehouseId { get; internal set; }

    /// <summary>调入仓库 Id。</summary>
    public long ToWarehouseId { get; internal set; }

    /// <summary>批次号。</summary>
    [MaxLength(64)]
    public string? BatchNo { get; internal set; }

    /// <summary>在途数量（发货数量）。</summary>
    public decimal Qty { get; internal set; }

    /// <summary>成本快照（发货时调出仓均价）。</summary>
    public decimal UnitCost { get; internal set; }

    /// <summary>发货时间。</summary>
    public DateTimeOffset ShippedAt { get; internal set; }

    /// <summary>预计到达时间。</summary>
    public DateTimeOffset ExpectArrive { get; internal set; }

    /// <summary>在途状态。</summary>
    public InTransitStatus Status { get; private set; } = InTransitStatus.InTransit;

    /// <summary>EF 物化用无参构造。</summary>
    public InTransitStock() { }

    /// <summary>创建在途库存行（发货登记）。</summary>
    /// <param name="tenantId">租户 Id。</param>
    /// <param name="transferId">调拨单 Id。</param>
    /// <param name="transferLineId">调拨明细 Id。</param>
    /// <param name="transferNo">调拨单号。</param>
    /// <param name="itemId">商品 Id。</param>
    /// <param name="fromWarehouseId">调出仓库 Id。</param>
    /// <param name="toWarehouseId">调入仓库 Id。</param>
    /// <param name="batchNo">批次号。</param>
    /// <param name="qty">在途数量（&gt;0）。</param>
    /// <param name="unitCost">成本快照。</param>
    /// <param name="shippedAt">发货时间。</param>
    /// <param name="expectArrive">预计到达时间。</param>
    public InTransitStock(
        long tenantId,
        long transferId,
        long transferLineId,
        string transferNo,
        long itemId,
        long fromWarehouseId,
        long toWarehouseId,
        string? batchNo,
        decimal qty,
        decimal unitCost,
        DateTimeOffset shippedAt,
        DateTimeOffset expectArrive)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(tenantId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(transferId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(transferLineId);
        ArgumentException.ThrowIfNullOrWhiteSpace(transferNo);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(itemId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(fromWarehouseId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(toWarehouseId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(qty);
        ArgumentOutOfRangeException.ThrowIfNegative(unitCost);
        if (expectArrive <= shippedAt)
        {
            throw new ArgumentOutOfRangeException(nameof(expectArrive), "预计到达时间必须晚于发货时间");
        }

        TenantId = tenantId;
        TransferId = transferId;
        TransferLineId = transferLineId;
        TransferNo = transferNo;
        ItemId = itemId;
        FromWarehouseId = fromWarehouseId;
        ToWarehouseId = toWarehouseId;
        BatchNo = batchNo;
        Qty = qty;
        UnitCost = unitCost;
        ShippedAt = shippedAt;
        ExpectArrive = expectArrive;
    }

    /// <summary>关闭在途（收货完成）。</summary>
    public void Close()
    {
        if (Status != InTransitStatus.InTransit)
        {
            throw new InvalidOperationException("仅在途行可关闭");
        }

        Status = InTransitStatus.Closed;
    }
}
