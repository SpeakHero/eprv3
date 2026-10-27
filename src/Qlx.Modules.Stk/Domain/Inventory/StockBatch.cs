/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-26
 * 描述：批次库存实体（stk_stock_batches）——「商品 × 仓库 × 批次」唯一，
 *       入库累计、出库按指定批次扣减，与余额同事务变动（spec §6-5）
 */

using System.ComponentModel.DataAnnotations;

using Microsoft.EntityFrameworkCore;

using Qlx.Abstractions.Domain;

namespace Qlx.Modules.Stk.Domain.Inventory;

/// <summary>
/// 批次库存（表 stk_stock_batches）。启用批次管理的商品必须按批次登记库存，
/// 批次与台账余额在同一事务内变动；批次均价独立按移动加权平均维护。
/// </summary>
[Index(nameof(TenantId), nameof(ItemId), nameof(WarehouseId), nameof(BatchNo), IsUnique = true)]
public sealed class StockBatch : Entity, ITenantEntity
{
    /// <summary>所属租户 Id。</summary>
    public long TenantId { get; private set; }

    /// <summary>商品 Id。</summary>
    public long ItemId { get; private set; }

    /// <summary>仓库 Id。</summary>
    public long WarehouseId { get; private set; }

    /// <summary>批次号（同一商品同一仓库内唯一）。</summary>
    [MaxLength(32)]
    public string BatchNo { get; private set; } = string.Empty;

    /// <summary>生产日期。</summary>
    public DateTimeOffset? ProduceDate { get; private set; }

    /// <summary>失效日期（批次列表按此升序，FEFO）。</summary>
    public DateTimeOffset? ExpiryDate { get; private set; }

    /// <summary>批次数量（4 位小数）。</summary>
    [Precision(18, 4)]
    public decimal Qty { get; private set; }

    /// <summary>批次移动加权平均单价（4 位小数）。</summary>
    [Precision(18, 4)]
    public decimal AvgCost { get; private set; }

    /// <summary>批次金额（2 位小数）。</summary>
    [Precision(18, 2)]
    public decimal Amount { get; private set; }

    /// <summary>EF 物化用无参构造。</summary>
    public StockBatch() { }

    /// <summary>新建批次库存行（零数量）。</summary>
    /// <param name="tenantId">租户 Id。</param>
    /// <param name="itemId">商品 Id。</param>
    /// <param name="warehouseId">仓库 Id。</param>
    /// <param name="batchNo">批次号。</param>
    public StockBatch(long tenantId, long itemId, long warehouseId, string batchNo)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(tenantId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(itemId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(warehouseId);
        ArgumentException.ThrowIfNullOrWhiteSpace(batchNo);
        TenantId = tenantId;
        ItemId = itemId;
        WarehouseId = warehouseId;
        BatchNo = batchNo;
    }

    /// <summary>记录批次效期信息（不改变数量金额）。</summary>
    /// <param name="produceDate">生产日期。</param>
    /// <param name="expiryDate">失效日期。</param>
    public void SetDates(DateTimeOffset? produceDate, DateTimeOffset? expiryDate)
    {
        ProduceDate = produceDate;
        ExpiryDate = expiryDate;
    }

    /// <summary>入库：数量与金额累加，均价按移动加权平均重算。</summary>
    /// <param name="qty">入库数量（正数）。</param>
    /// <param name="unitCost">入库单价。</param>
    public void ApplyIn(decimal qty, decimal unitCost)
    {
        decimal newQty = StockMath.Round(Qty + qty, StockMath.QtyDigits);
        decimal newAmount = StockMath.Round(
            Amount + StockMath.Round(qty * unitCost, StockMath.AmountDigits), StockMath.AmountDigits);

        Qty = newQty;
        Amount = newAmount;
        AvgCost = newQty == 0m ? 0m : StockMath.Round(newAmount / newQty, StockMath.CostDigits);
    }

    /// <summary>出库：按批次自身均价扣减金额，数量归零时金额归零。</summary>
    /// <param name="qty">出库数量（正数，调用方须保证不超批次数量）。</param>
    public void ApplyOut(decimal qty)
    {
        decimal newQty = StockMath.Round(Qty - qty, StockMath.QtyDigits);
        decimal newAmount = newQty == 0m
            ? 0m
            : StockMath.Round(
                Amount - StockMath.Round(qty * AvgCost, StockMath.AmountDigits), StockMath.AmountDigits);

        Qty = newQty;
        Amount = newAmount;
    }
}
