/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-26
 * 描述：库存台账只读查询服务——台账/流水/批次扁平出参 + 成本字段按请求权限裁剪（B-3-1）；
 *       台账不缓存（A-9），不做任何展示类计算（B-2）
 */

using Microsoft.AspNetCore.Http;

using Qlx.Modules.Stk.Domain.Inventory;
using Qlx.Modules.Stk.Dtos;
using Qlx.Modules.Stk.Repositories;

namespace Qlx.Modules.Stk.Services;

/// <summary>库存台账只读查询服务（模块内契约，供只读控制器使用）。</summary>
public interface IInventoryQueryService
{
    /// <summary>台账余额扁平列表（itemId/warehouseId 任一或均为空）。</summary>
    Task<IReadOnlyList<StockBalanceView>> ListBalancesAsync(
        long? itemId, long? warehouseId, CancellationToken ct);

    /// <summary>库存流水扁平列表（按 Id 倒序，最多 take 条）。</summary>
    Task<IReadOnlyList<StockTxnView>> ListTxnsAsync(
        long? itemId, long? warehouseId, int? sourceType,
        DateTimeOffset? fromTime, DateTimeOffset? toTime, int take, CancellationToken ct);

    /// <summary>批次库存扁平列表（按失效日期升序，空值排后）。</summary>
    Task<IReadOnlyList<StockBatchView>> ListBatchesAsync(
        long itemId, long warehouseId, CancellationToken ct);
}

/// <summary>
/// 库存台账只读查询服务实现。成本字段（单价/金额）按当前请求权限裁剪，
/// 其余原始字段（数量/冻结量/批次/时间）始终下发。
/// </summary>
/// <param name="repo">Stk 租户仓储。</param>
/// <param name="httpContextAccessor">HTTP 上下文访问器（成本字段权限判定）。</param>
internal sealed class InventoryQueryService(
    IStkTenantRepository repo,
    IHttpContextAccessor httpContextAccessor) : IInventoryQueryService
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<StockBalanceView>> ListBalancesAsync(
        long? itemId, long? warehouseId, CancellationToken ct)
    {
        bool includeCost = CostFieldAccess.HasCostAccess(httpContextAccessor);
        IReadOnlyList<StockBalance> balances = await repo
            .ListStockBalancesAsync(itemId, warehouseId, ct).ConfigureAwait(false);
        return [.. balances.Select(balance => Map(balance, includeCost))];
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<StockTxnView>> ListTxnsAsync(
        long? itemId, long? warehouseId, int? sourceType,
        DateTimeOffset? fromTime, DateTimeOffset? toTime, int take, CancellationToken ct)
    {
        bool includeCost = CostFieldAccess.HasCostAccess(httpContextAccessor);
        IReadOnlyList<StockTxn> txns = await repo
            .ListStockTxnsAsync(itemId, warehouseId, sourceType, fromTime, toTime, take, ct).ConfigureAwait(false);
        return [.. txns.Select(txn => Map(txn, includeCost))];
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<StockBatchView>> ListBatchesAsync(
        long itemId, long warehouseId, CancellationToken ct)
    {
        bool includeCost = CostFieldAccess.HasCostAccess(httpContextAccessor);
        IReadOnlyList<StockBatch> batches = await repo
            .ListStockBatchesAsync(itemId, warehouseId, ct).ConfigureAwait(false);
        return [.. batches.Select(batch => Map(batch, includeCost))];
    }

    private static StockBalanceView Map(StockBalance balance, bool includeCost)
        => new(
            balance.ItemId,
            balance.WarehouseId,
            balance.QtyOnhand,
            includeCost ? balance.AvgCost : null,
            includeCost ? balance.Amount : null,
            balance.QtyLocked,
            balance.LastTxnAt);

    private static StockTxnView Map(StockTxn txn, bool includeCost)
        => new(
            txn.Id,
            txn.TxnNo,
            txn.SourceType,
            txn.SourceDocNo,
            txn.SourceLineNo,
            txn.ItemId,
            txn.WarehouseId,
            txn.BatchNo,
            txn.Direction,
            txn.Qty,
            includeCost ? txn.UnitCost : null,
            includeCost ? txn.Amount : null,
            txn.BalanceQtyAfter,
            includeCost ? txn.BalanceAmountAfter : null,
            txn.OperatorId,
            txn.OccurredAt);

    private static StockBatchView Map(StockBatch batch, bool includeCost)
        => new(
            batch.ItemId,
            batch.WarehouseId,
            batch.BatchNo,
            batch.ProduceDate,
            batch.ExpiryDate,
            batch.Qty,
            includeCost ? batch.AvgCost : null,
            includeCost ? batch.Amount : null);
}
