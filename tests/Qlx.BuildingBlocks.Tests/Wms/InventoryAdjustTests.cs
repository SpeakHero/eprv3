/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-26
 * 描述：盘盈盘亏测试——金额估算三级回退、提交审批挂起、审批通过恰好 1 命令/行、
 *       重复应用幂等、驳回回草稿可重提、盘点复盘完成自动生成盘盈/盘亏草稿
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
using Qlx.Modules.Wms.Domain.Adjust;
using Qlx.Modules.Wms.Domain.Numbering;
using Qlx.Modules.Wms.Domain.Outbox;
using Qlx.Modules.Wms.Dtos;
using Qlx.Modules.Wms.Repositories;
using Qlx.Modules.Wms.Services;

namespace Qlx.BuildingBlocks.Tests.Wms;

/// <summary>盘盈盘亏测试。</summary>
public sealed class InventoryAdjustTests
{
    private const long TenantId = 88L;
    private const string Operator = "user-88";
    private const long OrgId = 9001L;

    private const long ItemA = 7401L;
    private const long ItemB = 7402L;

    private sealed class Harness
    {
        public required WmsTenantDbContext Db { get; init; }

        public required WarehouseService Warehouse { get; init; }

        public required InventoryAdjustService Service { get; init; }

        public required StockTakeService Take { get; init; }

        public required StubTakeStockQueryService Stock { get; init; }

        public required AdjustTxnCommandService Txn { get; init; }

        public required BasStubApprovalService Approvals { get; init; }
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
                [ItemA] = new StockItemQueryRow(ItemA, false, false, 5m, 1),
                [ItemB] = new StockItemQueryRow(ItemB, true, true, 3m, 1),
            },
        };
        var txn = new AdjustTxnCommandService();
        var stock = new StubTakeStockQueryService();
        var approvals = new BasStubApprovalService();
        var numberGenerator = new StubDocumentNumberGenerator();
        var orgQuery = new StubOrgQueryService();
        var creator = new StockTakeAdjustDraftCreator(
            tenantCtx, repo, numberGenerator, orgQuery, items, txn);

        return new Harness
        {
            Db = db,
            Stock = stock,
            Txn = txn,
            Approvals = approvals,
            Warehouse = new WarehouseService(
                tenantCtx,
                repo,
                numberGenerator,
                orgQuery,
                approvals,
                stock,
                new MemoryCacheService(Options.Create(new CacheOptions())),
                new InMemoryAuditService(),
                new BasStubSnowflakeIdGenerator(),
                user),
            Service = new InventoryAdjustService(
                tenantCtx,
                repo,
                numberGenerator,
                orgQuery,
                items,
                txn,
                new StockCommandQueue(db, tenantCtx),
                approvals,
                user),
            Take = new StockTakeService(
                tenantCtx, repo, numberGenerator, orgQuery, items, stock, txn, creator),
        };
    }

    private static Task<WarehouseRow> CreateWarehouseAsync(Harness h)
        => h.Warehouse.CreateAsync(
            new CreateWarehousePayload("调整仓", 1, OrgId, null, null, null),
            TestContext.Current.CancellationToken);

    private static CreateInventoryAdjustLinePayload Line(
        long itemId, decimal qty, string? batchNo = null)
        => new(itemId, batchNo, qty, null);

    private static Task<InventoryAdjustDetailRow> CreateDocAsync(
        Harness h, long warehouseId, int type, params CreateInventoryAdjustLinePayload[] lines)
        => h.Service.CreateAsync(
            new CreateInventoryAdjustPayload(type, warehouseId, "测试原因", null, lines),
            TestContext.Current.CancellationToken);

    private static async Task<List<WmsStockCommandOutbox>> LoadOutboxAsync(Harness h)
        => await h.Db.StockCommandOutbox.AsNoTracking()
            .OrderBy(row => row.Id)
            .ToListAsync(TestContext.Current.CancellationToken);

    private static async Task<List<InventoryAdjustDoc>> LoadDocsAsync(Harness h)
        => await h.Db.InventoryAdjustDocs.AsNoTracking()
            .OrderBy(doc => doc.Id)
            .ToListAsync(TestContext.Current.CancellationToken);

    // ============ 建单与金额估算 ============

    [Fact]
    public async Task CreateEstimatesTotalAmountFromAverageCost()
    {
        Harness h = Build();
        WarehouseRow warehouse = await CreateWarehouseAsync(h);
        h.Txn.AvgCosts[(ItemA, warehouse.Id)] = 5m;
        h.Txn.AvgCosts[(ItemB, warehouse.Id)] = 3.25m;

        InventoryAdjustDetailRow detail = await CreateDocAsync(
            h, warehouse.Id, (int)AdjustType.Gain, Line(ItemA, 4m), Line(ItemB, 2m, "B1"));

        Assert.Equal(1, detail.Adjust.Status);
        Assert.Equal(26.5m, detail.Adjust.TotalAmount);
        Assert.Equal(5m, detail.Lines[0].UnitCost);
        Assert.Equal(3.25m, detail.Lines[1].UnitCost);
        Assert.Empty(await LoadOutboxAsync(h));
    }

    [Fact]
    public async Task CreateRejectsInvalidPayload()
    {
        Harness h = Build();
        WarehouseRow warehouse = await CreateWarehouseAsync(h);

        ValidationException typeError = await Assert.ThrowsAsync<ValidationException>(
            () => CreateDocAsync(h, warehouse.Id, 3, Line(ItemA, 1m)));
        Assert.Contains(typeError.FieldErrors, static e => e.FieldName == "type");

        ValidationException qtyError = await Assert.ThrowsAsync<ValidationException>(
            () => CreateDocAsync(h, warehouse.Id, (int)AdjustType.Gain, Line(ItemA, 0m)));
        Assert.Contains(qtyError.FieldErrors, static e => e.FieldName == "lines[0].qty");

        ValidationException batchError = await Assert.ThrowsAsync<ValidationException>(
            () => CreateDocAsync(h, warehouse.Id, (int)AdjustType.Gain, Line(ItemB, 1m)));
        Assert.Contains(batchError.FieldErrors, static e => e.FieldName == "lines[0].batchNo");
    }

    // ============ 提交审批 ============

    [Fact]
    public async Task SubmitStartsApprovalAndMarksPending()
    {
        Harness h = Build();
        WarehouseRow warehouse = await CreateWarehouseAsync(h);
        h.Txn.AvgCosts[(ItemA, warehouse.Id)] = 5m;
        InventoryAdjustDetailRow detail = await CreateDocAsync(
            h, warehouse.Id, (int)AdjustType.Gain, Line(ItemA, 2m));

        InventoryAdjustRow submitted = await h.Service.SubmitAsync(
            detail.Adjust.Id, TestContext.Current.CancellationToken);

        Assert.Equal(2, submitted.Status);
        StartApprovalRequest started = Assert.Single(h.Approvals.Started);
        Assert.Equal(InventoryAdjustService.ApproveFlowCode, started.ApprovalDefinitionCode);
        Assert.Equal(InventoryAdjustService.ApproveBizType, started.BizType);

        List<WmsApprovalRequest> requests = await h.Db.ApprovalRequests.AsNoTracking()
            .ToListAsync(TestContext.Current.CancellationToken);
        WmsApprovalRequest request = Assert.Single(requests);
        Assert.Equal(InventoryAdjustService.ApproveBizType, request.BizType);
        Assert.Equal(detail.Adjust.Id, request.BizId);
        Assert.NotNull(request.ApprovalInstanceId);
        Assert.Empty(await LoadOutboxAsync(h));
    }

    // ============ 审批通过：入队 ============

    [Fact]
    public async Task ApprovedApplyEnqueuesOneCommandPerLine()
    {
        Harness h = Build();
        WarehouseRow warehouse = await CreateWarehouseAsync(h);
        h.Txn.AvgCosts[(ItemA, warehouse.Id)] = 5m;
        h.Txn.AvgCosts[(ItemB, warehouse.Id)] = 3.25m;
        InventoryAdjustDetailRow detail = await CreateDocAsync(
            h, warehouse.Id, (int)AdjustType.Gain, Line(ItemA, 4m), Line(ItemB, 2m, "B1"));
        _ = await h.Service.SubmitAsync(detail.Adjust.Id, TestContext.Current.CancellationToken);

        bool applied = await h.Service.ApplyAsync(
            detail.Adjust.Id, approved: true, TestContext.Current.CancellationToken);

        Assert.True(applied);
        InventoryAdjustDoc stored = Assert.Single(await LoadDocsAsync(h));
        Assert.Equal(AdjustDocStatus.Approved, stored.Status);

        List<WmsStockCommandOutbox> outbox = await LoadOutboxAsync(h);
        Assert.Equal(2, outbox.Count);
        Dictionary<int, PostStockTxnRequest> payloads = outbox.ToDictionary(
            static row => JsonSerializer.Deserialize<PostStockTxnRequest>(
                row.PayloadJson, QlxJsonDefaults.PreserveNull)!.SourceLineNo,
            static row => JsonSerializer.Deserialize<PostStockTxnRequest>(
                row.PayloadJson, QlxJsonDefaults.PreserveNull)!);

        Assert.Equal(StockSourceType.Gain, payloads[1].SourceType);
        Assert.Equal(StockDirection.In, payloads[1].Direction);
        Assert.Equal(5m, payloads[1].UnitCost);
        Assert.Equal(StockSourceType.Gain, payloads[2].SourceType);
        Assert.Equal(3.25m, payloads[2].UnitCost);
        Assert.Equal($"wms:py:{detail.Adjust.AdjustNo}:1", payloads[1].IdempotencyKey);
        Assert.Equal($"wms:py:{detail.Adjust.AdjustNo}:2", payloads[2].IdempotencyKey);

        // 审批通过只入队 Outbox，禁止同步直连台账
        Assert.Empty(h.Txn.Posts);
    }

    [Fact]
    public async Task LossApplyEnqueuesOutWithZeroUnitCost()
    {
        Harness h = Build();
        WarehouseRow warehouse = await CreateWarehouseAsync(h);
        h.Txn.AvgCosts[(ItemA, warehouse.Id)] = 5m;
        InventoryAdjustDetailRow detail = await CreateDocAsync(
            h, warehouse.Id, (int)AdjustType.Loss, Line(ItemA, 3m));
        _ = await h.Service.SubmitAsync(detail.Adjust.Id, TestContext.Current.CancellationToken);

        _ = await h.Service.ApplyAsync(
            detail.Adjust.Id, approved: true, TestContext.Current.CancellationToken);

        PostStockTxnRequest payload = JsonSerializer.Deserialize<PostStockTxnRequest>(
            Assert.Single(await LoadOutboxAsync(h)).PayloadJson, QlxJsonDefaults.PreserveNull)!;
        Assert.Equal(StockSourceType.Loss, payload.SourceType);
        Assert.Equal(StockDirection.Out, payload.Direction);
        Assert.Equal(3m, payload.Qty);
        Assert.Equal(0m, payload.UnitCost);
    }

    // ============ 幂等与驳回 ============

    [Fact]
    public async Task ApplyIsIdempotent()
    {
        Harness h = Build();
        WarehouseRow warehouse = await CreateWarehouseAsync(h);
        h.Txn.AvgCosts[(ItemA, warehouse.Id)] = 5m;
        InventoryAdjustDetailRow detail = await CreateDocAsync(
            h, warehouse.Id, (int)AdjustType.Gain, Line(ItemA, 2m));
        _ = await h.Service.SubmitAsync(detail.Adjust.Id, TestContext.Current.CancellationToken);
        _ = await h.Service.ApplyAsync(
            detail.Adjust.Id, approved: true, TestContext.Current.CancellationToken);

        bool again = await h.Service.ApplyAsync(
            detail.Adjust.Id, approved: true, TestContext.Current.CancellationToken);

        Assert.False(again);
        Assert.Single(await LoadOutboxAsync(h));
    }

    [Fact]
    public async Task ApplyBeforeApprovalDoesNothing()
    {
        Harness h = Build();
        WarehouseRow warehouse = await CreateWarehouseAsync(h);
        InventoryAdjustDetailRow detail = await CreateDocAsync(
            h, warehouse.Id, (int)AdjustType.Gain, Line(ItemA, 2m));

        bool applied = await h.Service.ApplyAsync(
            detail.Adjust.Id, approved: true, TestContext.Current.CancellationToken);

        Assert.False(applied);
        Assert.Empty(await LoadOutboxAsync(h));
        Assert.Equal(AdjustDocStatus.Draft, Assert.Single(await LoadDocsAsync(h)).Status);
    }

    [Fact]
    public async Task RejectReturnsToDraftAndAllowsResubmit()
    {
        Harness h = Build();
        WarehouseRow warehouse = await CreateWarehouseAsync(h);
        h.Txn.AvgCosts[(ItemA, warehouse.Id)] = 5m;
        InventoryAdjustDetailRow detail = await CreateDocAsync(
            h, warehouse.Id, (int)AdjustType.Gain, Line(ItemA, 2m));
        _ = await h.Service.SubmitAsync(detail.Adjust.Id, TestContext.Current.CancellationToken);

        bool applied = await h.Service.ApplyAsync(
            detail.Adjust.Id, approved: false, TestContext.Current.CancellationToken);

        Assert.True(applied);
        Assert.Equal(AdjustDocStatus.Draft, Assert.Single(await LoadDocsAsync(h)).Status);
        Assert.Empty(await LoadOutboxAsync(h));

        InventoryAdjustRow resubmitted = await h.Service.SubmitAsync(
            detail.Adjust.Id, TestContext.Current.CancellationToken);
        Assert.Equal(2, resubmitted.Status);
        Assert.Equal(2, h.Approvals.Started.Count);
    }

    // ============ 盘点完成自动生成草稿 ============

    [Fact]
    public async Task StockTakeRecheckCompletionCreatesGainAndLossDrafts()
    {
        Harness h = Build();
        WarehouseRow warehouse = await CreateWarehouseAsync(h);
        h.Txn.AvgCosts[(ItemA, warehouse.Id)] = 5m;
        h.Txn.AvgCosts[(ItemB, warehouse.Id)] = 3.25m;
        h.Stock.Balances.Add(new StockBalanceRow(ItemA, warehouse.Id, 10m, 5m, 50m, 0m, null));
        h.Stock.Balances.Add(new StockBalanceRow(ItemB, warehouse.Id, 4.5m, 3.25m, 14.63m, 0m, null));

        StockTakePlanRow plan = await h.Take.CreatePlanAsync(
            new CreateStockTakePlanPayload(
                new DateOnly(2026, 10, 26), 1, warehouse.Id, null, null, false, null),
            TestContext.Current.CancellationToken);
        StockTakeDetailRow take = await h.Take.GenerateAsync(
            plan.Id, TestContext.Current.CancellationToken);
        _ = await h.Take.StartAsync(take.Take.Id, TestContext.Current.CancellationToken);
        _ = await h.Take.SaveCountsAsync(
            take.Take.Id,
            new SaveStockTakeCountsPayload(
            [
                new SaveStockTakeCountLinePayload(take.Lines[0].Id, 11m, "盘盈"),
                new SaveStockTakeCountLinePayload(take.Lines[1].Id, 3m, "盘亏"),
            ]),
            TestContext.Current.CancellationToken);
        StockTakeFinishResultRow initial = await h.Take.FinishAsync(
            take.Take.Id, TestContext.Current.CancellationToken);

        StockTakeDetailRow recheck = await h.Take.GetAsync(
            initial.RecheckTakeId!.Value, TestContext.Current.CancellationToken);
        _ = await h.Take.SaveCountsAsync(
            recheck.Take.Id,
            new SaveStockTakeCountsPayload(
            [
                new SaveStockTakeCountLinePayload(recheck.Lines[0].Id, 11m, "复盘盘盈"),
                new SaveStockTakeCountLinePayload(recheck.Lines[1].Id, 3m, "复盘盘亏"),
            ]),
            TestContext.Current.CancellationToken);
        _ = await h.Take.FinishAsync(recheck.Take.Id, TestContext.Current.CancellationToken);

        List<InventoryAdjustDoc> docs = await LoadDocsAsync(h);
        Assert.Equal(2, docs.Count);

        InventoryAdjustDoc gain = Assert.Single(docs, static doc => doc.Type == (int)AdjustType.Gain);
        Assert.Equal(AdjustDocStatus.Draft, gain.Status);
        Assert.Equal(recheck.Take.Id, gain.TakeId);
        Assert.Equal(5m, gain.TotalAmount);

        InventoryAdjustDoc loss = Assert.Single(docs, static doc => doc.Type == (int)AdjustType.Loss);
        Assert.Equal(AdjustDocStatus.Draft, loss.Status);
        Assert.Equal(4.88m, loss.TotalAmount);

        Assert.Empty(await LoadOutboxAsync(h));
    }
}

/// <summary>库存写入命令桩：提供台账均价与冻结记录，禁止同步直连台账。</summary>
internal sealed class AdjustTxnCommandService : IStockTxnCommandService
{
    public Dictionary<(long ItemId, long WarehouseId), decimal> AvgCosts { get; } = [];

    public List<(long ItemId, decimal Delta)> LockCalls { get; } = [];

    public List<PostStockTxnRequest> Posts { get; } = [];

    public Task<StockPostResult> PostAsync(
        PostStockTxnRequest request, CancellationToken cancellationToken)
    {
        Posts.Add(request);
        return Task.FromResult(new StockPostResult(
            true, null, null, 0m, 0m, null, 0m));
    }

    public Task<IReadOnlyList<StockPostResult>> PostBatchAsync(
        IReadOnlyList<PostStockTxnRequest> requests, CancellationToken cancellationToken)
        => throw new InvalidOperationException("审批通过必须经 Outbox 分发，禁止同步直连台账");

    public Task AdjustLockedQtyAsync(
        long itemId, long warehouseId, decimal deltaQty, string reason, CancellationToken cancellationToken)
    {
        LockCalls.Add((itemId, deltaQty));
        return Task.CompletedTask;
    }

    public Task<StockBalanceRow?> FindBalanceAsync(
        long itemId, long warehouseId, CancellationToken cancellationToken)
        => Task.FromResult(AvgCosts.TryGetValue((itemId, warehouseId), out decimal avgCost)
            ? new StockBalanceRow(itemId, warehouseId, 100m, avgCost, 100m * avgCost, 0m, null)
            : (StockBalanceRow?)null);
}
