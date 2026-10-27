/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-26
 * 描述：Stk 模块仓储接口——纯数据访问层，禁止业务逻辑（读路径 AsNoTracking，写路径跟踪态）
 */

using Qlx.Abstractions.Stock;
using Qlx.Modules.Stk.Domain.Inventory;
using Qlx.Modules.Stk.Domain.Item;
using Qlx.Modules.Stk.Domain.Numbering;

namespace Qlx.Modules.Stk.Repositories;

/// <summary>Stk 租户域仓储。</summary>
internal interface IStkTenantRepository
{
    // === 商品档案 ===
    Task<IReadOnlyList<Item>> ListItemsAsync(CancellationToken ct);
    Task<IReadOnlyList<Item>> ListItemsByIdsAsync(IReadOnlyList<long> ids, CancellationToken ct);
    Task<Item?> FindItemAsync(long id, CancellationToken ct);
    Task<bool> IsItemCodeExistsAsync(string itemCode, CancellationToken ct);
    Task<bool> IsBarcodeExistsAsync(string barcode, long? excludeId, CancellationToken ct);
    Task AddItemAsync(Item item, CancellationToken ct);
    Task UpdateItemAsync(Item item, CancellationToken ct);

    // === 价格变更日志 ===
    Task<IReadOnlyList<ItemPriceLog>> ListPriceLogsAsync(long itemId, CancellationToken ct);
    Task AddPriceLogAsync(ItemPriceLog log, CancellationToken ct);

    // === 审批挂起请求 ===
    Task<StkApprovalRequest?> FindApprovalRequestAsync(long id, CancellationToken ct);
    Task<StkApprovalRequest?> FindApprovalRequestByInstanceAsync(string instanceId, CancellationToken ct);
    Task AddApprovalRequestAsync(StkApprovalRequest request, CancellationToken ct);
    Task UpdateApprovalRequestAsync(StkApprovalRequest request, CancellationToken ct);

    // === 库存台账（读路径 AsNoTracking / 写路径跟踪态）===
    Task<StockBalance?> FindStockBalanceAsync(long itemId, long warehouseId, CancellationToken ct);
    Task<StockBalance?> FindStockBalanceTrackedAsync(long itemId, long warehouseId, CancellationToken ct);
    Task<IReadOnlyList<StockBalance>> ListStockBalancesAsync(long? itemId, long? warehouseId, CancellationToken ct);
    Task AddStockBalanceAsync(StockBalance balance, CancellationToken ct);
    Task<bool> HasStockInWarehouseAsync(long warehouseId, CancellationToken ct);

    // === 库存流水（只增不改不删）===
    Task<IReadOnlyList<StockTxn>> ListStockTxnsAsync(
        long? itemId, long? warehouseId, int? sourceType,
        DateTimeOffset? from, DateTimeOffset? to, int take, CancellationToken ct);
    Task<StockTxn?> FindStockTxnByIdempotencyAsync(
        StockSourceType sourceType, string docNo, int lineNo, long itemId, long warehouseId,
        string? batchNo, StockDirection direction, CancellationToken ct);
    Task AddStockTxnAsync(StockTxn txn, CancellationToken ct);

    // === 批次库存 ===
    Task<StockBatch?> FindStockBatchTrackedAsync(long itemId, long warehouseId, string batchNo, CancellationToken ct);
    Task<IReadOnlyList<StockBatch>> ListStockBatchesAsync(long itemId, long warehouseId, CancellationToken ct);
    Task AddStockBatchAsync(StockBatch batch, CancellationToken ct);

    // === 期初建账 ===
    Task<IReadOnlyList<OpeningBatch>> ListOpeningBatchesAsync(
        OpeningBatchStatus? status, long? warehouseId, CancellationToken ct);
    Task<OpeningBatch?> FindOpeningBatchAsync(long id, CancellationToken ct);
    Task<OpeningBatch?> FindOpeningBatchTrackedAsync(long id, CancellationToken ct);
    Task AddOpeningBatchAsync(OpeningBatch batch, CancellationToken ct);
    Task RemoveOpeningBatchAsync(OpeningBatch batch, CancellationToken ct);
    Task<bool> HasPostedOpeningAsync(long warehouseId, CancellationToken ct);
    Task<IReadOnlyList<OpeningBatch>> ListPostedOpeningBatchesAsync(CancellationToken ct);
    Task<IReadOnlyList<OpeningItem>> ListOpeningItemsAsync(long openingBatchId, CancellationToken ct);
    Task<IReadOnlyList<OpeningItem>> ListOpeningItemsByBatchIdsAsync(
        IReadOnlyList<long> openingBatchIds, CancellationToken ct);
    Task AddOpeningItemsAsync(IReadOnlyList<OpeningItem> items, CancellationToken ct);
    Task ReplaceOpeningItemsAsync(
        long openingBatchId, IReadOnlyList<OpeningItem> items, CancellationToken ct);

    // === 成本调整 ===
    Task<IReadOnlyList<CostAdjust>> ListCostAdjustsAsync(CostAdjustStatus? status, CancellationToken ct);
    Task<CostAdjust?> FindCostAdjustAsync(long id, CancellationToken ct);
    Task<CostAdjust?> FindCostAdjustTrackedAsync(long id, CancellationToken ct);
    Task AddCostAdjustAsync(CostAdjust adjust, CancellationToken ct);
    Task RemoveCostAdjustAsync(CostAdjust adjust, CancellationToken ct);
    Task<IReadOnlyList<CostAdjustLine>> ListCostAdjustLinesAsync(long costAdjustId, CancellationToken ct);
    Task AddCostAdjustLinesAsync(IReadOnlyList<CostAdjustLine> lines, CancellationToken ct);
    Task ReplaceCostAdjustLinesAsync(
        long costAdjustId, IReadOnlyList<CostAdjustLine> lines, CancellationToken ct);

    // === 预警扫描分页（Id 游标升序，禁止一次性全量加载）===
    Task<IReadOnlyList<StockBalance>> ListStockBalancePageAsync(
        long afterId, int limit, CancellationToken ct);
    Task<IReadOnlyList<StockBatch>> ListStockBatchPageAsync(
        long afterId, int limit, CancellationToken ct);

    // === 批量读取（避免逐行 N+1）===
    Task<IReadOnlyList<Item>> FindItemsAsync(IReadOnlyList<long> ids, CancellationToken ct);
    Task<IReadOnlyList<StockBalance>> FindStockBalancesByKeysAsync(
        IReadOnlyList<long> itemIds, IReadOnlyList<long> warehouseIds, CancellationToken ct);
    Task<IReadOnlyList<StockBatch>> ListStockBatchesByKeysAsync(
        IReadOnlyList<long> itemIds, IReadOnlyList<long> warehouseIds, CancellationToken ct);

    // === 并发重试 ===
    void ResetChangeTracker();

    // === 事务与提交 ===
    Task<T> ExecuteInTransactionAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken ct);
    Task<int> SaveChangesAsync(CancellationToken ct);
}