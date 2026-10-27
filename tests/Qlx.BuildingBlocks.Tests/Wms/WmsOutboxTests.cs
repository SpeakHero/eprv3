/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-26 21:30:00 +08:00 中国标准时间 星期一
 * 描述：Wms 库存命令 Outbox 测试——同事务入队/回滚无残留、幂等键去重、分发成功置已分发、
 *       失败退避递增、5 次终态、幂等命中视为成功、终态人工重推
 */

using System.Data;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

using Qlx.Abstractions.Exceptions;
using Qlx.Abstractions.Jobs;
using Qlx.Abstractions.Queries;
using Qlx.Abstractions.Stock;
using Qlx.BuildingBlocks.Tests.Files;
using Qlx.BuildingBlocks.Tests.Jobs;
using Qlx.Modules.Wms;
using Qlx.Modules.Wms.Domain.Outbox;
using Qlx.Modules.Wms.Jobs;
using Qlx.Modules.Wms.Repositories;
using Qlx.Modules.Wms.Services;

namespace Qlx.BuildingBlocks.Tests.Wms;

/// <summary>库存命令 Outbox 与分发任务测试。</summary>
public sealed class WmsOutboxTests
{
    private const long TenantId = 77L;
    private const string Operator = "user-77";

    /// <summary>测试夹具：SQLite 库 + Outbox 入队 + 分发任务 + Stk 桩。</summary>
    private sealed class Harness
    {
        public required WmsTenantDbContext Db { get; init; }

        public required WmsTenantRepository Repo { get; init; }

        public required StockCommandQueue Queue { get; init; }

        public required WmsStockCommandDispatchJobHandler Job { get; init; }

        public required StubStockTxnCommandService Stock { get; init; }
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
        var stock = new StubStockTxnCommandService();

        return new Harness
        {
            Db = db,
            Repo = repo,
            Stock = stock,
            Queue = new StockCommandQueue(db, tenantCtx),
            Job = new WmsStockCommandDispatchJobHandler(
                db, stock, NullLogger<WmsStockCommandDispatchJobHandler>.Instance),
        };
    }

    private static PostStockTxnRequest Request(string key, int lineNo = 1, decimal qty = 5m)
        => new(
            StockSourceType.PurchaseIn,
            "WL-RK-T-0001",
            lineNo,
            7001L,
            8001L,
            null,
            StockDirection.In,
            qty,
            10m,
            Operator,
            key,
            DateTimeOffset.UtcNow);

    private static PostStockTxnRequest[] Single(string key) => [Request(key)];

    // ============ 同事务入队 / 回滚无残留（A-17） ============

    [Fact]
    public async Task EnqueueInsideTransactionCommitsWithBusinessState()
    {
        Harness h = Build();

        int added = await h.Repo.ExecuteInTransactionAsync(
            async token =>
            {
                int count = await h.Queue.EnqueueAsync([Request("wms:rk:WL-RK-T-0001:1")], token);
                _ = await h.Repo.SaveChangesAsync(token);
                return count;
            },
            TestContext.Current.CancellationToken);

        Assert.Equal(1, added);
        WmsStockCommandOutbox row = Assert.Single(await h.Db.StockCommandOutbox
            .AsNoTracking().ToListAsync(TestContext.Current.CancellationToken));
        Assert.Equal("wms:rk:WL-RK-T-0001:1", row.IdempotencyKey);
        Assert.Equal(WmsStockCommandState.Pending, row.State);
        Assert.Null(row.NextRetryAt);
    }

    [Fact]
    public async Task RollbackLeavesNoOutboxResidue()
    {
        Harness h = Build();

        _ = await Assert.ThrowsAsync<InvalidOperationException>(() => h.Repo.ExecuteInTransactionAsync<int>(
            async token =>
            {
                _ = await h.Queue.EnqueueAsync([Request("wms:rk:WL-RK-T-0002:1")], token);
                _ = await h.Repo.SaveChangesAsync(token);
                throw new InvalidOperationException("作业状态写入失败");
            },
            TestContext.Current.CancellationToken));

        h.Db.ChangeTracker.Clear();
        Assert.Equal(0, await h.Db.StockCommandOutbox.CountAsync(TestContext.Current.CancellationToken));
    }

    // ============ 幂等键租户内去重 ============

    [Fact]
    public async Task SameIdempotencyKeySecondEnqueueAddsZero()
    {
        Harness h = Build();
        PostStockTxnRequest[] requests = Single("wms:rk:WL-RK-T-0003:1");

        Assert.Equal(1, await h.Queue.EnqueueAsync(requests, TestContext.Current.CancellationToken));
        _ = await h.Db.SaveChangesAsync(TestContext.Current.CancellationToken);

        Assert.Equal(0, await h.Queue.EnqueueAsync(requests, TestContext.Current.CancellationToken));
        Assert.Equal(1, await h.Db.StockCommandOutbox.CountAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task DuplicateKeysInsideOneBatchAreRejected()
    {
        Harness h = Build();

        ValidationException ex = await Assert.ThrowsAsync<ValidationException>(() => h.Queue.EnqueueAsync(
            [Request("wms:rk:WL-RK-T-0004:1"), Request("wms:rk:WL-RK-T-0004:1", lineNo: 2)],
            TestContext.Current.CancellationToken));

        Assert.Equal("idempotencyKey", Assert.Single(ex.FieldErrors).FieldName);
        Assert.Equal(0, await h.Db.StockCommandOutbox.CountAsync(TestContext.Current.CancellationToken));
    }

    // ============ 分发成功 / 幂等命中 ============

    [Fact]
    public async Task DispatchSuccessMarksDispatched()
    {
        Harness h = Build();
        await h.Queue.EnqueueAsync(Single("wms:rk:WL-RK-T-0005:1"), TestContext.Current.CancellationToken);
        _ = await h.Db.SaveChangesAsync(TestContext.Current.CancellationToken);

        DateTimeOffset now = DateTimeOffset.UtcNow;
        CompletedResult result = await h.Job.DispatchOnceAsync(now, TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        WmsStockCommandOutbox row = await h.Db.StockCommandOutbox
            .AsNoTracking().SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(WmsStockCommandState.Dispatched, row.State);
        Assert.Equal(now, row.DispatchedAt);
        Assert.Null(row.NextRetryAt);
        Assert.Null(row.LastError);
        Assert.Single(h.Stock.Calls);
    }

    [Fact]
    public async Task DuplicateIdempotencyHitCountsAsDispatched()
    {
        Harness h = Build();
        h.Stock.Result = new StockPostResult(true, 99L, "SP-LS-000099", 10m, 100m, "STK_DUPLICATE", 0m);

        await h.Queue.EnqueueAsync(Single("wms:rk:WL-RK-T-0006:1"), TestContext.Current.CancellationToken);
        _ = await h.Db.SaveChangesAsync(TestContext.Current.CancellationToken);

        _ = await h.Job.DispatchOnceAsync(DateTimeOffset.UtcNow, TestContext.Current.CancellationToken);

        WmsStockCommandOutbox row = await h.Db.StockCommandOutbox
            .AsNoTracking().SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(WmsStockCommandState.Dispatched, row.State);
    }

    // ============ 失败退避 / 终态 ============

    [Fact]
    public async Task DispatchFailureSchedulesBackoffAndReachesTerminalAfterFiveAttempts()
    {
        Harness h = Build();
        h.Stock.Result = new StockPostResult(false, null, null, 0m, 0m, "STK_INSUFFICIENT", 2m);

        await h.Queue.EnqueueAsync(Single("wms:rk:WL-RK-T-0007:1"), TestContext.Current.CancellationToken);
        _ = await h.Db.SaveChangesAsync(TestContext.Current.CancellationToken);

        DateTimeOffset t0 = DateTimeOffset.UtcNow;

        // 第 1 次失败：退避 1 分钟，保留错误码
        _ = await h.Job.DispatchOnceAsync(t0, TestContext.Current.CancellationToken);
        WmsStockCommandOutbox row = await h.Db.StockCommandOutbox
            .AsNoTracking().SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(WmsStockCommandState.Failed, row.State);
        Assert.Equal(1, row.RetryCount);
        Assert.Equal("STK_INSUFFICIENT", row.LastError);
        Assert.Equal(t0.AddMinutes(1), row.NextRetryAt);
        Assert.False(row.IsTerminalFailure);

        // 未到重试时间：本轮不扫描
        _ = await h.Job.DispatchOnceAsync(
            t0.AddSeconds(30), TestContext.Current.CancellationToken);
        _ = Assert.Single(h.Stock.Calls);

        // 第 2~4 次失败：退避 2/4/8 分钟递增
        _ = await h.Job.DispatchOnceAsync(t0.AddMinutes(1), TestContext.Current.CancellationToken);
        row = await h.Db.StockCommandOutbox.AsNoTracking().SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(2, row.RetryCount);
        Assert.Equal(t0.AddMinutes(3), row.NextRetryAt);

        _ = await h.Job.DispatchOnceAsync(t0.AddMinutes(3), TestContext.Current.CancellationToken);
        _ = await h.Job.DispatchOnceAsync(t0.AddMinutes(7), TestContext.Current.CancellationToken);
        row = await h.Db.StockCommandOutbox.AsNoTracking().SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(4, row.RetryCount);
        Assert.Equal(t0.AddMinutes(15), row.NextRetryAt);

        // 第 5 次失败：终态（无下次执行时间）
        _ = await h.Job.DispatchOnceAsync(t0.AddMinutes(15), TestContext.Current.CancellationToken);
        row = await h.Db.StockCommandOutbox.AsNoTracking().SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(WmsStockCommandState.Failed, row.State);
        Assert.Equal(5, row.RetryCount);
        Assert.Null(row.NextRetryAt);
        Assert.True(row.IsTerminalFailure);

        // 终态不再被自动扫描
        _ = await h.Job.DispatchOnceAsync(t0.AddHours(1), TestContext.Current.CancellationToken);
        Assert.Equal(5, h.Stock.Calls.Count);
    }

    [Fact]
    public async Task TerminalFailureCanBeManuallyRetried()
    {
        Harness h = Build();
        h.Stock.Result = new StockPostResult(false, null, null, 0m, 0m, "STK_INSUFFICIENT", 0m);

        await h.Queue.EnqueueAsync(Single("wms:rk:WL-RK-T-0008:1"), TestContext.Current.CancellationToken);
        _ = await h.Db.SaveChangesAsync(TestContext.Current.CancellationToken);

        DateTimeOffset now = DateTimeOffset.UtcNow;
        for (var attempt = 0; attempt < WmsStockCommandOutbox.MaxRetryCount; attempt++)
        {
            _ = await h.Job.DispatchOnceAsync(now.AddMinutes(attempt * 20), TestContext.Current.CancellationToken);
        }

        WmsStockCommandOutbox row = await h.Db.StockCommandOutbox
            .SingleAsync(TestContext.Current.CancellationToken);
        Assert.True(row.IsTerminalFailure);

        row.ResetForRetry(now);
        Assert.Equal(WmsStockCommandState.Pending, row.State);
        Assert.Equal(0, row.RetryCount);
        Assert.Null(row.LastError);
        Assert.Equal(now, row.NextRetryAt);
        _ = await h.Db.SaveChangesAsync(TestContext.Current.CancellationToken);

        // 重推后恢复分发；成功即置已分发
        h.Stock.Result = new StockPostResult(true, 1L, "SP-LS-000001", 5m, 50m, null, 0m);
        _ = await h.Job.DispatchOnceAsync(now, TestContext.Current.CancellationToken);

        row = await h.Db.StockCommandOutbox.AsNoTracking().SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(WmsStockCommandState.Dispatched, row.State);
    }

    [Fact]
    public async Task NonTerminalRowCannotBeManuallyRetried()
    {
        Harness h = Build();
        await h.Queue.EnqueueAsync(Single("wms:rk:WL-RK-T-0009:1"), TestContext.Current.CancellationToken);
        _ = await h.Db.SaveChangesAsync(TestContext.Current.CancellationToken);

        WmsStockCommandOutbox row = await h.Db.StockCommandOutbox
            .SingleAsync(TestContext.Current.CancellationToken);

        _ = Assert.Throws<InvalidOperationException>(() => row.ResetForRetry(DateTimeOffset.UtcNow));
    }

    // ============ 租户隔离 ============

    [Fact]
    public async Task OutboxRowsOfOtherTenantAreInvisible()
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();

        var db = new WmsTenantDbContext(
            new JobStubTenantDatabaseResolver(),
            TenantId,
            new DbContextOptionsBuilder<WmsTenantDbContext>().UseSqlite(connection).Options);
        _ = db.Database.EnsureCreated();

        var queue = new StockCommandQueue(db, new TestTenantContext(TenantId));
        await queue.EnqueueAsync(Single("wms:rk:WL-RK-T-0010:1"), TestContext.Current.CancellationToken);
        _ = await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var otherDb = new WmsTenantDbContext(
            new JobStubTenantDatabaseResolver(),
            78L,
            new DbContextOptionsBuilder<WmsTenantDbContext>().UseSqlite(connection).Options);

        Assert.Equal(0, await otherDb.StockCommandOutbox.CountAsync(TestContext.Current.CancellationToken));
    }
}

/// <summary>库存写入命令桩：按 <see cref="Result"/> 返回，记录调用载荷。</summary>
internal sealed class StubStockTxnCommandService : IStockTxnCommandService
{
    /// <summary>下一次 <see cref="PostAsync"/> 的固定返回。</summary>
    public StockPostResult Result { get; set; } =
        new(true, 1L, "SP-LS-000001", 5m, 50m, null, 0m);

    /// <summary>已收到的调拨载荷（断言分发次数与幂等键）。</summary>
    public List<PostStockTxnRequest> Calls { get; } = [];

    public Task<StockPostResult> PostAsync(PostStockTxnRequest request, CancellationToken cancellationToken)
    {
        Calls.Add(request);
        return Task.FromResult(Result);
    }

    public Task<IReadOnlyList<StockPostResult>> PostBatchAsync(
        IReadOnlyList<PostStockTxnRequest> requests, CancellationToken cancellationToken)
        => throw new NotSupportedException();

    public Task AdjustLockedQtyAsync(
        long itemId, long warehouseId, decimal deltaQty, string reason, CancellationToken cancellationToken)
        => throw new NotSupportedException();

    public Task<StockBalanceRow?> FindBalanceAsync(
        long itemId, long warehouseId, CancellationToken cancellationToken)
        => throw new NotSupportedException();
}
