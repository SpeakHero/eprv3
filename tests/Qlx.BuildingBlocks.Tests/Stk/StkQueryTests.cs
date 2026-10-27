/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-26
 * 描述：跨模块库存查询与台账事件测试——IStockQueryService 台账/批次扁平出参、批次 FEFO 排序、
 *       成本字段按权限裁剪（B-3-1）、仓库有库存判定，以及事务提交后经 Outbox 持久发布的
 *       StockChangedEvent / OpeningPostedEvent / CostAdjustedEvent 与幂等重放不重复发布（spec §4.3）
 */

using System.Data;
using System.Globalization;
using System.Security.Claims;

using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Qlx.Abstractions.Approvals;
using Qlx.Abstractions.Constants;
using Qlx.Abstractions.Events;
using Qlx.Abstractions.Events.Stk;
using Qlx.Abstractions.Queries;
using Qlx.Abstractions.Stock;
using Qlx.BuildingBlocks.Tests.Bas;
using Qlx.BuildingBlocks.Tests.Files;
using Qlx.BuildingBlocks.Tests.Jobs;
using Qlx.BuildingBlocks.Tests.Portal.Auth;
using Qlx.Modules.Stk;
using Qlx.Modules.Stk.Domain.Inventory;
using Qlx.Modules.Stk.Domain.Item;
using Qlx.Modules.Stk.Dtos;
using Qlx.Modules.Stk.Repositories;
using Qlx.Modules.Stk.Services;
using Xunit;

namespace Qlx.BuildingBlocks.Tests.Stk;

/// <summary>跨模块库存查询与台账事件测试。</summary>
public sealed class StkQueryTests
{
    private const long TenantId = 42L;
    private const long WarehouseId = 7001L;
    private const long OtherWarehouseId = 7002L;
    private const string Operator = "user-9";

    private static readonly DateTimeOffset OccurredAt =
        new(2026, 10, 26, 9, 0, 0, TimeSpan.FromHours(8));

    private static readonly DateTimeOffset CompletedAt =
        new(2026, 10, 26, 10, 0, 0, TimeSpan.FromHours(8));

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>测试夹具：SQLite 库 + Stk 租户仓储 + 查询/台账/期初/成本调整服务与事件捕获。</summary>
    private sealed class Harness
    {
        public required StkTenantDbContext Db { get; init; }

        public required StkTenantRepository Repo { get; init; }

        public required HttpContextAccessor Http { get; init; }

        public required BasRecordingEventBus Events { get; init; }

        public required StockLedgerService Ledger { get; init; }

        public required StockQueryService Query { get; init; }

        public required BasStubApprovalService Approval { get; init; }

        public required InMemoryAuditService Audit { get; init; }

        public required OpeningService Opening { get; init; }

        public required CostAdjustService CostAdjust { get; init; }
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
        var http = new HttpContextAccessor
        {
            // 默认租户管理员：成本字段可见（裁剪用例单独覆盖无力/权限路径）
            HttpContext = new DefaultHttpContext { User = BuildUser(QlxMemberRoleValues.TenantAdmin) },
        };
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
            Http = http,
            Events = events,
            Ledger = ledger,
            Query = new StockQueryService(new TestTenantContext(TenantId), repo, http),
            Approval = approval,
            Audit = audit,
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

    /// <summary>按角色/权限构造请求身份（成本字段权限判定只认令牌声明）。</summary>
    private static ClaimsPrincipal BuildUser(string? role = null, params string[] permissions)
    {
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, Operator) };
        if (role is not null)
        {
            claims.Add(new Claim(QlxClaimConstants.MemberRole, role));
        }

        if (permissions.Length > 0)
        {
            claims.Add(new Claim(QlxClaimConstants.Permissions, string.Join(';', permissions)));
        }

        return new ClaimsPrincipal(new ClaimsIdentity(claims, authenticationType: "TestAuth"));
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
        string? idempotencyKey = null,
        long warehouseId = WarehouseId)
        => new(
            StockSourceType.PurchaseIn,
            docNo,
            lineNo,
            itemId,
            warehouseId,
            batchNo,
            direction,
            qty,
            unitCost,
            Operator,
            idempotencyKey ?? $"{docNo}-{lineNo}",
            OccurredAt);

    private static Task<StockPostResult> PostAsync(Harness h, PostStockTxnRequest request)
        => h.Ledger.PostAsync(request, Ct);

    /// <summary>直接插入批次行（乱序效期用于验证 FEFO 排序）。</summary>
    private static async Task AddBatchAsync(
        Harness h, long itemId, string batchNo, DateTimeOffset? expiryDate)
    {
        StockBatch batch = new(TenantId, itemId, WarehouseId, batchNo);
        batch.SetDates(produceDate: null, expiryDate);
        batch.ApplyIn(3m, 3m);
        _ = await h.Db.StockBatches.AddAsync(batch, Ct);
        _ = await h.Db.SaveChangesAsync(Ct);
    }

    private static List<TEvent> EventsOf<TEvent>(Harness h)
        where TEvent : IntegrationEvent
        => [.. h.Events.DurableEvents.OfType<TEvent>()];

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

    // ============ 1：台账余额实际值 ============

    [Fact]
    public async Task FindBalanceReturnsActualValues()
    {
        Harness h = Build();
        long itemId = await SeedItemAsync(h);
        _ = await PostAsync(h, Request(itemId, StockDirection.In, 10m, 10m));
        await h.Ledger.AdjustLockedQtyAsync(itemId, WarehouseId, 2m, "盘点冻结", Ct);

        StockBalanceRow? row = await h.Query.FindBalanceAsync(itemId, WarehouseId, Ct);

        Assert.NotNull(row);
        Assert.Equal(itemId, row!.ItemId);
        Assert.Equal(WarehouseId, row.WarehouseId);
        Assert.Equal(10m, row.QtyOnhand);
        Assert.Equal(10m, row.AvgCost);
        Assert.Equal(100m, row.Amount);
        Assert.Equal(2m, row.QtyLocked);
        Assert.Equal(OccurredAt, row.LastTxnAt);

        // 未建账的「商品×仓库」返回 null
        Assert.Null(await h.Query.FindBalanceAsync(itemId, OtherWarehouseId, Ct));
    }

    // ============ 2：成本字段按权限裁剪（B-3-1）============

    [Fact]
    public async Task CostFieldsAreTrimmedWithoutCostPermission()
    {
        Harness h = Build();
        long itemId = await SeedItemAsync(h);
        _ = await PostAsync(h, Request(itemId, StockDirection.In, 10m, 10m));

        // 未认证：均价/金额一律 null，数量仍下发
        h.Http.HttpContext = null;
        StockBalanceRow anonymous = (await h.Query.FindBalanceAsync(itemId, WarehouseId, Ct))!;
        Assert.Null(anonymous.AvgCost);
        Assert.Null(anonymous.Amount);
        Assert.Equal(10m, anonymous.QtyOnhand);

        // 仅菜单权限（无 stk.cost.field）同样裁剪
        h.Http.HttpContext = new DefaultHttpContext { User = BuildUser(role: null, "stk.item.menu") };
        StockBalanceRow trimmed = (await h.Query.FindBalanceAsync(itemId, WarehouseId, Ct))!;
        Assert.Null(trimmed.AvgCost);
        Assert.Null(trimmed.Amount);

        // 显式授予 stk.cost.field 的普通成员可见成本
        h.Http.HttpContext = new DefaultHttpContext
        {
            User = BuildUser(role: null, "stk.item.menu", "stk.cost.field"),
        };
        StockBalanceRow granted = (await h.Query.FindBalanceAsync(itemId, WarehouseId, Ct))!;
        Assert.Equal(10m, granted.AvgCost);
        Assert.Equal(100m, granted.Amount);
    }

    // ============ 3：按商品 / 按仓库过滤 ============

    [Fact]
    public async Task BalanceListsFilterByItemAndWarehouse()
    {
        Harness h = Build();
        long itemA = await SeedItemAsync(h);
        long itemB = await SeedItemAsync(h);

        _ = await PostAsync(h, Request(itemA, StockDirection.In, 10m, 10m, lineNo: 1));
        _ = await PostAsync(h, Request(
            itemA, StockDirection.In, 5m, 10m, docNo: "SRC-W2", lineNo: 1,
            warehouseId: OtherWarehouseId));
        _ = await PostAsync(h, Request(itemB, StockDirection.In, 3m, 10m, docNo: "SRC-B", lineNo: 1));

        IReadOnlyList<StockBalanceRow> byItem = await h.Query.ListBalancesByItemAsync(itemA, Ct);
        Assert.Equal(2, byItem.Count);
        Assert.All(byItem, row => Assert.Equal(itemA, row.ItemId));
        Assert.Equal(WarehouseId, byItem[0].WarehouseId);
        Assert.Equal(10m, byItem[0].QtyOnhand);
        Assert.Equal(OtherWarehouseId, byItem[1].WarehouseId);
        Assert.Equal(5m, byItem[1].QtyOnhand);

        IReadOnlyList<StockBalanceRow> byWarehouse = await h.Query.ListBalancesByWarehouseAsync(WarehouseId, Ct);
        Assert.Equal(2, byWarehouse.Count);
        Assert.All(byWarehouse, row => Assert.Equal(WarehouseId, row.WarehouseId));
        Assert.Equal([itemA, itemB], byWarehouse.Select(static row => row.ItemId));

        Assert.Empty(await h.Query.ListBalancesByWarehouseAsync(7003L, Ct));
    }

    // ============ 4：批次 FEFO（失效日期升序、空值最后、同日期批次号升序）============

    [Fact]
    public async Task BatchesAreOrderedByFefoWithNullExpiryLast()
    {
        Harness h = Build();
        long itemId = await SeedItemAsync(h, batchEnabled: true);

        DateTimeOffset sameExpiry = new(2027, 1, 31, 0, 0, 0, TimeSpan.FromHours(8));
        DateTimeOffset earlierExpiry = new(2026, 12, 1, 0, 0, 0, TimeSpan.FromHours(8));

        // 乱序插入：无失效日期 → B → C → A
        await AddBatchAsync(h, itemId, "N", expiryDate: null);
        await AddBatchAsync(h, itemId, "B", sameExpiry);
        await AddBatchAsync(h, itemId, "C", earlierExpiry);
        await AddBatchAsync(h, itemId, "A", sameExpiry);

        IReadOnlyList<StockBatchRow> rows = await h.Query.ListBatchesAsync(itemId, WarehouseId, Ct);

        Assert.Equal(["C", "A", "B", "N"], rows.Select(static row => row.BatchNo));
        Assert.Equal(earlierExpiry, rows[0].ExpiryDate);
        Assert.Null(rows[3].ExpiryDate);
        Assert.Equal(3m, rows[0].Qty);
        Assert.Equal(3m, rows[0].AvgCost);
        Assert.Equal(9m, rows[0].Amount);

        // 其它仓库无该商品批次
        Assert.Empty(await h.Query.ListBatchesAsync(itemId, OtherWarehouseId, Ct));
    }

    // ============ 5：仓库是否存在非零库存 ============

    [Fact]
    public async Task HasStockInWarehouseChecksPositiveQuantity()
    {
        Harness h = Build();
        long itemId = await SeedItemAsync(h);

        Assert.False(await h.Query.HasStockInWarehouseAsync(WarehouseId, Ct));

        _ = await PostAsync(h, Request(itemId, StockDirection.In, 1m, 10m));
        Assert.True(await h.Query.HasStockInWarehouseAsync(WarehouseId, Ct));
        Assert.False(await h.Query.HasStockInWarehouseAsync(OtherWarehouseId, Ct));

        _ = await PostAsync(h, Request(itemId, StockDirection.Out, 1m, 0m, docNo: "SRC-OUT"));
        Assert.False(await h.Query.HasStockInWarehouseAsync(WarehouseId, Ct));
    }

    // ============ 6：库存变动事件（提交后发布 + 幂等不重复发布）============

    [Fact]
    public async Task PostPublishesSingleStockChangedEventWithFields()
    {
        Harness h = Build();
        long itemId = await SeedItemAsync(h, batchEnabled: true);

        _ = await PostAsync(h, Request(itemId, StockDirection.In, 10m, 12.5m, batchNo: "A"));

        StockChangedEvent changed = Assert.Single(EventsOf<StockChangedEvent>(h));
        Assert.Equal(itemId, changed.ItemId);
        Assert.Equal(WarehouseId, changed.WarehouseId);
        Assert.Equal("A", changed.BatchNo);
        Assert.Equal((int)StockDirection.In, changed.Direction);
        Assert.Equal(10m, changed.Qty);
        Assert.Equal(10m, changed.BalanceQtyAfter);
        Assert.Equal("t-42", changed.TenantCode);
    }

    [Fact]
    public async Task DuplicatePostDoesNotPublishSecondEvent()
    {
        Harness h = Build();
        long itemId = await SeedItemAsync(h);
        PostStockTxnRequest request = Request(
            itemId, StockDirection.In, 10m, 10m, idempotencyKey: "IDEM-EVT-1");

        StockPostResult first = await PostAsync(h, request);
        StockPostResult second = await PostAsync(h, request);

        Assert.True(first.Success);
        Assert.True(second.Success);
        Assert.Equal(first.TxnId, second.TxnId);
        Assert.Single(EventsOf<StockChangedEvent>(h));
    }

    // ============ 7：期初过账事件（TotalAmount 正确 + 重复 Apply 不重发）============

    [Fact]
    public async Task OpeningPostPublishesOpeningPostedEventOnce()
    {
        Harness h = Build();
        long itemA = await SeedItemAsync(h);
        long itemB = await SeedItemAsync(h);

        OpeningBatchDetailRow draft = await h.Opening.CreateDraftAsync(
            new CreateOpeningPayload(
                WarehouseId,
                "期初建账",
                [new OpeningItemPayload(itemA, null, 10m, 10m), new OpeningItemPayload(itemB, null, 5m, 20m)]),
            Ct);
        StkApprovalRequestRow request = await h.Opening.SubmitAsync(draft.Header.Id, Ct);
        ApprovalCompletedEvent approved =
            CompletedEvent(request, OpeningService.OpeningFlowCode, ApprovalStatus.Approved);

        await h.Opening.ApplyAsync(approved, Ct);

        OpeningPostedEvent posted = Assert.Single(EventsOf<OpeningPostedEvent>(h));
        Assert.Equal(draft.Header.Id, posted.OpeningBatchId);
        Assert.Equal(draft.Header.BatchNo, posted.BatchNo);
        Assert.Equal(WarehouseId, posted.WarehouseId);
        Assert.Equal(200m, posted.TotalAmount);
        Assert.Equal("t-42", posted.TenantCode);

        // 幂等重放：不再过账，也不再重复发布期初事件
        await h.Opening.ApplyAsync(approved, Ct);
        Assert.Single(EventsOf<OpeningPostedEvent>(h));
        Assert.Equal(2, EventsOf<StockChangedEvent>(h).Count);
    }

    // ============ 8：成本调整事件（差额合计 + 重复 Apply 不重发）============

    [Fact]
    public async Task CostAdjustPublishesCostAdjustedEventOnce()
    {
        Harness h = Build();
        long itemId = await SeedItemAsync(h);
        _ = await PostAsync(h, Request(itemId, StockDirection.In, 10m, 10m, idempotencyKey: "SEED-001"));

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

        CostAdjustedEvent adjusted = Assert.Single(EventsOf<CostAdjustedEvent>(h));
        Assert.Equal(draft.Header.Id, adjusted.CostAdjustId);
        Assert.Equal(draft.Header.AdjNo, adjusted.AdjNo);
        Assert.Equal(20m, adjusted.TotalDiffAmount);
        Assert.Equal("t-42", adjusted.TenantCode);

        // 幂等重放：不再写红蓝字流水，也不再重复发布成本调整事件
        await h.CostAdjust.ApplyAsync(approved, Ct);
        Assert.Single(EventsOf<CostAdjustedEvent>(h));
    }
}
