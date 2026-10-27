/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-26 17:00:00 +08:00 中国标准时间 星期一
 * 描述：入库作业测试——无单禁入、清点上限、批次必填、库位跨仓阻断、完成同事务写 Outbox、
 *       单价三级回退（A-14）、状态机 409、完成事件载荷
 */

using System.Data;
using System.Text.Json;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

using Qlx.Abstractions.Exceptions;
using Qlx.Abstractions.Queries;
using Qlx.Abstractions.Serialization;
using Qlx.Abstractions.Stock;
using Qlx.BuildingBlocks.Caching;
using Qlx.BuildingBlocks.Tests.Bas;
using Qlx.BuildingBlocks.Tests.Files;
using Qlx.BuildingBlocks.Tests.Jobs;
using Qlx.Modules.Wms;
using Qlx.Modules.Wms.Domain.Outbox;
using Qlx.Modules.Wms.Dtos;
using Qlx.Modules.Wms.Repositories;
using Qlx.Modules.Wms.Services;

namespace Qlx.BuildingBlocks.Tests.Wms;

/// <summary>入库作业测试。</summary>
public sealed class StockInTests
{
    private const long TenantId = 88L;
    private const string Operator = "user-88";
    private const long OrgId = 9001L;

    private sealed class Harness
    {
        public required WmsTenantDbContext Db { get; init; }

        public required WmsTenantRepository Repo { get; init; }

        public required WarehouseService Warehouse { get; init; }

        public required StockInService Service { get; init; }

        public required StubItemQueryService Items { get; init; }

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
        var items = new StubItemQueryService
        {
            Rows =
            {
                [7001L] = new StockItemQueryRow(7001L, BatchEnabled: false, ExpiryEnabled: false, 12.5m, 1),
                [7002L] = new StockItemQueryRow(7002L, BatchEnabled: true, ExpiryEnabled: true, 3.25m, 1),
                [7003L] = new StockItemQueryRow(7003L, BatchEnabled: false, ExpiryEnabled: false, null, 1),
            },
        };

        return new Harness
        {
            Db = db,
            Repo = repo,
            Items = items,
            Events = events,
            Warehouse = new WarehouseService(
                tenantCtx,
                repo,
                new StubDocumentNumberGenerator(),
                new StubOrgQueryService(),
                new BasStubApprovalService(),
                new StubStockQueryService(),
                new MemoryCacheService(Options.Create(new CacheOptions())),
                new InMemoryAuditService(),
                new BasStubSnowflakeIdGenerator(),
                user),
            Service = new StockInService(
                tenantCtx,
                repo,
                new StubDocumentNumberGenerator(),
                new StubOrgQueryService(),
                items,
                new StockCommandQueue(db, tenantCtx),
                events,
                user),
        };
    }


    private static Task<WarehouseRow> CreateWarehouseAsync(Harness h, string name)
        => h.Warehouse.CreateAsync(new CreateWarehousePayload(name, 1, OrgId, null, null, null),
            TestContext.Current.CancellationToken);

    private static Task<StockInTaskDetailRow> CreateTaskAsync(
        Harness h,
        long warehouseId,
        IReadOnlyList<CreateStockInLinePayload> lines,
        int sourceType = 1,
        string sourceDocNo = "PO-20261026-0001")
        => h.Service.CreateAsync(
            new CreateStockInTaskPayload(sourceType, sourceDocNo, warehouseId, null, lines),
            TestContext.Current.CancellationToken);

    private static CreateStockInLinePayload Line(
        long itemId, decimal expectQty = 10m, long? locationId = null,
        string? batchNo = null, decimal? unitCost = null)
        => new(itemId, expectQty, locationId, batchNo, unitCost);

    private static CompleteStockInLinePayload Count(
        long lineId, decimal countQty, decimal? confirmQty = null,
        long? locationId = null, string? batchNo = null, decimal? unitCost = null)
        => new(lineId, countQty, confirmQty ?? countQty, locationId, batchNo, unitCost);

    // ============ 新建校验（V-1） ============

    [Fact]
    public async Task CreateRejectsEmptySourceDocNo()
    {
        Harness h = Build();
        WarehouseRow warehouse = await CreateWarehouseAsync(h, "一号仓");

        ValidationException ex = await Assert.ThrowsAsync<ValidationException>(
            () => CreateTaskAsync(h, warehouse.Id, [Line(7001L)], sourceDocNo: "   "));

        Assert.Contains(ex.FieldErrors, static e => e.FieldName == "sourceDocNo");
    }

    [Fact]
    public async Task CreateRejectsUnknownItemAndDisabledWarehouse()
    {
        Harness h = Build();
        WarehouseRow warehouse = await CreateWarehouseAsync(h, "一号仓");

        _ = await Assert.ThrowsAsync<ValidationException>(
            () => CreateTaskAsync(h, warehouse.Id, [Line(9999L)]));

        _ = await h.Warehouse.ToggleAsync(warehouse.Id, TestContext.Current.CancellationToken);
        await h.Warehouse.ApplyDisableApprovalAsync(
            warehouse.Id, approved: true, TestContext.Current.CancellationToken);

        ValidationException disabled = await Assert.ThrowsAsync<ValidationException>(
            () => CreateTaskAsync(h, warehouse.Id, [Line(7001L)]));
        Assert.Contains(disabled.FieldErrors, static e => e.FieldName == "warehouseId");
    }

    // ============ 完成校验 ============

    [Fact]
    public async Task CompleteRejectsConfirmQtyOverCountQty()
    {
        Harness h = Build();
        WarehouseRow warehouse = await CreateWarehouseAsync(h, "一号仓");
        StockInTaskDetailRow task = await CreateTaskAsync(h, warehouse.Id, [Line(7001L)]);
        _ = await h.Service.StartAsync(task.Task.Id, TestContext.Current.CancellationToken);

        long lineId = Assert.Single(task.Lines).Id;
        ValidationException ex = await Assert.ThrowsAsync<ValidationException>(
            () => h.Service.CompleteAsync(
                task.Task.Id,
                new CompleteStockInTaskPayload([Count(lineId, countQty: 5m, confirmQty: 6m)]),
                TestContext.Current.CancellationToken));

        Assert.Contains(ex.FieldErrors, static e => e.FieldName == "lines[0].confirmQty");
        Assert.Empty(await LoadOutboxAsync(h));
    }

    [Fact]
    public async Task CompleteRejectsBatchEnabledItemWithoutBatchNo()
    {
        Harness h = Build();
        WarehouseRow warehouse = await CreateWarehouseAsync(h, "一号仓");
        StockInTaskDetailRow task = await CreateTaskAsync(h, warehouse.Id, [Line(7002L)]);
        _ = await h.Service.StartAsync(task.Task.Id, TestContext.Current.CancellationToken);

        long lineId = Assert.Single(task.Lines).Id;
        ValidationException ex = await Assert.ThrowsAsync<ValidationException>(
            () => h.Service.CompleteAsync(
                task.Task.Id,
                new CompleteStockInTaskPayload([Count(lineId, 5m)]),
                TestContext.Current.CancellationToken));

        Assert.Contains(ex.FieldErrors, static e => e.FieldName == "lines[0].batchNo");
    }

    [Fact]
    public async Task CompleteRejectsLocationOfAnotherWarehouse()
    {
        Harness h = Build();
        WarehouseRow warehouse = await CreateWarehouseAsync(h, "一号仓");
        WarehouseRow other = await CreateWarehouseAsync(h, "二号仓");
        ZoneRow zone = await h.Warehouse.CreateZoneAsync(
            other.Id, new CreateZonePayload("A", "A 区", 1), TestContext.Current.CancellationToken);
        LocationRow foreign = await h.Warehouse.CreateLocationAsync(
            other.Id,
            zone.Id,
            new CreateLocationPayload("A-01", null, 1),
            TestContext.Current.CancellationToken);

        StockInTaskDetailRow task = await CreateTaskAsync(h, warehouse.Id, [Line(7001L)]);
        _ = await h.Service.StartAsync(task.Task.Id, TestContext.Current.CancellationToken);

        long lineId = Assert.Single(task.Lines).Id;
        ValidationException ex = await Assert.ThrowsAsync<ValidationException>(
            () => h.Service.CompleteAsync(
                task.Task.Id,
                new CompleteStockInTaskPayload([Count(lineId, 5m, locationId: foreign.Id)]),
                TestContext.Current.CancellationToken));

        Assert.Contains(ex.FieldErrors, static e => e.FieldName!.Contains("locationId", StringComparison.Ordinal));
    }

    // ============ 完成：同事务写 Outbox + 事件 ============

    [Fact]
    public async Task CompleteWritesOneOutboxRowPerLineAndPublishesEvent()
    {
        Harness h = Build();
        WarehouseRow warehouse = await CreateWarehouseAsync(h, "一号仓");
        StockInTaskDetailRow task = await CreateTaskAsync(
            h, warehouse.Id, [Line(7001L, unitCost: 8m), Line(7003L)]);
        _ = await h.Service.StartAsync(task.Task.Id, TestContext.Current.CancellationToken);

        List<CompleteStockInLinePayload> counts =
        [
            Count(task.Lines[0].Id, 5m, confirmQty: 5m, unitCost: 8m),
            Count(task.Lines[1].Id, 2m, confirmQty: 2m),
        ];

        StockInTaskDetailRow finished = await h.Service.CompleteAsync(
            task.Task.Id, new CompleteStockInTaskPayload(counts),
            TestContext.Current.CancellationToken);

        Assert.Equal(3, finished.Task.Status);

        List<WmsStockCommandOutbox> outbox = await LoadOutboxAsync(h);
        Assert.Equal(2, outbox.Count);
        foreach (WmsStockCommandOutbox row in outbox)
        {
            PostStockTxnRequest payload = JsonSerializer.Deserialize<PostStockTxnRequest>(
                row.PayloadJson, QlxJsonDefaults.PreserveNull)!;
            Assert.Equal(StockSourceType.PurchaseIn, payload.SourceType);
            Assert.Equal(StockDirection.In, payload.Direction);
            Assert.Equal(task.Task.TaskNo, payload.SourceDocNo);
            Assert.StartsWith("wms:rk:", payload.IdempotencyKey, StringComparison.Ordinal);
        }

        // A-14：行值优先，其次商品参考成本，最后 0
        decimal[] costs = [.. outbox
            .Select(static row => JsonSerializer.Deserialize<PostStockTxnRequest>(
                row.PayloadJson, QlxJsonDefaults.PreserveNull)!)
            .OrderBy(static payload => payload.SourceLineNo)
            .Select(static payload => payload.UnitCost)];
        Assert.Equal(2, costs.Length);
        Assert.Equal(8m, costs[0]);
        Assert.Equal(0m, costs[1]);

        Qlx.Abstractions.Events.Wms.StockInCompletedEvent completed =
            Assert.IsType<Qlx.Abstractions.Events.Wms.StockInCompletedEvent>(
                Assert.Single(h.Events.DurableEvents));
        Assert.Equal(task.Task.Id, completed.TaskId);
        Assert.Equal(task.Task.TaskNo, completed.TaskNo);
        Assert.Equal(warehouse.Id, completed.WarehouseId);
        Assert.Equal("PO-20261026-0001", completed.SourceDocNo);
    }

    [Fact]
    public async Task CompletingTwiceReturnsInvalidStateConflict()
    {
        Harness h = Build();
        WarehouseRow warehouse = await CreateWarehouseAsync(h, "一号仓");
        StockInTaskDetailRow task = await CreateTaskAsync(h, warehouse.Id, [Line(7001L)]);
        _ = await h.Service.StartAsync(task.Task.Id, TestContext.Current.CancellationToken);

        long lineId = Assert.Single(task.Lines).Id;
        _ = await h.Service.CompleteAsync(
            task.Task.Id,
            new CompleteStockInTaskPayload([Count(lineId, 5m)]),
            TestContext.Current.CancellationToken);

        ConflictException ex = await Assert.ThrowsAsync<ConflictException>(
            () => h.Service.CompleteAsync(
                task.Task.Id,
                new CompleteStockInTaskPayload([Count(lineId, 5m)]),
                TestContext.Current.CancellationToken));

        Assert.Equal(StockInService.ErrorInvalidState, ex.ErrorCode);
        _ = Assert.Single(await LoadOutboxAsync(h));
    }

    [Fact]
    public async Task CancelledTaskCannotBeStartedOrCompleted()
    {
        Harness h = Build();
        WarehouseRow warehouse = await CreateWarehouseAsync(h, "一号仓");
        StockInTaskDetailRow task = await CreateTaskAsync(h, warehouse.Id, [Line(7001L)]);
        _ = await h.Service.CancelAsync(task.Task.Id, TestContext.Current.CancellationToken);

        ConflictException start = await Assert.ThrowsAsync<ConflictException>(
            () => h.Service.StartAsync(task.Task.Id, TestContext.Current.CancellationToken));
        Assert.Equal(StockInService.ErrorInvalidState, start.ErrorCode);

        ConflictException complete = await Assert.ThrowsAsync<ConflictException>(
            () => h.Service.CompleteAsync(
                task.Task.Id,
                new CompleteStockInTaskPayload([Count(task.Lines[0].Id, 1m)]),
                TestContext.Current.CancellationToken));
        Assert.Equal(StockInService.ErrorInvalidState, complete.ErrorCode);
    }

    [Fact]
    public async Task CompletingWithoutAnyConfirmedQtyIsRejected()
    {
        Harness h = Build();
        WarehouseRow warehouse = await CreateWarehouseAsync(h, "一号仓");
        StockInTaskDetailRow task = await CreateTaskAsync(h, warehouse.Id, [Line(7001L)]);
        _ = await h.Service.StartAsync(task.Task.Id, TestContext.Current.CancellationToken);

        long lineId = Assert.Single(task.Lines).Id;
        ValidationException ex = await Assert.ThrowsAsync<ValidationException>(
            () => h.Service.CompleteAsync(
                task.Task.Id,
                new CompleteStockInTaskPayload([Count(lineId, 5m, confirmQty: 0m)]),
                TestContext.Current.CancellationToken));

        Assert.Contains(ex.FieldErrors, static e => e.FieldName == "lines");
        Assert.Empty(await LoadOutboxAsync(h));
    }

    private static async Task<List<WmsStockCommandOutbox>> LoadOutboxAsync(Harness h)
        => await h.Db.StockCommandOutbox.AsNoTracking()
            .ToListAsync(TestContext.Current.CancellationToken);
}

/// <summary>商品只读查询桩：可配置商品集合。</summary>
internal sealed class StubItemQueryService : IItemQueryService
{
    public Dictionary<long, StockItemQueryRow> Rows { get; } = [];

    public Task<StockItemQueryRow?> FindAsync(long id, CancellationToken cancellationToken)
        => Task.FromResult(Rows.TryGetValue(id, out StockItemQueryRow? row) ? row : null);

    public Task<IReadOnlyList<StockItemQueryRow>> FindByIdsAsync(
        IReadOnlyList<long> ids, CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<StockItemQueryRow>>(
            [.. ids.Where(Rows.ContainsKey).Select(id => Rows[id])]);
}
