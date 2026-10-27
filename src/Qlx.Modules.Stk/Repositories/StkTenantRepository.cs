/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-26
 * 描述：Stk 租户域仓储实现——基于 StkTenantDbContext，读路径 AsNoTracking，写路径跟踪态；
 *       台账/流水/批次读取禁止缓存（A-9）
 */

using Microsoft.EntityFrameworkCore;

using Qlx.Abstractions.Stock;
using Qlx.Modules.Stk.Domain.Inventory;
using Qlx.Modules.Stk.Domain.Item;
using Qlx.Modules.Stk.Domain.Numbering;

namespace Qlx.Modules.Stk.Repositories;

/// <summary>Stk 租户域仓储实现。</summary>
/// <param name="db">当前请求的 Stk 租户 DbContext。</param>
internal sealed class StkTenantRepository(StkTenantDbContext db) : IStkTenantRepository
{
    private readonly StkTenantDbContext _db = db;

    /// <inheritdoc />
    public async Task<IReadOnlyList<Item>> ListItemsAsync(CancellationToken ct)
        => await _db.Items.AsNoTracking().OrderBy(i => i.ItemCode).ToListAsync(ct).ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<IReadOnlyList<Item>> ListItemsByIdsAsync(
        IReadOnlyList<long> ids, CancellationToken ct)
        => ids.Count == 0
            ? []
            : await _db.Items.AsNoTracking()
                .Where(i => ids.Contains(i.Id))
                .ToListAsync(ct).ConfigureAwait(false);

    /// <inheritdoc />
    public Task<Item?> FindItemAsync(long id, CancellationToken ct)
        => _db.Items.FirstOrDefaultAsync(i => i.Id == id, ct);

    /// <inheritdoc />
    public Task<bool> IsItemCodeExistsAsync(string itemCode, CancellationToken ct)
        => _db.Items.AsNoTracking().AnyAsync(i => i.ItemCode == itemCode, ct);

    /// <inheritdoc />
    public Task<bool> IsBarcodeExistsAsync(string barcode, long? excludeId, CancellationToken ct)
        => _db.Items.AsNoTracking()
            .AnyAsync(i => i.Barcode == barcode && (excludeId == null || i.Id != excludeId.Value), ct);

    /// <inheritdoc />
    public async Task AddItemAsync(Item item, CancellationToken ct)
        => _ = await _db.Items.AddAsync(item, ct).ConfigureAwait(false);

    /// <inheritdoc />
    public Task UpdateItemAsync(Item item, CancellationToken ct)
    {
        _ = _db.Items.Update(item);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ItemPriceLog>> ListPriceLogsAsync(long itemId, CancellationToken ct)
    {
        // SQLite 不支持 DateTimeOffset 排序下推；按商品取出后内存排序（单商品日志量有限）
        List<ItemPriceLog> logs = await _db.PriceLogs.AsNoTracking()
            .Where(l => l.ItemId == itemId)
            .ToListAsync(ct).ConfigureAwait(false);
        return [.. logs.OrderByDescending(l => l.CreatedAt).ThenByDescending(l => l.Id)];
    }

    /// <inheritdoc />
    public async Task AddPriceLogAsync(ItemPriceLog log, CancellationToken ct)
        => _ = await _db.PriceLogs.AddAsync(log, ct).ConfigureAwait(false);

    /// <inheritdoc />
    public Task<StkApprovalRequest?> FindApprovalRequestAsync(long id, CancellationToken ct)
        => _db.ApprovalRequests.FirstOrDefaultAsync(r => r.Id == id, ct);

    /// <inheritdoc />
    public Task<StkApprovalRequest?> FindApprovalRequestByInstanceAsync(string instanceId, CancellationToken ct)
        => _db.ApprovalRequests.FirstOrDefaultAsync(r => r.ApprovalInstanceId == instanceId, ct);

    /// <inheritdoc />
    public async Task AddApprovalRequestAsync(StkApprovalRequest request, CancellationToken ct)
        => _ = await _db.ApprovalRequests.AddAsync(request, ct).ConfigureAwait(false);

    /// <inheritdoc />
    public Task UpdateApprovalRequestAsync(StkApprovalRequest request, CancellationToken ct)
    {
        _ = _db.ApprovalRequests.Update(request);
        return Task.CompletedTask;
    }

    // ============ 库存台账 ============

    /// <inheritdoc />
    public Task<StockBalance?> FindStockBalanceAsync(long itemId, long warehouseId, CancellationToken ct)
        => _db.StockBalances.AsNoTracking()
            .FirstOrDefaultAsync(b => b.ItemId == itemId && b.WarehouseId == warehouseId, ct);

    /// <inheritdoc />
    public Task<StockBalance?> FindStockBalanceTrackedAsync(long itemId, long warehouseId, CancellationToken ct)
        => _db.StockBalances
            .FirstOrDefaultAsync(b => b.ItemId == itemId && b.WarehouseId == warehouseId, ct);

    /// <inheritdoc />
    public async Task<IReadOnlyList<StockBalance>> ListStockBalancesAsync(
        long? itemId, long? warehouseId, CancellationToken ct)
        => await _db.StockBalances.AsNoTracking()
            .Where(b => (itemId == null || b.ItemId == itemId.Value)
                && (warehouseId == null || b.WarehouseId == warehouseId.Value))
            .OrderBy(b => b.ItemId)
            .ThenBy(b => b.WarehouseId)
            .ToListAsync(ct).ConfigureAwait(false);

    /// <inheritdoc />
    public async Task AddStockBalanceAsync(StockBalance balance, CancellationToken ct)
        => _ = await _db.StockBalances.AddAsync(balance, ct).ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<bool> HasStockInWarehouseAsync(long warehouseId, CancellationToken ct)
    {
        // SQLite 无法在 SQL 中比较 decimal；投影后内存判定（仅取数量列，仓库维度行数有限）
        List<decimal> quantities = await _db.StockBalances.AsNoTracking()
            .Where(b => b.WarehouseId == warehouseId)
            .Select(b => b.QtyOnhand)
            .ToListAsync(ct).ConfigureAwait(false);
        return quantities.Exists(static qty => qty > 0m);
    }

    // ============ 库存流水（只增不改不删）============

    /// <inheritdoc />
    public async Task<IReadOnlyList<StockTxn>> ListStockTxnsAsync(
        long? itemId, long? warehouseId, int? sourceType,
        DateTimeOffset? from, DateTimeOffset? to, int take, CancellationToken ct)
    {
        List<StockTxn> rows = await _db.StockTxns.AsNoTracking()
            .Where(t => (itemId == null || t.ItemId == itemId.Value)
                && (warehouseId == null || t.WarehouseId == warehouseId.Value)
                && (sourceType == null || t.SourceType == sourceType.Value))
            .OrderByDescending(t => t.Id)
            .ToListAsync(ct).ConfigureAwait(false);

        // SQLite 无法翻译 DateTimeOffset 比较，时间区间在内存过滤（明细行已按商品/仓库/来源类型收敛）
        IEnumerable<StockTxn> filtered = rows;
        if (from is not null)
        {
            filtered = filtered.Where(t => t.OccurredAt >= from.Value);
        }

        if (to is not null)
        {
            filtered = filtered.Where(t => t.OccurredAt <= to.Value);
        }

        return [.. filtered.Take(take)];
    }

    /// <inheritdoc />
    public Task<StockTxn?> FindStockTxnByIdempotencyAsync(
        StockSourceType sourceType, string docNo, int lineNo, long itemId, long warehouseId,
        string? batchNo, StockDirection direction, CancellationToken ct)
    {
        int sourceTypeValue = (int)sourceType;
        int directionValue = (int)direction;
        return _db.StockTxns.AsNoTracking()
            .FirstOrDefaultAsync(
                t => t.SourceType == sourceTypeValue
                    && t.SourceDocNo == docNo
                    && t.SourceLineNo == lineNo
                    && t.ItemId == itemId
                    && t.WarehouseId == warehouseId
                    && t.BatchNo == batchNo
                    && t.Direction == directionValue,
                ct);
    }

    /// <inheritdoc />
    public async Task AddStockTxnAsync(StockTxn txn, CancellationToken ct)
        => _ = await _db.StockTxns.AddAsync(txn, ct).ConfigureAwait(false);

    // ============ 批次库存 ============

    /// <inheritdoc />
    public Task<StockBatch?> FindStockBatchTrackedAsync(
        long itemId, long warehouseId, string batchNo, CancellationToken ct)
        => _db.StockBatches
            .FirstOrDefaultAsync(
                b => b.ItemId == itemId && b.WarehouseId == warehouseId && b.BatchNo == batchNo, ct);

    /// <inheritdoc />
    public async Task<IReadOnlyList<StockBatch>> ListStockBatchesAsync(
        long itemId, long warehouseId, CancellationToken ct)
    {
        // SQLite 不支持 DateTimeOffset 排序下推；取出后内存按失效日期升序（空值排后，FEFO 建议）
        List<StockBatch> batches = await _db.StockBatches.AsNoTracking()
            .Where(b => b.ItemId == itemId && b.WarehouseId == warehouseId)
            .ToListAsync(ct).ConfigureAwait(false);

        return [.. batches
            .OrderBy(b => b.ExpiryDate is null)
            .ThenBy(b => b.ExpiryDate)
            .ThenBy(b => b.BatchNo, StringComparer.Ordinal)];
    }

    /// <inheritdoc />
    public async Task AddStockBatchAsync(StockBatch batch, CancellationToken ct)
        => _ = await _db.StockBatches.AddAsync(batch, ct).ConfigureAwait(false);

    // ============ 预警扫描分页 ============

    /// <inheritdoc />
    public async Task<IReadOnlyList<StockBalance>> ListStockBalancePageAsync(
        long afterId, int limit, CancellationToken ct)
        => await _db.StockBalances.AsNoTracking()
            .Where(b => b.Id > afterId)
            // SQLite 不支持小数比较下推（金额类字段存 TEXT），游标仅用雪花 Id，判定在调用方内存完成
            .OrderBy(b => b.Id)
            .Take(limit)
            .ToListAsync(ct).ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<IReadOnlyList<StockBatch>> ListStockBatchPageAsync(
        long afterId, int limit, CancellationToken ct)
        => await _db.StockBatches.AsNoTracking()
            .Where(b => b.Id > afterId)
            .OrderBy(b => b.Id)
            .Take(limit)
            .ToListAsync(ct).ConfigureAwait(false);

    // ============ 期初建账 ============

    /// <inheritdoc />
    public async Task<IReadOnlyList<OpeningBatch>> ListOpeningBatchesAsync(
        OpeningBatchStatus? status, long? warehouseId, CancellationToken ct)
        => await _db.OpeningBatches.AsNoTracking()
            .Where(b => (status == null || b.Status == status.Value)
                && (warehouseId == null || b.WarehouseId == warehouseId.Value))
            .OrderByDescending(b => b.Id)
            .ToListAsync(ct).ConfigureAwait(false);

    /// <inheritdoc />
    public Task<OpeningBatch?> FindOpeningBatchAsync(long id, CancellationToken ct)
        => _db.OpeningBatches.AsNoTracking().FirstOrDefaultAsync(b => b.Id == id, ct);

    /// <inheritdoc />
    public Task<OpeningBatch?> FindOpeningBatchTrackedAsync(long id, CancellationToken ct)
        => _db.OpeningBatches.FirstOrDefaultAsync(b => b.Id == id, ct);

    /// <inheritdoc />
    public async Task AddOpeningBatchAsync(OpeningBatch batch, CancellationToken ct)
        => _ = await _db.OpeningBatches.AddAsync(batch, ct).ConfigureAwait(false);

    /// <inheritdoc />
    public Task RemoveOpeningBatchAsync(OpeningBatch batch, CancellationToken ct)
    {
        _ = _db.OpeningBatches.Remove(batch);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<bool> HasPostedOpeningAsync(long warehouseId, CancellationToken ct)
        => _db.OpeningBatches.AsNoTracking()
            .AnyAsync(b => b.WarehouseId == warehouseId && b.Status == OpeningBatchStatus.Posted, ct);

    /// <inheritdoc />
    public async Task<IReadOnlyList<OpeningBatch>> ListPostedOpeningBatchesAsync(CancellationToken ct)
        => await _db.OpeningBatches.AsNoTracking()
            .Where(b => b.Status == OpeningBatchStatus.Posted)
            .ToListAsync(ct).ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<IReadOnlyList<OpeningItem>> ListOpeningItemsAsync(
        long openingBatchId, CancellationToken ct)
        => await _db.OpeningItems.AsNoTracking()
            .Where(i => i.OpeningBatchId == openingBatchId)
            .OrderBy(i => i.Id)
            .ToListAsync(ct).ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<IReadOnlyList<OpeningItem>> ListOpeningItemsByBatchIdsAsync(
        IReadOnlyList<long> openingBatchIds, CancellationToken ct)
    {
        if (openingBatchIds.Count == 0)
        {
            return [];
        }

        return await _db.OpeningItems.AsNoTracking()
            .Where(i => openingBatchIds.Contains(i.OpeningBatchId))
            .ToListAsync(ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task AddOpeningItemsAsync(IReadOnlyList<OpeningItem> items, CancellationToken ct)
    {
        if (items.Count > 0)
        {
            await _db.OpeningItems.AddRangeAsync(items, ct).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// <see cref="OpeningItem"/> 为无审计字段的 <c>Entity</c>，明细整体替换按规范使用
    /// <c>ExecuteDeleteAsync</c> 直接删除旧行，避免「先查全量 materialize」的改写反模式。
    /// </remarks>
    public async Task ReplaceOpeningItemsAsync(
        long openingBatchId, IReadOnlyList<OpeningItem> items, CancellationToken ct)
    {
        _ = await _db.OpeningItems
            .Where(i => i.OpeningBatchId == openingBatchId)
            .ExecuteDeleteAsync(ct).ConfigureAwait(false);
        await AddOpeningItemsAsync(items, ct).ConfigureAwait(false);
    }

    // ============ 成本调整 ============

    /// <inheritdoc />
    public async Task<IReadOnlyList<CostAdjust>> ListCostAdjustsAsync(
        CostAdjustStatus? status, CancellationToken ct)
        => await _db.CostAdjusts.AsNoTracking()
            .Where(a => status == null || a.Status == status.Value)
            .OrderByDescending(a => a.Id)
            .ToListAsync(ct).ConfigureAwait(false);

    /// <inheritdoc />
    public Task<CostAdjust?> FindCostAdjustAsync(long id, CancellationToken ct)
        => _db.CostAdjusts.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id, ct);

    /// <inheritdoc />
    public Task<CostAdjust?> FindCostAdjustTrackedAsync(long id, CancellationToken ct)
        => _db.CostAdjusts.FirstOrDefaultAsync(a => a.Id == id, ct);

    /// <inheritdoc />
    public async Task AddCostAdjustAsync(CostAdjust adjust, CancellationToken ct)
        => _ = await _db.CostAdjusts.AddAsync(adjust, ct).ConfigureAwait(false);

    /// <inheritdoc />
    public Task RemoveCostAdjustAsync(CostAdjust adjust, CancellationToken ct)
    {
        _ = _db.CostAdjusts.Remove(adjust);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<CostAdjustLine>> ListCostAdjustLinesAsync(
        long costAdjustId, CancellationToken ct)
        => await _db.CostAdjustLines.AsNoTracking()
            .Where(l => l.CostAdjustId == costAdjustId)
            .OrderBy(l => l.Id)
            .ToListAsync(ct).ConfigureAwait(false);

    /// <inheritdoc />
    public async Task AddCostAdjustLinesAsync(IReadOnlyList<CostAdjustLine> lines, CancellationToken ct)
    {
        if (lines.Count > 0)
        {
            await _db.CostAdjustLines.AddRangeAsync(lines, ct).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// <see cref="CostAdjustLine"/> 为无审计字段的 <c>Entity</c>，明细整体替换按规范使用
    /// <c>ExecuteDeleteAsync</c> 直接删除旧行。
    /// </remarks>
    public async Task ReplaceCostAdjustLinesAsync(
        long costAdjustId, IReadOnlyList<CostAdjustLine> lines, CancellationToken ct)
    {
        _ = await _db.CostAdjustLines
            .Where(l => l.CostAdjustId == costAdjustId)
            .ExecuteDeleteAsync(ct).ConfigureAwait(false);
        await AddCostAdjustLinesAsync(lines, ct).ConfigureAwait(false);
    }

    // ============ 批量读取（避免逐行 N+1）============

    /// <inheritdoc />
    public async Task<IReadOnlyList<Item>> FindItemsAsync(IReadOnlyList<long> ids, CancellationToken ct)
    {
        if (ids.Count == 0)
        {
            return [];
        }

        return await _db.Items.AsNoTracking()
            .Where(i => ids.Contains(i.Id))
            .ToListAsync(ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<StockBalance>> FindStockBalancesByKeysAsync(
        IReadOnlyList<long> itemIds, IReadOnlyList<long> warehouseIds, CancellationToken ct)
    {
        if (itemIds.Count == 0 || warehouseIds.Count == 0)
        {
            return [];
        }

        return await _db.StockBalances.AsNoTracking()
            .Where(b => itemIds.Contains(b.ItemId) && warehouseIds.Contains(b.WarehouseId))
            .ToListAsync(ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<StockBatch>> ListStockBatchesByKeysAsync(
        IReadOnlyList<long> itemIds, IReadOnlyList<long> warehouseIds, CancellationToken ct)
    {
        if (itemIds.Count == 0 || warehouseIds.Count == 0)
        {
            return [];
        }

        return await _db.StockBatches.AsNoTracking()
            .Where(b => itemIds.Contains(b.ItemId) && warehouseIds.Contains(b.WarehouseId))
            .ToListAsync(ct).ConfigureAwait(false);
    }

    // ============ 并发重试 ============

    /// <inheritdoc />
    public void ResetChangeTracker() => _db.ChangeTracker.Clear();

    // ============ 事务与提交 ============

    /// <inheritdoc />
    public async Task<T> ExecuteInTransactionAsync<T>(
        Func<CancellationToken, Task<T>> action, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(action);

        if (_db.Database.CurrentTransaction is not null)
        {
            return await action(ct).ConfigureAwait(false);
        }

        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction =
            await _db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
        try
        {
            T result = await action(ct).ConfigureAwait(false);
            await transaction.CommitAsync(ct).ConfigureAwait(false);
            return result;
        }
        catch
        {
            await transaction.RollbackAsync(ct).ConfigureAwait(false);
            throw;
        }
        finally
        {
            await transaction.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public Task<int> SaveChangesAsync(CancellationToken ct) => _db.SaveChangesAsync(ct);
}
