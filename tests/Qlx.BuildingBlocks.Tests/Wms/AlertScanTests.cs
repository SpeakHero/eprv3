/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-26
 * 描述：库存预警扫描测试——四类阈值边界、当日二次扫描零新增、已处理/已忽略不复活、
 *       批量上限分页、处理说明必填、事件仅对新生成预警持久发布
 */

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

using Qlx.Abstractions.Events;
using Qlx.Abstractions.Events.Wms;
using Qlx.Abstractions.Exceptions;
using Qlx.Abstractions.Modules;
using Qlx.Abstractions.Queries;
using Qlx.BuildingBlocks.Tests.Bas;
using Qlx.BuildingBlocks.Tests.Files;
using Qlx.BuildingBlocks.Tests.Jobs;
using Qlx.Modules.Wms;
using Qlx.Modules.Wms.Domain.Alert;
using Qlx.Modules.Wms.Domain.Transfer;
using Qlx.Modules.Wms.Dtos;
using Qlx.Modules.Wms.Jobs;
using Qlx.Modules.Wms.Repositories;
using Qlx.Modules.Wms.Services;

namespace Qlx.BuildingBlocks.Tests.Wms;

/// <summary>库存预警扫描与处理回填测试。</summary>
public sealed class AlertScanTests
{
    private const long TenantId = 99L;
    private const string Operator = "user-99";

    private const long ItemA = 8801L;
    private const long ItemB = 8802L;
    private const long WhEnabled = 9101L;
    private const long WhDisabled = 9102L;

    private static readonly DateTimeOffset Now = new(2026, 10, 26, 2, 0, 0, TimeSpan.FromHours(8));

    private sealed class Harness
    {
        public required WmsTenantDbContext Db { get; init; }

        public required WmsTenantRepository Repo { get; init; }

        public required InventoryAlertScanJobHandler Job { get; init; }

        public required InventoryAlertService Service { get; init; }

        public required StubAlertStockScanQueryService Stock { get; init; }

        public required StubItemQueryService Items { get; init; }

        public required StubAlertWarehouseQueryService Warehouses { get; init; }

        public required StubAlertParameterProvider Parameters { get; init; }

        public required BasRecordingEventBus Events { get; init; }
    }

    private static Harness Build()
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        var db = new WmsTenantDbContext(
            new JobStubTenantDatabaseResolver(),
            TenantId,
            new DbContextOptionsBuilder<WmsTenantDbContext>().UseSqlite(connection).Options);
        _ = db.Database.EnsureCreated();

        var repo = new WmsTenantRepository(db);
        var tenantCtx = new TestTenantContext(TenantId);
        var user = new TestCurrentUserContext(Operator);
        var items = new StubItemQueryService
        {
            Rows =
            {
                [ItemA] = new StockItemQueryRow(ItemA, false, true, 5m, 1),
                [ItemB] = new StockItemQueryRow(ItemB, false, false, 3m, 2),
            },
        };
        var warehouses = new StubAlertWarehouseQueryService
        {
            Rows =
            {
                [WhEnabled] = new WarehouseQueryRow(WhEnabled, null, "WH-A", "启用仓", 1, 1),
                [WhDisabled] = new WarehouseQueryRow(WhDisabled, null, "WH-B", "停用仓", 1, 2),
            },
        };
        var stock = new StubAlertStockScanQueryService();
        var parameters = new StubAlertParameterProvider();
        var events = new BasRecordingEventBus();

        return new Harness
        {
            Db = db,
            Repo = repo,
            Stock = stock,
            Items = items,
            Warehouses = warehouses,
            Parameters = parameters,
            Events = events,
            Job = new InventoryAlertScanJobHandler(
                repo, stock, items, warehouses, parameters, events, tenantCtx,
                NullLogger<InventoryAlertScanJobHandler>.Instance),
            Service = new InventoryAlertService(repo, user),
        };
    }

    private static Task<List<InventoryAlert>> LoadAlertsAsync(Harness h, InventoryAlertType? type = null)
        => h.Db.InventoryAlerts.AsNoTracking()
            .Where(a => type == null || a.AlertType == type.Value)
            .OrderBy(a => a.Id)
            .ToListAsync(TestContext.Current.CancellationToken);

    // ============ 四类阈值 ============

    [Fact]
    public async Task SafetyStockOnlyAlertsEnabledItemAndWarehouse()
    {
        Harness h = Build();
        h.Parameters.Ints[InventoryAlertScanJobHandler.ParameterSafetyStock] = 10;
        h.Stock.Balances.AddRange(
        [
            new StockBalanceScanRow(1, ItemA, WhEnabled, 5m, Now.AddDays(-1)),
            new StockBalanceScanRow(2, ItemA, WhDisabled, 0m, Now.AddDays(-1)),
            new StockBalanceScanRow(3, ItemB, WhEnabled, 1m, Now.AddDays(-1)),
            new StockBalanceScanRow(4, ItemA, WhEnabled, 12m, Now.AddDays(-1)),
        ]);

        JobScanResult result = await ScanAsync(h);

        Assert.Equal(1, result.Created);
        List<InventoryAlert> alerts = await LoadAlertsAsync(h, InventoryAlertType.SafetyStock);
        InventoryAlert alert = Assert.Single(alerts);
        Assert.Equal(ItemA, alert.ItemId);
        Assert.Equal(WhEnabled, alert.WarehouseId);
        Assert.Equal(string.Empty, alert.BatchNo);
        Assert.Equal(10m, alert.Threshold);
        Assert.Equal(5m, alert.ActualValue);
        Assert.Equal(new DateOnly(2026, 10, 26), alert.AlertDate);
        Assert.Equal(InventoryAlertStatus.Pending, alert.Status);
    }

    [Fact]
    public async Task SafetyStockDisabledWhenThresholdIsZero()
    {
        Harness h = Build();
        h.Parameters.Ints[InventoryAlertScanJobHandler.ParameterSafetyStock] = 0;
        h.Stock.Balances.Add(new StockBalanceScanRow(1, ItemA, WhEnabled, 0m, Now.AddDays(-1)));

        JobScanResult result = await ScanAsync(h);

        Assert.Equal(0, result.SafetyStock);
        Assert.Empty(await LoadAlertsAsync(h));
        Assert.Empty(h.Events.DurableEvents);
    }

    [Fact]
    public async Task StagnantUsesLastTxnDeadlineAndSkipsRowsWithoutTxn()
    {
        Harness h = Build();
        h.Parameters.Ints[InventoryAlertScanJobHandler.ParameterStagnantDays] = 90;
        h.Stock.Balances.AddRange(
        [
            new StockBalanceScanRow(1, ItemA, WhEnabled, 10m, Now.AddDays(-100)),
            new StockBalanceScanRow(2, ItemA, WhEnabled, 0m, Now.AddDays(-100)),
            new StockBalanceScanRow(3, ItemA, WhEnabled, 10m, Now.AddDays(-10)),
            new StockBalanceScanRow(4, ItemA, WhEnabled, 10m, null),
        ]);

        JobScanResult result = await ScanAsync(h);

        Assert.Equal(1, result.Stagnant);
        InventoryAlert alert = Assert.Single(await LoadAlertsAsync(h, InventoryAlertType.Stagnant));
        Assert.Equal(90m, alert.Threshold);
        Assert.Equal(100m, alert.ActualValue);
    }

    [Fact]
    public async Task NearExpiryRequiresQtyAndExpiryEnabledItem()
    {
        Harness h = Build();
        h.Parameters.Ints[InventoryAlertScanJobHandler.ParameterExpiryDays] = 30;
        h.Stock.Batches.AddRange(
        [
            new StockBatchScanRow(1, ItemA, WhEnabled, "B-1", Now.AddDays(10), 5m),
            new StockBatchScanRow(2, ItemB, WhEnabled, "B-2", Now.AddDays(10), 5m),
            new StockBatchScanRow(3, ItemA, WhEnabled, "B-3", Now.AddDays(10), 0m),
            new StockBatchScanRow(4, ItemA, WhEnabled, "B-4", Now.AddDays(60), 5m),
        ]);

        JobScanResult result = await ScanAsync(h);

        Assert.Equal(1, result.NearExpiry);
        InventoryAlert alert = Assert.Single(await LoadAlertsAsync(h, InventoryAlertType.NearExpiry));
        Assert.Equal("B-1", alert.BatchNo);
        Assert.Equal(30m, alert.Threshold);
        Assert.Equal(10m, alert.ActualValue);
    }

    [Fact]
    public async Task InTransitOverdueUsesShippedAtAndDestinationWarehouse()
    {
        Harness h = Build();
        h.Parameters.Ints[InventoryAlertScanJobHandler.ParameterInTransitDays] = 7;
        await AddInTransitAsync(h, 5002L, closed: false, Now.AddDays(-10));
        await AddInTransitAsync(h, 5003L, closed: true, Now.AddDays(-10));
        await AddInTransitAsync(h, 5004L, closed: false, Now.AddDays(-1));

        JobScanResult result = await ScanAsync(h);

        Assert.Equal(1, result.InTransitOverdue);
        InventoryAlert alert = Assert.Single(await LoadAlertsAsync(h, InventoryAlertType.InTransitOverdue));
        Assert.Equal(WhDisabled, alert.WarehouseId);
        Assert.Equal(7m, alert.Threshold);
        Assert.Equal(10m, alert.ActualValue);
    }

    // ============ 幂等 / 状态 ============

    [Fact]
    public async Task SecondScanSameDayCreatesNothingAndPublishesNothing()
    {
        Harness h = Build();
        h.Parameters.Ints[InventoryAlertScanJobHandler.ParameterSafetyStock] = 10;
        h.Stock.Balances.Add(new StockBalanceScanRow(1, ItemA, WhEnabled, 3m, Now.AddDays(-1)));

        JobScanResult first = await ScanAsync(h);
        JobScanResult second = await ScanAsync(h);

        Assert.Equal(1, first.Created);
        Assert.Equal(0, second.Created);
        Assert.Single(await LoadAlertsAsync(h));
        Assert.Single(h.Events.DurableEvents.OfType<InventoryWarningRaisedEvent>());
    }

    [Fact]
    public async Task HandledAlertDoesNotReappearSameDay()
    {
        Harness h = Build();
        h.Parameters.Ints[InventoryAlertScanJobHandler.ParameterSafetyStock] = 10;
        h.Stock.Balances.Add(new StockBalanceScanRow(1, ItemA, WhEnabled, 3m, Now.AddDays(-1)));
        _ = await ScanAsync(h);
        InventoryAlert alert = Assert.Single(await LoadAlertsAsync(h));

        _ = await h.Service.HandleAsync(
            alert.Id,
            new HandleInventoryAlertPayload("已补货 20 件"),
            TestContext.Current.CancellationToken);
        JobScanResult again = await ScanAsync(h);

        Assert.Equal(0, again.Created);
        InventoryAlert reloaded = Assert.Single(await LoadAlertsAsync(h));
        Assert.Equal(InventoryAlertStatus.Handled, reloaded.Status);
        Assert.Equal("已补货 20 件", reloaded.HandledNote);
        Assert.Equal(Operator, reloaded.HandledBy);
        Assert.NotNull(reloaded.HandledAt);
    }

    [Fact]
    public async Task HandleRequiresNoteAndRejectsRepeatTransition()
    {
        Harness h = Build();
        h.Parameters.Ints[InventoryAlertScanJobHandler.ParameterSafetyStock] = 10;
        h.Stock.Balances.Add(new StockBalanceScanRow(1, ItemA, WhEnabled, 3m, Now.AddDays(-1)));
        _ = await ScanAsync(h);
        InventoryAlert alert = Assert.Single(await LoadAlertsAsync(h));

        ValidationException blank = await Assert.ThrowsAsync<ValidationException>(
            () => h.Service.HandleAsync(
                alert.Id,
                new HandleInventoryAlertPayload("   "),
                TestContext.Current.CancellationToken));
        Assert.Contains(blank.FieldErrors, static e => e.FieldName == "handledNote");

        _ = await h.Service.HandleAsync(
            alert.Id,
            new HandleInventoryAlertPayload("已处理"),
            TestContext.Current.CancellationToken);

        ConflictException conflict = await Assert.ThrowsAsync<ConflictException>(
            () => h.Service.HandleAsync(
                alert.Id,
                new HandleInventoryAlertPayload("再次处理"),
                TestContext.Current.CancellationToken));
        Assert.Equal(InventoryAlertService.ErrorInvalidState, conflict.ErrorCode);
    }

    [Fact]
    public async Task IgnoreMarksAlertIgnored()
    {
        Harness h = Build();
        h.Parameters.Ints[InventoryAlertScanJobHandler.ParameterSafetyStock] = 10;
        h.Stock.Balances.Add(new StockBalanceScanRow(1, ItemA, WhEnabled, 3m, Now.AddDays(-1)));
        _ = await ScanAsync(h);
        InventoryAlert alert = Assert.Single(await LoadAlertsAsync(h));

        InventoryAlertRow row = await h.Service.IgnoreAsync(
            alert.Id, TestContext.Current.CancellationToken);

        Assert.Equal((int)InventoryAlertStatus.Ignored, row.Status);
        Assert.Null(row.HandledNote);
        Assert.Equal(Operator, row.HandledBy);
    }

    // ============ 批量上限 ============

    [Fact]
    public async Task BatchLimitPagesThroughAllRowsAndPublishesEventPerAlert()
    {
        Harness h = Build();
        h.Parameters.Ints[InventoryAlertScanJobHandler.ParameterSafetyStock] = 2;
        for (int index = 1; index <= 501; index++)
        {
            long itemId = 20_000L + index;
            h.Items.Rows[itemId] = new StockItemQueryRow(itemId, false, false, 1m, 1);
            h.Stock.Balances.Add(new StockBalanceScanRow(index, itemId, WhEnabled, 1m, Now.AddDays(-1)));
        }

        JobScanResult result = await ScanAsync(h);

        Assert.Equal(501, result.Created);
        Assert.Equal(501, (await LoadAlertsAsync(h)).Count);
        Assert.Equal(501, h.Events.DurableEvents.OfType<InventoryWarningRaisedEvent>().Count());
    }

    // ============ 内部工具 ============

    private static async Task<JobScanResult> ScanAsync(Harness h)
    {
        JobScanResult before = CountAll(h);
        _ = await h.Job.ScanOnceAsync(Now, TestContext.Current.CancellationToken);
        JobScanResult after = CountAll(h);
        return new JobScanResult(
            after.SafetyStock - before.SafetyStock,
            after.Stagnant - before.Stagnant,
            after.NearExpiry - before.NearExpiry,
            after.InTransitOverdue - before.InTransitOverdue);
    }

    private static JobScanResult CountAll(Harness h)
        => new(
            CountByType(h, InventoryAlertType.SafetyStock),
            CountByType(h, InventoryAlertType.Stagnant),
            CountByType(h, InventoryAlertType.NearExpiry),
            CountByType(h, InventoryAlertType.InTransitOverdue));

    private static int CountByType(Harness h, InventoryAlertType type)
        => h.Db.InventoryAlerts.AsNoTracking().Count(a => a.AlertType == type);

    private static async Task AddInTransitAsync(
        Harness h, long lineId, bool closed, DateTimeOffset shippedAt)
    {
        InTransitStock stock = new(
            TenantId,
            transferId: 5001,
            transferLineId: lineId,
            transferNo: "TR-8801",
            itemId: ItemA,
            fromWarehouseId: WhEnabled,
            toWarehouseId: WhDisabled,
            batchNo: null,
            qty: 5m,
            unitCost: 1m,
            shippedAt,
            expectArrive: shippedAt.AddDays(3));
        if (closed)
        {
            stock.Close();
        }

        await h.Repo.AddInTransitStockAsync(stock, TestContext.Current.CancellationToken);
        _ = await h.Repo.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>本轮扫描统计（直接从库中按类型计数，避免依赖返回载荷）。</summary>
    private sealed record JobScanResult(int SafetyStock, int Stagnant, int NearExpiry, int InTransitOverdue)
    {
        public int Created => SafetyStock + Stagnant + NearExpiry + InTransitOverdue;
    }
}

/// <summary>预警扫描源数据桩：台账/批次行按 Id 游标分页。</summary>
internal sealed class StubAlertStockScanQueryService : IStockAlertScanQueryService
{
    public List<StockBalanceScanRow> Balances { get; } = [];

    public List<StockBatchScanRow> Batches { get; } = [];

    public Task<IReadOnlyList<StockBalanceScanRow>> ListBalancePageAsync(
        long afterId, int limit, CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<StockBalanceScanRow>>(
            [.. Balances.Where(row => row.Id > afterId).OrderBy(static row => row.Id).Take(limit)]);

    public Task<IReadOnlyList<StockBatchScanRow>> ListBatchPageAsync(
        long afterId, int limit, CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<StockBatchScanRow>>(
            [.. Batches.Where(row => row.Id > afterId).OrderBy(static row => row.Id).Take(limit)]);
}

/// <summary>仓库只读查询桩：可配置仓库集合。</summary>
internal sealed class StubAlertWarehouseQueryService : IWarehouseQueryService
{
    public Dictionary<long, WarehouseQueryRow> Rows { get; } = [];

    public Task<IReadOnlyList<WarehouseQueryRow>> ListAllAsync(CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<WarehouseQueryRow>>([.. Rows.Values.OrderBy(static row => row.Id)]);

    public Task<IReadOnlyList<WarehouseQueryRow>> ListByOrgAsync(
        long orgId, CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<WarehouseQueryRow>>(
            [.. Rows.Values.Where(row => row.OrgId == orgId).OrderBy(static row => row.Id)]);

    public Task<WarehouseQueryRow?> FindAsync(long id, CancellationToken cancellationToken)
        => Task.FromResult(Rows.TryGetValue(id, out WarehouseQueryRow? row) ? row : null);

    public Task<IReadOnlyList<WarehouseQueryRow>> FindByIdsAsync(
        IReadOnlyList<long> ids, CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<WarehouseQueryRow>>(
            [.. ids.Where(Rows.ContainsKey).Select(id => Rows[id])]);
}

/// <summary>系统参数桩：整数参数按键覆盖，未覆盖回落 fallback。</summary>
internal sealed class StubAlertParameterProvider : ISystemParameterProvider
{
    public Dictionary<string, int> Ints { get; } = new(StringComparer.Ordinal);

    public Task<string> GetStringAsync(
        string paramKey, string fallback, CancellationToken cancellationToken)
        => Task.FromResult(Ints.TryGetValue(paramKey, out int value)
            ? value.ToString(System.Globalization.CultureInfo.InvariantCulture)
            : fallback);

    public Task<int> GetIntAsync(string paramKey, int fallback, CancellationToken cancellationToken)
        => Task.FromResult(Ints.TryGetValue(paramKey, out int value) ? value : fallback);
}
