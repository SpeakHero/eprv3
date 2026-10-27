/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-26
 * 描述：库存预警扫描任务（wms.alert.scan）——四类阈值判定（安全库存/积压/临期/在途超期），
 *       每类按 Id 游标分页 ≤500 行/批 + 逐批保存（禁止一次性全量加载），当日幂等（A-16），
 *       已处理/已忽略当日不重复生成；新预警经 Outbox 持久发布 InventoryWarningRaisedEvent
 */

using System.Diagnostics;
using System.Globalization;

using Microsoft.Extensions.Logging;

using Qlx.Abstractions.Events.Wms;
using Qlx.Abstractions.Jobs;
using Qlx.Abstractions.Modules;
using Qlx.Abstractions.Queries;
using Qlx.Abstractions.Tenancy;
using Qlx.BuildingBlocks.EventBus;
using Qlx.Modules.Wms.Domain.Alert;
using Qlx.Modules.Wms.Domain.Transfer;
using Qlx.Modules.Wms.Repositories;

namespace Qlx.Modules.Wms.Jobs;

/// <summary>
/// 库存预警扫描任务处理器。每类先取当日已存在预警键集合做内存去重，
/// 再按游标分页扫描源数据（台账 / 批次 / 在途），命中阈值即生成当日预警并逐批保存，
/// 保存后仅对本批新生成预警持久发布 <see cref="InventoryWarningRaisedEvent"/>。
/// </summary>
/// <param name="repo">Wms 租户仓储（在途分页与预警落库）。</param>
/// <param name="stockScanQuery">Stk 预警扫描只读契约（台账/批次分页）。</param>
/// <param name="itemQuery">跨模块商品只读查询（启停与有效期属性）。</param>
/// <param name="warehouseQuery">跨模块仓库只读查询（启停状态）。</param>
/// <param name="parameters">系统参数读取（四类阈值，缺失取默认）。</param>
/// <param name="eventBus">事件总线（新预警持久发布）。</param>
/// <param name="tenantCtx">当前租户上下文。</param>
/// <param name="logger">日志器。</param>
internal sealed partial class InventoryAlertScanJobHandler(
    IWmsTenantRepository repo,
    IStockAlertScanQueryService stockScanQuery,
    IItemQueryService itemQuery,
    IWarehouseQueryService warehouseQuery,
    ISystemParameterProvider parameters,
    IEventBus eventBus,
    ITenantContext tenantCtx,
    ILogger<InventoryAlertScanJobHandler> logger) : IJobHandler
{
    /// <summary>单类单批最大扫描行数（spec §14.5）。</summary>
    internal const int BatchSize = 500;

    /// <summary>安全库存阈值参数键（0 = 关闭，R-9）。</summary>
    internal const string ParameterSafetyStock = "wms.alert.safety.stock";

    /// <summary>积压天数参数键。</summary>
    internal const string ParameterStagnantDays = "wms.alert.stagnant.days";

    /// <summary>临期天数参数键。</summary>
    internal const string ParameterExpiryDays = "wms.alert.expiry.days";

    /// <summary>在途超期天数参数键。</summary>
    internal const string ParameterInTransitDays = "wms.transfer.in_transit.days";

    private static readonly TimeSpan s_localOffset = TimeSpan.FromHours(8);

    private readonly IWmsTenantRepository _repo = repo;
    private readonly IStockAlertScanQueryService _stockScanQuery = stockScanQuery;
    private readonly IItemQueryService _itemQuery = itemQuery;
    private readonly IWarehouseQueryService _warehouseQuery = warehouseQuery;
    private readonly ISystemParameterProvider _parameters = parameters;
    private readonly IEventBus _eventBus = eventBus;
    private readonly ITenantContext _tenantCtx = tenantCtx;
    private readonly ILogger<InventoryAlertScanJobHandler> _logger = logger;

    /// <inheritdoc />
    public string TaskCode => WmsJobCodes.AlertScan;

    /// <inheritdoc />
    public Task<CompletedResult> ExecuteAsync(JobContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        return ScanOnceAsync(DateTimeOffset.UtcNow, cancellationToken);
    }

    /// <summary>
    /// 执行一轮扫描（内部可见，供单测注入时间点验证四类阈值与当日幂等）。
    /// </summary>
    /// <param name="now">本轮基准时间。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>本轮统计结果。</returns>
    internal async Task<CompletedResult> ScanOnceAsync(
        DateTimeOffset now, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();

        long? tenantId = _tenantCtx.TenantId;
        if (tenantId is null || tenantId <= 0)
        {
            LogTenantMissing();
            return new CompletedResult(
                new { Created = 0, AlertDate = (DateOnly?)null },
                null, null, stopwatch.ElapsedMilliseconds, IsSuccess: false,
                "无租户上下文（ITenantContext.TenantId 为空），跳过库存预警扫描。");
        }

        DateOnly alertDate = DateOnly.FromDateTime(now.ToOffset(s_localOffset).DateTime);
        int safetyStock = await _parameters
            .GetIntAsync(ParameterSafetyStock, 0, cancellationToken).ConfigureAwait(false);
        int stagnantDays = await _parameters
            .GetIntAsync(ParameterStagnantDays, 90, cancellationToken).ConfigureAwait(false);
        int expiryDays = await _parameters
            .GetIntAsync(ParameterExpiryDays, 30, cancellationToken).ConfigureAwait(false);
        int inTransitDays = await _parameters
            .GetIntAsync(ParameterInTransitDays, 7, cancellationToken).ConfigureAwait(false);

        int safetyCreated = await ScanSafetyStockAsync(
            tenantId.Value, alertDate, safetyStock, cancellationToken).ConfigureAwait(false);
        int stagnantCreated = await ScanStagnantAsync(
            tenantId.Value, alertDate, stagnantDays, now, cancellationToken).ConfigureAwait(false);
        int expiryCreated = await ScanNearExpiryAsync(
            tenantId.Value, alertDate, expiryDays, now, cancellationToken).ConfigureAwait(false);
        int inTransitCreated = await ScanInTransitOverdueAsync(
            tenantId.Value, alertDate, inTransitDays, now, cancellationToken).ConfigureAwait(false);

        int created = safetyCreated + stagnantCreated + expiryCreated + inTransitCreated;
        LogScanCompleted(tenantId.Value, alertDate, safetyCreated, stagnantCreated, expiryCreated,
            inTransitCreated);

        return new CompletedResult(
            new
            {
                AlertDate = alertDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                SafetyStock = safetyCreated,
                Stagnant = stagnantCreated,
                NearExpiry = expiryCreated,
                InTransitOverdue = inTransitCreated,
                Created = created,
            },
            null, null, stopwatch.ElapsedMilliseconds, IsSuccess: true);
    }

    // ============ 四类扫描 ============

    /// <summary>类型 1 安全库存：现存数量低于阈值，且商品/仓库均启用（阈值 0 = 关闭，R-9）。</summary>
    private async Task<int> ScanSafetyStockAsync(
        long tenantId, DateOnly alertDate, int threshold, CancellationToken ct)
    {
        if (threshold <= 0)
        {
            return 0;
        }

        HashSet<(long ItemId, long WarehouseId, string BatchNo)> existing =
            await LoadExistingKeysAsync(InventoryAlertType.SafetyStock, alertDate, ct).ConfigureAwait(false);

        int created = 0;
        long afterId = 0;
        while (true)
        {
            IReadOnlyList<StockBalanceScanRow> page = await _stockScanQuery
                .ListBalancePageAsync(afterId, BatchSize, ct).ConfigureAwait(false);
            if (page.Count == 0)
            {
                break;
            }

            afterId = page[^1].Id;
            List<StockBalanceScanRow> candidates =
                [.. page.Where(row => row.QtyOnhand < threshold)];
            if (candidates.Count > 0)
            {
                Dictionary<long, StockItemQueryRow> items = await LoadItemsAsync(
                    [.. candidates.Select(static row => row.ItemId)], ct).ConfigureAwait(false);
                Dictionary<long, WarehouseQueryRow> warehouses = await LoadWarehousesAsync(
                    [.. candidates.Select(static row => row.WarehouseId)], ct).ConfigureAwait(false);

                List<InventoryAlert> batch = [];
                foreach (StockBalanceScanRow row in candidates)
                {
                    if (!IsEnabled(items, row.ItemId) || !IsEnabled(warehouses, row.WarehouseId))
                    {
                        continue;
                    }

                    if (!existing.Add((row.ItemId, row.WarehouseId, string.Empty)))
                    {
                        continue;
                    }

                    batch.Add(new InventoryAlert(
                        tenantId, InventoryAlertType.SafetyStock, row.ItemId, row.WarehouseId,
                        batchNo: null, threshold, row.QtyOnhand, alertDate));
                }

                created += await SaveAndPublishAsync(batch, ct).ConfigureAwait(false);
            }

            if (page.Count < BatchSize)
            {
                break;
            }
        }

        return created;
    }

    /// <summary>类型 2 积压：有库存且最近流水早于 now - days（无流水行无法判定，跳过并登记偏差）。</summary>
    private async Task<int> ScanStagnantAsync(
        long tenantId, DateOnly alertDate, int days, DateTimeOffset now, CancellationToken ct)
    {
        if (days <= 0)
        {
            return 0;
        }

        DateTimeOffset deadline = now.AddDays(-days);
        HashSet<(long ItemId, long WarehouseId, string BatchNo)> existing =
            await LoadExistingKeysAsync(InventoryAlertType.Stagnant, alertDate, ct).ConfigureAwait(false);

        int created = 0;
        long afterId = 0;
        while (true)
        {
            IReadOnlyList<StockBalanceScanRow> page = await _stockScanQuery
                .ListBalancePageAsync(afterId, BatchSize, ct).ConfigureAwait(false);
            if (page.Count == 0)
            {
                break;
            }

            afterId = page[^1].Id;
            List<(StockBalanceScanRow Row, DateTimeOffset LastTxnAt)> candidates =
            [
                .. page
                    .Where(static row => row.QtyOnhand > 0m && row.LastTxnAt != null)
                    .Select(static row => (row, row.LastTxnAt!.Value))
                    .Where(candidate => candidate.Item2 < deadline),
            ];
            if (candidates.Count > 0)
            {
                List<InventoryAlert> batch = [];
                foreach ((StockBalanceScanRow row, DateTimeOffset lastTxnAt) in candidates)
                {
                    if (!existing.Add((row.ItemId, row.WarehouseId, string.Empty)))
                    {
                        continue;
                    }

                    batch.Add(new InventoryAlert(
                        tenantId, InventoryAlertType.Stagnant, row.ItemId, row.WarehouseId,
                        batchNo: null, days, RoundDays(now - lastTxnAt), alertDate));
                }

                created += await SaveAndPublishAsync(batch, ct).ConfigureAwait(false);
            }

            if (page.Count < BatchSize)
            {
                break;
            }
        }

        return created;
    }

    /// <summary>类型 3 临期：批次有库存且失效日期不晚于 now + days，商品启用有效期管理。</summary>
    private async Task<int> ScanNearExpiryAsync(
        long tenantId, DateOnly alertDate, int days, DateTimeOffset now, CancellationToken ct)
    {
        if (days <= 0)
        {
            return 0;
        }

        DateTimeOffset deadline = now.AddDays(days);
        HashSet<(long ItemId, long WarehouseId, string BatchNo)> existing =
            await LoadExistingKeysAsync(InventoryAlertType.NearExpiry, alertDate, ct).ConfigureAwait(false);

        int created = 0;
        long afterId = 0;
        while (true)
        {
            IReadOnlyList<StockBatchScanRow> page = await _stockScanQuery
                .ListBatchPageAsync(afterId, BatchSize, ct).ConfigureAwait(false);
            if (page.Count == 0)
            {
                break;
            }

            afterId = page[^1].Id;
            List<(StockBatchScanRow Row, DateTimeOffset ExpiryDate)> candidates =
            [
                .. page
                    .Where(static row => row.Qty > 0m && row.ExpiryDate != null)
                    .Select(static row => (row, row.ExpiryDate!.Value))
                    .Where(candidate => candidate.Item2 <= deadline),
            ];
            if (candidates.Count > 0)
            {
                Dictionary<long, StockItemQueryRow> items = await LoadItemsAsync(
                    [.. candidates.Select(static candidate => candidate.Row.ItemId)], ct)
                    .ConfigureAwait(false);

                List<InventoryAlert> batch = [];
                foreach ((StockBatchScanRow row, DateTimeOffset expiryDate) in candidates)
                {
                    if (!items.TryGetValue(row.ItemId, out StockItemQueryRow? item)
                        || !item.ExpiryEnabled
                        || item.Status != 1)
                    {
                        continue;
                    }

                    if (!existing.Add((row.ItemId, row.WarehouseId, row.BatchNo)))
                    {
                        continue;
                    }

                    batch.Add(new InventoryAlert(
                        tenantId, InventoryAlertType.NearExpiry, row.ItemId, row.WarehouseId,
                        row.BatchNo, days, RoundDays(expiryDate - now), alertDate));
                }

                created += await SaveAndPublishAsync(batch, ct).ConfigureAwait(false);
            }

            if (page.Count < BatchSize)
            {
                break;
            }
        }

        return created;
    }

    /// <summary>类型 4 在途超期：在途行发货时间早于 now - days（预警仓库取调入仓）。</summary>
    private async Task<int> ScanInTransitOverdueAsync(
        long tenantId, DateOnly alertDate, int days, DateTimeOffset now, CancellationToken ct)
    {
        if (days <= 0)
        {
            return 0;
        }

        DateTimeOffset deadline = now.AddDays(-days);
        HashSet<(long ItemId, long WarehouseId, string BatchNo)> existing =
            await LoadExistingKeysAsync(InventoryAlertType.InTransitOverdue, alertDate, ct)
                .ConfigureAwait(false);

        int created = 0;
        long afterId = 0;
        while (true)
        {
            IReadOnlyList<InTransitStock> page = await _repo
                .ListInTransitStockPageAsync(afterId, BatchSize, ct).ConfigureAwait(false);
            if (page.Count == 0)
            {
                break;
            }

            afterId = page[^1].Id;
            List<InventoryAlert> batch = [];
            foreach (InTransitStock row in page)
            {
                if (row.ShippedAt >= deadline)
                {
                    continue;
                }

                string batchNo = string.IsNullOrWhiteSpace(row.BatchNo) ? string.Empty : row.BatchNo.Trim();
                if (!existing.Add((row.ItemId, row.ToWarehouseId, batchNo)))
                {
                    continue;
                }

                batch.Add(new InventoryAlert(
                    tenantId, InventoryAlertType.InTransitOverdue, row.ItemId, row.ToWarehouseId,
                    batchNo, days, RoundDays(now - row.ShippedAt), alertDate));
            }

            created += await SaveAndPublishAsync(batch, ct).ConfigureAwait(false);
            if (page.Count < BatchSize)
            {
                break;
            }
        }

        return created;
    }

    // ============ 内部实现 ============

    /// <summary>取当日同类型已有预警键集合（含已处理/已忽略，保证当日不重复生成）。</summary>
    private async Task<HashSet<(long ItemId, long WarehouseId, string BatchNo)>> LoadExistingKeysAsync(
        InventoryAlertType alertType, DateOnly alertDate, CancellationToken ct)
    {
        IReadOnlyList<InventoryAlert> rows = await _repo
            .ListInventoryAlertsByDateAsync(alertType, alertDate, ct).ConfigureAwait(false);
        return [.. rows.Select(static row => (row.ItemId, row.WarehouseId, row.BatchNo))];
    }

    /// <summary>逐批落库并仅对本批新预警持久发布事件（事务提交后发布，at-least-once）。</summary>
    private async Task<int> SaveAndPublishAsync(List<InventoryAlert> alerts, CancellationToken ct)
    {
        if (alerts.Count == 0)
        {
            return 0;
        }

        await _repo.AddInventoryAlertsAsync(alerts, ct).ConfigureAwait(false);
        _ = await _repo.SaveChangesAsync(ct).ConfigureAwait(false);

        string tenantCode = _tenantCtx.TenantCode
            ?? alerts[0].TenantId.ToString(CultureInfo.InvariantCulture);
        foreach (InventoryAlert alert in alerts)
        {
            await _eventBus.PublishDurableAsync(
                new InventoryWarningRaisedEvent
                {
                    AlertId = alert.Id,
                    AlertType = (int)alert.AlertType,
                    ItemId = alert.ItemId,
                    WarehouseId = alert.WarehouseId,
                    TenantCode = tenantCode,
                },
                ct).ConfigureAwait(false);
        }

        return alerts.Count;
    }

    private async Task<Dictionary<long, StockItemQueryRow>> LoadItemsAsync(
        IReadOnlyList<long> itemIds, CancellationToken ct)
    {
        List<long> distinct = [.. itemIds.Distinct()];
        if (distinct.Count == 0)
        {
            return [];
        }

        IReadOnlyList<StockItemQueryRow> rows = await _itemQuery
            .FindByIdsAsync(distinct, ct).ConfigureAwait(false);
        return rows.ToDictionary(static item => item.Id);
    }

    private async Task<Dictionary<long, WarehouseQueryRow>> LoadWarehousesAsync(
        IReadOnlyList<long> warehouseIds, CancellationToken ct)
    {
        List<long> distinct = [.. warehouseIds.Distinct()];
        if (distinct.Count == 0)
        {
            return [];
        }

        IReadOnlyList<WarehouseQueryRow> rows = await _warehouseQuery
            .FindByIdsAsync(distinct, ct).ConfigureAwait(false);
        return rows.ToDictionary(static warehouse => warehouse.Id);
    }

    private static bool IsEnabled(Dictionary<long, StockItemQueryRow> items, long itemId)
        => items.TryGetValue(itemId, out StockItemQueryRow? item) && item.Status == 1;

    private static bool IsEnabled(Dictionary<long, WarehouseQueryRow> warehouses, long warehouseId)
        => warehouses.TryGetValue(warehouseId, out WarehouseQueryRow? warehouse) && warehouse.Status == 1;

    private static decimal RoundDays(TimeSpan span)
        => Math.Round((decimal)span.TotalDays, 2, MidpointRounding.AwayFromZero);

    [LoggerMessage(Level = LogLevel.Warning, Message = "库存预警扫描跳过：无租户上下文")]
    private partial void LogTenantMissing();

    [LoggerMessage(Level = LogLevel.Information, Message = "租户 {TenantId} 库存预警扫描完成（{AlertDate}）：安全库存 {SafetyStock}，积压 {Stagnant}，临期 {NearExpiry}，在途超期 {InTransitOverdue}")]
    private partial void LogScanCompleted(
        long tenantId, DateOnly alertDate, int safetyStock, int stagnant, int nearExpiry, int inTransitOverdue);
}
