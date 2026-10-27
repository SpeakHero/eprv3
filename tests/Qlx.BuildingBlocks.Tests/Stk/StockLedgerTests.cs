/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-26
 * 描述：库存台账铁律测试——移动加权平均、负库存拦截、幂等去重、批次同事务、冻结量、
 *       流水只增、Version 递增、精度 AwayFromZero（spec §6）
 */

using System.Data;

using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Qlx.Abstractions.Stock;
using Qlx.BuildingBlocks.Numbering;
using Qlx.BuildingBlocks.Tests.Bas;
using Qlx.BuildingBlocks.Tests.Files;
using Qlx.BuildingBlocks.Tests.Jobs;
using Qlx.Modules.Stk;
using Qlx.Modules.Stk.Domain.Inventory;
using Qlx.Modules.Stk.Domain.Item;
using Qlx.Modules.Stk.Repositories;
using Qlx.Modules.Stk.Services;

namespace Qlx.BuildingBlocks.Tests.Stk;

/// <summary>库存台账铁律测试。</summary>
public sealed class StockLedgerTests
{
    private const long TenantId = 42L;
    private const long WarehouseId = 7001L;
    private const string Operator = "user-9";

    private static readonly DateTimeOffset OccurredAt =
        new(2026, 10, 26, 9, 0, 0, TimeSpan.FromHours(8));

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>测试夹具：SQLite 库 + 台账仓储 + 台账服务。</summary>
    private sealed class Harness
    {
        public required StkTenantDbContext Db { get; init; }

        public required StkTenantRepository Repo { get; init; }

        public required StockLedgerService Service { get; init; }
    }

    private static Harness Build()
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        if (connection.State != ConnectionState.Open)
        {
            connection.Open();
        }

        var db = new StkTenantDbContext(
            new JobStubTenantDatabaseResolver(),
            TenantId,
            new DbContextOptionsBuilder<StkTenantDbContext>().UseSqlite(connection).Options);
        _ = db.Database.EnsureCreated();

        var repo = new StkTenantRepository(db);
        return new Harness
        {
            Db = db,
            Repo = repo,
            Service = new StockLedgerService(
                new TestTenantContext(TenantId),
                repo,
                new StkStubDocumentNumberGenerator(),
                new TestCurrentUserContext(Operator),
                new BasRecordingEventBus()),
        };
    }

    /// <summary>先插入商品（台账服务会校验商品存在）。</summary>
    private static async Task<long> SeedItemAsync(Harness h, bool batchEnabled = false)
    {
        Item item = new(
            TenantId, $"T-{Guid.NewGuid():N}"[..12], "测试商品", null, null,
            categoryId: 5001L, unitId: 9001L, batchEnabled, expiryEnabled: false,
            taxRate: 0.13m, refCostPrice: null, guidePrice: null);
        _ = await h.Db.Items.AddAsync(item, Ct);
        _ = await h.Db.SaveChangesAsync(Ct);
        return item.Id;
    }

    private static PostStockTxnRequest Request(
        long itemId,
        StockDirection direction,
        decimal qty,
        decimal unitCost,
        string? batchNo = null,
        string docNo = "SRC-001",
        int lineNo = 1,
        string? idempotencyKey = null)
        => new(
            StockSourceType.PurchaseIn,
            docNo,
            lineNo,
            itemId,
            WarehouseId,
            batchNo,
            direction,
            qty,
            unitCost,
            Operator,
            idempotencyKey ?? $"{docNo}-{lineNo}",
            OccurredAt);

    private static Task<StockPostResult> PostAsync(Harness h, PostStockTxnRequest request)
        => h.Service.PostAsync(request, Ct);

    private static Task<StockBalance> LoadBalanceAsync(Harness h, long itemId)
        => h.Db.StockBalances.AsNoTracking()
            .SingleAsync(b => b.ItemId == itemId && b.WarehouseId == WarehouseId, Ct);

    private static Task<StockBatch> LoadBatchAsync(Harness h, long itemId, string batchNo)
        => h.Db.StockBatches.AsNoTracking()
            .SingleAsync(b => b.ItemId == itemId && b.WarehouseId == WarehouseId && b.BatchNo == batchNo, Ct);

    private static Task<int> CountTxnsAsync(Harness h)
        => h.Db.StockTxns.AsNoTracking().CountAsync(Ct);

    // ============ 1/2：移动加权平均（IN 两次 → OUT 一次）============

    [Fact]
    public async Task InTwiceThenOutUsesMovingWeightedAverage()
    {
        Harness h = Build();
        long itemId = await SeedItemAsync(h);

        StockPostResult firstIn = await PostAsync(h, Request(itemId, StockDirection.In, 10m, 10m, lineNo: 1));
        StockPostResult secondIn = await PostAsync(
            h, Request(itemId, StockDirection.In, 10m, 20m, docNo: "SRC-002", lineNo: 1));

        Assert.True(firstIn.Success);
        Assert.True(secondIn.Success);

        StockBalance afterIn = await LoadBalanceAsync(h, itemId);
        Assert.Equal(20m, afterIn.QtyOnhand);
        Assert.Equal(300m, afterIn.Amount);
        Assert.Equal(15m, afterIn.AvgCost);
        Assert.Equal(300m, secondIn.BalanceAmountAfter);

        StockPostResult outResult = await PostAsync(
            h, Request(itemId, StockDirection.Out, 5m, 0m, docNo: "SRC-003"));

        Assert.True(outResult.Success);
        Assert.Equal(15m, outResult.BalanceQtyAfter);
        Assert.Equal(225m, outResult.BalanceAmountAfter);

        StockBalance afterOut = await LoadBalanceAsync(h, itemId);
        Assert.Equal(15m, afterOut.QtyOnhand);
        Assert.Equal(225m, afterOut.Amount);
        Assert.Equal(15m, afterOut.AvgCost);

        // 出库单价取当时移动加权平均价，流水留存结存金额快照
        StockTxn txn = await h.Db.StockTxns.AsNoTracking()
            .SingleAsync(t => t.Id == outResult.TxnId!.Value, Ct);
        Assert.Equal(15m, txn.UnitCost);
        Assert.Equal(75m, txn.Amount);
        Assert.Equal(225m, txn.BalanceAmountAfter);
        Assert.Equal((int)StockDirection.Out, txn.Direction);
    }

    // ============ 3：超量出库整单拦截（零写入）============

    [Fact]
    public async Task OverOutIsBlockedWithoutAnyWrite()
    {
        Harness h = Build();
        long itemId = await SeedItemAsync(h);
        _ = await PostAsync(h, Request(itemId, StockDirection.In, 20m, 10m));

        int txnCountBefore = await CountTxnsAsync(h);
        StockBalance before = await LoadBalanceAsync(h, itemId);

        StockPostResult result = await PostAsync(
            h, Request(itemId, StockDirection.Out, 25m, 0m, docNo: "SRC-OUT"));

        Assert.False(result.Success);
        Assert.Equal(StockLedgerService.ErrorInsufficient, result.ErrorCode);
        Assert.Equal(5m, result.ShortageQty);
        Assert.Null(result.TxnId);

        StockBalance after = await LoadBalanceAsync(h, itemId);
        Assert.Equal(before.QtyOnhand, after.QtyOnhand);
        Assert.Equal(before.Amount, after.Amount);
        Assert.Equal(before.Version, after.Version);
        Assert.Equal(txnCountBefore, await CountTxnsAsync(h));
    }

    // ============ 4：幂等——同一来源键重复投递只落一条流水 ============

    [Fact]
    public async Task DuplicatePostIsIdempotent()
    {
        Harness h = Build();
        long itemId = await SeedItemAsync(h);
        PostStockTxnRequest request = Request(itemId, StockDirection.In, 10m, 10m, idempotencyKey: "IDEM-1");

        StockPostResult first = await PostAsync(h, request);
        StockPostResult second = await PostAsync(h, request);

        Assert.True(first.Success);
        Assert.True(second.Success);
        Assert.Equal(first.TxnId, second.TxnId);
        Assert.Equal(first.TxnNo, second.TxnNo);

        Assert.Equal(1, await CountTxnsAsync(h));
        StockBalance balance = await LoadBalanceAsync(h, itemId);
        Assert.Equal(10m, balance.QtyOnhand);
        Assert.Equal(100m, balance.Amount);
        Assert.Equal(1L, balance.Version);
    }

    // ============ 5：批次商品（必填/存在性/批次扣减）============

    [Fact]
    public async Task BatchItemRequiresBatchAndDeductsSpecifiedBatch()
    {
        Harness h = Build();
        long itemId = await SeedItemAsync(h, batchEnabled: true);

        _ = await PostAsync(h, Request(itemId, StockDirection.In, 10m, 10m, batchNo: "A", lineNo: 1));
        _ = await PostAsync(h, Request(itemId, StockDirection.In, 10m, 20m, batchNo: "B", docNo: "SRC-002"));

        // 缺批次号 → 批次必填
        StockPostResult missing = await PostAsync(
            h, Request(itemId, StockDirection.In, 1m, 10m, batchNo: null, docNo: "SRC-003"));
        Assert.False(missing.Success);
        Assert.Equal(StockLedgerService.ErrorBatchRequired, missing.ErrorCode);

        // 批次不存在 → 批次不存在
        StockPostResult notFound = await PostAsync(
            h, Request(itemId, StockDirection.Out, 1m, 0m, batchNo: "NOPE", docNo: "SRC-004"));
        Assert.False(notFound.Success);
        Assert.Equal(StockLedgerService.ErrorBatchNotFound, notFound.ErrorCode);

        // 指定批次出库 4 件（按台账移动加权均价 15 结算）
        StockPostResult outResult = await PostAsync(
            h, Request(itemId, StockDirection.Out, 4m, 0m, batchNo: "A", docNo: "SRC-005"));
        Assert.True(outResult.Success);

        StockBatch batchA = await LoadBatchAsync(h, itemId, "A");
        Assert.Equal(6m, batchA.Qty);
        Assert.Equal(60m, batchA.Amount);
        Assert.Equal(10m, batchA.AvgCost);

        StockBatch batchB = await LoadBatchAsync(h, itemId, "B");
        Assert.Equal(10m, batchB.Qty);
        Assert.Equal(200m, batchB.Amount);

        StockBalance balance = await LoadBalanceAsync(h, itemId);
        Assert.Equal(16m, balance.QtyOnhand);
        Assert.Equal(240m, balance.Amount);
        Assert.Equal(15m, balance.AvgCost);
    }

    [Fact]
    public async Task BatchShortageIsBlockedWithShortageQuantity()
    {
        Harness h = Build();
        long itemId = await SeedItemAsync(h, batchEnabled: true);
        _ = await PostAsync(h, Request(itemId, StockDirection.In, 10m, 10m, batchNo: "A", lineNo: 1));
        _ = await PostAsync(h, Request(itemId, StockDirection.In, 10m, 20m, batchNo: "B", docNo: "SRC-002"));

        StockPostResult result = await PostAsync(
            h, Request(itemId, StockDirection.Out, 12m, 0m, batchNo: "A", docNo: "SRC-003"));

        Assert.False(result.Success);
        Assert.Equal(StockLedgerService.ErrorInsufficient, result.ErrorCode);
        Assert.Equal(2m, result.ShortageQty);

        // 零写入：批次与余额均保持原值
        Assert.Equal(10m, (await LoadBatchAsync(h, itemId, "A")).Qty);
        Assert.Equal(20m, (await LoadBalanceAsync(h, itemId)).QtyOnhand);
        Assert.Equal(2, await CountTxnsAsync(h));
    }

    // ============ 6：冻结量（可用量 = 现存 − 冻结）============

    [Fact]
    public async Task LockedQuantityReducesAvailableQuantity()
    {
        Harness h = Build();
        long itemId = await SeedItemAsync(h);
        _ = await PostAsync(h, Request(itemId, StockDirection.In, 5m, 10m));

        await h.Service.AdjustLockedQtyAsync(itemId, WarehouseId, 5m, "盘点冻结", Ct);

        StockPostResult blocked = await PostAsync(
            h, Request(itemId, StockDirection.Out, 6m, 0m, docNo: "SRC-OUT1"));
        Assert.False(blocked.Success);
        Assert.Equal(StockLedgerService.ErrorInsufficient, blocked.ErrorCode);
        Assert.Equal(6m, blocked.ShortageQty);

        // 释放冻结后可正常出库
        await h.Service.AdjustLockedQtyAsync(itemId, WarehouseId, -5m, "解除冻结", Ct);

        StockPostResult released = await PostAsync(
            h, Request(itemId, StockDirection.Out, 5m, 0m, docNo: "SRC-OUT2"));
        Assert.True(released.Success);
        Assert.Equal(0m, released.BalanceQtyAfter);
        Assert.Equal(0m, released.BalanceAmountAfter);

        StockBalance balance = await LoadBalanceAsync(h, itemId);
        Assert.Equal(0m, balance.QtyLocked);
        Assert.Equal(0m, balance.QtyOnhand);
        // 数量归零时金额归零，均价保留原位
        Assert.Equal(0m, balance.Amount);
        Assert.Equal(10m, balance.AvgCost);
    }

    [Fact]
    public async Task LockedQuantityCannotBecomeNegative()
    {
        Harness h = Build();
        long itemId = await SeedItemAsync(h);
        _ = await PostAsync(h, Request(itemId, StockDirection.In, 5m, 10m));

        Qlx.Abstractions.Exceptions.ConflictException ex =
            await Assert.ThrowsAsync<Qlx.Abstractions.Exceptions.ConflictException>(
                () => h.Service.AdjustLockedQtyAsync(itemId, WarehouseId, -1m, "过度释放", Ct));

        Assert.Equal("STK_LOCKED_NEGATIVE", ex.ErrorCode);
        Assert.Equal(0m, (await LoadBalanceAsync(h, itemId)).QtyLocked);
    }

    // ============ 7：流水只增（代码层守护）============

    [Fact]
    public async Task StockTxnCannotBeUpdatedOrDeleted()
    {
        Harness h = Build();
        long itemId = await SeedItemAsync(h);
        _ = await PostAsync(h, Request(itemId, StockDirection.In, 1m, 10m));

        // 流水由提交路径写入后仍处于跟踪态，先清空跟踪器再以独立实例验证守护
        h.Repo.ResetChangeTracker();
        StockTxn txn = await h.Db.StockTxns.AsNoTracking().SingleAsync(Ct);

        h.Db.Entry(txn).State = EntityState.Modified;
        InvalidOperationException update = await Assert.ThrowsAsync<InvalidOperationException>(
            () => h.Db.SaveChangesAsync(Ct));
        Assert.Equal("库存流水只允许新增，禁止更新或删除", update.Message);

        h.Db.Entry(txn).State = EntityState.Deleted;
        _ = await Assert.ThrowsAsync<InvalidOperationException>(() => h.Db.SaveChangesAsync(Ct));
        Assert.Equal(1, await CountTxnsAsync(h));
    }

    // ============ 8：Version 每次变动递增 ============

    [Fact]
    public async Task VersionIncrementsOnEveryBalanceChange()
    {
        Harness h = Build();
        long itemId = await SeedItemAsync(h);

        _ = await PostAsync(h, Request(itemId, StockDirection.In, 10m, 10m));
        Assert.Equal(1L, (await LoadBalanceAsync(h, itemId)).Version);

        _ = await PostAsync(h, Request(itemId, StockDirection.In, 10m, 20m, docNo: "SRC-002"));
        Assert.Equal(2L, (await LoadBalanceAsync(h, itemId)).Version);

        _ = await PostAsync(h, Request(itemId, StockDirection.Out, 5m, 0m, docNo: "SRC-003"));
        Assert.Equal(3L, (await LoadBalanceAsync(h, itemId)).Version);

        await h.Service.AdjustLockedQtyAsync(itemId, WarehouseId, 2m, "冻结", Ct);
        Assert.Equal(4L, (await LoadBalanceAsync(h, itemId)).Version);
    }

    // ============ 9：精度——金额 2 位 AwayFromZero ============

    [Fact]
    public async Task AmountRoundingUsesAwayFromZero()
    {
        Harness h = Build();
        long itemId = await SeedItemAsync(h);

        // 4 件 @ 10.005 → 金额 40.02、均价 10.005；出库 3 件按 30.015 → 30.02（远离零）
        _ = await PostAsync(h, Request(itemId, StockDirection.In, 4m, 10.005m));

        StockBalance afterIn = await LoadBalanceAsync(h, itemId);
        Assert.Equal(40.02m, afterIn.Amount);
        Assert.Equal(10.005m, afterIn.AvgCost);

        StockPostResult outResult = await PostAsync(
            h, Request(itemId, StockDirection.Out, 3m, 0m, docNo: "SRC-OUT"));

        Assert.True(outResult.Success);
        Assert.Equal(10.00m, outResult.BalanceAmountAfter);

        StockTxn txn = await h.Db.StockTxns.AsNoTracking()
            .SingleAsync(t => t.Id == outResult.TxnId!.Value, Ct);
        Assert.Equal(30.02m, txn.Amount);
        Assert.Equal(10.00m, txn.BalanceAmountAfter);
        Assert.Equal(1m, txn.BalanceQtyAfter);
    }

    // ============ 10：批量提交（单事务，任一行失败整批回滚）============

    [Fact]
    public async Task PostBatchRollsBackWholeBatchWhenAnyRowFails()
    {
        Harness h = Build();
        long itemId = await SeedItemAsync(h);
        _ = await PostAsync(h, Request(itemId, StockDirection.In, 10m, 10m));

        IReadOnlyList<PostStockTxnRequest> requests =
        [
            Request(itemId, StockDirection.Out, 3m, 0m, docNo: "BAT-1", lineNo: 1),
            Request(itemId, StockDirection.Out, 50m, 0m, docNo: "BAT-1", lineNo: 2),
        ];

        IReadOnlyList<StockPostResult> results = await h.Service.PostBatchAsync(requests, Ct);

        Assert.Equal(2, results.Count);
        Assert.False(results[0].Success);
        Assert.Null(results[0].ErrorCode);
        Assert.False(results[1].Success);
        Assert.Equal(StockLedgerService.ErrorInsufficient, results[1].ErrorCode);
        // 缺口按事务内当前状态计算（行 1 已扣 3，可用 7，缺口 50 − 7 = 43）；整批回滚后需按落库状态重算
        Assert.Equal(43m, results[1].ShortageQty);

        // 整批零落库
        Assert.Equal(1, await CountTxnsAsync(h));
        StockBalance balance = await LoadBalanceAsync(h, itemId);
        Assert.Equal(10m, balance.QtyOnhand);
        Assert.Equal(100m, balance.Amount);
        Assert.Equal(1L, balance.Version);
    }

    // ============ 11：只读查询（成本字段裁剪 + 时间过滤可下推）============

    [Fact]
    public async Task InventoryQueryTrimsCostFieldsWithoutPermission()
    {
        Harness h = Build();
        long itemId = await SeedItemAsync(h);
        _ = await PostAsync(h, Request(itemId, StockDirection.In, 10m, 10m));

        var service = new InventoryQueryService(h.Repo, new HttpContextAccessor());

        Qlx.Modules.Stk.Dtos.StockTxnView anon = Assert.Single(
            await service.ListTxnsAsync(itemId, WarehouseId, null, null, null, 10, Ct));
        Assert.Null(anon.UnitCost);
        Assert.Null(anon.Amount);
        Assert.Null(anon.BalanceAmountAfter);
        Assert.Equal(10m, anon.Qty);

        Qlx.Modules.Stk.Dtos.StockBalanceView anonBalance = Assert.Single(
            await service.ListBalancesAsync(itemId, null, Ct));
        Assert.Null(anonBalance.AvgCost);
        Assert.Null(anonBalance.Amount);
        Assert.Equal(10m, anonBalance.QtyOnhand);
    }

    [Fact]
    public async Task TxnsCanBeFilteredByOccurredAtRange()
    {
        Harness h = Build();
        long itemId = await SeedItemAsync(h);
        _ = await PostAsync(h, Request(itemId, StockDirection.In, 10m, 10m));

        var service = new InventoryQueryService(h.Repo, new HttpContextAccessor());

        IReadOnlyList<Qlx.Modules.Stk.Dtos.StockTxnView> inRange = await service.ListTxnsAsync(
            itemId, WarehouseId, (int)StockSourceType.PurchaseIn,
            OccurredAt.AddHours(-1), OccurredAt.AddHours(1), 10, Ct);
        Assert.Single(inRange);

        IReadOnlyList<Qlx.Modules.Stk.Dtos.StockTxnView> outOfRange = await service.ListTxnsAsync(
            itemId, WarehouseId, null, OccurredAt.AddDays(1), null, 10, Ct);
        Assert.Empty(outOfRange);
    }
}

/// <summary>单据号生成桩：<c>{prefix}-{orgCode}-202610-{seq:000000}</c>，流水单调递增。</summary>
internal sealed class StkStubDocumentNumberGenerator : IDocumentNumberGenerator
{
    private int _sequence;

    public Task<string> NextAsync(string prefix, string orgCode, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prefix);
        ArgumentException.ThrowIfNullOrWhiteSpace(orgCode);
        int sequence = Interlocked.Increment(ref _sequence);
        return Task.FromResult($"{prefix}-{orgCode}-202610-{sequence:000000}");
    }
}
