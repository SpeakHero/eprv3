/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-26
 * 描述：Wms 收口测试——四类审批完成事件回放各落地一次、同实例重复事件幂等、驳回/撤回路径、
 *       未登记业务类型与未知实例忽略、库存命令失败终态人工重推
 */

using System.Globalization;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

using Qlx.Abstractions.Approvals;
using Qlx.Abstractions.Exceptions;
using Qlx.Abstractions.Queries;
using Qlx.Abstractions.Stock;
using Qlx.BuildingBlocks.Caching;
using Qlx.BuildingBlocks.Tests.Bas;
using Qlx.BuildingBlocks.Tests.Files;
using Qlx.BuildingBlocks.Tests.Jobs;
using Qlx.Modules.Wms;
using Qlx.Modules.Wms.Domain.Adjust;
using Qlx.Modules.Wms.Domain.Numbering;
using Qlx.Modules.Wms.Domain.Outbound;
using Qlx.Modules.Wms.Domain.Outbox;
using Qlx.Modules.Wms.Domain.Transfer;
using Qlx.Modules.Wms.Domain.Warehouse;
using Qlx.Modules.Wms.Dtos;
using Qlx.Modules.Wms.Repositories;
using Qlx.Modules.Wms.Services;

namespace Qlx.BuildingBlocks.Tests.Wms;

/// <summary>Wms 审批应用器与库存命令重推测试。</summary>
public sealed class WmsApprovalApplierTests
{
    private const long TenantId = 66L;
    private const string Operator = "user-66";
    private const long OrgId = 9001L;

    private const long Item = 7601L;

    private sealed class Harness
    {
        public required WmsTenantDbContext Db { get; init; }

        public required WmsTenantRepository Repo { get; init; }

        public required WarehouseService Warehouse { get; init; }

        public required StockOutService StockOut { get; init; }

        public required TransferService Transfer { get; init; }

        public required InventoryAdjustService Adjust { get; init; }

        public required StockCommandQueue Queue { get; init; }

        public required StockCommandService Commands { get; init; }

        public required WmsApprovalApplier Applier { get; init; }

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
        var parameters = new StubOutboundParameterProvider();
        var items = new StubItemQueryService
        {
            Rows = { [Item] = new StockItemQueryRow(Item, false, false, 5m, 1) },
        };
        var queue = new StockCommandQueue(db, tenantCtx);

        var warehouse = new WarehouseService(
            tenantCtx,
            repo,
            new StubDocumentNumberGenerator(),
            new StubOrgQueryService(),
            approval,
            stock,
            new MemoryCacheService(Options.Create(new CacheOptions())),
            new InMemoryAuditService(),
            new BasStubSnowflakeIdGenerator(),
            user);
        var stockOut = new StockOutService(
            tenantCtx,
            repo,
            new StubDocumentNumberGenerator(),
            new StubOrgQueryService(),
            items,
            stock,
            txn,
            queue,
            approval,
            parameters,
            events,
            user);
        var transfer = new TransferService(
            tenantCtx,
            repo,
            new StubDocumentNumberGenerator(),
            new StubOrgQueryService(),
            items,
            txn,
            queue,
            approval,
            parameters,
            events,
            user);
        var adjust = new InventoryAdjustService(
            tenantCtx,
            repo,
            new StubDocumentNumberGenerator(),
            new StubOrgQueryService(),
            items,
            txn,
            queue,
            approval,
            user);

        return new Harness
        {
            Db = db,
            Repo = repo,
            Warehouse = warehouse,
            StockOut = stockOut,
            Transfer = transfer,
            Adjust = adjust,
            Queue = queue,
            Commands = new StockCommandService(repo),
            Txn = txn,
            Approval = approval,
            Events = events,
            Applier = new WmsApprovalApplier(repo, stockOut, transfer, adjust, warehouse),
        };
    }

    private static Task<WarehouseRow> CreateWarehouseAsync(Harness h, string name)
        => h.Warehouse.CreateAsync(
            new CreateWarehousePayload(name, 1, OrgId, null, null, null),
            TestContext.Current.CancellationToken);

    private static ApprovalCompletedEvent Completed(
        string instanceId, string bizType, ApprovalStatus status)
        => new(
            instanceId,
            "flow.test",
            bizType,
            "0",
            status,
            Operator,
            DateTimeOffset.UtcNow)
        {
            TenantCode = TenantId.ToString(CultureInfo.InvariantCulture),
        };

    private static async Task<List<WmsApprovalRequest>> LoadApprovalRequestsAsync(Harness h)
        => await h.Db.ApprovalRequests.AsNoTracking()
            .OrderBy(request => request.Id)
            .ToListAsync(TestContext.Current.CancellationToken);

    private static async Task<int> CountOutboxAsync(Harness h)
        => await h.Db.StockCommandOutbox
            .CountAsync(TestContext.Current.CancellationToken);

    // ============ 大额出库 ============

    [Fact]
    public async Task ApprovedOutboundReplayCompletesTaskExactlyOnce()
    {
        Harness h = Build();
        WarehouseRow warehouse = await CreateWarehouseAsync(h, "出库仓");
        h.Txn.AvgCosts[(Item, warehouse.Id)] = 6000m;

        StockOutTaskDetailRow task = await h.StockOut.CreateAsync(
            new CreateStockOutTaskPayload(1, "SO-20261026-9001", warehouse.Id, null,
                [new CreateStockOutLinePayload(Item, 10m, null, null)]),
            TestContext.Current.CancellationToken);
        long lineId = Assert.Single(task.Lines).Id;
        _ = await h.StockOut.PickAsync(
            task.Task.Id,
            new PickStockOutTaskPayload([new PickStockOutLinePayload(lineId, 10m, null, null)]),
            TestContext.Current.CancellationToken);
        _ = await h.StockOut.CheckAsync(
            task.Task.Id,
            new CheckStockOutTaskPayload("handover-01", [new CheckStockOutLinePayload(lineId, 10m)]),
            TestContext.Current.CancellationToken);

        StockOutCompleteResultRow submitted = await h.StockOut.CompleteAsync(
            task.Task.Id, TestContext.Current.CancellationToken);
        Assert.True(submitted.ApprovalRequired);
        WmsApprovalRequest pending = Assert.Single(await LoadApprovalRequestsAsync(h));
        Assert.Equal(StockOutService.OutboundLargeBizType, pending.BizType);

        await h.Applier.ApplyAsync(
            Completed(pending.ApprovalInstanceId!, pending.BizType, ApprovalStatus.Approved),
            TestContext.Current.CancellationToken);

        StockOutTask stored = await h.Db.StockOutTasks.AsNoTracking()
            .SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(StockOutTaskStatus.Completed, stored.Status);
        Assert.Equal(1, await CountOutboxAsync(h));
        _ = Assert.Single(h.Events.DurableEvents);

        WmsApprovalRequest applied = Assert.Single(await LoadApprovalRequestsAsync(h));
        Assert.Equal(WmsApprovalRequestStatus.Approved, applied.Status);
        Assert.NotNull(applied.AppliedAt);

        // 同一实例重复事件：请求已非待审批，不再二次应用
        await h.Applier.ApplyAsync(
            Completed(applied.ApprovalInstanceId!, applied.BizType, ApprovalStatus.Approved),
            TestContext.Current.CancellationToken);

        Assert.Equal(1, await CountOutboxAsync(h));
        _ = Assert.Single(h.Events.DurableEvents);
        Assert.Equal(StockOutTaskStatus.Completed, (await h.Db.StockOutTasks.AsNoTracking()
            .SingleAsync(TestContext.Current.CancellationToken)).Status);
    }

    // ============ 调拨 ============

    [Fact]
    public async Task ApprovedTransferReplayActivatesExactlyOnce()
    {
        Harness h = Build();
        WarehouseRow from = await CreateWarehouseAsync(h, "调出仓");
        WarehouseRow to = await CreateWarehouseAsync(h, "调入仓");

        StockTransferDetailRow created = await h.Transfer.CreateAsync(
            new CreateStockTransferPayload(from.Id, to.Id, null, null,
                [new CreateStockTransferLinePayload(Item, 10m, null)]),
            TestContext.Current.CancellationToken);
        Assert.Equal(1, created.Transfer.Status);

        WmsApprovalRequest pending = Assert.Single(await LoadApprovalRequestsAsync(h));
        await h.Applier.ApplyAsync(
            Completed(pending.ApprovalInstanceId!, TransferService.TransferBizType, ApprovalStatus.Approved),
            TestContext.Current.CancellationToken);

        Assert.Equal(TransferStatus.Effective, (await h.Db.StockTransfers.AsNoTracking()
            .SingleAsync(TestContext.Current.CancellationToken)).Status);
        Assert.Equal(WmsApprovalRequestStatus.Approved,
            Assert.Single(await LoadApprovalRequestsAsync(h)).Status);

        await h.Applier.ApplyAsync(
            Completed(pending.ApprovalInstanceId!, TransferService.TransferBizType, ApprovalStatus.Approved),
            TestContext.Current.CancellationToken);

        Assert.Equal(TransferStatus.Effective, (await h.Db.StockTransfers.AsNoTracking()
            .SingleAsync(TestContext.Current.CancellationToken)).Status);
    }

    [Fact]
    public async Task RejectedTransferIsVoidedAndRequestMarkedRejected()
    {
        Harness h = Build();
        WarehouseRow from = await CreateWarehouseAsync(h, "调出仓");
        WarehouseRow to = await CreateWarehouseAsync(h, "调入仓");
        _ = await h.Transfer.CreateAsync(
            new CreateStockTransferPayload(from.Id, to.Id, null, null,
                [new CreateStockTransferLinePayload(Item, 10m, null)]),
            TestContext.Current.CancellationToken);

        WmsApprovalRequest pending = Assert.Single(await LoadApprovalRequestsAsync(h));
        await h.Applier.ApplyAsync(
            Completed(pending.ApprovalInstanceId!, TransferService.TransferBizType, ApprovalStatus.Rejected),
            TestContext.Current.CancellationToken);

        Assert.Equal(TransferStatus.Voided, (await h.Db.StockTransfers.AsNoTracking()
            .SingleAsync(TestContext.Current.CancellationToken)).Status);

        WmsApprovalRequest request = Assert.Single(await LoadApprovalRequestsAsync(h));
        Assert.Equal(WmsApprovalRequestStatus.Rejected, request.Status);
        Assert.Null(request.AppliedAt);
    }

    [Fact]
    public async Task WithdrawnTransferIsVoidedAndRequestMarkedWithdrawn()
    {
        Harness h = Build();
        WarehouseRow from = await CreateWarehouseAsync(h, "调出仓");
        WarehouseRow to = await CreateWarehouseAsync(h, "调入仓");
        _ = await h.Transfer.CreateAsync(
            new CreateStockTransferPayload(from.Id, to.Id, null, null,
                [new CreateStockTransferLinePayload(Item, 10m, null)]),
            TestContext.Current.CancellationToken);

        WmsApprovalRequest pending = Assert.Single(await LoadApprovalRequestsAsync(h));
        await h.Applier.ApplyAsync(
            Completed(pending.ApprovalInstanceId!, TransferService.TransferBizType, ApprovalStatus.Withdrawn),
            TestContext.Current.CancellationToken);

        Assert.Equal(TransferStatus.Voided, (await h.Db.StockTransfers.AsNoTracking()
            .SingleAsync(TestContext.Current.CancellationToken)).Status);
        Assert.Equal(WmsApprovalRequestStatus.Withdrawn,
            Assert.Single(await LoadApprovalRequestsAsync(h)).Status);
    }

    // ============ 盘盈盘亏 ============

    [Fact]
    public async Task ApprovedInventoryAdjustReplayEnqueuesExactlyOnce()
    {
        Harness h = Build();
        WarehouseRow warehouse = await CreateWarehouseAsync(h, "调整仓");
        h.Txn.AvgCosts[(Item, warehouse.Id)] = 5m;

        InventoryAdjustDetailRow doc = await h.Adjust.CreateAsync(
            new CreateInventoryAdjustPayload(
                (int)AdjustType.Gain, warehouse.Id, "测试原因", null,
                [new CreateInventoryAdjustLinePayload(Item, null, 2m, null)]),
            TestContext.Current.CancellationToken);
        _ = await h.Adjust.SubmitAsync(doc.Adjust.Id, TestContext.Current.CancellationToken);

        WmsApprovalRequest pending = Assert.Single(await LoadApprovalRequestsAsync(h));
        await h.Applier.ApplyAsync(
            Completed(pending.ApprovalInstanceId!, InventoryAdjustService.ApproveBizType, ApprovalStatus.Approved),
            TestContext.Current.CancellationToken);

        InventoryAdjustDoc stored = await h.Db.InventoryAdjustDocs.AsNoTracking()
            .SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(AdjustDocStatus.Approved, stored.Status);
        Assert.Equal(1, await CountOutboxAsync(h));

        await h.Applier.ApplyAsync(
            Completed(pending.ApprovalInstanceId!, InventoryAdjustService.ApproveBizType, ApprovalStatus.Approved),
            TestContext.Current.CancellationToken);

        Assert.Equal(1, await CountOutboxAsync(h));
        Assert.Equal(WmsApprovalRequestStatus.Approved,
            Assert.Single(await LoadApprovalRequestsAsync(h)).Status);
    }

    // ============ 仓库停用 ============

    [Fact]
    public async Task ApprovedWarehouseDisableDisablesExactlyOnce()
    {
        Harness h = Build();
        WarehouseRow warehouse = await CreateWarehouseAsync(h, "待停用仓");

        WmsApprovalRequest request = new(
            TenantId, WmsApprovalApplier.WarehouseDisableBizType, warehouse.Id, null);
        request.BindInstance("inst-wh");
        await h.Repo.AddApprovalRequestAsync(request, TestContext.Current.CancellationToken);
        _ = await h.Repo.SaveChangesAsync(TestContext.Current.CancellationToken);

        await h.Applier.ApplyAsync(
            Completed("inst-wh", WmsApprovalApplier.WarehouseDisableBizType, ApprovalStatus.Approved),
            TestContext.Current.CancellationToken);

        Assert.Equal(WarehouseStatus.Disabled, (await h.Db.Warehouses.AsNoTracking()
            .SingleAsync(w => w.Id == warehouse.Id, TestContext.Current.CancellationToken)).Status);
        Assert.Equal(WmsApprovalRequestStatus.Approved,
            Assert.Single(await LoadApprovalRequestsAsync(h)).Status);
    }

    [Fact]
    public async Task RejectedWarehouseDisableKeepsWarehouseEnabled()
    {
        Harness h = Build();
        WarehouseRow warehouse = await CreateWarehouseAsync(h, "保留启用仓");

        WmsApprovalRequest request = new(
            TenantId, WmsApprovalApplier.WarehouseDisableBizType, warehouse.Id, null);
        request.BindInstance("inst-wh");
        await h.Repo.AddApprovalRequestAsync(request, TestContext.Current.CancellationToken);
        _ = await h.Repo.SaveChangesAsync(TestContext.Current.CancellationToken);

        await h.Applier.ApplyAsync(
            Completed("inst-wh", WmsApprovalApplier.WarehouseDisableBizType, ApprovalStatus.Rejected),
            TestContext.Current.CancellationToken);

        Assert.Equal(WarehouseStatus.Enabled, (await h.Db.Warehouses.AsNoTracking()
            .SingleAsync(w => w.Id == warehouse.Id, TestContext.Current.CancellationToken)).Status);
        Assert.Equal(WmsApprovalRequestStatus.Rejected,
            Assert.Single(await LoadApprovalRequestsAsync(h)).Status);
    }

    // ============ 忽略路径 ============

    [Fact]
    public async Task UnhandledBizTypeIsIgnored()
    {
        Harness h = Build();

        await h.Applier.ApplyAsync(
            Completed("inst-x", "bas.org.create", ApprovalStatus.Approved),
            TestContext.Current.CancellationToken);

        Assert.Empty(await LoadApprovalRequestsAsync(h));
    }

    [Fact]
    public async Task UnknownApprovalInstanceIsIgnored()
    {
        Harness h = Build();
        WarehouseRow from = await CreateWarehouseAsync(h, "调出仓");
        WarehouseRow to = await CreateWarehouseAsync(h, "调入仓");
        _ = await h.Transfer.CreateAsync(
            new CreateStockTransferPayload(from.Id, to.Id, null, null,
                [new CreateStockTransferLinePayload(Item, 10m, null)]),
            TestContext.Current.CancellationToken);

        await h.Applier.ApplyAsync(
            Completed("inst-missing", TransferService.TransferBizType, ApprovalStatus.Approved),
            TestContext.Current.CancellationToken);

        Assert.Equal(TransferStatus.PendingApproval, (await h.Db.StockTransfers.AsNoTracking()
            .SingleAsync(TestContext.Current.CancellationToken)).Status);
        Assert.Equal(WmsApprovalRequestStatus.Pending,
            Assert.Single(await LoadApprovalRequestsAsync(h)).Status);
    }

    // ============ 库存命令人工重推（R-8） ============

    [Fact]
    public async Task TerminalFailureCommandCanBeRetriedOnce()
    {
        Harness h = Build();
        _ = await h.Queue.EnqueueAsync(
            [Request("wms:rk:WL-RK-T-9001:1")], TestContext.Current.CancellationToken);
        _ = await h.Db.SaveChangesAsync(TestContext.Current.CancellationToken);

        WmsStockCommandOutbox row = await h.Db.StockCommandOutbox
            .SingleAsync(TestContext.Current.CancellationToken);
        DateTimeOffset failedAt = DateTimeOffset.UtcNow;
        for (var attempt = 0; attempt < WmsStockCommandOutbox.MaxRetryCount; attempt++)
        {
            row.RegisterFailure(failedAt, "STK_INSUFFICIENT");
        }

        _ = await h.Db.SaveChangesAsync(TestContext.Current.CancellationToken);
        Assert.True(row.IsTerminalFailure);

        StockCommandRow retried = await h.Commands.RetryAsync(
            row.Id, TestContext.Current.CancellationToken);

        Assert.Equal((int)WmsStockCommandState.Pending, retried.State);
        Assert.Equal(0, retried.RetryCount);
        Assert.Null(retried.LastError);
        Assert.NotNull(retried.NextRetryAt);

        // 已恢复为非终态：再次重推 409
        ConflictException conflict = await Assert.ThrowsAsync<ConflictException>(
            () => h.Commands.RetryAsync(row.Id, TestContext.Current.CancellationToken));
        Assert.Equal(StockCommandService.ErrorInvalidState, conflict.ErrorCode);
    }

    [Fact]
    public async Task NonTerminalCommandCannotBeRetried()
    {
        Harness h = Build();
        _ = await h.Queue.EnqueueAsync(
            [Request("wms:rk:WL-RK-T-9002:1")], TestContext.Current.CancellationToken);
        _ = await h.Db.SaveChangesAsync(TestContext.Current.CancellationToken);

        WmsStockCommandOutbox row = await h.Db.StockCommandOutbox
            .SingleAsync(TestContext.Current.CancellationToken);

        ConflictException conflict = await Assert.ThrowsAsync<ConflictException>(
            () => h.Commands.RetryAsync(row.Id, TestContext.Current.CancellationToken));
        Assert.Equal(StockCommandService.ErrorInvalidState, conflict.ErrorCode);
    }

    [Fact]
    public async Task RetryingUnknownCommandReturnsNotFound()
    {
        Harness h = Build();

        _ = await Assert.ThrowsAsync<NotFoundException>(
            () => h.Commands.RetryAsync(999_999L, TestContext.Current.CancellationToken));
    }

    private static PostStockTxnRequest Request(string idempotencyKey)
        => new(
            StockSourceType.PurchaseIn,
            "WL-RK-T-9001",
            1,
            Item,
            8001L,
            null,
            StockDirection.In,
            5m,
            10m,
            Operator,
            idempotencyKey,
            DateTimeOffset.UtcNow);
}
