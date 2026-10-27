/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-26
 * 描述：期初建账明细实体（stk_opening_items）——金额由后端按「数量 × 单价」舍入计算（V-1），
 *       明细只随草稿整体替换，不提供单独更新入口
 */

using System.ComponentModel.DataAnnotations;

using Microsoft.EntityFrameworkCore;

using Qlx.Abstractions.Domain;

namespace Qlx.Modules.Stk.Domain.Inventory;

/// <summary>
/// 期初建账明细（表 stk_opening_items）。金额 = <c>Round(数量 × 单价, 2)</c>，
/// 由后端在构造时计算，前端不得传入金额（V-1）。
/// </summary>
[Index(nameof(TenantId), nameof(OpeningBatchId))]
public sealed class OpeningItem : Entity, ITenantEntity
{
    /// <summary>所属租户 Id。</summary>
    public long TenantId { get; internal set; }

    /// <summary>所属期初批次 Id。</summary>
    public long OpeningBatchId { get; internal set; }

    /// <summary>商品 Id（原始引用）。</summary>
    public long ItemId { get; private set; }

    /// <summary>批次号（非批次商品为空）。</summary>
    [MaxLength(32)]
    public string? BatchNo { get; private set; }

    /// <summary>期初数量（4 位小数）。</summary>
    [Precision(18, 4)]
    public decimal Qty { get; private set; }

    /// <summary>期初单价（4 位小数）。</summary>
    [Precision(18, 4)]
    public decimal UnitCost { get; private set; }

    /// <summary>期初金额（2 位小数，后端计算）。</summary>
    [Precision(18, 2)]
    public decimal Amount { get; private set; }

    /// <summary>EF 物化用无参构造。</summary>
    public OpeningItem() { }

    /// <summary>新建期初明细。</summary>
    /// <param name="tenantId">租户 Id。</param>
    /// <param name="openingBatchId">期初批次 Id。</param>
    /// <param name="itemId">商品 Id。</param>
    /// <param name="batchNo">批次号。</param>
    /// <param name="qty">数量（正数）。</param>
    /// <param name="unitCost">单价（非负）。</param>
    public OpeningItem(
        long tenantId, long openingBatchId, long itemId, string? batchNo, decimal qty, decimal unitCost)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(tenantId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(openingBatchId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(itemId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(qty);
        ArgumentOutOfRangeException.ThrowIfNegative(unitCost);
        TenantId = tenantId;
        OpeningBatchId = openingBatchId;
        ItemId = itemId;
        BatchNo = batchNo;
        Qty = qty;
        UnitCost = unitCost;
        Amount = StockMath.Round(qty * unitCost, StockMath.AmountDigits);
    }
}
