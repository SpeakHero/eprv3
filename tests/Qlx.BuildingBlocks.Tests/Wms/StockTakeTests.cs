/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-26
 * 描述：盘点与冻结测试——范围快照、品类范围过滤、冻结精确性、重复生成 409、盲盘隐藏账面量、
 *       初盘→复盘、复盘完成解冻与盘盈亏草稿、无差异直接完成、取消解冻幂等
 */

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

using Qlx.Abstractions.Exceptions;
using Qlx.Abstractions.Queries;
using Qlx.Abstractions.Stock;
using Qlx.BuildingBlocks.Caching;
using Qlx.BuildingBlocks.Tests.Bas;
using Qlx.BuildingBlocks.Tests.Files;
using Qlx.BuildingBlocks.Tests.Jobs;
using Qlx.Modules.Wms;
using Qlx.Modules.Wms.Dtos;
using Qlx.Modules.Wms.Repositories;
using Qlx.Modules.Wms.Services;

namespace Qlx.BuildingBlocks.Tests.Wms;

/// <summary>盘点与冻结测试。</summary>
public sealed class StockTakeTests
{
    private const long TenantId = 88L;
    private const string Operator = "user-88";
    private const long OrgId = 9001L;

    private const long ItemA = 7301L;
    private const long ItemB = 7302L;
    private const long CategoryA = 101L;
    private const long CategoryB = 102L;

    private sealed class Harness
    {
        public required WmsTenantDbContext Db { get; init; }

        public required WarehouseService Warehouse { get; init; }

        public required StockTakeService Service { get; init; }

        public required StubTakeStockQueryService Stock { get; init; }

        public required StubStockTakeTxnCommandService Txn { get; init; }

        public required StubStockTakeDraftCreator Drafts { get; init; }
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
        var stock = new StubTakeStockQueryService();
        var txn = new StubStockTakeTxnCommandService();
        var drafts = new StubStockTakeDraftCreator();
        var items = new StubItemQueryService
        {
            Rows =
            {
                [ItemA] = new StockItemQueryRow(ItemA, false, false, 5m, 1, CategoryA),
                [ItemB] = new StockItemQueryRow(ItemB, true, true, 3m, 1, CategoryB),
            },
        };

        return new Harness
        {
            Db = db,
            Stock = stock,
            Txn = txn,
            Drafts = drafts,
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
            Service = new StockTakeService(
                tenantCtx,
                repo,
                new StubDocumentNumberGenerator(),
                new StubOrgQueryService(),
                items,
                stock,
                txn,
                drafts),
        };
    }

    private static Task<WarehouseRow> CreateWarehouseAsync(Harness h)
        => h.Warehouse.CreateAsync(
            new CreateWarehousePayload("盘点仓", 1, OrgId, null, null, null),
            TestContext.Current.CancellationToken);

    private static Task<StockTakePlanRow> CreatePlanAsync(
        Harness h,
        long warehouseId,
        int scopeType = 1,
        long? categoryId = null,
        long? locationId = null,
        bool isBlind = false)
        => h.Service.CreatePlanAsync(
            new CreateStockTakePlanPayload(
                new DateOnly(2026, 10, 26), scopeType, warehouseId, categoryId, locationId, isBlind, null),
            TestContext.Current.CancellationToken);

    private static void SeedBalances(Harness h, long warehouseId)
    {
        h.Stock.Balances.Add(new StockBalanceRow(ItemA, warehouseId, 10m, 5m, 50m, 0m, null));
        h.Stock.Balances.Add(new StockBalanceRow(ItemB, warehouseId, 4.5m, 3m, 13.5m, 0m, null));
    }

    private static async Task<StockTakeDetailRow> PreparedTakeAsync(
        Harness h, long warehouseId, bool isBlind = false)
    {
        StockTakePlanRow plan = await CreatePlanAsync(h, warehouseId, isBlind: isBlind);
        return await h.Service.GenerateAsync(
            plan.Id, TestContext.Current.CancellationToken);
    }

    private static Task<StockTakeDetailRow> SaveCountsAsync(
        Harness h, long takeId, params SaveStockTakeCountLinePayload[] lines)
        => h.Service.SaveCountsAsync(
            takeId, new SaveStockTakeCountsPayload(lines), TestContext.Current.CancellationToken);

    // ============ 生成：快照 + 冻结 ============

    [Fact]
    public async Task GenerateSnapshotsBalancesAndFreezesEachLine()
    {
        Harness h = Build();
        WarehouseRow warehouse = await CreateWarehouseAsync(h);
        SeedBalances(h, warehouse.Id);
        StockTakePlanRow plan = await CreatePlanAsync(h, warehouse.Id);

        StockTakeDetailRow take = await h.Service.GenerateAsync(
            plan.Id, TestContext.Current.CancellationToken);

        Assert.Equal(1, take.Take.Status);
        Assert.Equal(1, take.Take.Round);
        Assert.Equal(2, take.Lines.Count);
        Assert.Equal(10m, take.Lines[0].BookQty);
        Assert.Equal(4.5m, take.Lines[1].BookQty);
        Assert.All(take.Lines, static line => Assert.True(line.Frozen));

        Assert.Equal(2, h.Txn.LockCalls.Count);
        Assert.Contains(h.Txn.LockCalls, call => call.ItemId == ItemA && call.Delta == 10m);
        Assert.Contains(h.Txn.LockCalls, call => call.ItemId == ItemB && call.Delta == 4.5m);

        var stored = await h.Db.StockTakePlans.AsNoTracking()
            .SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(take.Take.Id, stored.TakeId);
    }

    [Fact]
    public async Task GenerateSplitsBatchManagedItemByBatchAndKeepsFreezeTotal()
    {
        Harness h = Build();
        WarehouseRow warehouse = await CreateWarehouseAsync(h);
        h.Stock.Balances.Add(new StockBalanceRow(ItemB, warehouse.Id, 7m, 3m, 21m, 0m, null));
        h.Stock.Batches.Add(new StockBatchRow(
            ItemB, warehouse.Id, "B-002", null, null, 4m, 3m, 12m));
        h.Stock.Batches.Add(new StockBatchRow(
            ItemB, warehouse.Id, "B-001", null, null, 3m, 3m, 9m));
        StockTakePlanRow plan = await CreatePlanAsync(h, warehouse.Id);

        StockTakeDetailRow take = await h.Service.GenerateAsync(
            plan.Id, TestContext.Current.CancellationToken);

        Assert.Equal(2, take.Lines.Count);
        Assert.Equal("B-001", take.Lines[0].BatchNo);
        Assert.Equal(3m, take.Lines[0].BookQty);
        Assert.Equal("B-002", take.Lines[1].BatchNo);
        Assert.Equal(4m, take.Lines[1].BookQty);
        Assert.All(take.Lines, static line => Assert.True(line.Frozen));

        // 冻结仍按商品×仓库累计：逐批次调用，合计等于台账现存
        Assert.Equal(2, h.Txn.LockCalls.Count);
        Assert.Equal(7m, h.Txn.LockCalls.Sum(static call => call.Delta));
    }

    [Fact]
    public async Task GenerateCategoryScopeOnlyIncludesMatchingItems()
    {
        Harness h = Build();
        WarehouseRow warehouse = await CreateWarehouseAsync(h);
        SeedBalances(h, warehouse.Id);
        StockTakePlanRow plan = await CreatePlanAsync(
            h, warehouse.Id, scopeType: 2, categoryId: CategoryA);

        StockTakeDetailRow take = await h.Service.GenerateAsync(
            plan.Id, TestContext.Current.CancellationToken);

        StockTakeLineRow line = Assert.Single(take.Lines);
        Assert.Equal(ItemA, line.ItemId);
        Assert.Equal(10m, line.BookQty);
        Assert.Single(h.Txn.LockCalls);
        Assert.Equal(10m, h.Txn.LockCalls[0].Delta);
    }

    [Fact]
    public async Task GenerateRejectsCategoryScopeWithoutCategory()
    {
        Harness h = Build();
        WarehouseRow warehouse = await CreateWarehouseAsync(h);

        ValidationException ex = await Assert.ThrowsAsync<ValidationException>(
            () => CreatePlanAsync(h, warehouse.Id, scopeType: 2));

        Assert.Contains(ex.FieldErrors, static e => e.FieldName == "categoryId");
    }

    [Fact]
    public async Task GenerateTwiceReturnsConflictWithoutExtraFreeze()
    {
        Harness h = Build();
        WarehouseRow warehouse = await CreateWarehouseAsync(h);
        SeedBalances(h, warehouse.Id);
        StockTakePlanRow plan = await CreatePlanAsync(h, warehouse.Id);
        _ = await h.Service.GenerateAsync(plan.Id, TestContext.Current.CancellationToken);

        ConflictException ex = await Assert.ThrowsAsync<ConflictException>(
            () => h.Service.GenerateAsync(plan.Id, TestContext.Current.CancellationToken));

        Assert.Equal(StockTakeService.ErrorInvalidState, ex.ErrorCode);
        Assert.Equal(2, h.Txn.LockCalls.Count);
    }

    // ============ 盲盘字段裁剪 ============

    [Fact]
    public async Task BlindTakeDetailHidesBookQtyAndDiff()
    {
        Harness h = Build();
        WarehouseRow warehouse = await CreateWarehouseAsync(h);
        SeedBalances(h, warehouse.Id);
        StockTakeDetailRow take = await PreparedTakeAsync(h, warehouse.Id, isBlind: true);
        _ = await h.Service.StartAsync(take.Take.Id, TestContext.Current.CancellationToken);

        StockTakeLineRow line = take.Lines[0];
        _ = await SaveCountsAsync(
            h, take.Take.Id,
            new SaveStockTakeCountLinePayload(line.Id, 8m, "盘亏"));

        StockTakeDetailRow detail = await h.Service.GetAsync(
            take.Take.Id, TestContext.Current.CancellationToken);

        Assert.All(detail.Lines, static row =>
        {
            Assert.Null(row.BookQty);
            Assert.Null(row.DiffQty);
        });
        Assert.Equal(8m, detail.Lines[0].ActualQty);
    }

    [Fact]
    public async Task VisibleTakeDetailReturnsBookQtyAndDiff()
    {
        Harness h = Build();
        WarehouseRow warehouse = await CreateWarehouseAsync(h);
        SeedBalances(h, warehouse.Id);
        StockTakeDetailRow take = await PreparedTakeAsync(h, warehouse.Id);
        _ = await h.Service.StartAsync(take.Take.Id, TestContext.Current.CancellationToken);
        StockTakeLineRow line = take.Lines[0];
        _ = await SaveCountsAsync(
            h, take.Take.Id, new SaveStockTakeCountLinePayload(line.Id, 8m, "盘亏"));

        StockTakeDetailRow detail = await h.Service.GetAsync(
            take.Take.Id, TestContext.Current.CancellationToken);

        Assert.Equal(10m, detail.Lines[0].BookQty);
        Assert.Equal(-2m, detail.Lines[0].DiffQty);
    }

    // ============ 初盘 → 复盘 ============

    [Fact]
    public async Task FinishInitialWithDiffCreatesRecheckTake()
    {
        Harness h = Build();
        WarehouseRow warehouse = await CreateWarehouseAsync(h);
        SeedBalances(h, warehouse.Id);
        StockTakeDetailRow take = await PreparedTakeAsync(h, warehouse.Id);
        _ = await h.Service.StartAsync(take.Take.Id, TestContext.Current.CancellationToken);
        _ = await SaveCountsAsync(
            h, take.Take.Id,
            new SaveStockTakeCountLinePayload(take.Lines[0].Id, 8m, "盘亏"),
            new SaveStockTakeCountLinePayload(take.Lines[1].Id, 4.5m, null));

        StockTakeFinishResultRow result = await h.Service.FinishAsync(
            take.Take.Id, TestContext.Current.CancellationToken);

        Assert.Equal(3, result.Detail.Take.Status);
        Assert.NotNull(result.RecheckTakeId);

        StockTakeDetailRow recheck = await h.Service.GetAsync(
            result.RecheckTakeId!.Value, TestContext.Current.CancellationToken);
        Assert.Equal(2, recheck.Take.Round);
        Assert.Equal(4, recheck.Take.Status);
        Assert.Equal(take.Take.Id, recheck.Take.SourceTakeId);
        StockTakeLineRow line = Assert.Single(recheck.Lines);
        Assert.Equal(ItemA, line.ItemId);
        Assert.Equal(10m, line.BookQty);

        // 初盘完成不解冻
        Assert.Equal(2, h.Txn.LockCalls.Count);
        Assert.DoesNotContain(h.Txn.LockCalls, static call => call.Delta < 0m);
    }

    [Fact]
    public async Task FinishRecheckUnfreezesOnceAndCreatesDraft()
    {
        Harness h = Build();
        WarehouseRow warehouse = await CreateWarehouseAsync(h);
        SeedBalances(h, warehouse.Id);
        StockTakeDetailRow take = await PreparedTakeAsync(h, warehouse.Id);
        _ = await h.Service.StartAsync(take.Take.Id, TestContext.Current.CancellationToken);
        _ = await SaveCountsAsync(
            h, take.Take.Id,
            new SaveStockTakeCountLinePayload(take.Lines[0].Id, 8m, "盘亏"),
            new SaveStockTakeCountLinePayload(take.Lines[1].Id, 4.5m, null));
        StockTakeFinishResultRow initial = await h.Service.FinishAsync(
            take.Take.Id, TestContext.Current.CancellationToken);

        StockTakeDetailRow recheck = await h.Service.GetAsync(
            initial.RecheckTakeId!.Value, TestContext.Current.CancellationToken);
        _ = await SaveCountsAsync(
            h, recheck.Take.Id,
            new SaveStockTakeCountLinePayload(recheck.Lines[0].Id, 11m, "复盘盘盈"));

        StockTakeFinishResultRow result = await h.Service.FinishAsync(
            recheck.Take.Id, TestContext.Current.CancellationToken);

        Assert.Equal(5, result.Detail.Take.Status);
        Assert.Null(result.RecheckTakeId);

        // 初盘全部冻结行各解冻一次（含无差异行）
        List<(long ItemId, decimal Delta, string Reason)> unfreeze = [.. h.Txn.LockCalls
            .Where(static call => call.Delta < 0m)];
        Assert.Equal(2, unfreeze.Count);
        Assert.Contains(unfreeze, static call => call.ItemId == ItemA && call.Delta == -10m);
        Assert.Contains(unfreeze, static call => call.ItemId == ItemB && call.Delta == -4.5m);

        StockTakeAdjustDraftContext draft = Assert.Single(h.Drafts.Contexts);
        Assert.Equal(recheck.Take.Id, draft.TakeId);
        StockTakeAdjustDraftLine draftLine = Assert.Single(draft.Lines);
        Assert.Equal(ItemA, draftLine.ItemId);
        Assert.Equal(1m, draftLine.DiffQty);

        int lockCount = h.Txn.LockCalls.Count;
        _ = await Assert.ThrowsAsync<ConflictException>(
            () => h.Service.FinishAsync(recheck.Take.Id, TestContext.Current.CancellationToken));
        Assert.Equal(lockCount, h.Txn.LockCalls.Count);
    }

    [Fact]
    public async Task FinishRecheckDraftFailureDoesNotUnfreeze()
    {
        Harness h = Build();
        WarehouseRow warehouse = await CreateWarehouseAsync(h);
        SeedBalances(h, warehouse.Id);
        StockTakeDetailRow take = await PreparedTakeAsync(h, warehouse.Id);
        _ = await h.Service.StartAsync(take.Take.Id, TestContext.Current.CancellationToken);
        _ = await SaveCountsAsync(
            h, take.Take.Id,
            new SaveStockTakeCountLinePayload(take.Lines[0].Id, 8m, "盘亏"),
            new SaveStockTakeCountLinePayload(take.Lines[1].Id, 4.5m, null));
        StockTakeFinishResultRow initial = await h.Service.FinishAsync(
            take.Take.Id, TestContext.Current.CancellationToken);
        StockTakeDetailRow recheck = await h.Service.GetAsync(
            initial.RecheckTakeId!.Value, TestContext.Current.CancellationToken);
        _ = await SaveCountsAsync(
            h, recheck.Take.Id,
            new SaveStockTakeCountLinePayload(recheck.Lines[0].Id, 9m, "复盘盘亏"));

        h.Drafts.Fail = true;
        _ = await Assert.ThrowsAsync<InvalidOperationException>(
            () => h.Service.FinishAsync(recheck.Take.Id, TestContext.Current.CancellationToken));

        // 草稿单号冲突先于解冻暴露：失败后不得调用任何解冻命令
        Assert.DoesNotContain(h.Txn.LockCalls, static call => call.Delta < 0m);
    }

    [Fact]
    public async Task FinishInitialWithoutDiffCompletesAndUnfreezes()
    {
        Harness h = Build();
        WarehouseRow warehouse = await CreateWarehouseAsync(h);
        SeedBalances(h, warehouse.Id);
        StockTakeDetailRow take = await PreparedTakeAsync(h, warehouse.Id);
        _ = await h.Service.StartAsync(take.Take.Id, TestContext.Current.CancellationToken);
        _ = await SaveCountsAsync(
            h, take.Take.Id,
            new SaveStockTakeCountLinePayload(take.Lines[0].Id, 10m, null),
            new SaveStockTakeCountLinePayload(take.Lines[1].Id, 4.5m, null));

        StockTakeFinishResultRow result = await h.Service.FinishAsync(
            take.Take.Id, TestContext.Current.CancellationToken);

        Assert.Equal(5, result.Detail.Take.Status);
        Assert.Null(result.RecheckTakeId);
        Assert.Equal(2, h.Txn.LockCalls.Count(static call => call.Delta < 0m));
        Assert.Empty(h.Drafts.Contexts);
    }

    // ============ 取消与状态机 ============

    [Fact]
    public async Task CancelUnfreezesOnceAndSecondCancelConflicts()
    {
        Harness h = Build();
        WarehouseRow warehouse = await CreateWarehouseAsync(h);
        SeedBalances(h, warehouse.Id);
        StockTakeDetailRow take = await PreparedTakeAsync(h, warehouse.Id);
        _ = await h.Service.StartAsync(take.Take.Id, TestContext.Current.CancellationToken);

        StockTakeRow cancelled = await h.Service.CancelAsync(
            take.Take.Id, TestContext.Current.CancellationToken);

        Assert.Equal(6, cancelled.Status);
        Assert.Equal(2, h.Txn.LockCalls.Count(static call => call.Delta < 0m));

        int lockCount = h.Txn.LockCalls.Count;
        ConflictException ex = await Assert.ThrowsAsync<ConflictException>(
            () => h.Service.CancelAsync(take.Take.Id, TestContext.Current.CancellationToken));
        Assert.Equal(StockTakeService.ErrorInvalidState, ex.ErrorCode);
        Assert.Equal(lockCount, h.Txn.LockCalls.Count);
    }

    [Fact]
    public async Task FinishPendingTakeReturnsConflict()
    {
        Harness h = Build();
        WarehouseRow warehouse = await CreateWarehouseAsync(h);
        SeedBalances(h, warehouse.Id);
        StockTakeDetailRow take = await PreparedTakeAsync(h, warehouse.Id);

        _ = await Assert.ThrowsAsync<ConflictException>(
            () => h.Service.FinishAsync(take.Take.Id, TestContext.Current.CancellationToken));

        _ = await h.Service.StartAsync(take.Take.Id, TestContext.Current.CancellationToken);
        _ = await Assert.ThrowsAsync<ConflictException>(
            () => h.Service.StartAsync(take.Take.Id, TestContext.Current.CancellationToken));
    }

    // ============ 校验 ============

    [Fact]
    public async Task SaveCountsRejectsForeignLineAndMissingDiffReason()
    {
        Harness h = Build();
        WarehouseRow warehouse = await CreateWarehouseAsync(h);
        SeedBalances(h, warehouse.Id);
        StockTakeDetailRow take = await PreparedTakeAsync(h, warehouse.Id);
        _ = await h.Service.StartAsync(take.Take.Id, TestContext.Current.CancellationToken);

        ValidationException foreign = await Assert.ThrowsAsync<ValidationException>(
            () => SaveCountsAsync(h, take.Take.Id,
                new SaveStockTakeCountLinePayload(999999L, 1m, "x")));
        Assert.Contains(foreign.FieldErrors, static e => e.FieldName == "lines[0].lineId");

        ValidationException missingReason = await Assert.ThrowsAsync<ValidationException>(
            () => SaveCountsAsync(h, take.Take.Id,
                new SaveStockTakeCountLinePayload(take.Lines[0].Id, 8m, "  ")));
        Assert.Contains(missingReason.FieldErrors, static e => e.FieldName == "lines[0].diffReason");
    }
}

/// <summary>库存只读查询桩：按仓库返回可配置台账行与批次行。</summary>
internal sealed class StubTakeStockQueryService : IStockQueryService
{
    public List<StockBalanceRow> Balances { get; } = [];

    public List<StockBatchRow> Batches { get; } = [];

    public Task<bool> HasStockInWarehouseAsync(long warehouseId, CancellationToken cancellationToken)
        => Task.FromResult(Balances.Any(balance => balance.WarehouseId == warehouseId));

    public Task<StockBalanceRow?> FindBalanceAsync(
        long itemId, long warehouseId, CancellationToken cancellationToken)
        => Task.FromResult(Balances.FirstOrDefault(
            balance => balance.ItemId == itemId && balance.WarehouseId == warehouseId));

    public Task<IReadOnlyList<StockBalanceRow>> ListBalancesByItemAsync(
        long itemId, CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<StockBalanceRow>>(
            [.. Balances.Where(balance => balance.ItemId == itemId)]);

    public Task<IReadOnlyList<StockBalanceRow>> ListBalancesByWarehouseAsync(
        long warehouseId, CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<StockBalanceRow>>(
            [.. Balances.Where(balance => balance.WarehouseId == warehouseId)]);

    public Task<IReadOnlyList<StockBatchRow>> ListBatchesAsync(
        long itemId, long warehouseId, CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<StockBatchRow>>(
            [.. Batches.Where(batch => batch.ItemId == itemId && batch.WarehouseId == warehouseId)]);
}

/// <summary>库存命令桩：仅记录冻结/解冻调用，禁止同步写台账。</summary>
internal sealed class StubStockTakeTxnCommandService : IStockTxnCommandService
{
    public List<(long ItemId, decimal Delta, string Reason)> LockCalls { get; } = [];

    public Task AdjustLockedQtyAsync(
        long itemId, long warehouseId, decimal deltaQty, string reason, CancellationToken cancellationToken)
    {
        LockCalls.Add((itemId, deltaQty, reason));
        return Task.CompletedTask;
    }

    public Task<StockPostResult> PostAsync(
        PostStockTxnRequest request, CancellationToken cancellationToken)
        => throw new InvalidOperationException("盘点冻结只经冻结量调整，禁止同步写台账");

    public Task<IReadOnlyList<StockPostResult>> PostBatchAsync(
        IReadOnlyList<PostStockTxnRequest> requests, CancellationToken cancellationToken)
        => throw new InvalidOperationException("盘点冻结只经冻结量调整，禁止同步写台账");

    public Task<StockBalanceRow?> FindBalanceAsync(
        long itemId, long warehouseId, CancellationToken cancellationToken)
        => Task.FromResult<StockBalanceRow?>(null);
}

/// <summary>盘盈亏草稿生成桩：记录生成上下文。</summary>
internal sealed class StubStockTakeDraftCreator : IStockTakeAdjustDraftCreator
{
    public List<StockTakeAdjustDraftContext> Contexts { get; } = [];

    public bool Fail { get; set; }

    public Task<int> CreateAsync(StockTakeAdjustDraftContext context, CancellationToken ct)
    {
        if (Fail)
        {
            throw new InvalidOperationException("模拟盘盈亏单号冲突");
        }

        Contexts.Add(context);
        return Task.FromResult(1);
    }
}
