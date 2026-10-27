/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-26
 * 描述：跨模块库存只读查询服务（IStockQueryService 实现）——台账/批次扁平出参 + 批次 FEFO 排序 +
 *       成本字段按请求权限裁剪（B-3-1）；台账/批次不缓存（A-9），不做任何展示类计算（B-2）
 */

using Microsoft.AspNetCore.Http;

using Qlx.Abstractions.Exceptions;
using Qlx.Abstractions.Queries;
using Qlx.Abstractions.Tenancy;
using Qlx.Modules.Stk.Domain.Inventory;
using Qlx.Modules.Stk.Repositories;

namespace Qlx.Modules.Stk.Services;

/// <summary>
/// 跨模块库存只读查询服务。仅依赖当前租户上下文、Stk 租户仓储与 HTTP 上下文（成本字段权限判定），
/// 不做跨模块查询、不缓存、不拼装展示字符串；成本字段（均价/金额）无 <c>stk.cost.field</c> 权限时
/// 一律置 <see langword="null"/>，数量与冻结量始终下发（B-3-1）。
/// </summary>
/// <param name="tenantCtx">当前租户上下文。</param>
/// <param name="repo">Stk 租户仓储。</param>
/// <param name="httpContextAccessor">HTTP 上下文访问器（成本字段权限判定）。</param>
internal sealed class StockQueryService(
    ITenantContext tenantCtx,
    IStkTenantRepository repo,
    IHttpContextAccessor httpContextAccessor) : IStockQueryService
{
    private readonly ITenantContext _tenantCtx = tenantCtx;
    private readonly IStkTenantRepository _repo = repo;
    private readonly IHttpContextAccessor _httpContextAccessor = httpContextAccessor;

    private long TenantId => _tenantCtx.TenantId
        ?? throw new ForbiddenException("未识别到租户上下文");

    /// <inheritdoc />
    public async Task<StockBalanceRow?> FindBalanceAsync(
        long itemId, long warehouseId, CancellationToken cancellationToken)
    {
        _ = TenantId;
        bool includeCost = CostFieldAccess.HasCostAccess(_httpContextAccessor);
        StockBalance? balance = await _repo
            .FindStockBalanceAsync(itemId, warehouseId, cancellationToken).ConfigureAwait(false);
        return balance is null ? null : Map(balance, includeCost);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<StockBalanceRow>> ListBalancesByItemAsync(
        long itemId, CancellationToken cancellationToken)
    {
        _ = TenantId;
        bool includeCost = CostFieldAccess.HasCostAccess(_httpContextAccessor);
        IReadOnlyList<StockBalance> balances = await _repo
            .ListStockBalancesAsync(itemId, warehouseId: null, cancellationToken).ConfigureAwait(false);
        return [.. balances.Select(balance => Map(balance, includeCost))];
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<StockBalanceRow>> ListBalancesByWarehouseAsync(
        long warehouseId, CancellationToken cancellationToken)
    {
        _ = TenantId;
        bool includeCost = CostFieldAccess.HasCostAccess(_httpContextAccessor);
        IReadOnlyList<StockBalance> balances = await _repo
            .ListStockBalancesAsync(itemId: null, warehouseId, cancellationToken).ConfigureAwait(false);
        return [.. balances.Select(balance => Map(balance, includeCost))];
    }

    /// <inheritdoc />
    /// <remarks>
    /// FEFO 属于拣货业务规则（B-3-2），排序在内存完成（SQLite 不支持 <see cref="DateTimeOffset"/> 排序下推）：
    /// 失效日期升序、无失效日期排最后、同日期按批次号升序。
    /// </remarks>
    public async Task<IReadOnlyList<StockBatchRow>> ListBatchesAsync(
        long itemId, long warehouseId, CancellationToken cancellationToken)
    {
        _ = TenantId;
        bool includeCost = CostFieldAccess.HasCostAccess(_httpContextAccessor);
        IReadOnlyList<StockBatch> batches = await _repo
            .ListStockBatchesAsync(itemId, warehouseId, cancellationToken).ConfigureAwait(false);
        return
        [
            .. batches
                .OrderBy(static batch => batch.ExpiryDate is null)
                .ThenBy(static batch => batch.ExpiryDate)
                .ThenBy(static batch => batch.BatchNo, StringComparer.Ordinal)
                .Select(batch => Map(batch, includeCost)),
        ];
    }

    /// <inheritdoc />
    public async Task<bool> HasStockInWarehouseAsync(long warehouseId, CancellationToken cancellationToken)
    {
        _ = TenantId;
        return await _repo.HasStockInWarehouseAsync(warehouseId, cancellationToken).ConfigureAwait(false);
    }

    private static StockBalanceRow Map(StockBalance balance, bool includeCost)
        => new(
            balance.ItemId,
            balance.WarehouseId,
            balance.QtyOnhand,
            includeCost ? balance.AvgCost : null,
            includeCost ? balance.Amount : null,
            balance.QtyLocked,
            balance.LastTxnAt);

    private static StockBatchRow Map(StockBatch batch, bool includeCost)
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
