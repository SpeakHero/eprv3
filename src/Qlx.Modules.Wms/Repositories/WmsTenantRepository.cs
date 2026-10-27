/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-26 10:00:00 +08:00 中国标准时间 星期一
 * 描述：Wms 租户域仓储实现——基于 WmsTenantDbContext，读路径 AsNoTracking，写路径跟踪态
 */

using Microsoft.EntityFrameworkCore;

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

/// <summary>Wms 租户域仓储实现。</summary>
/// <param name="db">当前请求的 Wms 租户 DbContext。</param>
internal sealed class WmsTenantRepository(WmsTenantDbContext db) : IWmsTenantRepository
{
    private readonly WmsTenantDbContext _db = db;

    /// <inheritdoc />
    public async Task<IReadOnlyList<Warehouse>> ListWarehousesAsync(CancellationToken ct)
        => await _db.Warehouses.AsNoTracking().OrderBy(w => w.Code).ToListAsync(ct).ConfigureAwait(false);

    /// <inheritdoc />
    public Task<Warehouse?> FindWarehouseAsync(long id, CancellationToken ct)
        => _db.Warehouses.FirstOrDefaultAsync(w => w.Id == id, ct);

    /// <inheritdoc />
    public Task<bool> IsWarehouseCodeExistsAsync(string code, CancellationToken ct)
        => _db.Warehouses.AsNoTracking().AnyAsync(w => w.Code == code && !w.IsDeleted, ct);

    /// <inheritdoc />
    public async Task AddWarehouseAsync(Warehouse warehouse, CancellationToken ct)
        => _ = await _db.Warehouses.AddAsync(warehouse, ct).ConfigureAwait(false);

    /// <inheritdoc />
    public Task UpdateWarehouseAsync(Warehouse warehouse, CancellationToken ct)
    {
        _ = _db.Warehouses.Update(warehouse);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<int> CountZonesAsync(long warehouseId, CancellationToken ct)
        => _db.Zones.AsNoTracking().CountAsync(z => z.WarehouseId == warehouseId && !z.IsDeleted, ct);

    /// <inheritdoc />
    public Task<int> CountLocationsAsync(long warehouseId, CancellationToken ct)
        => _db.Locations.AsNoTracking().CountAsync(l => l.WarehouseId == warehouseId && !l.IsDeleted, ct);

    /// <inheritdoc />
    public async Task<bool> HasUnfinishedOperationsAsync(long warehouseId, CancellationToken ct)
    {
        bool hasInbound = await _db.StockInTasks.AsNoTracking()
            .AnyAsync(t => t.WarehouseId == warehouseId
                && (t.Status == StockInTaskStatus.Pending || t.Status == StockInTaskStatus.InProgress), ct)
            .ConfigureAwait(false);
        if (hasInbound)
        {
            return true;
        }

        bool hasOutbound = await _db.StockOutTasks.AsNoTracking()
            .AnyAsync(t => t.WarehouseId == warehouseId
                && (t.Status == StockOutTaskStatus.Pending
                    || t.Status == StockOutTaskStatus.Picking
                    || t.Status == StockOutTaskStatus.PendingCheck), ct)
            .ConfigureAwait(false);
        if (hasOutbound)
        {
            return true;
        }

        // 调拨：待审批/已生效/已发货均视为在途未完结（已收货/已作废为完结）
        return await _db.StockTransfers.AsNoTracking()
            .AnyAsync(t => (t.FromWarehouseId == warehouseId || t.ToWarehouseId == warehouseId)
                && t.Status != TransferStatus.Received
                && t.Status != TransferStatus.Voided, ct)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<WarehouseZone>> ListZonesAsync(long warehouseId, CancellationToken ct)
        => await _db.Zones.AsNoTracking()
            .Where(z => z.WarehouseId == warehouseId && !z.IsDeleted)
            .OrderBy(z => z.SortOrder).ThenBy(z => z.Code)
            .ToListAsync(ct).ConfigureAwait(false);

    /// <inheritdoc />
    public Task<WarehouseZone?> FindZoneAsync(long id, CancellationToken ct)
        => _db.Zones.FirstOrDefaultAsync(z => z.Id == id, ct);

    /// <inheritdoc />
    public Task<bool> IsZoneCodeExistsAsync(long warehouseId, string code, long? excludeId, CancellationToken ct)
        => _db.Zones.AsNoTracking()
            .AnyAsync(z => z.WarehouseId == warehouseId && z.Code == code && !z.IsDeleted
                && (excludeId == null || z.Id != excludeId.Value), ct);

    /// <inheritdoc />
    public async Task AddZoneAsync(WarehouseZone zone, CancellationToken ct)
        => _ = await _db.Zones.AddAsync(zone, ct).ConfigureAwait(false);

    /// <inheritdoc />
    public Task UpdateZoneAsync(WarehouseZone zone, CancellationToken ct)
    {
        _ = _db.Zones.Update(zone);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<int> CountLocationsUnderZoneAsync(long zoneId, CancellationToken ct)
        => _db.Locations.AsNoTracking().CountAsync(l => l.ZoneId == zoneId && !l.IsDeleted, ct);

    /// <inheritdoc />
    public async Task<IReadOnlyList<WarehouseLocation>> ListLocationsAsync(long warehouseId, CancellationToken ct)
        => await _db.Locations.AsNoTracking()
            .Where(l => l.WarehouseId == warehouseId && !l.IsDeleted)
            .OrderBy(l => l.ZoneId).ThenBy(l => l.SortOrder).ThenBy(l => l.Code)
            .ToListAsync(ct).ConfigureAwait(false);

    /// <inheritdoc />
    public Task<WarehouseLocation?> FindLocationAsync(long id, CancellationToken ct)
        => _db.Locations.FirstOrDefaultAsync(l => l.Id == id, ct);

    /// <inheritdoc />
    public Task<bool> IsLocationCodeExistsAsync(long zoneId, string code, long? excludeId, CancellationToken ct)
        => _db.Locations.AsNoTracking()
            .AnyAsync(l => l.ZoneId == zoneId && l.Code == code && !l.IsDeleted
                && (excludeId == null || l.Id != excludeId.Value), ct);

    /// <inheritdoc />
    public async Task AddLocationAsync(WarehouseLocation location, CancellationToken ct)
        => _ = await _db.Locations.AddAsync(location, ct).ConfigureAwait(false);

    /// <inheritdoc />
    public Task UpdateLocationAsync(WarehouseLocation location, CancellationToken ct)
    {
        _ = _db.Locations.Update(location);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task<bool> HasOpenTaskLinesAtLocationAsync(long locationId, CancellationToken ct)
    {
        bool hasInbound = await (
            from line in _db.StockInTaskLines.AsNoTracking()
            join task in _db.StockInTasks.AsNoTracking() on line.TaskId equals task.Id
            where line.LocationId == locationId
                && (task.Status == StockInTaskStatus.Pending || task.Status == StockInTaskStatus.InProgress)
            select line.Id).AnyAsync(ct).ConfigureAwait(false);
        if (hasInbound)
        {
            return true;
        }

        return await (
            from line in _db.StockOutTaskLines.AsNoTracking()
            join task in _db.StockOutTasks.AsNoTracking() on line.TaskId equals task.Id
            where line.LocationId == locationId
                && (task.Status == StockOutTaskStatus.Pending
                    || task.Status == StockOutTaskStatus.Picking
                    || task.Status == StockOutTaskStatus.PendingCheck)
            select line.Id).AnyAsync(ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<StockInTask>> ListStockInTasksAsync(
        int? status, long? warehouseId, int? sourceType, CancellationToken ct)
        => await _db.StockInTasks.AsNoTracking()
            .Where(t => (status == null || (int)t.Status == status.Value)
                && (warehouseId == null || t.WarehouseId == warehouseId.Value)
                && (sourceType == null || t.SourceType == sourceType.Value))
            // SQLite 不支持 DateTimeOffset 排序下推；雪花 Id 单调递增，等价按创建倒序（新单在前）
            .OrderByDescending(t => t.Id)
            .ToListAsync(ct).ConfigureAwait(false);

    /// <inheritdoc />
    public Task<StockInTask?> FindStockInTaskAsync(long id, CancellationToken ct)
        => _db.StockInTasks
            .Include(t => t.Lines)
            .FirstOrDefaultAsync(t => t.Id == id, ct);

    /// <inheritdoc />
    public async Task AddStockInTaskAsync(StockInTask task, CancellationToken ct)
        => _ = await _db.StockInTasks.AddAsync(task, ct).ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<IReadOnlyList<StockOutTask>> ListStockOutTasksAsync(
        int? status, long? warehouseId, int? sourceType, CancellationToken ct)
        => await _db.StockOutTasks.AsNoTracking()
            .Where(t => (status == null || (int)t.Status == status.Value)
                && (warehouseId == null || t.WarehouseId == warehouseId.Value)
                && (sourceType == null || t.SourceType == sourceType.Value))
            // SQLite 不支持 DateTimeOffset 排序下推；雪花 Id 单调递增，等价按创建倒序（新单在前）
            .OrderByDescending(t => t.Id)
            .ToListAsync(ct).ConfigureAwait(false);

    /// <inheritdoc />
    public Task<StockOutTask?> FindStockOutTaskAsync(long id, CancellationToken ct)
        => _db.StockOutTasks
            .Include(t => t.Lines)
            .FirstOrDefaultAsync(t => t.Id == id, ct);

    /// <inheritdoc />
    public async Task AddStockOutTaskAsync(StockOutTask task, CancellationToken ct)
        => _ = await _db.StockOutTasks.AddAsync(task, ct).ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<IReadOnlyList<StockTransfer>> ListStockTransfersAsync(
        int? status, long? warehouseId, CancellationToken ct)
        => await _db.StockTransfers.AsNoTracking()
            .Where(t => (status == null || (int)t.Status == status.Value)
                && (warehouseId == null
                    || t.FromWarehouseId == warehouseId.Value
                    || t.ToWarehouseId == warehouseId.Value))
            // SQLite 不支持 DateTimeOffset 排序下推；雪花 Id 单调递增，等价按创建倒序（新单在前）
            .OrderByDescending(t => t.Id)
            .ToListAsync(ct).ConfigureAwait(false);

    /// <inheritdoc />
    public Task<StockTransfer?> FindStockTransferAsync(long id, CancellationToken ct)
        => _db.StockTransfers
            .Include(t => t.Lines)
            .FirstOrDefaultAsync(t => t.Id == id, ct);

    /// <inheritdoc />
    public async Task AddStockTransferAsync(StockTransfer transfer, CancellationToken ct)
        => _ = await _db.StockTransfers.AddAsync(transfer, ct).ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<IReadOnlyList<InTransitStock>> ListInTransitStocksAsync(
        int? status, long? warehouseId, CancellationToken ct)
        => await _db.InTransitStocks.AsNoTracking()
            .Where(t => (status == null || (int)t.Status == status.Value)
                && (warehouseId == null
                    || t.FromWarehouseId == warehouseId.Value
                    || t.ToWarehouseId == warehouseId.Value))
            // SQLite 不支持 DateTimeOffset 排序下推；雪花 Id 单调递增，等价按创建倒序（新行在前）
            .OrderByDescending(t => t.Id)
            .ToListAsync(ct).ConfigureAwait(false);

    /// <inheritdoc />
    public Task<InTransitStock?> FindInTransitStockAsync(long transferLineId, CancellationToken ct)
        => _db.InTransitStocks.FirstOrDefaultAsync(t => t.TransferLineId == transferLineId, ct);

    /// <inheritdoc />
    public async Task AddInTransitStockAsync(InTransitStock stock, CancellationToken ct)
        => _ = await _db.InTransitStocks.AddAsync(stock, ct).ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<IReadOnlyList<InTransitStock>> ListInTransitStockPageAsync(
        long afterId, int limit, CancellationToken ct)
        => await _db.InTransitStocks.AsNoTracking()
            .Where(t => t.Status == InTransitStatus.InTransit && t.Id > afterId)
            .OrderBy(t => t.Id)
            .Take(limit)
            .ToListAsync(ct).ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<IReadOnlyList<StockTakePlan>> ListStockTakePlansAsync(
        int? scopeType, long? warehouseId, CancellationToken ct)
        => await _db.StockTakePlans.AsNoTracking()
            .Where(p => (scopeType == null || p.ScopeType == scopeType.Value)
                && (warehouseId == null || p.WarehouseId == warehouseId.Value))
            // SQLite 不支持 DateTimeOffset 排序下推；雪花 Id 单调递增，等价按创建倒序（新单在前）
            .OrderByDescending(p => p.Id)
            .ToListAsync(ct).ConfigureAwait(false);

    /// <inheritdoc />
    public Task<StockTakePlan?> FindStockTakePlanAsync(long id, CancellationToken ct)
        => _db.StockTakePlans.FirstOrDefaultAsync(p => p.Id == id, ct);

    /// <inheritdoc />
    public async Task AddStockTakePlanAsync(StockTakePlan plan, CancellationToken ct)
        => _ = await _db.StockTakePlans.AddAsync(plan, ct).ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<IReadOnlyList<StockTake>> ListStockTakesAsync(
        int? status, long? warehouseId, CancellationToken ct)
        => await _db.StockTakes.AsNoTracking()
            .Where(t => (status == null || (int)t.Status == status.Value)
                && (warehouseId == null || t.WarehouseId == warehouseId.Value))
            // SQLite 不支持 DateTimeOffset 排序下推；雪花 Id 单调递增，等价按创建倒序（新单在前）
            .OrderByDescending(t => t.Id)
            .ToListAsync(ct).ConfigureAwait(false);

    /// <inheritdoc />
    public Task<StockTake?> FindStockTakeAsync(long id, CancellationToken ct)
        => _db.StockTakes
            .Include(t => t.Lines)
            .FirstOrDefaultAsync(t => t.Id == id, ct);

    /// <inheritdoc />
    public async Task AddStockTakeAsync(StockTake take, CancellationToken ct)
        => _ = await _db.StockTakes.AddAsync(take, ct).ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<IReadOnlyList<InventoryAdjustDoc>> ListInventoryAdjustDocsAsync(
        int? type, int? status, long? warehouseId, CancellationToken ct)
        => await _db.InventoryAdjustDocs.AsNoTracking()
            .Where(d => (type == null || d.Type == type.Value)
                && (status == null || (int)d.Status == status.Value)
                && (warehouseId == null || d.WarehouseId == warehouseId.Value))
            // SQLite 不支持 DateTimeOffset 排序下推；雪花 Id 单调递增，等价按创建倒序（新单在前）
            .OrderByDescending(d => d.Id)
            .ToListAsync(ct).ConfigureAwait(false);

    /// <inheritdoc />
    public Task<InventoryAdjustDoc?> FindInventoryAdjustDocAsync(long id, CancellationToken ct)
        => _db.InventoryAdjustDocs
            .Include(d => d.Lines)
            .FirstOrDefaultAsync(d => d.Id == id, ct);

    /// <inheritdoc />
    public async Task AddInventoryAdjustDocAsync(InventoryAdjustDoc doc, CancellationToken ct)
        => _ = await _db.InventoryAdjustDocs.AddAsync(doc, ct).ConfigureAwait(false);

    // ============ 库存预警 ============

    /// <inheritdoc />
    public async Task<IReadOnlyList<InventoryAlert>> ListInventoryAlertsAsync(
        int? alertType, int? status, long? warehouseId, CancellationToken ct)
        => await _db.InventoryAlerts.AsNoTracking()
            .Where(a => (alertType == null || (int)a.AlertType == alertType.Value)
                && (status == null || (int)a.Status == status.Value)
                && (warehouseId == null || a.WarehouseId == warehouseId.Value))
            // SQLite 不支持 DateTimeOffset 排序下推；雪花 Id 单调递增，等价按创建倒序（新预警在前）
            .OrderByDescending(a => a.Id)
            .ToListAsync(ct).ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<IReadOnlyList<InventoryAlert>> ListInventoryAlertsByDateAsync(
        InventoryAlertType alertType, DateOnly alertDate, CancellationToken ct)
        => await _db.InventoryAlerts.AsNoTracking()
            .Where(a => a.AlertType == alertType && a.AlertDate == alertDate)
            .ToListAsync(ct).ConfigureAwait(false);

    /// <inheritdoc />
    public Task<InventoryAlert?> FindInventoryAlertAsync(long id, CancellationToken ct)
        => _db.InventoryAlerts.FirstOrDefaultAsync(a => a.Id == id, ct);

    /// <inheritdoc />
    public async Task AddInventoryAlertsAsync(IReadOnlyList<InventoryAlert> alerts, CancellationToken ct)
        => await _db.InventoryAlerts.AddRangeAsync(alerts, ct).ConfigureAwait(false);

    /// <inheritdoc />
    public async Task AddApprovalRequestAsync(WmsApprovalRequest request, CancellationToken ct)
        => _ = await _db.ApprovalRequests.AddAsync(request, ct).ConfigureAwait(false);

    /// <inheritdoc />
    public Task<WmsApprovalRequest?> FindPendingApprovalRequestAsync(
        string bizType, long bizId, CancellationToken ct)
        => _db.ApprovalRequests
            .Where(r => r.BizType == bizType && r.BizId == bizId
                && r.Status == WmsApprovalRequestStatus.Pending)
            .OrderByDescending(r => r.Id)
            .FirstOrDefaultAsync(ct);

    /// <inheritdoc />
    public Task<WmsApprovalRequest?> FindApprovalRequestByInstanceAsync(
        string approvalInstanceId, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(approvalInstanceId);
        return _db.ApprovalRequests
            .FirstOrDefaultAsync(r => r.ApprovalInstanceId == approvalInstanceId, ct);
    }

    /// <inheritdoc />
    public Task<WmsStockCommandOutbox?> FindStockCommandOutboxAsync(long id, CancellationToken ct)
        => _db.StockCommandOutbox.FirstOrDefaultAsync(r => r.Id == id, ct);

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
