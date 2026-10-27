/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-26 23:40:00 +08:00 中国标准时间 星期一
 * 描述：Wms × Stk 跨模块联调测试——Wms 作业完成只写 Outbox，由分发任务逐条投递真实
 *       StockLedgerService 落账；覆盖入库/加权平均出库/调拨在途/盘点冻结与解冻/盘盈亏/
 *       幂等重投/预警扫描的端到端闭环
 */

using System.Security.Claims;
using System.Text.Json;

using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using Qlx.Abstractions.Constants;
using Qlx.Abstractions.Events.Wms;
using Qlx.Abstractions.Jobs;
using Qlx.Abstractions.Queries;
using Qlx.Abstractions.Serialization;
using Qlx.Abstractions.Stock;
using Qlx.BuildingBlocks.Caching;
using Qlx.BuildingBlocks.Tests.Bas;
using Qlx.BuildingBlocks.Tests.Files;
using Qlx.BuildingBlocks.Tests.Jobs;
using Qlx.BuildingBlocks.Tests.Stk;
using Qlx.Modules.Stk;
using Qlx.Modules.Stk.Domain.Inventory;
using Qlx.Modules.Stk.Domain.Item;
using Qlx.Modules.Stk.Repositories;
using Qlx.Modules.Stk.Services;
using Qlx.Modules.Wms;
using Qlx.Modules.Wms.Domain.Adjust;
using Qlx.Modules.Wms.Domain.Outbox;
using Qlx.Modules.Wms.Domain.Transfer;
using Qlx.Modules.Wms.Dtos;
using Qlx.Modules.Wms.Jobs;
using Qlx.Modules.Wms.Repositories;
using Qlx.Modules.Wms.Services;

namespace Qlx.BuildingBlocks.Tests.Wms;

/// <summary>
/// Wms 作业域与 Stk 台账域的跨模块联调测试。两个模块各自使用独立的内存 SQLite 库，
/// Wms 侧只写 Outbox，由分发任务注入**真实** <see cref="StockLedgerService"/> 逐条投递落账。
/// </summary>
public sealed class WmsIntegrationTests
{
    private const long TenantId = 101L;
    private const string Operator = "user-101";
    private const long OrgId = 9001L;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>双库联调夹具：Wms 上下文 + Stk 上下文 + 真实台账服务 + 分发任务。</summary>
    private sealed class Harness : IAsyncDisposable
    {
        public required SqliteConnection WmsConnection { get; init; }

        public required SqliteConnection StkConnection { get; init; }

        public required WmsTenantDbContext WmsDb { get; init; }

        public required StkTenantDbContext StkDb { get; init; }

        public required WmsTenantRepository WmsRepo { get; init; }

        public required StkTenantRepository StkRepo { get; init; }

        public required StockLedgerService Ledger { get; init; }

        public required StockCommandQueue Queue { get; init; }

        public required WmsStockCommandDispatchJobHandler Dispatch { get; init; }

        public required StubItemQueryService Items { get; init; }

        public required StubAlertWarehouseQueryService Warehouses { get; init; }

        public required StubAlertParameterProvider Parameters { get; init; }

        public required BasRecordingEventBus Events { get; init; }

        public required BasStubApprovalService Approval { get; init; }

        public required WarehouseService Warehouse { get; init; }

        public required StockInService StockIn { get; init; }

        public required StockOutService StockOut { get; init; }

        public required TransferService Transfer { get; init; }

        public required StockTakeService StockTake { get; init; }

        public required InventoryAdjustService Adjust { get; init; }

        public required InventoryAlertScanJobHandler AlertJob { get; init; }

        public async ValueTask DisposeAsync()
        {
            await WmsDb.DisposeAsync().ConfigureAwait(false);
            await StkDb.DisposeAsync().ConfigureAwait(false);
            await WmsConnection.DisposeAsync().ConfigureAwait(false);
            await StkConnection.DisposeAsync().ConfigureAwait(false);
        }
    }

    private static Harness Build()
    {
        // 两个模块必须各用一条独立的内存连接，否则 EnsureCreated 会因为「库已存在」跳过建表
        var wmsConnection = new SqliteConnection("DataSource=:memory:");
        wmsConnection.Open();
        var stkConnection = new SqliteConnection("DataSource=:memory:");
        stkConnection.Open();

        var wmsDb = new WmsTenantDbContext(
            new JobStubTenantDatabaseResolver(),
            TenantId,
            new DbContextOptionsBuilder<WmsTenantDbContext>().UseSqlite(wmsConnection).Options);
        _ = wmsDb.Database.EnsureCreated();

        var stkDb = new StkTenantDbContext(
            new JobStubTenantDatabaseResolver(),
            TenantId,
            new DbContextOptionsBuilder<StkTenantDbContext>().UseSqlite(stkConnection).Options);
        _ = stkDb.Database.EnsureCreated();

        var wmsRepo = new WmsTenantRepository(wmsDb);
        var stkRepo = new StkTenantRepository(stkDb);
        var tenantCtx = new TestTenantContext(TenantId);
        var user = new TestCurrentUserContext(Operator);
        var events = new BasRecordingEventBus();
        var approval = new BasStubApprovalService();
        var items = new StubItemQueryService();
        var numbers = new StubDocumentNumberGenerator();
        var orgQuery = new StubOrgQueryService();
        var stockQuery = new StockQueryService(tenantCtx, stkRepo, BuildHttpAccessor());
        var alertWarehouses = new StubAlertWarehouseQueryService();
        var alertParameters = new StubAlertParameterProvider();
        var queue = new StockCommandQueue(wmsDb, tenantCtx);
        var cache = new MemoryCacheService(Options.Create(new CacheOptions()));

        // 分发任务注入真实台账服务（禁止使用桩，否则无法验证跨模块落账）
        var ledger = new StockLedgerService(
            tenantCtx, stkRepo, new StkStubDocumentNumberGenerator(), user, events);

        return new Harness
        {
            WmsConnection = wmsConnection,
            StkConnection = stkConnection,
            WmsDb = wmsDb,
            StkDb = stkDb,
            WmsRepo = wmsRepo,
            StkRepo = stkRepo,
            Ledger = ledger,
            Queue = queue,
            Events = events,
            Approval = approval,
            Items = items,
            Warehouses = alertWarehouses,
            Parameters = alertParameters,
            Dispatch = new WmsStockCommandDispatchJobHandler(
                wmsDb, ledger, NullLogger<WmsStockCommandDispatchJobHandler>.Instance),
            Warehouse = new WarehouseService(
                tenantCtx,
                wmsRepo,
                numbers,
                orgQuery,
                approval,
                stockQuery,
                cache,
                new InMemoryAuditService(),
                new BasStubSnowflakeIdGenerator(),
                user),
            StockIn = new StockInService(
                tenantCtx, wmsRepo, numbers, orgQuery, items, queue, events, user),
            StockOut = new StockOutService(
                tenantCtx,
                wmsRepo,
                numbers,
                orgQuery,
                items,
                stockQuery,
                ledger,
                queue,
                approval,
                new StubOutboundParameterProvider(),
                events,
                user),
            Transfer = new TransferService(
                tenantCtx,
                wmsRepo,
                numbers,
                orgQuery,
                items,
                ledger,
                queue,
                approval,
                new StubOutboundParameterProvider(),
                events,
                user),
            StockTake = new StockTakeService(
                tenantCtx,
                wmsRepo,
                numbers,
                orgQuery,
                items,
                stockQuery,
                ledger,
                new StubStockTakeDraftCreator()),
            Adjust = new InventoryAdjustService(
                tenantCtx, wmsRepo, numbers, orgQuery, items, ledger, queue, approval, user),
            AlertJob = new InventoryAlertScanJobHandler(
                wmsRepo,
                new StockAlertScanQueryService(tenantCtx, stkRepo),
                items,
                alertWarehouses,
                alertParameters,
                events,
                tenantCtx,
                NullLogger<InventoryAlertScanJobHandler>.Instance),
        };
    }

    /// <summary>成本字段权限判定只认令牌声明：这里按 TenantAdmin 构造请求身份。</summary>
    private static HttpContextAccessor BuildHttpAccessor()
    {
        var identity = new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, Operator),
                new Claim(QlxClaimConstants.MemberRole, QlxMemberRoleValues.TenantAdmin),
                new Claim(QlxClaimConstants.Permissions, "stk.cost.field"),
            ],
            authenticationType: "TestAuth");

        return new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) },
        };
    }

    // ============ 公共工具 ============

    private static async Task<long> SeedItemAsync(
        Harness h, bool batchEnabled = false, decimal? refCostPrice = null)
    {
        Item item = new(
            TenantId, $"T-{Guid.NewGuid():N}"[..12], "联调商品", null, null,
            categoryId: 5001L, unitId: 9001L, batchEnabled, expiryEnabled: false,
            taxRate: 0.13m, refCostPrice, guidePrice: null);
        _ = await h.StkDb.Items.AddAsync(item, Ct);
        _ = await h.StkDb.SaveChangesAsync(Ct);

        h.Items.Rows[item.Id] = new StockItemQueryRow(
            item.Id, batchEnabled, ExpiryEnabled: false, refCostPrice, 1, 5001L);
        return item.Id;
    }

    private static Task<WarehouseRow> CreateWarehouseAsync(Harness h, string name)
        => h.Warehouse.CreateAsync(
            new CreateWarehousePayload(name, 1, OrgId, null, null, null), Ct);

    private static Task<CompletedResult> DispatchAsync(Harness h)
        => h.Dispatch.DispatchOnceAsync(DateTimeOffset.UtcNow, Ct);

    /// <summary>入库：建单 → 开始 → 完成（写 Outbox）→ 分发落账。</summary>
    private static async Task CompleteInboundAsync(
        Harness h, long warehouseId, long itemId, decimal qty, decimal unitCost, string sourceDocNo)
    {
        StockInTaskDetailRow task = await h.StockIn.CreateAsync(
            new CreateStockInTaskPayload(
                1, sourceDocNo, warehouseId, null,
                [new CreateStockInLinePayload(itemId, qty, null, null, unitCost)]),
            Ct);
        _ = await h.StockIn.StartAsync(task.Task.Id, Ct);

        long lineId = Assert.Single(task.Lines).Id;
        _ = await h.StockIn.CompleteAsync(
            task.Task.Id,
            new CompleteStockInTaskPayload(
                [new CompleteStockInLinePayload(lineId, qty, qty, null, null, unitCost)]),
            Ct);

        _ = await DispatchAsync(h);
    }

    /// <summary>出库：建单 → 拣货 → 复核（不完成，交由用例决定是否完成）。</summary>
    private static async Task<StockOutTaskDetailRow> PrepareOutboundAsync(
        Harness h, long warehouseId, long itemId, decimal qty, string sourceDocNo)
    {
        StockOutTaskDetailRow task = await h.StockOut.CreateAsync(
            new CreateStockOutTaskPayload(
                1, sourceDocNo, warehouseId, null,
                [new CreateStockOutLinePayload(itemId, qty, null, null)]),
            Ct);
        long lineId = Assert.Single(task.Lines).Id;
        _ = await h.StockOut.PickAsync(
            task.Task.Id, new PickStockOutTaskPayload([new PickStockOutLinePayload(lineId, qty, null, null)]), Ct);

        return await h.StockOut.CheckAsync(
            task.Task.Id,
            new CheckStockOutTaskPayload("handover-01", [new CheckStockOutLinePayload(lineId, qty)]),
            Ct);
    }

    private static async Task<List<WmsStockCommandOutbox>> LoadOutboxAsync(Harness h)
        => await h.WmsDb.StockCommandOutbox.AsNoTracking()
            .OrderBy(static row => row.Id)
            .ToListAsync(Ct);

    private static async Task<List<StockTxn>> LoadTxnsAsync(Harness h, long itemId)
        => await h.StkDb.StockTxns.AsNoTracking()
            .Where(txn => txn.ItemId == itemId)
            .OrderBy(static txn => txn.Id)
            .ToListAsync(Ct);

    private static Task<StockBalance> LoadBalanceAsync(Harness h, long itemId, long warehouseId)
        => h.StkDb.StockBalances.AsNoTracking()
            .SingleAsync(balance => balance.ItemId == itemId && balance.WarehouseId == warehouseId, Ct);

    private static PostStockTxnRequest PayloadOf(WmsStockCommandOutbox row)
        => JsonSerializer.Deserialize<PostStockTxnRequest>(row.PayloadJson, QlxJsonDefaults.PreserveNull)!;

    // ============ 一、入库完成 → Outbox → 台账入库 ============

    [Fact]
    public async Task StockInCompletionDispatchesPurchaseInToLedger()
    {
        await using Harness h = Build();
        long itemId = await SeedItemAsync(h);
        long warehouseId = (await CreateWarehouseAsync(h, "入库仓")).Id;

        await CompleteInboundAsync(h, warehouseId, itemId, qty: 5m, unitCost: 8m, "PO-IT-0001");

        WmsStockCommandOutbox outbox = Assert.Single(await LoadOutboxAsync(h));
        Assert.Equal(WmsStockCommandState.Dispatched, outbox.State);
        Assert.NotNull(outbox.DispatchedAt);

        StockTxn txn = Assert.Single(await LoadTxnsAsync(h, itemId));
        Assert.Equal((int)StockSourceType.PurchaseIn, txn.SourceType);
        Assert.Equal((int)StockDirection.In, txn.Direction);
        Assert.Equal(warehouseId, txn.WarehouseId);
        Assert.Equal(5m, txn.Qty);
        Assert.Equal(8m, txn.UnitCost);
        Assert.Equal(40m, txn.Amount);
        Assert.Equal(5m, txn.BalanceQtyAfter);
        Assert.Equal(40m, txn.BalanceAmountAfter);

        StockBalance balance = await LoadBalanceAsync(h, itemId, warehouseId);
        Assert.Equal(5m, balance.QtyOnhand);
        Assert.Equal(8m, balance.AvgCost);
        Assert.Equal(40m, balance.Amount);
        Assert.Equal(0m, balance.QtyLocked);
    }

    // ============ 二、两次入库 → 加权平均出库 ============

    [Fact]
    public async Task StockOutDispatchesSaleOutAtWeightedAverageAfterTwoInbounds()
    {
        await using Harness h = Build();
        long itemId = await SeedItemAsync(h);
        long warehouseId = (await CreateWarehouseAsync(h, "出库仓")).Id;

        await CompleteInboundAsync(h, warehouseId, itemId, qty: 10m, unitCost: 10m, "PO-IT-0002A");
        await CompleteInboundAsync(h, warehouseId, itemId, qty: 10m, unitCost: 20m, "PO-IT-0002B");

        StockOutTaskDetailRow pending = await PrepareOutboundAsync(
            h, warehouseId, itemId, qty: 8m, "SO-IT-0002");
        StockOutCompleteResultRow completed = await h.StockOut.CompleteAsync(pending.Task.Id, Ct);

        Assert.False(completed.ApprovalRequired);
        Assert.Equal(120m, completed.TotalAmount);

        _ = await DispatchAsync(h);

        StockTxn outTxn = Assert.Single(
            await LoadTxnsAsync(h, itemId), static txn => txn.Direction == (int)StockDirection.Out);
        Assert.Equal((int)StockSourceType.SaleOut, outTxn.SourceType);
        Assert.Equal(8m, outTxn.Qty);
        Assert.Equal(15m, outTxn.UnitCost);
        Assert.Equal(120m, outTxn.Amount);
        Assert.Equal(12m, outTxn.BalanceQtyAfter);
        Assert.Equal(180m, outTxn.BalanceAmountAfter);

        StockBalance balance = await LoadBalanceAsync(h, itemId, warehouseId);
        Assert.Equal(12m, balance.QtyOnhand);
        Assert.Equal(15m, balance.AvgCost);
        Assert.Equal(180m, balance.Amount);

        List<WmsStockCommandOutbox> outbox = await LoadOutboxAsync(h);
        Assert.Equal(3, outbox.Count);
        Assert.All(outbox, static row => Assert.Equal(WmsStockCommandState.Dispatched, row.State));
    }

    // ============ 三、调拨发货 / 收货 → 转出、转入 + 在途关闭 ============

    [Fact]
    public async Task TransferShipAndReceiveDispatchTransferOutAndInWithClosedInTransit()
    {
        await using Harness h = Build();
        long itemId = await SeedItemAsync(h);
        WarehouseRow from = await CreateWarehouseAsync(h, "调出仓");
        WarehouseRow to = await CreateWarehouseAsync(h, "调入仓");

        await CompleteInboundAsync(h, from.Id, itemId, qty: 10m, unitCost: 8m, "PO-IT-0003");

        StockTransferDetailRow created = await h.Transfer.CreateAsync(
            new CreateStockTransferPayload(
                from.Id, to.Id, null, null, [new CreateStockTransferLinePayload(itemId, 10m, null)]),
            Ct);
        await h.Transfer.ApplyApprovalAsync(created.Transfer.Id, approved: true, Ct);

        long lineId = Assert.Single(created.Lines).Id;
        _ = await h.Transfer.ShipAsync(
            created.Transfer.Id,
            new ShipStockTransferPayload([new ShipStockTransferLinePayload(lineId, 4m)]),
            Ct);
        _ = await DispatchAsync(h);

        StockTxn outTxn = Assert.Single(
            await LoadTxnsAsync(h, itemId),
            static txn => txn.SourceType == (int)StockSourceType.TransferOut);
        Assert.Equal((int)StockDirection.Out, outTxn.Direction);
        Assert.Equal(4m, outTxn.Qty);
        Assert.Equal(8m, outTxn.UnitCost);
        Assert.Equal(32m, outTxn.Amount);
        Assert.Equal(6m, outTxn.BalanceQtyAfter);
        Assert.Equal(48m, outTxn.BalanceAmountAfter);

        InTransitStock open = Assert.Single(await h.WmsDb.InTransitStocks.AsNoTracking().ToListAsync(Ct));
        Assert.Equal(InTransitStatus.InTransit, open.Status);
        Assert.Equal(4m, open.Qty);

        _ = await h.Transfer.ReceiveAsync(
            created.Transfer.Id,
            new ReceiveStockTransferPayload([new ReceiveStockTransferLinePayload(lineId, 4m, null)]),
            Ct);
        _ = await DispatchAsync(h);

        StockTxn inTxn = Assert.Single(
            await LoadTxnsAsync(h, itemId),
            static txn => txn.SourceType == (int)StockSourceType.TransferIn);
        Assert.Equal((int)StockDirection.In, inTxn.Direction);
        Assert.Equal(to.Id, inTxn.WarehouseId);
        Assert.Equal(4m, inTxn.Qty);
        Assert.Equal(8m, inTxn.UnitCost);
        Assert.Equal(32m, inTxn.Amount);

        StockBalance toBalance = await LoadBalanceAsync(h, itemId, to.Id);
        Assert.Equal(4m, toBalance.QtyOnhand);
        Assert.Equal(8m, toBalance.AvgCost);
        Assert.Equal(32m, toBalance.Amount);

        InTransitStock closed = Assert.Single(await h.WmsDb.InTransitStocks.AsNoTracking().ToListAsync(Ct));
        Assert.Equal(InTransitStatus.Closed, closed.Status);

        Assert.All(
            (await LoadOutboxAsync(h)).Where(static row =>
                PayloadOf(row).SourceType is StockSourceType.TransferOut or StockSourceType.TransferIn),
            static row => Assert.Equal(WmsStockCommandState.Dispatched, row.State));
    }

    // ============ 四、盘点冻结 → 出库分发被台账拦截 ============

    [Fact]
    public async Task StockTakeFreezeBlocksOutboundAndDispatchFailsWithInsufficient()
    {
        await using Harness h = Build();
        long itemId = await SeedItemAsync(h);
        long warehouseId = (await CreateWarehouseAsync(h, "冻结仓")).Id;

        await CompleteInboundAsync(h, warehouseId, itemId, qty: 10m, unitCost: 5m, "PO-IT-0004");

        StockTakePlanRow plan = await h.StockTake.CreatePlanAsync(
            new CreateStockTakePlanPayload(
                new DateOnly(2026, 10, 26), 1, warehouseId, null, null, false, null),
            Ct);
        _ = await h.StockTake.GenerateAsync(plan.Id, Ct);

        StockBalance frozen = await LoadBalanceAsync(h, itemId, warehouseId);
        Assert.Equal(10m, frozen.QtyLocked);
        Assert.Equal(10m, frozen.QtyOnhand);

        StockOutTaskDetailRow pending = await PrepareOutboundAsync(
            h, warehouseId, itemId, qty: 5m, "SO-IT-0004");
        _ = await h.StockOut.CompleteAsync(pending.Task.Id, Ct);

        _ = await DispatchAsync(h);

        List<WmsStockCommandOutbox> outbox = await LoadOutboxAsync(h);
        WmsStockCommandOutbox failed = Assert.Single(
            outbox, static row => row.State == WmsStockCommandState.Failed);
        Assert.Equal("STK_INSUFFICIENT", failed.LastError);
        Assert.Equal(1, failed.RetryCount);
        Assert.False(failed.IsTerminalFailure);

        // 冻结量全部占用，出库分发不得新增任何流水
        _ = Assert.Single(await LoadTxnsAsync(h, itemId));
        Assert.Equal(10m, (await LoadBalanceAsync(h, itemId, warehouseId)).QtyOnhand);
    }

    // ============ 五、盘点取消 / 无差异完成 → 冻结解除 ============

    [Fact]
    public async Task StockTakeFreezeIsReleasedOnCancelAndOnFinish()
    {
        await using Harness h = Build();
        long itemId = await SeedItemAsync(h);
        long warehouseId = (await CreateWarehouseAsync(h, "解冻仓")).Id;

        await CompleteInboundAsync(h, warehouseId, itemId, qty: 10m, unitCost: 5m, "PO-IT-0005");

        StockTakePlanRow firstPlan = await h.StockTake.CreatePlanAsync(
            new CreateStockTakePlanPayload(
                new DateOnly(2026, 10, 26), 1, warehouseId, null, null, false, null),
            Ct);
        StockTakeDetailRow first = await h.StockTake.GenerateAsync(firstPlan.Id, Ct);
        Assert.Equal(10m, (await LoadBalanceAsync(h, itemId, warehouseId)).QtyLocked);

        _ = await h.StockTake.StartAsync(first.Take.Id, Ct);
        _ = await h.StockTake.CancelAsync(first.Take.Id, Ct);
        Assert.Equal(0m, (await LoadBalanceAsync(h, itemId, warehouseId)).QtyLocked);

        StockTakePlanRow secondPlan = await h.StockTake.CreatePlanAsync(
            new CreateStockTakePlanPayload(
                new DateOnly(2026, 10, 26), 1, warehouseId, null, null, false, null),
            Ct);
        StockTakeDetailRow second = await h.StockTake.GenerateAsync(secondPlan.Id, Ct);
        Assert.Equal(10m, (await LoadBalanceAsync(h, itemId, warehouseId)).QtyLocked);

        _ = await h.StockTake.StartAsync(second.Take.Id, Ct);
        StockTakeLineRow line = Assert.Single(second.Lines);
        _ = await h.StockTake.SaveCountsAsync(
            second.Take.Id,
            new SaveStockTakeCountsPayload(
                [new SaveStockTakeCountLinePayload(line.Id, line.BookQty!.Value, null)]),
            Ct);
        StockTakeFinishResultRow finished = await h.StockTake.FinishAsync(second.Take.Id, Ct);

        Assert.Equal(5, finished.Detail.Take.Status);
        Assert.Equal(0m, (await LoadBalanceAsync(h, itemId, warehouseId)).QtyLocked);
        _ = Assert.Single(await LoadTxnsAsync(h, itemId));
    }

    // ============ 六、盘亏审批通过 → Outbox → 台账出库 ============

    [Fact]
    public async Task LossAdjustApprovalDispatchesLossToLedger()
    {
        await using Harness h = Build();
        long itemId = await SeedItemAsync(h);
        long warehouseId = (await CreateWarehouseAsync(h, "盘亏仓")).Id;

        await CompleteInboundAsync(h, warehouseId, itemId, qty: 10m, unitCost: 10m, "PO-IT-0006");

        InventoryAdjustDetailRow detail = await h.Adjust.CreateAsync(
            new CreateInventoryAdjustPayload(
                (int)AdjustType.Loss, warehouseId, "联调盘亏", null,
                [new CreateInventoryAdjustLinePayload(itemId, null, 3m, null)]),
            Ct);
        _ = await h.Adjust.SubmitAsync(detail.Adjust.Id, Ct);
        Assert.True(await h.Adjust.ApplyAsync(detail.Adjust.Id, approved: true, Ct));

        PostStockTxnRequest payload = PayloadOf(Assert.Single(
            await LoadOutboxAsync(h), static row => PayloadOf(row).SourceType == StockSourceType.Loss));
        Assert.Equal(StockDirection.Out, payload.Direction);
        Assert.Equal(3m, payload.Qty);
        Assert.Equal(0m, payload.UnitCost);
        Assert.StartsWith("wms:py:", payload.IdempotencyKey, StringComparison.Ordinal);

        _ = await DispatchAsync(h);

        StockTxn txn = Assert.Single(
            await LoadTxnsAsync(h, itemId), static row => row.SourceType == (int)StockSourceType.Loss);
        Assert.Equal((int)StockDirection.Out, txn.Direction);
        Assert.Equal(3m, txn.Qty);
        Assert.Equal(10m, txn.UnitCost);
        Assert.Equal(30m, txn.Amount);
        Assert.Equal(7m, txn.BalanceQtyAfter);
        Assert.Equal(70m, txn.BalanceAmountAfter);

        StockBalance balance = await LoadBalanceAsync(h, itemId, warehouseId);
        Assert.Equal(7m, balance.QtyOnhand);
        Assert.Equal(10m, balance.AvgCost);
        Assert.Equal(70m, balance.Amount);
    }

    // ============ 七、盘盈审批通过 → Outbox → 台账入库 ============

    [Fact]
    public async Task GainAdjustApprovalDispatchesGainToLedger()
    {
        await using Harness h = Build();
        long itemId = await SeedItemAsync(h);
        long warehouseId = (await CreateWarehouseAsync(h, "盘盈仓")).Id;

        await CompleteInboundAsync(h, warehouseId, itemId, qty: 10m, unitCost: 10m, "PO-IT-0007");

        InventoryAdjustDetailRow detail = await h.Adjust.CreateAsync(
            new CreateInventoryAdjustPayload(
                (int)AdjustType.Gain, warehouseId, "联调盘盈", null,
                [new CreateInventoryAdjustLinePayload(itemId, null, 2m, null)]),
            Ct);
        _ = await h.Adjust.SubmitAsync(detail.Adjust.Id, Ct);
        Assert.True(await h.Adjust.ApplyAsync(detail.Adjust.Id, approved: true, Ct));

        _ = await DispatchAsync(h);

        StockTxn txn = Assert.Single(
            await LoadTxnsAsync(h, itemId), static row => row.SourceType == (int)StockSourceType.Gain);
        Assert.Equal((int)StockDirection.In, txn.Direction);
        Assert.Equal(2m, txn.Qty);
        Assert.Equal(10m, txn.UnitCost);
        Assert.Equal(20m, txn.Amount);
        Assert.Equal(12m, txn.BalanceQtyAfter);
        Assert.Equal(120m, txn.BalanceAmountAfter);

        StockBalance balance = await LoadBalanceAsync(h, itemId, warehouseId);
        Assert.Equal(12m, balance.QtyOnhand);
        Assert.Equal(10m, balance.AvgCost);
        Assert.Equal(120m, balance.Amount);
    }

    // ============ 八、台账幂等 + Outbox 重投 ============

    [Fact]
    public async Task DuplicatePostAndOutboxRedeliveryWriteSingleTxn()
    {
        await using Harness h = Build();
        long itemId = await SeedItemAsync(h);
        long warehouseId = (await CreateWarehouseAsync(h, "幂等仓")).Id;

        var request = new PostStockTxnRequest(
            StockSourceType.PurchaseIn, "SRC-IT-0008", 1, itemId, warehouseId, null,
            StockDirection.In, 5m, 10m, Operator, "wms:it:0008:1", DateTimeOffset.UtcNow);

        StockPostResult first = await h.Ledger.PostAsync(request, Ct);
        Assert.True(first.Success);
        Assert.NotNull(first.TxnId);

        // 相同 8 列唯一键重复提交：幂等命中按成功返回，且不重复写流水
        StockPostResult second = await h.Ledger.PostAsync(request, Ct);
        Assert.True(second.Success);
        Assert.Equal(first.TxnId, second.TxnId);
        _ = Assert.Single(await LoadTxnsAsync(h, itemId));

        // Outbox 重投：幂等键不同但台账 8 列相同 → 分发视为已分发，仍只有一条流水
        PostStockTxnRequest redelivered = request with { IdempotencyKey = "wms:it:0008:2" };
        Assert.Equal(1, await h.Queue.EnqueueAsync([redelivered], Ct));
        _ = await h.WmsDb.SaveChangesAsync(Ct);

        _ = await DispatchAsync(h);

        WmsStockCommandOutbox row = Assert.Single(await LoadOutboxAsync(h));
        Assert.Equal(WmsStockCommandState.Dispatched, row.State);
        _ = Assert.Single(await LoadTxnsAsync(h, itemId));

        // 已分发行不会被再次扫描
        _ = await DispatchAsync(h);
        _ = Assert.Single(await LoadTxnsAsync(h, itemId));
        Assert.Equal(5m, (await LoadBalanceAsync(h, itemId, warehouseId)).QtyOnhand);
    }

    // ============ 九、预警扫描读取真实台账 ============

    [Fact]
    public async Task AlertScanReadsRealLedgerAndPublishesWarning()
    {
        await using Harness h = Build();
        long itemId = await SeedItemAsync(h);
        WarehouseRow warehouse = await CreateWarehouseAsync(h, "预警仓");

        await CompleteInboundAsync(h, warehouse.Id, itemId, qty: 5m, unitCost: 5m, "PO-IT-0009");

        h.Warehouses.Rows[warehouse.Id] = new WarehouseQueryRow(
            warehouse.Id, warehouse.OrgId, warehouse.Code, warehouse.Name, warehouse.WhType, 1);
        h.Parameters.Ints[InventoryAlertScanJobHandler.ParameterSafetyStock] = 10;

        _ = await h.AlertJob.ScanOnceAsync(DateTimeOffset.UtcNow, Ct);

        InventoryWarningRaisedEvent raised = Assert.Single(
            h.Events.DurableEvents.OfType<InventoryWarningRaisedEvent>());
        Assert.Equal(itemId, raised.ItemId);
        Assert.Equal(warehouse.Id, raised.WarehouseId);

        Assert.Equal(1, await h.WmsDb.InventoryAlerts.AsNoTracking().CountAsync(Ct));
        StockBalance balance = await LoadBalanceAsync(h, itemId, warehouse.Id);
        Assert.Equal(5m, balance.QtyOnhand);
    }
}
