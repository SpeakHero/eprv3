/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-26
 * 描述：调拨与在途测试——同仓阻断、跨公司/内部流程码、发货成本快照与在途行、
 *       无台账 409 WMS_NO_STOCK、收货入账并关闭在途、差异必填、发货后禁止作废、在途扁平列表
 */

using System.Text.Json;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

using Qlx.Abstractions.Approvals;
using Qlx.Abstractions.Exceptions;
using Qlx.Abstractions.Queries;
using Qlx.Abstractions.Serialization;
using Qlx.Abstractions.Stock;
using Qlx.BuildingBlocks.Caching;
using Qlx.BuildingBlocks.Tests.Bas;
using Qlx.BuildingBlocks.Tests.Files;
using Qlx.BuildingBlocks.Tests.Jobs;
using Qlx.Modules.Wms;
using Qlx.Modules.Wms.Domain.Numbering;
using Qlx.Modules.Wms.Domain.Outbox;
using Qlx.Modules.Wms.Domain.Transfer;
using Qlx.Modules.Wms.Dtos;
using Qlx.Modules.Wms.Repositories;
using Qlx.Modules.Wms.Services;

namespace Qlx.BuildingBlocks.Tests.Wms;

/// <summary>调拨与在途测试。</summary>
public sealed class TransferTests
{
    private const long TenantId = 99L;
    private const string Operator = "user-99";
    private const long OrgId = 9001L;
    private const long OtherOrgId = 9002L;

    private const long Item = 7201L;
    private const long BatchItem = 7202L;

    private sealed class Harness
    {
        public required WmsTenantDbContext Db { get; init; }

        public required WarehouseService Warehouse { get; init; }

        public required TransferService Service { get; init; }

        public required StubOutboundTxnCommandService Txn { get; init; }

        public required BasStubApprovalService Approval { get; init; }

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
        var events = new BasRecordingEventBus();
        var approval = new BasStubApprovalService();
        var stock = new StubStockQueryService();
        var txn = new StubOutboundTxnCommandService();
        var items = new StubItemQueryService
        {
            Rows =
            {
                [Item] = new StockItemQueryRow(Item, false, false, 12.5m, 1),
                [BatchItem] = new StockItemQueryRow(BatchItem, true, true, 3m, 1),
            },
        };

        return new Harness
        {
            Db = db,
            Txn = txn,
            Approval = approval,
            Events = events,
            Warehouse = new WarehouseService(
                tenantCtx,
                repo,
                new StubDocumentNumberGenerator(),
                new StubOrgQueryService(),
                approval,
                stock,
                new MemoryCacheService(Options.Create(new CacheOptions())),
                new InMemoryAuditService(),
                new BasStubSnowflakeIdGenerator(),
                user),
            Service = new TransferService(
                tenantCtx,
                repo,
                new StubDocumentNumberGenerator(),
                new StubOrgQueryService(),
                items,
                txn,
                new StockCommandQueue(db, tenantCtx),
                approval,
                new StubOutboundParameterProvider(),
                events,
                user),
        };
    }

    private static Task<WarehouseRow> CreateWarehouseAsync(
        Harness h, string name, long? orgId = OrgId)
        => h.Warehouse.CreateAsync(
            new CreateWarehousePayload(name, 1, orgId, null, null, null),
            TestContext.Current.CancellationToken);

    private static Task<StockTransferDetailRow> CreateTransferAsync(
        Harness h,
        long fromWarehouseId,
        long toWarehouseId,
        IReadOnlyList<CreateStockTransferLinePayload> lines)
        => h.Service.CreateAsync(
            new CreateStockTransferPayload(fromWarehouseId, toWarehouseId, null, null, lines),
            TestContext.Current.CancellationToken);

    private static CreateStockTransferLinePayload Line(
        long itemId, decimal qty = 10m, string? batchNo = null)
        => new(itemId, qty, batchNo);

    private static async Task<StockTransferDetailRow> ToEffectiveAsync(Harness h)
    {
        WarehouseRow from = await CreateWarehouseAsync(h, "调出仓");
        WarehouseRow to = await CreateWarehouseAsync(h, "调入仓");
        StockTransferDetailRow created = await CreateTransferAsync(h, from.Id, to.Id, [Line(Item, 10m)]);
        await h.Service.ApplyApprovalAsync(
            created.Transfer.Id, approved: true, TestContext.Current.CancellationToken);
        return created;
    }

    // ============ 新建（V-1 / 流程码选择） ============

    [Fact]
    public async Task CreateRejectsSameWarehouse()
    {
        Harness h = Build();
        WarehouseRow warehouse = await CreateWarehouseAsync(h, "一号仓");

        ValidationException ex = await Assert.ThrowsAsync<ValidationException>(
            () => CreateTransferAsync(h, warehouse.Id, warehouse.Id, [Line(Item)]));

        Assert.Contains(ex.FieldErrors, static e => e.FieldName == "toWarehouseId");
        Assert.Empty(h.Approval.Started);
    }

    [Fact]
    public async Task CreateSelectsInternalFlowForSameOrg()
    {
        Harness h = Build();
        WarehouseRow from = await CreateWarehouseAsync(h, "调出仓", OrgId);
        WarehouseRow to = await CreateWarehouseAsync(h, "调入仓", OrgId);

        StockTransferDetailRow created = await CreateTransferAsync(h, from.Id, to.Id, [Line(Item)]);

        Assert.Equal(1, created.Transfer.Status);
        Assert.Equal(OrgId, created.Transfer.FromOrgId);
        Assert.Equal(OrgId, created.Transfer.ToOrgId);
        _ = Assert.Single(created.Lines);

        StartApprovalRequest started = Assert.Single(h.Approval.Started);
        Assert.Equal(TransferService.InternalFlowCode, started.ApprovalDefinitionCode);
        Assert.Equal(TransferService.TransferBizType, started.BizType);

        WmsApprovalRequest request =
            Assert.Single(await LoadApprovalsAsync(h));
        Assert.Equal("inst-1", request.ApprovalInstanceId);
        Assert.Equal(TransferService.TransferBizType, request.BizType);
        Assert.Equal(created.Transfer.Id, request.BizId);
    }

    [Fact]
    public async Task CreateSelectsCrossFlowWhenOrgDiffers()
    {
        Harness h = Build();
        WarehouseRow from = await CreateWarehouseAsync(h, "调出仓", OrgId);
        WarehouseRow to = await CreateWarehouseAsync(h, "调入仓", OtherOrgId);

        _ = await CreateTransferAsync(h, from.Id, to.Id, [Line(Item)]);

        StartApprovalRequest started = Assert.Single(h.Approval.Started);
        Assert.Equal(TransferService.CrossFlowCode, started.ApprovalDefinitionCode);
    }

    [Fact]
    public async Task CreateSelectsCrossFlowWhenOrgMissing()
    {
        Harness h = Build();
        WarehouseRow from = await CreateWarehouseAsync(h, "调出仓", orgId: null);
        WarehouseRow to = await CreateWarehouseAsync(h, "调入仓", OrgId);

        _ = await CreateTransferAsync(h, from.Id, to.Id, [Line(Item)]);

        StartApprovalRequest started = Assert.Single(h.Approval.Started);
        Assert.Equal(TransferService.CrossFlowCode, started.ApprovalDefinitionCode);
    }

    [Fact]
    public async Task CreateRejectsBatchEnabledItemWithoutBatchNo()
    {
        Harness h = Build();
        WarehouseRow from = await CreateWarehouseAsync(h, "调出仓");
        WarehouseRow to = await CreateWarehouseAsync(h, "调入仓");

        ValidationException ex = await Assert.ThrowsAsync<ValidationException>(
            () => CreateTransferAsync(h, from.Id, to.Id, [Line(BatchItem)]));

        Assert.Contains(ex.FieldErrors, static e => e.FieldName == "lines[0].batchNo");
    }

    // ============ 发货 ============

    [Fact]
    public async Task ShipWritesTransferOutCommandAndInTransitRow()
    {
        Harness h = Build();
        StockTransferDetailRow created = await ToEffectiveAsync(h);
        h.Txn.AvgCosts[(Item, created.Transfer.FromWarehouseId)] = 12.3456m;
        long lineId = Assert.Single(created.Lines).Id;

        StockTransferDetailRow shipped = await h.Service.ShipAsync(
            created.Transfer.Id,
            new ShipStockTransferPayload([new ShipStockTransferLinePayload(lineId, 10m)]),
            TestContext.Current.CancellationToken);

        Assert.Equal(3, shipped.Transfer.Status);
        Assert.Equal(10m, shipped.Lines[0].ShipQty);
        Assert.Equal(12.3456m, shipped.Lines[0].UnitCost!.Value);

        WmsStockCommandOutbox outbox = Assert.Single(await LoadOutboxAsync(h));
        PostStockTxnRequest payload = JsonSerializer.Deserialize<PostStockTxnRequest>(
            outbox.PayloadJson, QlxJsonDefaults.PreserveNull)!;
        Assert.Equal(StockSourceType.TransferOut, payload.SourceType);
        Assert.Equal(StockDirection.Out, payload.Direction);
        Assert.Equal(created.Transfer.FromWarehouseId, payload.WarehouseId);
        Assert.Equal(0m, payload.UnitCost);
        Assert.StartsWith("wms:db-out:", payload.IdempotencyKey, StringComparison.Ordinal);

        InTransitStock row = Assert.Single(await LoadInTransitAsync(h));
        Assert.Equal(InTransitStatus.InTransit, row.Status);
        Assert.Equal(10m, row.Qty);
        Assert.Equal(12.3456m, row.UnitCost);
        Assert.Equal(created.Transfer.FromWarehouseId, row.FromWarehouseId);
        Assert.Equal(created.Transfer.ToWarehouseId, row.ToWarehouseId);
        Assert.Equal(created.Transfer.InTransitDays, (row.ExpectArrive - row.ShippedAt).Days);

        Qlx.Abstractions.Events.Wms.TransferShippedEvent shippedEvent =
            Assert.IsType<Qlx.Abstractions.Events.Wms.TransferShippedEvent>(
                Assert.Single(h.Events.DurableEvents));
        Assert.Equal(created.Transfer.Id, shippedEvent.TransferId);
        Assert.Equal(created.Transfer.TransferNo, shippedEvent.TransferNo);
    }

    [Fact]
    public async Task ShipWithoutStockReturnsConflictAndNoOutbox()
    {
        Harness h = Build();
        StockTransferDetailRow created = await ToEffectiveAsync(h);
        long lineId = Assert.Single(created.Lines).Id;

        ConflictException ex = await Assert.ThrowsAsync<ConflictException>(
            () => h.Service.ShipAsync(
                created.Transfer.Id,
                new ShipStockTransferPayload([new ShipStockTransferLinePayload(lineId, 10m)]),
                TestContext.Current.CancellationToken));

        Assert.Equal(TransferService.ErrorNoStock, ex.ErrorCode);
        Assert.Empty(await LoadOutboxAsync(h));
        Assert.Empty(await LoadInTransitAsync(h));
        Assert.Empty(h.Events.DurableEvents);

        StockTransferDetailRow reloaded = await h.Service.GetAsync(
            created.Transfer.Id, TestContext.Current.CancellationToken);
        Assert.Equal(2, reloaded.Transfer.Status);
        Assert.Equal(0m, reloaded.Lines[0].ShipQty);
    }

    [Fact]
    public async Task ShipRejectsQtyOverTransferQty()
    {
        Harness h = Build();
        WarehouseRow from = await CreateWarehouseAsync(h, "调出仓");
        WarehouseRow to = await CreateWarehouseAsync(h, "调入仓");
        StockTransferDetailRow created = await CreateTransferAsync(h, from.Id, to.Id, [Line(Item, 5m)]);
        await h.Service.ApplyApprovalAsync(
            created.Transfer.Id, approved: true, TestContext.Current.CancellationToken);
        long lineId = Assert.Single(created.Lines).Id;

        ValidationException ex = await Assert.ThrowsAsync<ValidationException>(
            () => h.Service.ShipAsync(
                created.Transfer.Id,
                new ShipStockTransferPayload([new ShipStockTransferLinePayload(lineId, 6m)]),
                TestContext.Current.CancellationToken));

        Assert.Contains(ex.FieldErrors, static e => e.FieldName == "lines[0].shipQty");
        Assert.Empty(await LoadOutboxAsync(h));
    }

    // ============ 收货 ============

    [Fact]
    public async Task ReceiveWritesTransferInCommandAndClosesInTransit()
    {
        Harness h = Build();
        WarehouseRow from = await CreateWarehouseAsync(h, "调出仓");
        WarehouseRow to = await CreateWarehouseAsync(h, "调入仓");
        StockTransferDetailRow created = await CreateTransferAsync(
            h, from.Id, to.Id, [Line(Item, 10m), Line(BatchItem, 6m, "B-001")]);
        await h.Service.ApplyApprovalAsync(
            created.Transfer.Id, approved: true, TestContext.Current.CancellationToken);
        h.Txn.AvgCosts[(Item, from.Id)] = 8m;
        h.Txn.AvgCosts[(BatchItem, from.Id)] = 3m;
        long first = created.Lines[0].Id;
        long second = created.Lines[1].Id;
        _ = await h.Service.ShipAsync(
            created.Transfer.Id,
            new ShipStockTransferPayload(
                [new ShipStockTransferLinePayload(first, 10m),
                 new ShipStockTransferLinePayload(second, 6m)]),
            TestContext.Current.CancellationToken);

        StockTransferDetailRow received = await h.Service.ReceiveAsync(
            created.Transfer.Id,
            new ReceiveStockTransferPayload(
                [new ReceiveStockTransferLinePayload(first, 10m, null),
                 new ReceiveStockTransferLinePayload(second, 4m, "运输破损")]),
            TestContext.Current.CancellationToken);

        Assert.Equal(4, received.Transfer.Status);
        Assert.Equal(10m, received.Lines[0].ReceiveQty);
        Assert.Equal(4m, received.Lines[1].ReceiveQty);
        Assert.Equal("运输破损", received.Lines[1].DiffNote);

        List<WmsStockCommandOutbox> inbound = [.. (await LoadOutboxAsync(h)).Where(static row =>
            JsonSerializer.Deserialize<PostStockTxnRequest>(
                row.PayloadJson, QlxJsonDefaults.PreserveNull)!.Direction == StockDirection.In)];
        Assert.Equal(2, inbound.Count);
        PostStockTxnRequest secondInbound = JsonSerializer.Deserialize<PostStockTxnRequest>(
            inbound[1].PayloadJson, QlxJsonDefaults.PreserveNull)!;
        Assert.Equal(StockSourceType.TransferIn, secondInbound.SourceType);
        Assert.Equal(to.Id, secondInbound.WarehouseId);
        Assert.Equal(3m, secondInbound.UnitCost);
        Assert.StartsWith("wms:db-in:", secondInbound.IdempotencyKey, StringComparison.Ordinal);

        List<InTransitStock> transit = await LoadInTransitAsync(h);
        Assert.Equal(2, transit.Count);
        Assert.All(transit, static row => Assert.Equal(InTransitStatus.Closed, row.Status));
        Assert.Contains(h.Events.DurableEvents,
            static e => e is Qlx.Abstractions.Events.Wms.TransferReceivedEvent);
    }

    [Fact]
    public async Task ReceiveRequiresDiffNoteWhenShort()
    {
        Harness h = Build();
        StockTransferDetailRow created = await ToEffectiveAsync(h);
        h.Txn.AvgCosts[(Item, created.Transfer.FromWarehouseId)] = 8m;
        long lineId = Assert.Single(created.Lines).Id;
        _ = await h.Service.ShipAsync(
            created.Transfer.Id,
            new ShipStockTransferPayload([new ShipStockTransferLinePayload(lineId, 10m)]),
            TestContext.Current.CancellationToken);

        ValidationException ex = await Assert.ThrowsAsync<ValidationException>(
            () => h.Service.ReceiveAsync(
                created.Transfer.Id,
                new ReceiveStockTransferPayload(
                    [new ReceiveStockTransferLinePayload(lineId, 7m, " ")]),
                TestContext.Current.CancellationToken));

        Assert.Contains(ex.FieldErrors, static e => e.FieldName == "lines[0].diffNote");
        StockTransferDetailRow reloaded = await h.Service.GetAsync(
            created.Transfer.Id, TestContext.Current.CancellationToken);
        Assert.Equal(3, reloaded.Transfer.Status);
        Assert.Equal(0m, reloaded.Lines[0].ReceiveQty);
        InTransitStock row = Assert.Single(await LoadInTransitAsync(h));
        Assert.Equal(InTransitStatus.InTransit, row.Status);
        Assert.Single(await LoadOutboxAsync(h));
    }

    // ============ 作废 ============

    [Fact]
    public async Task VoidRejectsAfterShipped()
    {
        Harness h = Build();
        StockTransferDetailRow created = await ToEffectiveAsync(h);
        h.Txn.AvgCosts[(Item, created.Transfer.FromWarehouseId)] = 8m;
        long lineId = Assert.Single(created.Lines).Id;
        _ = await h.Service.ShipAsync(
            created.Transfer.Id,
            new ShipStockTransferPayload([new ShipStockTransferLinePayload(lineId, 10m)]),
            TestContext.Current.CancellationToken);

        ConflictException ex = await Assert.ThrowsAsync<ConflictException>(
            () => h.Service.VoidAsync(created.Transfer.Id, TestContext.Current.CancellationToken));

        Assert.Equal(TransferService.ErrorInvalidState, ex.ErrorCode);
    }

    [Fact]
    public async Task VoidPendingTransferSucceeds()
    {
        Harness h = Build();
        WarehouseRow from = await CreateWarehouseAsync(h, "调出仓");
        WarehouseRow to = await CreateWarehouseAsync(h, "调入仓");
        StockTransferDetailRow created = await CreateTransferAsync(h, from.Id, to.Id, [Line(Item)]);

        StockTransferRow voided = await h.Service.VoidAsync(
            created.Transfer.Id, TestContext.Current.CancellationToken);

        Assert.Equal(5, voided.Status);
    }

    // ============ 在途列表 ============

    [Fact]
    public async Task InTransitListReturnsFlatRows()
    {
        Harness h = Build();
        StockTransferDetailRow created = await ToEffectiveAsync(h);
        h.Txn.AvgCosts[(Item, created.Transfer.FromWarehouseId)] = 8m;
        long lineId = Assert.Single(created.Lines).Id;
        _ = await h.Service.ShipAsync(
            created.Transfer.Id,
            new ShipStockTransferPayload([new ShipStockTransferLinePayload(lineId, 10m)]),
            TestContext.Current.CancellationToken);

        IReadOnlyList<InTransitStockRow> open = await h.Service.ListInTransitAsync(
            status: 1, warehouseId: null, TestContext.Current.CancellationToken);
        InTransitStockRow row = Assert.Single(open);
        Assert.Equal(created.Transfer.TransferNo, row.TransferNo);
        Assert.Equal(lineId, row.TransferLineId);
        Assert.Equal(1, row.Status);

        Assert.Empty(await h.Service.ListInTransitAsync(
            status: 2, warehouseId: null, TestContext.Current.CancellationToken));
    }

    private static async Task<List<WmsStockCommandOutbox>> LoadOutboxAsync(Harness h)
        => await h.Db.StockCommandOutbox.AsNoTracking()
            .OrderBy(static row => row.Id)
            .ToListAsync(TestContext.Current.CancellationToken);

    private static async Task<List<InTransitStock>> LoadInTransitAsync(Harness h)
        => await h.Db.InTransitStocks.AsNoTracking()
            .OrderBy(static row => row.Id)
            .ToListAsync(TestContext.Current.CancellationToken);

    private static async Task<List<WmsApprovalRequest>> LoadApprovalsAsync(Harness h)
        => await h.Db.ApprovalRequests.AsNoTracking()
            .ToListAsync(TestContext.Current.CancellationToken);
}
