/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-26 10:00:00 +08:00 中国标准时间 星期一
 * 描述：Wms 模块仓储接口——纯数据访问层，禁止业务逻辑（读路径 AsNoTracking，写路径跟踪态）
 */

using Qlx.Modules.Wms.Domain.Adjust;
using Qlx.Modules.Wms.Domain.Alert;
using Qlx.Modules.Wms.Domain.Inbound;
using Qlx.Modules.Wms.Domain.Numbering;
using Qlx.Modules.Wms.Domain.Outbound;
using Qlx.Modules.Wms.Domain.Outbox;
using Qlx.Modules.Wms.Domain.StockTake;
using Qlx.Modules.Wms.Domain.Transfer;
using Qlx.Modules.Wms.Domain.Warehouse;

namespace Qlx.Modules.Wms.Repositories;

/// <summary>Wms 租户域仓储。</summary>
internal interface IWmsTenantRepository
{
    // === 仓库 ===
    Task<IReadOnlyList<Warehouse>> ListWarehousesAsync(CancellationToken ct);
    Task<Warehouse?> FindWarehouseAsync(long id, CancellationToken ct);
    Task<bool> IsWarehouseCodeExistsAsync(string code, CancellationToken ct);
    Task AddWarehouseAsync(Warehouse warehouse, CancellationToken ct);
    Task UpdateWarehouseAsync(Warehouse warehouse, CancellationToken ct);
    Task<int> CountZonesAsync(long warehouseId, CancellationToken ct);
    Task<int> CountLocationsAsync(long warehouseId, CancellationToken ct);

    /// <summary>仓库是否存在未完成作业单（入库待作业/作业中、出库待作业/拣货中/待复核、调拨非已收货/已作废）。</summary>
    Task<bool> HasUnfinishedOperationsAsync(long warehouseId, CancellationToken ct);

    // === 库区 ===
    Task<IReadOnlyList<WarehouseZone>> ListZonesAsync(long warehouseId, CancellationToken ct);
    Task<WarehouseZone?> FindZoneAsync(long id, CancellationToken ct);
    Task<bool> IsZoneCodeExistsAsync(long warehouseId, string code, long? excludeId, CancellationToken ct);
    Task AddZoneAsync(WarehouseZone zone, CancellationToken ct);
    Task UpdateZoneAsync(WarehouseZone zone, CancellationToken ct);
    Task<int> CountLocationsUnderZoneAsync(long zoneId, CancellationToken ct);

    // === 库位 ===
    Task<IReadOnlyList<WarehouseLocation>> ListLocationsAsync(long warehouseId, CancellationToken ct);
    Task<WarehouseLocation?> FindLocationAsync(long id, CancellationToken ct);
    Task<bool> IsLocationCodeExistsAsync(long zoneId, string code, long? excludeId, CancellationToken ct);
    Task AddLocationAsync(WarehouseLocation location, CancellationToken ct);
    Task UpdateLocationAsync(WarehouseLocation location, CancellationToken ct);

    /// <summary>库位是否被未完成入库/出库作业明细引用（父单未完结）。</summary>
    Task<bool> HasOpenTaskLinesAtLocationAsync(long locationId, CancellationToken ct);

    // === 入库作业 ===
    Task<IReadOnlyList<StockInTask>> ListStockInTasksAsync(
        int? status, long? warehouseId, int? sourceType, CancellationToken ct);
    Task<StockInTask?> FindStockInTaskAsync(long id, CancellationToken ct);
    Task AddStockInTaskAsync(StockInTask task, CancellationToken ct);

    // === 出库作业 ===
    Task<IReadOnlyList<StockOutTask>> ListStockOutTasksAsync(
        int? status, long? warehouseId, int? sourceType, CancellationToken ct);
    Task<StockOutTask?> FindStockOutTaskAsync(long id, CancellationToken ct);
    Task AddStockOutTaskAsync(StockOutTask task, CancellationToken ct);

    // === 调拨与在途 ===
    Task<IReadOnlyList<StockTransfer>> ListStockTransfersAsync(
        int? status, long? warehouseId, CancellationToken ct);
    Task<StockTransfer?> FindStockTransferAsync(long id, CancellationToken ct);
    Task AddStockTransferAsync(StockTransfer transfer, CancellationToken ct);
    Task<IReadOnlyList<InTransitStock>> ListInTransitStocksAsync(
        int? status, long? warehouseId, CancellationToken ct);
    Task<InTransitStock?> FindInTransitStockAsync(long transferLineId, CancellationToken ct);
    Task AddInTransitStockAsync(InTransitStock stock, CancellationToken ct);
    Task<IReadOnlyList<InTransitStock>> ListInTransitStockPageAsync(
        long afterId, int limit, CancellationToken ct);

    // === 盘点计划与盘点单 ===
    Task<IReadOnlyList<StockTakePlan>> ListStockTakePlansAsync(
        int? scopeType, long? warehouseId, CancellationToken ct);
    Task<StockTakePlan?> FindStockTakePlanAsync(long id, CancellationToken ct);
    Task AddStockTakePlanAsync(StockTakePlan plan, CancellationToken ct);
    Task<IReadOnlyList<StockTake>> ListStockTakesAsync(
        int? status, long? warehouseId, CancellationToken ct);
    Task<StockTake?> FindStockTakeAsync(long id, CancellationToken ct);
    Task AddStockTakeAsync(StockTake take, CancellationToken ct);

    // === 盘盈盘亏 ===
    Task<IReadOnlyList<InventoryAdjustDoc>> ListInventoryAdjustDocsAsync(
        int? type, int? status, long? warehouseId, CancellationToken ct);
    Task<InventoryAdjustDoc?> FindInventoryAdjustDocAsync(long id, CancellationToken ct);
    Task AddInventoryAdjustDocAsync(InventoryAdjustDoc doc, CancellationToken ct);

    // === 库存预警 ===
    Task<IReadOnlyList<InventoryAlert>> ListInventoryAlertsAsync(
        int? alertType, int? status, long? warehouseId, CancellationToken ct);
    Task<IReadOnlyList<InventoryAlert>> ListInventoryAlertsByDateAsync(
        InventoryAlertType alertType, DateOnly alertDate, CancellationToken ct);
    Task<InventoryAlert?> FindInventoryAlertAsync(long id, CancellationToken ct);
    Task AddInventoryAlertsAsync(IReadOnlyList<InventoryAlert> alerts, CancellationToken ct);

    // === 审批挂起请求 ===
    Task AddApprovalRequestAsync(WmsApprovalRequest request, CancellationToken ct);
    Task<WmsApprovalRequest?> FindPendingApprovalRequestAsync(
        string bizType, long bizId, CancellationToken ct);
    Task<WmsApprovalRequest?> FindApprovalRequestByInstanceAsync(
        string approvalInstanceId, CancellationToken ct);

    // === 库存命令 Outbox ===
    Task<WmsStockCommandOutbox?> FindStockCommandOutboxAsync(long id, CancellationToken ct);

    // === 事务与提交 ===
    Task<T> ExecuteInTransactionAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken ct);
    Task<int> SaveChangesAsync(CancellationToken ct);
}
