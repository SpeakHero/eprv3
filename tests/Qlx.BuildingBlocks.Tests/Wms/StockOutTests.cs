/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-26
 * 描述：出库作业测试——无单禁入、拣货上限、批次必填、复核不等 409、FEFO 建议、
 *       完成同事务写 Outbox（A-17）、大额审批挂起不写 Outbox、状态机 409
 */

using System.Text.Json;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

using Qlx.Abstractions.Approvals;
using Qlx.Abstractions.Exceptions;
using Qlx.Abstractions.Modules;
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
using Qlx.Modules.Wms.Dtos;
using Qlx.Modules.Wms.Repositories;
using Qlx.Modules.Wms.Services;

namespace Qlx.BuildingBlocks.Tests.Wms;

/// <summary>出库作业测试。</summary>
public sealed class StockOutTests
{
    private const long TenantId = 88L;
    private const string Operator = "user-88";
    private const long OrgId = 9001L;

    private const long NonBatchItem = 7101L;
    private const long BatchItem = 7102L;

    private sealed class Harness
    {
        public required WmsTenantDbContext Db { get; init; }

        public required WarehouseService Warehouse { get; init; }

        public required StockOutService Service { get; init; }

        public required StubStockQueryService Stock { get; init; }

        public required StubOutboundTxnCommandService Txn { get; init; }

        public required StubOutboundParameterProvider Parameters { get; init; }

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
        var parameters = new StubOutboundParameterProvider();
        var items = new StubItemQueryService
        {
            Rows =
            {
                [NonBatchItem] = new StockItemQueryRow(NonBatchItem, false, false, 12.5m, 1),
                [BatchItem] = new StockItemQueryRow(BatchItem, true, true, 3m, 1),
            },
        };

        return new Harness
        {
            Db = db,
            Stock = stock,
            Txn = txn,
            Parameters = parameters,
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
            Service = new StockOutService(
                tenantCtx,
                repo,
                new StubDocumentNumberGenerator(),
                new StubOrgQueryService(),
                items,
                stock,
                txn,
                new StockCommandQueue(db, tenantCtx),
                approval,
                parameters,
                events,
                user),
        };
    }

    private static Task<WarehouseRow> CreateWarehouseAsync(Harness h, string name)
        => h.Warehouse.CreateAsync(new CreateWarehousePayload(name, 1, OrgId, null, null, null),
            TestContext.Current.CancellationToken);

    private static Task<StockOutTaskDetailRow> CreateTaskAsync(
        Harness h,
        long warehouseId,
        IReadOnlyList<CreateStockOutLinePayload> lines,
        string sourceDocNo = "SO-20261026-0001")
        => h.Service.CreateAsync(
            new CreateStockOutTaskPayload(1, sourceDocNo, warehouseId, null, lines),
            TestContext.Current.CancellationToken);

    private static CreateStockOutLinePayload Line(
        long itemId, decimal applyQty = 10m, string? batchNo = null)
        => new(itemId, applyQty, null, batchNo);

    private static PickStockOutLinePayload Pick(long lineId, decimal pickQty, string? batchNo = null)
        => new(lineId, pickQty, null, batchNo);

    private static CheckStockOutLinePayload Check(long lineId, decimal checkQty)
        => new(lineId, checkQty);

    private static async Task<StockOutTaskDetailRow> ToPendingCheckAsync(
        Harness h, long warehouseId, string? batchNo = null)
    {
        StockOutTaskDetailRow task = await CreateTaskAsync(h, warehouseId,
            [Line(NonBatchItem, 10m, batchNo)]);
        StockOutTaskLineRow line = Assert.Single(task.Lines);
        _ = await h.Service.PickAsync(
            task.Task.Id,
            new PickStockOutTaskPayload([Pick(line.Id, 10m, batchNo)]),
            TestContext.Current.CancellationToken);
        return await h.Service.CheckAsync(
            task.Task.Id,
            new CheckStockOutTaskPayload("handover-01", [Check(line.Id, 10m)]),
            TestContext.Current.CancellationToken);
    }

    // ============ 新建校验（V-1） ============

    [Fact]
    public async Task CreateRejectsEmptySourceDocNo()
    {
        Harness h = Build();
        WarehouseRow warehouse = await CreateWarehouseAsync(h, "一号仓");

        ValidationException ex = await Assert.ThrowsAsync<ValidationException>(
            () => CreateTaskAsync(h, warehouse.Id, [Line(NonBatchItem)], sourceDocNo: " "));

        Assert.Contains(ex.FieldErrors, static e => e.FieldName == "sourceDocNo");
    }

    // ============ 拣货校验 ============

    [Fact]
    public async Task PickRejectsQtyOverApplyQty()
    {
        Harness h = Build();
        WarehouseRow warehouse = await CreateWarehouseAsync(h, "一号仓");
        StockOutTaskDetailRow task = await CreateTaskAsync(h, warehouse.Id, [Line(NonBatchItem, 5m)]);

        ValidationException ex = await Assert.ThrowsAsync<ValidationException>(
            () => h.Service.PickAsync(
                task.Task.Id,
                new PickStockOutTaskPayload([Pick(task.Lines[0].Id, 6m)]),
                TestContext.Current.CancellationToken));

        Assert.Contains(ex.FieldErrors, static e => e.FieldName == "lines[0].pickQty");
        Assert.Empty(await LoadOutboxAsync(h));
    }

    [Fact]
    public async Task PickRejectsBatchEnabledItemWithoutBatchNo()
    {
        Harness h = Build();
        WarehouseRow warehouse = await CreateWarehouseAsync(h, "一号仓");
        StockOutTaskDetailRow task = await CreateTaskAsync(h, warehouse.Id, [Line(BatchItem)]);

        ValidationException ex = await Assert.ThrowsAsync<ValidationException>(
            () => h.Service.PickAsync(
                task.Task.Id,
                new PickStockOutTaskPayload([Pick(task.Lines[0].Id, 3m)]),
                TestContext.Current.CancellationToken));

        Assert.Contains(ex.FieldErrors, static e => e.FieldName == "lines[0].batchNo");
    }

    // ============ 复核（409 WMS_CHECK_MISMATCH）============

    [Fact]
    public async Task CheckMismatchReturnsConflict()
    {
        Harness h = Build();
        WarehouseRow warehouse = await CreateWarehouseAsync(h, "一号仓");
        StockOutTaskDetailRow task = await CreateTaskAsync(h, warehouse.Id, [Line(NonBatchItem)]);
        long lineId = Assert.Single(task.Lines).Id;
        _ = await h.Service.PickAsync(
            task.Task.Id,
            new PickStockOutTaskPayload([Pick(lineId, 8m)]),
            TestContext.Current.CancellationToken);

        ConflictException ex = await Assert.ThrowsAsync<ConflictException>(
            () => h.Service.CheckAsync(
                task.Task.Id,
                new CheckStockOutTaskPayload("handover-01", [Check(lineId, 7m)]),
                TestContext.Current.CancellationToken));

        Assert.Equal(StockOutService.ErrorCheckMismatch, ex.ErrorCode);
    }

    [Fact]
    public async Task CheckRequiresHandoverTo()
    {
        Harness h = Build();
        WarehouseRow warehouse = await CreateWarehouseAsync(h, "一号仓");
        StockOutTaskDetailRow task = await CreateTaskAsync(h, warehouse.Id, [Line(NonBatchItem)]);
        long lineId = Assert.Single(task.Lines).Id;
        _ = await h.Service.PickAsync(
            task.Task.Id,
            new PickStockOutTaskPayload([Pick(lineId, 8m)]),
            TestContext.Current.CancellationToken);

        ValidationException ex = await Assert.ThrowsAsync<ValidationException>(
            () => h.Service.CheckAsync(
                task.Task.Id,
                new CheckStockOutTaskPayload("  ", [Check(lineId, 8m)]),
                TestContext.Current.CancellationToken));

        Assert.Contains(ex.FieldErrors, static e => e.FieldName == "handoverTo");
    }

    // ============ 完成：同事务写 Outbox + 事件 ============

    [Fact]
    public async Task CompleteWritesOneOutboxRowPerLineAndPublishesEvent()
    {
        Harness h = Build();
        WarehouseRow warehouse = await CreateWarehouseAsync(h, "一号仓");
        h.Txn.AvgCosts[(NonBatchItem, warehouse.Id)] = 12.5m;
        h.Txn.AvgCosts[(BatchItem, warehouse.Id)] = 3m;

        StockOutTaskDetailRow task = await CreateTaskAsync(h, warehouse.Id,
            [Line(NonBatchItem, 10m), Line(BatchItem, 4m, "B-001")]);
        long first = task.Lines[0].Id;
        long second = task.Lines[1].Id;
        _ = await h.Service.PickAsync(
            task.Task.Id,
            new PickStockOutTaskPayload([Pick(first, 6m), Pick(second, 4m, "B-001")]),
            TestContext.Current.CancellationToken);
        _ = await h.Service.CheckAsync(
            task.Task.Id,
            new CheckStockOutTaskPayload("handover-01", [Check(first, 6m), Check(second, 4m)]),
            TestContext.Current.CancellationToken);

        StockOutCompleteResultRow result = await h.Service.CompleteAsync(
            task.Task.Id, TestContext.Current.CancellationToken);

        Assert.False(result.ApprovalRequired);
        Assert.Equal(4, result.Task.Status);
        Assert.Equal(87m, result.TotalAmount);

        List<WmsStockCommandOutbox> outbox = await LoadOutboxAsync(h);
        Assert.Equal(2, outbox.Count);
        foreach (WmsStockCommandOutbox row in outbox)
        {
            PostStockTxnRequest payload = JsonSerializer.Deserialize<PostStockTxnRequest>(
                row.PayloadJson, QlxJsonDefaults.PreserveNull)!;
            Assert.Equal(StockSourceType.SaleOut, payload.SourceType);
            Assert.Equal(StockDirection.Out, payload.Direction);
            Assert.Equal(task.Task.TaskNo, payload.SourceDocNo);
            Assert.Equal(0m, payload.UnitCost);
            Assert.StartsWith("wms:ck:", payload.IdempotencyKey, StringComparison.Ordinal);
        }

        Assert.Empty(h.Approval.Started);
        Qlx.Abstractions.Events.Wms.StockOutCompletedEvent completed =
            Assert.IsType<Qlx.Abstractions.Events.Wms.StockOutCompletedEvent>(
                Assert.Single(h.Events.DurableEvents));
        Assert.Equal(task.Task.Id, completed.TaskId);
        Assert.Equal(task.Task.TaskNo, completed.TaskNo);
        Assert.Equal(warehouse.Id, completed.WarehouseId);
        Assert.Equal("SO-20261026-0001", completed.SourceDocNo);
    }

    // ============ 大额审批挂起 ============

    [Fact]
    public async Task LargeAmountIsHeldForApprovalWithoutOutbox()
    {
        Harness h = Build();
        WarehouseRow warehouse = await CreateWarehouseAsync(h, "一号仓");
        h.Txn.AvgCosts[(NonBatchItem, warehouse.Id)] = 6000m;
        StockOutTaskDetailRow pending = await ToPendingCheckAsync(h, warehouse.Id);

        StockOutCompleteResultRow result = await h.Service.CompleteAsync(
            pending.Task.Id, TestContext.Current.CancellationToken);

        Assert.True(result.ApprovalRequired);
        Assert.Equal(60000m, result.TotalAmount);
        Assert.Equal(3, result.Task.Status);
        Assert.Empty(await LoadOutboxAsync(h));
        Assert.Empty(h.Events.DurableEvents);

        StartApprovalRequest started = Assert.Single(h.Approval.Started);
        Assert.Equal(StockOutService.OutboundLargeFlowCode, started.ApprovalDefinitionCode);
        Assert.Equal(StockOutService.OutboundLargeBizType, started.BizType);

        WmsApprovalRequest request = Assert.Single(await LoadApprovalsAsync(h));
        Assert.Equal(WmsApprovalRequestStatus.Pending, request.Status);
        Assert.Equal(StockOutService.OutboundLargeBizType, request.BizType);
        Assert.Equal(pending.Task.Id, request.BizId);
        Assert.Equal("inst-1", request.ApprovalInstanceId);
    }

    [Fact]
    public async Task CompletingTwiceWhileApprovalPendingIsConflict()
    {
        Harness h = Build();
        WarehouseRow warehouse = await CreateWarehouseAsync(h, "一号仓");
        h.Txn.AvgCosts[(NonBatchItem, warehouse.Id)] = 6000m;
        StockOutTaskDetailRow pending = await ToPendingCheckAsync(h, warehouse.Id);
        _ = await h.Service.CompleteAsync(pending.Task.Id, TestContext.Current.CancellationToken);

        ConflictException ex = await Assert.ThrowsAsync<ConflictException>(
            () => h.Service.CompleteAsync(pending.Task.Id, TestContext.Current.CancellationToken));

        Assert.Equal(StockOutService.ErrorApprovalPending, ex.ErrorCode);
        _ = Assert.Single(h.Approval.Started);
        Assert.Empty(await LoadOutboxAsync(h));
    }

    [Fact]
    public async Task CancelledTaskCannotBeCompleted()
    {
        Harness h = Build();
        WarehouseRow warehouse = await CreateWarehouseAsync(h, "一号仓");
        StockOutTaskDetailRow pending = await ToPendingCheckAsync(h, warehouse.Id);
        _ = await h.Service.CancelAsync(pending.Task.Id, TestContext.Current.CancellationToken);

        ConflictException ex = await Assert.ThrowsAsync<ConflictException>(
            () => h.Service.CompleteAsync(pending.Task.Id, TestContext.Current.CancellationToken));

        Assert.Equal(StockOutService.ErrorInvalidState, ex.ErrorCode);
    }

    // ============ FEFO 建议 ============

    [Fact]
    public async Task DetailReturnsFefoSuggestionsForEachLineItem()
    {
        Harness h = Build();
        WarehouseRow warehouse = await CreateWarehouseAsync(h, "一号仓");
        DateTimeOffset early = new(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);
        DateTimeOffset late = new(2027, 5, 1, 0, 0, 0, TimeSpan.Zero);
        h.Stock.Batches.AddRange(
        [
            new StockBatchRow(BatchItem, warehouse.Id, "B-001", null, early, 5m, null, null),
            new StockBatchRow(BatchItem, warehouse.Id, "B-002", null, late, 9m, null, null),
            new StockBatchRow(NonBatchItem, warehouse.Id, "N-001", null, null, 7m, null, null),
        ]);

        StockOutTaskDetailRow task = await CreateTaskAsync(h, warehouse.Id,
            [Line(NonBatchItem, 1m), Line(BatchItem, 2m)]);

        Assert.Equal(3, task.FefoSuggestions.Count);
        StockOutFefoSuggestionRow first = task.FefoSuggestions[0];
        Assert.Equal(task.Lines[0].LineNo, first.LineNo);
        Assert.Equal(NonBatchItem, first.ItemId);
        Assert.Equal("N-001", first.BatchNo);
        Assert.Equal(early, task.FefoSuggestions[1].ExpiryDate);
        Assert.Equal(late, task.FefoSuggestions[2].ExpiryDate);
    }

    private static async Task<List<WmsStockCommandOutbox>> LoadOutboxAsync(Harness h)
        => await h.Db.StockCommandOutbox.AsNoTracking()
            .ToListAsync(TestContext.Current.CancellationToken);

    private static async Task<List<WmsApprovalRequest>> LoadApprovalsAsync(Harness h)
        => await h.Db.ApprovalRequests.AsNoTracking()
            .ToListAsync(TestContext.Current.CancellationToken);
}

/// <summary>系统参数桩：按 key 覆盖字符串参数，未覆盖回落 fallback。</summary>
internal sealed class StubOutboundParameterProvider : ISystemParameterProvider
{
    public Dictionary<string, string> Strings { get; } = new(StringComparer.Ordinal);

    public Task<string> GetStringAsync(string paramKey, string fallback, CancellationToken cancellationToken)
        => Task.FromResult(Strings.TryGetValue(paramKey, out string? value) ? value : fallback);

    public Task<int> GetIntAsync(string paramKey, int fallback, CancellationToken cancellationToken)
        => Task.FromResult(fallback);
}

/// <summary>库存写入命令桩：只提供台账均价（A-13），出库完成禁止同步直连台账。</summary>
internal sealed class StubOutboundTxnCommandService : IStockTxnCommandService
{
    public Dictionary<(long ItemId, long WarehouseId), decimal> AvgCosts { get; } = [];

    public Task<StockPostResult> PostAsync(PostStockTxnRequest request, CancellationToken cancellationToken)
        => throw new InvalidOperationException("出库完成必须经 Outbox 分发，禁止同步直连台账");

    public Task<IReadOnlyList<StockPostResult>> PostBatchAsync(
        IReadOnlyList<PostStockTxnRequest> requests, CancellationToken cancellationToken)
        => throw new InvalidOperationException("出库完成必须经 Outbox 分发，禁止同步直连台账");

    public Task AdjustLockedQtyAsync(
        long itemId, long warehouseId, decimal deltaQty, string reason, CancellationToken cancellationToken)
        => Task.CompletedTask;

    public Task<StockBalanceRow?> FindBalanceAsync(
        long itemId, long warehouseId, CancellationToken cancellationToken)
        => Task.FromResult(AvgCosts.TryGetValue((itemId, warehouseId), out decimal avgCost)
            ? new StockBalanceRow(itemId, warehouseId, 100m, avgCost, 100m * avgCost, 0m, null)
            : (StockBalanceRow?)null);
}
