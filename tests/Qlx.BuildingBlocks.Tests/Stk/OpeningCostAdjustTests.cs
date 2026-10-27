/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-26
 * 描述：期初建账 / 成本调整测试——期初提交审批 + 过账写 Opening 流水 + 每仓库唯一已过账 + 应用幂等 +
 *       驳回回落 + 非法流转；成本调整旧均价/差额后端取值 + 阈值选定义码 + 审批通过红蓝字成对写流水 +
 *       应用幂等 + 冻结库存阻断（spec §5.1 / §6 / §7 / §8）
 */

using System.Data;
using System.Globalization;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Qlx.Abstractions.Approvals;
using Qlx.Abstractions.Exceptions;
using Qlx.Abstractions.Stock;
using Qlx.BuildingBlocks.Tests.Bas;
using Qlx.BuildingBlocks.Tests.Files;
using Qlx.BuildingBlocks.Tests.Jobs;
using Qlx.BuildingBlocks.Tests.Portal.Auth;
using Qlx.Modules.Stk;
using Qlx.Modules.Stk.Domain.Inventory;
using Qlx.Modules.Stk.Domain.Item;
using Qlx.Modules.Stk.Domain.Numbering;
using Qlx.Modules.Stk.Dtos;
using Qlx.Modules.Stk.Repositories;
using Qlx.Modules.Stk.Services;

namespace Qlx.BuildingBlocks.Tests.Stk;

/// <summary>期初建账与成本调整测试。</summary>
public sealed class OpeningCostAdjustTests
{
    private const long TenantId = 42L;
    private const long WarehouseId = 7001L;
    private const string Operator = "user-9";

    private static readonly DateTimeOffset CompletedAt =
        new(2026, 10, 26, 10, 0, 0, TimeSpan.FromHours(8));

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>测试夹具：SQLite 库 + 台账/期初/成本调整服务与全部桩依赖。</summary>
    private sealed class Harness
    {
        public required StkTenantDbContext Db { get; init; }

        public required StkTenantRepository Repo { get; init; }

        public required StockLedgerService Ledger { get; init; }

        public required BasStubApprovalService Approval { get; init; }

        public required InMemoryAuditService Audit { get; init; }

        public required OpeningService Opening { get; init; }

        public required CostAdjustService CostAdjust { get; init; }

        public required BasRecordingEventBus Events { get; init; }
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
        var events = new BasRecordingEventBus();
        var ledger = new StockLedgerService(
            new TestTenantContext(TenantId),
            repo,
            new StkStubDocumentNumberGenerator(),
            new TestCurrentUserContext(Operator),
            events);
        var approval = new BasStubApprovalService();
        var audit = new InMemoryAuditService();

        return new Harness
        {
            Db = db,
            Repo = repo,
            Ledger = ledger,
            Approval = approval,
            Audit = audit,
            Events = events,
            Opening = new OpeningService(
                new TestTenantContext(TenantId),
                repo,
                new StkStubDocumentNumberGenerator(),
                ledger,
                approval,
                audit,
                new BasStubSnowflakeIdGenerator(),
                new TestCurrentUserContext(Operator),
                events),
            CostAdjust = new CostAdjustService(
                new TestTenantContext(TenantId),
                repo,
                new StkStubDocumentNumberGenerator(),
                ledger,
                approval,
                new StubSystemParameterProvider(),
                audit,
                new BasStubSnowflakeIdGenerator(),
                new TestCurrentUserContext(Operator),
                events),
        };
    }

    /// <summary>先插入商品（台账/期初/成本调整均校验商品存在）。</summary>
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

    private static Task<StockBalance> LoadBalanceAsync(Harness h, long itemId)
        => h.Db.StockBalances.AsNoTracking()
            .SingleAsync(b => b.ItemId == itemId && b.WarehouseId == WarehouseId, Ct);

    private static Task<int> CountTxnsAsync(Harness h, StockSourceType sourceType)
        => h.Db.StockTxns.AsNoTracking()
            .CountAsync(t => t.SourceType == (int)sourceType, Ct);

    private static ApprovalCompletedEvent CompletedEvent(
        StkApprovalRequestRow request, string flowCode, ApprovalStatus status)
        => new(
            request.ApprovalInstanceId!,
            flowCode,
            request.BizType,
            request.BizId.ToString(CultureInfo.InvariantCulture),
            status,
            Operator,
            CompletedAt)
        {
            TenantCode = TenantId.ToString(CultureInfo.InvariantCulture),
        };

    /// <summary>新建期初草稿并提交过账审批。</summary>
    private static async Task<(long BatchId, string BatchNo, StkApprovalRequestRow Request)>
        CreateAndSubmitOpeningAsync(Harness h, params OpeningItemPayload[] lines)
    {
        OpeningBatchDetailRow draft = await h.Opening.CreateDraftAsync(
            new CreateOpeningPayload(WarehouseId, "期初建账", lines), Ct);
        StkApprovalRequestRow request = await h.Opening.SubmitAsync(draft.Header.Id, Ct);
        return (draft.Header.Id, draft.Header.BatchNo, request);
    }

    // ============ 1：期初建草稿 → 提交 → 审批通过过账（台账 2 行 + 流水 2 条）============

    [Fact]
    public async Task OpeningDraftSubmittedThenApprovedPostsLedgerWithTwoTxns()
    {
        Harness h = Build();
        long itemA = await SeedItemAsync(h);
        long itemB = await SeedItemAsync(h);

        (long batchId, string batchNo, StkApprovalRequestRow request) = await CreateAndSubmitOpeningAsync(
            h,
            new OpeningItemPayload(itemA, null, 10m, 10m),
            new OpeningItemPayload(itemB, null, 5m, 20m));

        Assert.StartsWith("SP-QC", batchNo, StringComparison.Ordinal);
        Assert.Equal((int)StkApprovalRequestStatus.Pending, request.Status);
        Assert.Equal(OpeningService.OpeningFlowCode, Assert.Single(h.Approval.Started).ApprovalDefinitionCode);

        OpeningBatchDetailRow detail = await h.Opening.GetAsync(batchId, Ct);
        Assert.Equal((int)OpeningBatchStatus.Pending, detail.Header.Status);
        Assert.Equal(2, detail.Items.Count);
        Assert.Equal(100m, detail.Items[0].Amount);
        Assert.Equal(100m, detail.Items[1].Amount);

        await h.Opening.ApplyAsync(
            CompletedEvent(request, OpeningService.OpeningFlowCode, ApprovalStatus.Approved), Ct);

        OpeningBatchDetailRow posted = await h.Opening.GetAsync(batchId, Ct);
        Assert.Equal((int)OpeningBatchStatus.Posted, posted.Header.Status);
        Assert.NotNull(posted.Header.PostedAt);

        StockBalance balanceA = await LoadBalanceAsync(h, itemA);
        Assert.Equal(10m, balanceA.QtyOnhand);
        Assert.Equal(100m, balanceA.Amount);
        Assert.Equal(10m, balanceA.AvgCost);

        StockBalance balanceB = await LoadBalanceAsync(h, itemB);
        Assert.Equal(5m, balanceB.QtyOnhand);
        Assert.Equal(100m, balanceB.Amount);
        Assert.Equal(20m, balanceB.AvgCost);

        List<StockTxn> txns = await h.Db.StockTxns.AsNoTracking()
            .Where(t => t.SourceType == (int)StockSourceType.Opening)
            .OrderBy(t => t.SourceLineNo)
            .ToListAsync(Ct);
        Assert.Equal(2, txns.Count);
        Assert.All(txns, txn => Assert.Equal(batchNo, txn.SourceDocNo));
        Assert.All(txns, txn => Assert.Equal((int)StockDirection.In, txn.Direction));
        Assert.Equal(1, txns[0].SourceLineNo);
        Assert.Equal(2, txns[1].SourceLineNo);

        OpeningWarehouseSummaryRow summary = Assert.Single(await h.Opening.GetWarehouseSummaryAsync(Ct));
        Assert.Equal(WarehouseId, summary.WarehouseId);
        Assert.Equal(15m, summary.TotalQty);
        Assert.Equal(200m, summary.TotalAmount);

        StkApprovalRequest stored = await h.Db.ApprovalRequests.AsNoTracking().SingleAsync(Ct);
        Assert.Equal(StkApprovalRequestStatus.Approved, stored.Status);
        Assert.NotNull(stored.AppliedAt);
    }

    // ============ 2：期初幂等——同一事件重复应用不改台账/流水 ============

    [Fact]
    public async Task OpeningApplyIsIdempotentForSameApprovalInstance()
    {
        Harness h = Build();
        long itemId = await SeedItemAsync(h);

        (long batchId, _, StkApprovalRequestRow request) = await CreateAndSubmitOpeningAsync(
            h, new OpeningItemPayload(itemId, null, 10m, 10m));
        ApprovalCompletedEvent approved =
            CompletedEvent(request, OpeningService.OpeningFlowCode, ApprovalStatus.Approved);

        await h.Opening.ApplyAsync(approved, Ct);
        int auditCount = h.Audit.GetCapturedChanges().Count;

        await h.Opening.ApplyAsync(approved, Ct);

        Assert.Equal(1, await CountTxnsAsync(h, StockSourceType.Opening));
        StockBalance balance = await LoadBalanceAsync(h, itemId);
        Assert.Equal(10m, balance.QtyOnhand);
        Assert.Equal(100m, balance.Amount);
        Assert.Equal((int)OpeningBatchStatus.Posted, (await h.Opening.GetAsync(batchId, Ct)).Header.Status);
        Assert.Equal(auditCount, h.Audit.GetCapturedChanges().Count);
    }

    // ============ 3：期初驳回——批次回草稿且不写流水 ============

    [Fact]
    public async Task OpeningRejectedReturnsBatchToDraftWithoutLedger()
    {
        Harness h = Build();
        long itemId = await SeedItemAsync(h);

        (long batchId, _, StkApprovalRequestRow request) = await CreateAndSubmitOpeningAsync(
            h, new OpeningItemPayload(itemId, null, 10m, 10m));

        await h.Opening.ApplyAsync(
            CompletedEvent(request, OpeningService.OpeningFlowCode, ApprovalStatus.Rejected), Ct);

        OpeningBatchDetailRow rejected = await h.Opening.GetAsync(batchId, Ct);
        Assert.Equal((int)OpeningBatchStatus.Draft, rejected.Header.Status);
        Assert.Null(rejected.Header.PostedAt);
        Assert.Equal(0, await CountTxnsAsync(h, StockSourceType.Opening));
        Assert.Empty(await h.Db.StockBalances.AsNoTracking().ToListAsync(Ct));

        StkApprovalRequest stored = await h.Db.ApprovalRequests.AsNoTracking().SingleAsync(Ct);
        Assert.Equal(StkApprovalRequestStatus.Rejected, stored.Status);
    }

    // ============ 4：每仓库唯一已过账——同仓库第二批次提交即拒绝 ============

    [Fact]
    public async Task OpeningSecondBatchInSameWarehouseIsBlocked()
    {
        Harness h = Build();
        long itemId = await SeedItemAsync(h);

        (_, _, StkApprovalRequestRow firstRequest) = await CreateAndSubmitOpeningAsync(
            h, new OpeningItemPayload(itemId, null, 10m, 10m));
        await h.Opening.ApplyAsync(
            CompletedEvent(firstRequest, OpeningService.OpeningFlowCode, ApprovalStatus.Approved), Ct);

        OpeningBatchDetailRow second = await h.Opening.CreateDraftAsync(
            new CreateOpeningPayload(
                WarehouseId, "第二批次", [new OpeningItemPayload(itemId, null, 3m, 10m)]),
            Ct);

        ConflictException ex = await Assert.ThrowsAsync<ConflictException>(
            () => h.Opening.SubmitAsync(second.Header.Id, Ct));

        Assert.Equal(OpeningService.ErrorAlreadyPosted, ex.ErrorCode);
        Assert.Equal(
            (int)OpeningBatchStatus.Draft, (await h.Opening.GetAsync(second.Header.Id, Ct)).Header.Status);
        Assert.Single(h.Approval.Started);
        Assert.Equal(1, await CountTxnsAsync(h, StockSourceType.Opening));
    }

    // ============ 5：期初非法流转——已过账后不可改删 ============

    [Fact]
    public async Task OpeningPostedBatchCannotBeUpdatedOrDeleted()
    {
        Harness h = Build();
        long itemId = await SeedItemAsync(h);

        (long batchId, _, StkApprovalRequestRow request) = await CreateAndSubmitOpeningAsync(
            h, new OpeningItemPayload(itemId, null, 10m, 10m));
        await h.Opening.ApplyAsync(
            CompletedEvent(request, OpeningService.OpeningFlowCode, ApprovalStatus.Approved), Ct);

        ConflictException update = await Assert.ThrowsAsync<ConflictException>(
            () => h.Opening.UpdateDraftAsync(
                batchId,
                new UpdateOpeningPayload(
                    WarehouseId, "改数量", [new OpeningItemPayload(itemId, null, 1m, 10m)]),
                Ct));
        Assert.Equal("STK_INVALID_STATE", update.ErrorCode);

        ConflictException delete = await Assert.ThrowsAsync<ConflictException>(
            () => h.Opening.DeleteDraftAsync(batchId, Ct));
        Assert.Equal("STK_INVALID_STATE", delete.ErrorCode);

        // 明细与台账均未被改动
        OpeningBatchDetailRow detail = await h.Opening.GetAsync(batchId, Ct);
        Assert.Equal(10m, Assert.Single(detail.Items).Qty);
        Assert.Equal(10m, (await LoadBalanceAsync(h, itemId)).QtyOnhand);
    }

    // ============ 6：成本调整小额——定义码/旧均价/差额/红蓝字流水/最终均价 ============

    [Fact]
    public async Task CostAdjustSmallChangeUsesNormalFlowAndRewritesAverageCost()
    {
        Harness h = Build();
        long itemId = await SeedItemAsync(h);
        _ = await h.Ledger.PostAsync(
            new PostStockTxnRequest(
                StockSourceType.PurchaseIn, "SEED-001", 1, itemId, WarehouseId, null,
                StockDirection.In, 10m, 10m, Operator, "seed-001", CompletedAt),
            Ct);

        CostAdjustDetailRow draft = await h.CostAdjust.CreateDraftAsync(
            new CreateCostAdjustPayload(
                ReasonCode: 1,
                WarehouseId: WarehouseId,
                Lines: [new CostAdjustLinePayload(itemId, WarehouseId, 12m)],
                Remark: "成本调整"),
            Ct);

        Assert.StartsWith("SP-CB", draft.Header.AdjNo, StringComparison.Ordinal);
        Assert.Equal(20m, draft.Header.TotalDiffAmount);
        CostAdjustLineRow line = Assert.Single(draft.Lines);
        Assert.Equal(10m, line.OldAvgCost);
        Assert.Equal(12m, line.NewAvgCost);
        Assert.Equal(20m, line.DiffAmount);

        StkApprovalRequestRow request = await h.CostAdjust.SubmitAsync(draft.Header.Id, Ct);
        Assert.Equal((int)StkApprovalRequestStatus.Pending, request.Status);
        Assert.Equal("stk.cost.adjust", request.BizType);
        Assert.Equal(CostAdjustService.NormalFlowCode, Assert.Single(h.Approval.Started).ApprovalDefinitionCode);

        await h.CostAdjust.ApplyAsync(
            CompletedEvent(request, CostAdjustService.NormalFlowCode, ApprovalStatus.Approved), Ct);

        CostAdjustDetailRow applied = await h.CostAdjust.GetAsync(draft.Header.Id, Ct);
        Assert.Equal((int)CostAdjustStatus.Approved, applied.Header.Status);
        Assert.NotNull(applied.Header.AppliedAt);

        StockBalance balance = await LoadBalanceAsync(h, itemId);
        Assert.Equal(10m, balance.QtyOnhand);
        Assert.Equal(12m, balance.AvgCost);
        Assert.Equal(120m, balance.Amount);

        List<StockTxn> txns = await h.Db.StockTxns.AsNoTracking()
            .Where(t => t.SourceType == (int)StockSourceType.CostAdjust)
            .OrderBy(t => t.SourceLineNo)
            .ToListAsync(Ct);
        Assert.Equal(2, txns.Count);
        Assert.All(txns, txn => Assert.Equal(applied.Header.AdjNo, txn.SourceDocNo));
        Assert.Equal((int)StockDirection.Out, txns[0].Direction);
        Assert.Equal(10m, txns[0].Qty);
        // 红字出库按当时移动加权平均价（旧均价 10）结算，金额随批次出清归零
        Assert.Equal(10m, txns[0].UnitCost);
        Assert.Equal(100m, txns[0].Amount);
        Assert.Equal((int)StockDirection.In, txns[1].Direction);
        Assert.Equal(10m, txns[1].Qty);
        Assert.Equal(12m, txns[1].UnitCost);
    }

    // ============ 7：成本调整大额——按阈值选大额审批定义码 ============

    [Fact]
    public async Task CostAdjustLargeChangeUsesLargeFlowDefinition()
    {
        Harness h = Build();
        long itemId = await SeedItemAsync(h);
        _ = await h.Ledger.PostAsync(
            new PostStockTxnRequest(
                StockSourceType.PurchaseIn, "SEED-001", 1, itemId, WarehouseId, null,
                StockDirection.In, 10m, 10m, Operator, "seed-001", CompletedAt),
            Ct);

        CostAdjustDetailRow draft = await h.CostAdjust.CreateDraftAsync(
            new CreateCostAdjustPayload(
                ReasonCode: 2,
                WarehouseId: WarehouseId,
                Lines: [new CostAdjustLinePayload(itemId, WarehouseId, 2010m)],
                Remark: "大额调整"),
            Ct);

        Assert.Equal(20000m, draft.Header.TotalDiffAmount);

        _ = await h.CostAdjust.SubmitAsync(draft.Header.Id, Ct);

        Assert.Equal(CostAdjustService.LargeFlowCode, Assert.Single(h.Approval.Started).ApprovalDefinitionCode);
        Assert.Equal("stk.cost.adjust", h.Approval.Started[0].BizType);
    }

    // ============ 8：成本调整幂等——重复应用不重复写流水、均价不变 ============

    [Fact]
    public async Task CostAdjustApplyIsIdempotentForSameApprovalInstance()
    {
        Harness h = Build();
        long itemId = await SeedItemAsync(h);
        _ = await h.Ledger.PostAsync(
            new PostStockTxnRequest(
                StockSourceType.PurchaseIn, "SEED-001", 1, itemId, WarehouseId, null,
                StockDirection.In, 10m, 10m, Operator, "seed-001", CompletedAt),
            Ct);

        CostAdjustDetailRow draft = await h.CostAdjust.CreateDraftAsync(
            new CreateCostAdjustPayload(
                ReasonCode: 1,
                WarehouseId: WarehouseId,
                Lines: [new CostAdjustLinePayload(itemId, WarehouseId, 12m)],
                Remark: "成本调整"),
            Ct);
        StkApprovalRequestRow request = await h.CostAdjust.SubmitAsync(draft.Header.Id, Ct);
        ApprovalCompletedEvent approved =
            CompletedEvent(request, CostAdjustService.NormalFlowCode, ApprovalStatus.Approved);

        await h.CostAdjust.ApplyAsync(approved, Ct);
        await h.CostAdjust.ApplyAsync(approved, Ct);

        Assert.Equal(2, await CountTxnsAsync(h, StockSourceType.CostAdjust));
        StockBalance balance = await LoadBalanceAsync(h, itemId);
        Assert.Equal(10m, balance.QtyOnhand);
        Assert.Equal(12m, balance.AvgCost);
        Assert.Equal(120m, balance.Amount);
    }

    // ============ 9：成本调整冻结阻断——QtyLocked>0 提交即拒绝 ============

    [Fact]
    public async Task CostAdjustIsBlockedWhenStockIsLocked()
    {
        Harness h = Build();
        long itemId = await SeedItemAsync(h);
        _ = await h.Ledger.PostAsync(
            new PostStockTxnRequest(
                StockSourceType.PurchaseIn, "SEED-001", 1, itemId, WarehouseId, null,
                StockDirection.In, 10m, 10m, Operator, "seed-001", CompletedAt),
            Ct);
        await h.Ledger.AdjustLockedQtyAsync(itemId, WarehouseId, 2m, "盘点冻结", Ct);

        CostAdjustDetailRow draft = await h.CostAdjust.CreateDraftAsync(
            new CreateCostAdjustPayload(
                ReasonCode: 3,
                WarehouseId: WarehouseId,
                Lines: [new CostAdjustLinePayload(itemId, WarehouseId, 12m)],
                Remark: "冻结期调整"),
            Ct);

        ConflictException ex = await Assert.ThrowsAsync<ConflictException>(
            () => h.CostAdjust.SubmitAsync(draft.Header.Id, Ct));

        Assert.Equal("STK_COST_ADJUST_LOCKED", ex.ErrorCode);
        Assert.Equal(
            (int)CostAdjustStatus.Draft,
            (await h.CostAdjust.GetAsync(draft.Header.Id, Ct)).Header.Status);
        Assert.Empty(h.Approval.Started);
        Assert.Equal(10m, (await LoadBalanceAsync(h, itemId)).AvgCost);
    }
}
