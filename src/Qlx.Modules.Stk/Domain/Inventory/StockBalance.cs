/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-26
 * 描述：库存台账余额实体（stk_stock_balances）——「商品 × 仓库」唯一，移动加权平均由领域方法维护，
 *       Version 为并发令牌（非 rowversion，A-8），无软删
 */

using Microsoft.EntityFrameworkCore;

using Qlx.Abstractions.Domain;

namespace Qlx.Modules.Stk.Domain.Inventory;

/// <summary>
/// 库存台账余额（表 stk_stock_balances）。台账只由库存流水驱动（spec §6），
/// 任何外部不得直接改写数量/金额，只能经 <see cref="ApplyIn"/> / <see cref="ApplyOut"/> /
/// <see cref="ApplyLockedDelta"/> 领域方法变更并递增 <see cref="Version"/>。
/// </summary>
[Index(nameof(TenantId), nameof(ItemId), nameof(WarehouseId), IsUnique = true)]
public sealed class StockBalance : Entity, ITenantEntity
{
    /// <summary>所属租户 Id。</summary>
    public long TenantId { get; private set; }

    /// <summary>商品 Id（原始引用，不联表取名称）。</summary>
    public long ItemId { get; private set; }

    /// <summary>仓库 Id（原始引用）。</summary>
    public long WarehouseId { get; private set; }

    /// <summary>现存数量（4 位小数）。</summary>
    [Precision(18, 4)]
    public decimal QtyOnhand { get; private set; }

    /// <summary>移动加权平均单价（4 位小数）。</summary>
    [Precision(18, 4)]
    public decimal AvgCost { get; private set; }

    /// <summary>库存金额（2 位小数）。</summary>
    [Precision(18, 2)]
    public decimal Amount { get; private set; }

    /// <summary>冻结量（盘点冻结/占用，4 位小数）。</summary>
    [Precision(18, 4)]
    public decimal QtyLocked { get; private set; }

    /// <summary>最近一次流水发生时间（业务时间）。</summary>
    public DateTimeOffset? LastTxnAt { get; private set; }

    /// <summary>并发令牌（每次变动 +1，A-8）。</summary>
    public long Version { get; private set; }

    /// <summary>EF 物化用无参构造。</summary>
    public StockBalance() { }

    /// <summary>为「商品 × 仓库」新建零库存台账行。</summary>
    /// <param name="tenantId">租户 Id。</param>
    /// <param name="itemId">商品 Id。</param>
    /// <param name="warehouseId">仓库 Id。</param>
    public StockBalance(long tenantId, long itemId, long warehouseId)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(tenantId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(itemId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(warehouseId);
        TenantId = tenantId;
        ItemId = itemId;
        WarehouseId = warehouseId;
    }

    /// <summary>
    /// 入库：数量累加、金额累加 <c>Round(qty * unitCost, 2)</c>，均价按 <c>Round(金额 / 数量, 4)</c> 重算。
    /// </summary>
    /// <param name="qty">入库数量（正数）。</param>
    /// <param name="unitCost">入库单价。</param>
    /// <param name="occurredAt">业务发生时间。</param>
    public void ApplyIn(decimal qty, decimal unitCost, DateTimeOffset occurredAt)
    {
        decimal newQty = StockMath.Round(QtyOnhand + qty, StockMath.QtyDigits);
        decimal newAmount = StockMath.Round(
            Amount + StockMath.Round(qty * unitCost, StockMath.AmountDigits), StockMath.AmountDigits);

        QtyOnhand = newQty;
        Amount = newAmount;
        AvgCost = newQty == 0m ? 0m : StockMath.Round(newAmount / newQty, StockMath.CostDigits);
        LastTxnAt = occurredAt;
        IncreaseVersion();
    }

    /// <summary>
    /// 出库：按当前移动加权平均价扣减金额，数量扣减；数量归零时金额同步归零，均价保留原位。
    /// </summary>
    /// <param name="qty">出库数量（正数，调用方须保证不超可用量）。</param>
    /// <param name="occurredAt">业务发生时间。</param>
    /// <returns>本次出库采用的单价（变动前的移动加权平均价）。</returns>
    public decimal ApplyOut(decimal qty, DateTimeOffset occurredAt)
    {
        decimal unitCost = AvgCost;
        decimal newQty = StockMath.Round(QtyOnhand - qty, StockMath.QtyDigits);
        decimal newAmount = newQty == 0m
            ? 0m
            : StockMath.Round(
                Amount - StockMath.Round(qty * unitCost, StockMath.AmountDigits), StockMath.AmountDigits);

        QtyOnhand = newQty;
        Amount = newAmount;
        LastTxnAt = occurredAt;
        IncreaseVersion();
        return unitCost;
    }

    /// <summary>冻结量增减（正数冻结、负数释放）。调用方须保证冻结量不为负。</summary>
    /// <param name="deltaQty">冻结量增量。</param>
    public void ApplyLockedDelta(decimal deltaQty)
    {
        QtyLocked = StockMath.Round(QtyLocked + deltaQty, StockMath.QtyDigits);
        IncreaseVersion();
    }

    /// <summary>递增并发令牌（每次台账变动必须调用）。</summary>
    public void IncreaseVersion() => Version += 1;
}
