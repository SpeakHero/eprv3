/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-26
 * 描述：库存预警扫描只读查询服务（IStockAlertScanQueryService 实现）——台账/批次按 Id 游标分页，
 *       仅下发预警判定所需原始字段，不做成本裁剪与展示映射（B-2）
 */

using Qlx.Abstractions.Exceptions;
using Qlx.Abstractions.Queries;
using Qlx.Abstractions.Tenancy;
using Qlx.Modules.Stk.Domain.Inventory;
using Qlx.Modules.Stk.Repositories;

namespace Qlx.Modules.Stk.Services;

/// <summary>
/// 库存预警扫描只读查询服务。分页按主键 Id 升序（雪花 Id 单调递增），
/// 单批行数由调用方控制上限，禁止一次性全量加载（spec §14.5）。
/// </summary>
/// <param name="tenantCtx">当前租户上下文。</param>
/// <param name="repo">Stk 租户仓储。</param>
internal sealed class StockAlertScanQueryService(
    ITenantContext tenantCtx,
    IStkTenantRepository repo) : IStockAlertScanQueryService
{
    private const int MaxPageSize = 500;

    private readonly ITenantContext _tenantCtx = tenantCtx;
    private readonly IStkTenantRepository _repo = repo;

    private long TenantId => _tenantCtx.TenantId
        ?? throw new ForbiddenException("未识别到租户上下文");

    /// <inheritdoc />
    public async Task<IReadOnlyList<StockBalanceScanRow>> ListBalancePageAsync(
        long afterId, int limit, CancellationToken cancellationToken)
    {
        _ = TenantId;
        int pageSize = NormalizePageSize(limit);
        IReadOnlyList<StockBalance> rows = await _repo
            .ListStockBalancePageAsync(afterId, pageSize, cancellationToken).ConfigureAwait(false);
        return
        [
            .. rows.Select(static row => new StockBalanceScanRow(
                row.Id, row.ItemId, row.WarehouseId, row.QtyOnhand, row.LastTxnAt)),
        ];
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<StockBatchScanRow>> ListBatchPageAsync(
        long afterId, int limit, CancellationToken cancellationToken)
    {
        _ = TenantId;
        int pageSize = NormalizePageSize(limit);
        IReadOnlyList<StockBatch> rows = await _repo
            .ListStockBatchPageAsync(afterId, pageSize, cancellationToken).ConfigureAwait(false);
        return
        [
            .. rows.Select(static row => new StockBatchScanRow(
                row.Id, row.ItemId, row.WarehouseId, row.BatchNo, row.ExpiryDate, row.Qty)),
        ];
    }

    private static int NormalizePageSize(int limit)
        => limit is > 0 and <= MaxPageSize ? limit : MaxPageSize;
}
