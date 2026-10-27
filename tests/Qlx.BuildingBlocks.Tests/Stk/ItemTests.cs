/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-26
 * 描述：商品档案测试——编码「分类编码 + 4 位流水」（N-1/A-12）、条码唯一冲突、入参不含商品编码、
 *       新建审批挂起与同实例幂等应用、关键属性变更挂起与税率日志、有库存停用阻断、成本字段按请求权限裁剪
 */

using System.Data;
using System.Globalization;
using System.Security.Claims;

using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

using Qlx.Abstractions.Approvals;
using Qlx.Abstractions.Constants;
using Qlx.Abstractions.Exceptions;
using Qlx.Abstractions.Queries;
using Qlx.BuildingBlocks.Caching;
using Qlx.BuildingBlocks.Numbering;
using Qlx.BuildingBlocks.Tests.Bas;
using Qlx.BuildingBlocks.Tests.Files;
using Qlx.BuildingBlocks.Tests.Jobs;
using Qlx.Modules.Stk;
using Qlx.Modules.Stk.Domain.Item;
using Qlx.Modules.Stk.Domain.Numbering;
using Qlx.Modules.Stk.Dtos;
using Qlx.Modules.Stk.Repositories;
using Qlx.Modules.Stk.Services;
using Qlx.Modules.Stk.Services.Numbering;

namespace Qlx.BuildingBlocks.Tests.Stk;

/// <summary>商品档案测试。</summary>
public sealed class ItemTests
{
    private const long TenantId = 42L;
    private const string Operator = "user-9";
    private const string ItemCreateFlowCode = "flow.stk.item.create";
    private const string ItemKeyFieldChangeFlowCode = "flow.stk.item.keyfield.change";
    private const string ItemDisableFlowCode = "flow.stk.item.disable";

    /// <summary>测试夹具：SQLite 库 + 商品服务 + 全部桩依赖。</summary>
    private sealed class Harness
    {
        public required StkTenantDbContext Db { get; init; }

        public required StkTenantRepository Repo { get; init; }

        public required InMemorySerialSequenceStore Serials { get; init; }

        public required StubItemCategoryQueryService Categories { get; init; }

        public required StubItemStockQueryService Stock { get; init; }

        public required BasStubApprovalService Approval { get; init; }

        public required InMemoryAuditService Audit { get; init; }

        public required HttpContextAccessor Http { get; init; }

        public required ItemService Service { get; init; }
    }

    private static Harness Build(
        SqliteConnection? connection = null,
        long tenantId = TenantId,
        bool ensureCreated = true,
        bool hasStock = false,
        ClaimsPrincipal? user = null)
    {
        SqliteConnection ownedConnection = connection ?? new SqliteConnection("DataSource=:memory:");
        if (ownedConnection.State != ConnectionState.Open)
        {
            ownedConnection.Open();
        }

        var db = new StkTenantDbContext(
            new JobStubTenantDatabaseResolver(),
            tenantId,
            new DbContextOptionsBuilder<StkTenantDbContext>().UseSqlite(ownedConnection).Options);
        if (ensureCreated)
        {
            _ = db.Database.EnsureCreated();
        }

        var repo = new StkTenantRepository(db);
        var serials = new InMemorySerialSequenceStore();
        var categories = new StubItemCategoryQueryService();
        var stock = new StubItemStockQueryService { HasStock = hasStock };
        var approval = new BasStubApprovalService();
        var cache = new MemoryCacheService(Options.Create(new CacheOptions()));
        var audit = new InMemoryAuditService();
        var http = new HttpContextAccessor();
        if (user is not null)
        {
            http.HttpContext = new DefaultHttpContext { User = user };
        }

        var tenantCtx = new TestTenantContext(tenantId);
        var codeGenerator = new StkItemCodeGenerator(categories, serials, repo);

        return new Harness
        {
            Db = db,
            Repo = repo,
            Serials = serials,
            Categories = categories,
            Stock = stock,
            Approval = approval,
            Audit = audit,
            Http = http,
            Service = new ItemService(
                tenantCtx,
                repo,
                codeGenerator,
                categories,
                stock,
                approval,
                cache,
                audit,
                new BasStubSnowflakeIdGenerator(),
                new TestCurrentUserContext(Operator),
                http),
        };
    }

    private static CreateItemPayload CreatePayload(string name = "冰红茶", string? barcode = null)
        => new(
            name,
            "500ml",
            barcode,
            StubItemCategoryQueryService.KnownCategoryId,
            UnitId: 9001L,
            BatchEnabled: false,
            ExpiryEnabled: false,
            TaxRate: 0.13m,
            RefCostPrice: 2.5m,
            GuidePrice: 3.5m);

    private static UpdateItemPayload UpdatePayload(
        string name = "冰红茶",
        string? barcode = null,
        decimal taxRate = 0.13m,
        long unitId = 9001L)
        => new(
            name,
            "500ml",
            barcode,
            StubItemCategoryQueryService.KnownCategoryId,
            unitId,
            BatchEnabled: false,
            ExpiryEnabled: false,
            taxRate,
            RefCostPrice: 2.5m,
            GuidePrice: 3.5m);

    private static Task<StkApprovalRequestRow> SubmitCreateAsync(
        Harness h, string name = "冰红茶", string? barcode = null)
        => h.Service.CreateAsync(CreatePayload(name, barcode), TestContext.Current.CancellationToken);

    private static ApprovalCompletedEvent ApprovedEvent(StkApprovalRequestRow request)
        => new(
            request.ApprovalInstanceId!,
            ItemCreateFlowCode,
            request.BizType,
            request.BizId.ToString(CultureInfo.InvariantCulture),
            ApprovalStatus.Approved,
            Operator,
            DateTimeOffset.UtcNow)
        {
            TenantCode = TenantId.ToString(CultureInfo.InvariantCulture),
        };

    private static Task ApplyAsync(Harness h, StkApprovalRequestRow request)
        => h.Service.ApplyAsync(ApprovedEvent(request), TestContext.Current.CancellationToken);

    /// <summary>提交新建申请并通过审批，返回落库后的商品扁平行。</summary>
    private static async Task<ItemRow> CreateApprovedItemAsync(
        Harness h, string name = "冰红茶", string? barcode = null)
    {
        StkApprovalRequestRow request = await SubmitCreateAsync(h, name, barcode);
        await ApplyAsync(h, request);
        ItemRow? row = (await h.Service.ListAsync(TestContext.Current.CancellationToken))
            .FirstOrDefault(item => string.Equals(item.Name, name, StringComparison.Ordinal));
        return row ?? throw new InvalidOperationException($"审批应用后未生成商品：{name}。");
    }

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

    // ============ 编码：分类编码 + 4 位流水（N-1 / A-12）============

    [Fact]
    public async Task CodeGeneratorUsesCategoryCodeAndFourDigitSequence()
    {
        Harness h = Build();
        var generator = new StkItemCodeGenerator(h.Categories, h.Serials, h.Repo);

        Assert.Equal(
            "010001",
            await generator.NextAsync(StubItemCategoryQueryService.KnownCategoryId, TestContext.Current.CancellationToken));
        Assert.Equal(
            "010002",
            await generator.NextAsync(StubItemCategoryQueryService.KnownCategoryId, TestContext.Current.CancellationToken));
    }

    // ============ 条码租户内唯一（过滤索引 + 后端校验）============

    [Fact]
    public async Task DuplicateBarcodeIsRejectedWithErrorCode()
    {
        const string barcode = "690000000001";
        Harness h = Build();
        _ = await CreateApprovedItemAsync(h, "冰红茶", barcode);

        ConflictException ex = await Assert.ThrowsAsync<ConflictException>(
            () => SubmitCreateAsync(h, "绿茶", barcode));

        Assert.Equal("STK_BARCODE_EXISTS", ex.ErrorCode);
    }

    // ============ 价格非负校验（V-1：400 而非入库负成本）============

    [Fact]
    public async Task NegativeReferenceCostPriceIsRejected()
    {
        Harness h = Build();
        CreateItemPayload payload = CreatePayload("冰红茶") with { RefCostPrice = -0.01m };

        ValidationException ex = await Assert.ThrowsAsync<ValidationException>(
            () => h.Service.CreateAsync(payload, TestContext.Current.CancellationToken));

        Assert.Equal("refCostPrice", ex.FieldErrors[0].FieldName);
        Assert.Empty(h.Approval.Started);
    }

    [Fact]
    public async Task NegativeGuidePriceIsRejected()
    {
        Harness h = Build();
        ItemRow created = await CreateApprovedItemAsync(h);
        UpdateItemPayload payload = UpdatePayload() with { GuidePrice = -1m };

        ValidationException ex = await Assert.ThrowsAsync<ValidationException>(
            () => h.Service.UpdateAsync(
                created.Id, payload, TestContext.Current.CancellationToken));

        Assert.Equal("guidePrice", ex.FieldErrors[0].FieldName);
    }

    // ============ N-1：新建/更新入参不含商品编码 ============

    [Fact]
    public void CreateAndUpdatePayloadsMustNotExposeItemCode()
    {
        Assert.Null(typeof(CreateItemPayload).GetProperty("ItemCode"));
        Assert.Null(typeof(UpdateItemPayload).GetProperty("ItemCode"));

        // 编码仅作为只读回显字段存在于出参
        Assert.NotNull(typeof(ItemRow).GetProperty("ItemCode"));
    }

    // ============ 新建：审批挂起 → 应用 → 同实例幂等 ============

    [Fact]
    public async Task CreateIsPendedThenAppliedOncePerInstance()
    {
        Harness h = Build();

        StkApprovalRequestRow request = await SubmitCreateAsync(h);
        Assert.Equal((int)StkApprovalRequestStatus.Pending, request.Status);
        Assert.False(string.IsNullOrWhiteSpace(request.ApprovalInstanceId));
        Assert.Equal(ItemCreateFlowCode, Assert.Single(h.Approval.Started).ApprovalDefinitionCode);
        Assert.Empty(await h.Service.ListAsync(TestContext.Current.CancellationToken));

        await ApplyAsync(h, request);

        ItemRow row = Assert.Single(await h.Service.ListAsync(TestContext.Current.CancellationToken));
        Assert.Equal("010001", row.ItemCode);
        Assert.Equal((int)ItemStatus.Enabled, row.Status);
        Assert.Contains(
            h.Audit.GetCapturedChanges(),
            entry => string.Equals(entry.EntityType, nameof(Item), StringComparison.Ordinal)
                && string.Equals(entry.Operation, "INSERT", StringComparison.Ordinal));

        // 同一 ApprovalInstanceId 二次事件：幂等，不重复建单/不重复审计
        int auditCount = h.Audit.GetCapturedChanges().Count;
        await ApplyAsync(h, request);
        Assert.Single(await h.Service.ListAsync(TestContext.Current.CancellationToken));
        Assert.Equal(auditCount, h.Audit.GetCapturedChanges().Count);

        StkApprovalRequest applied = Assert.Single(
            h.Db.ApprovalRequests.AsNoTracking());
        Assert.Equal(StkApprovalRequestStatus.Approved, applied.Status);
        Assert.NotNull(applied.AppliedAt);
    }

    // ============ 关键属性变更：挂起 → 应用 → 税率日志（字段 3）============

    [Fact]
    public async Task KeyFieldChangeIsPendedAndWritesTaxRatePriceLog()
    {
        Harness h = Build();
        ItemRow created = await CreateApprovedItemAsync(h);

        UpdateItemResult pended = await h.Service.UpdateAsync(
            created.Id,
            UpdatePayload(taxRate: 0.09m),
            TestContext.Current.CancellationToken);

        Assert.True(pended.PendingApproval);
        Assert.Null(pended.Row);
        StkApprovalRequestRow request = Assert.IsType<StkApprovalRequestRow>(pended.Request);
        Assert.Equal("stk.item.keyfield.change", request.BizType);
        Assert.Equal(
            ItemKeyFieldChangeFlowCode,
            h.Approval.Started[^1].ApprovalDefinitionCode);

        // 挂起期间税率保持不变
        Assert.Equal(0.13m, (await h.Service.GetAsync(created.Id, TestContext.Current.CancellationToken)).TaxRate);

        await ApplyAsync(h, request);

        ItemRow applied = await h.Service.GetAsync(created.Id, TestContext.Current.CancellationToken);
        Assert.Equal(0.09m, applied.TaxRate);

        ItemPriceLogRow log = Assert.Single(
            await h.Service.ListPriceLogsAsync(created.Id, TestContext.Current.CancellationToken));
        Assert.Equal(3, log.Field);
        Assert.Equal("0.13", log.OldValue);
        Assert.Equal("0.09", log.NewValue);
        Assert.Equal(Operator, log.ApprovedBy);

        // 同实例二次应用：不重复写价格日志
        await ApplyAsync(h, request);
        Assert.Single(await h.Service.ListPriceLogsAsync(created.Id, TestContext.Current.CancellationToken));
    }

    // ============ 停用前置：有库存阻断；停用申请可挂起 ============

    [Fact]
    public async Task DisablingItemWithStockIsBlockedAndDisableRequestIsPended()
    {
        Harness h = Build(hasStock: true);
        ItemRow created = await CreateApprovedItemAsync(h);

        ConflictException ex = await Assert.ThrowsAsync<ConflictException>(
            () => h.Service.ToggleAsync(created.Id, TestContext.Current.CancellationToken));

        Assert.Equal("STK_ITEM_HAS_STOCK", ex.ErrorCode);
        Assert.Equal(
            (int)ItemStatus.Enabled,
            (await h.Service.GetAsync(created.Id, TestContext.Current.CancellationToken)).Status);

        StkApprovalRequestRow request = await h.Service.SubmitDisableRequestAsync(
            created.Id, TestContext.Current.CancellationToken);
        Assert.Equal("stk.item.disable", request.BizType);
        Assert.Equal(ItemDisableFlowCode, h.Approval.Started[^1].ApprovalDefinitionCode);
    }

    // ============ 成本字段：按当前请求权限裁剪（B-3-1 / C-7-2）============

    [Fact]
    public async Task CostFieldsAreTrimmedPerRequestPermission()
    {
        Harness h = Build(user: BuildUser(role: QlxMemberRoleValues.TenantAdmin));
        ItemRow created = await CreateApprovedItemAsync(h);

        ItemRow adminView = await h.Service.GetAsync(created.Id, TestContext.Current.CancellationToken);
        Assert.Equal(2.5m, adminView.RefCostPrice);
        Assert.Equal(3.5m, adminView.GuidePrice);

        // 显式授权 stk.cost.field 的普通成员同样可见
        h.Http.HttpContext = new DefaultHttpContext
        {
            User = BuildUser(role: null, "stk.item.menu", "stk.cost.field"),
        };
        Assert.Equal(
            2.5m,
            (await h.Service.GetAsync(created.Id, TestContext.Current.CancellationToken)).RefCostPrice);

        // 无权限（仅菜单权限 / 未认证）一律不下发成本字段；缓存值不受首个请求权限影响
        h.Http.HttpContext = new DefaultHttpContext { User = BuildUser(role: null, "stk.item.menu") };
        ItemRow trimmed = await h.Service.GetAsync(created.Id, TestContext.Current.CancellationToken);
        Assert.Null(trimmed.RefCostPrice);
        Assert.Null(trimmed.GuidePrice);

        h.Http.HttpContext = null;
        ItemRow anonymous = (await h.Service.ListAsync(TestContext.Current.CancellationToken))
            .Single(item => item.Id == created.Id);
        Assert.Null(anonymous.RefCostPrice);
        Assert.Null(anonymous.GuidePrice);
    }
}

/// <summary>商品分类只读查询桩：仅 <see cref="KnownCategoryId"/> 存在（编码 01）。</summary>
internal sealed class StubItemCategoryQueryService : IProductCategoryQueryService
{
    public const long KnownCategoryId = 5001L;
    public const string KnownCategoryCode = "01";

    private static readonly ProductCategoryQueryRow s_row =
        new(KnownCategoryId, null, KnownCategoryCode, "饮料", 1, 1);

    public Task<IReadOnlyList<ProductCategoryQueryRow>> ListAllAsync(CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<ProductCategoryQueryRow>>([s_row]);

    public Task<ProductCategoryQueryRow?> FindAsync(long id, CancellationToken cancellationToken)
        => Task.FromResult(id == KnownCategoryId ? s_row : (ProductCategoryQueryRow?)null);
}

/// <summary>库存只读查询桩：<see cref="HasStock"/> 决定商品是否存在非零库存。</summary>
internal sealed class StubItemStockQueryService : IStockQueryService
{
    public bool HasStock { get; set; }

    public Task<bool> HasStockInWarehouseAsync(long warehouseId, CancellationToken cancellationToken)
        => Task.FromResult(HasStock);

    public Task<StockBalanceRow?> FindBalanceAsync(
        long itemId, long warehouseId, CancellationToken cancellationToken)
        => Task.FromResult<StockBalanceRow?>(null);

    public Task<IReadOnlyList<StockBalanceRow>> ListBalancesByItemAsync(
        long itemId, CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<StockBalanceRow>>(
            HasStock ? [new StockBalanceRow(itemId, 1L, 10m, null, null, 0m, null)] : []);

    public Task<IReadOnlyList<StockBalanceRow>> ListBalancesByWarehouseAsync(
        long warehouseId, CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<StockBalanceRow>>([]);

    public Task<IReadOnlyList<StockBatchRow>> ListBatchesAsync(
        long itemId, long warehouseId, CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<StockBatchRow>>([]);
}
