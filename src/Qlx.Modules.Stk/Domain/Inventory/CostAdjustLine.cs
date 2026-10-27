/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-26
 * 描述：成本调整明细实体（stk_cost_adjust_lines）——旧均价取台账当前均价、差额由后端计算（V-1），
 *       明细只随草稿整体替换，不提供单独更新入口
 */

using Microsoft.EntityFrameworkCore;

using Qlx.Abstractions.Domain;

namespace Qlx.Modules.Stk.Domain.Inventory;

/// <summary>
/// 成本调整明细（表 stk_cost_adjust_lines）。旧均价取台账当前平均单价，
/// 差额 = <c>Round((新均价 − 旧均价) × 台账数量, 2)</c>，由后端计算，前端不得传入（V-1）。
/// </summary>
[Index(nameof(TenantId), nameof(CostAdjustId))]
public sealed class CostAdjustLine : Entity, ITenantEntity
{
    /// <summary>所属租户 Id。</summary>
    public long TenantId { get; internal set; }

    /// <summary>所属成本调整单 Id。</summary>
    public long CostAdjustId { get; internal set; }

    /// <summary>商品 Id（原始引用）。</summary>
    public long ItemId { get; private set; }

    /// <summary>仓库 Id（原始引用）。</summary>
    public long WarehouseId { get; private set; }

    /// <summary>旧移动加权平均单价（台账当前值，4 位小数）。</summary>
    [Precision(18, 4)]
    public decimal OldAvgCost { get; private set; }

    /// <summary>新移动加权平均单价（4 位小数）。</summary>
    [Precision(18, 4)]
    public decimal NewAvgCost { get; private set; }

    /// <summary>差额金额（2 位小数，后端计算）。</summary>
    [Precision(18, 2)]
    public decimal DiffAmount { get; private set; }

    /// <summary>EF 物化用无参构造。</summary>
    public CostAdjustLine() { }

    /// <summary>新建成本调整明细。</summary>
    /// <param name="tenantId">租户 Id。</param>
    /// <param name="costAdjustId">成本调整单 Id。</param>
    /// <param name="itemId">商品 Id。</param>
    /// <param name="warehouseId">仓库 Id。</param>
    /// <param name="oldAvgCost">旧均价（台账当前值）。</param>
    /// <param name="newAvgCost">新均价。</param>
    /// <param name="qty">台账数量（用于计算差额）。</param>
    public CostAdjustLine(
        long tenantId,
        long costAdjustId,
        long itemId,
        long warehouseId,
        decimal oldAvgCost,
        decimal newAvgCost,
        decimal qty)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(tenantId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(costAdjustId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(itemId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(warehouseId);
        ArgumentOutOfRangeException.ThrowIfNegative(oldAvgCost);
        ArgumentOutOfRangeException.ThrowIfNegative(newAvgCost);
        TenantId = tenantId;
        CostAdjustId = costAdjustId;
        ItemId = itemId;
        WarehouseId = warehouseId;
        OldAvgCost = oldAvgCost;
        NewAvgCost = newAvgCost;
        DiffAmount = StockMath.Round((newAvgCost - oldAvgCost) * qty, StockMath.AmountDigits);
    }
}
