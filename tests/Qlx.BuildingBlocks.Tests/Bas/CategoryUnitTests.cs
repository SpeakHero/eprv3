/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-25 17:20:00 +08:00 中国标准时间 星期日
 * 描述：阶段三「商品分类与计量单位」测试——层级连续编码与并发唯一、层级上限 422、
 *       有下级分类删除阻断、换算率变更审计旧值/新值、分类停用审批通过生效与同实例幂等
 */

using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

using Qlx.Abstractions.Approvals;
using Qlx.Abstractions.Audit;
using Qlx.Abstractions.Exceptions;
using Qlx.Abstractions.Serialization;
using Qlx.BuildingBlocks.Caching;
using Qlx.BuildingBlocks.Numbering;
using Qlx.BuildingBlocks.Numbering.Pinyin;
using Qlx.BuildingBlocks.Support.Logs;
using Qlx.BuildingBlocks.Tests.Files;
using Qlx.BuildingBlocks.Tests.Jobs;
using Qlx.BuildingBlocks.Tests.Portal.Auth;
using Qlx.Modules.Bas;
using Qlx.Modules.Bas.Domain;
using Qlx.Modules.Bas.Domain.Goods;
using Qlx.Modules.Bas.Dtos;
using Qlx.Modules.Bas.Repositories;
using Qlx.Modules.Bas.Services;
using Qlx.Modules.Bas.Services.Numbering;

namespace Qlx.BuildingBlocks.Tests.Bas;

public sealed class CategoryUnitTests
{
    private const long TenantId = 42L;
    private const string Operator = "user-9";

    /// <summary>测试夹具：Sqlite 库 + 分类/单位服务 + 审批应用器（全部桩依赖）。</summary>
    private sealed class Harness
    {
        public required BasTenantDbContext Db { get; init; }

        public required BasTenantRepository Repo { get; init; }

        public required InMemorySerialSequenceStore Serials { get; init; }

        public required MemoryCacheService Cache { get; init; }

        public required InMemoryAuditService Audit { get; init; }

        public required StubSystemParameterProvider Parameters { get; init; }

        public required BasStubApprovalService Approval { get; init; }

        public required ItemCategoryService CategoryService { get; init; }

        public required UnitService UnitService { get; init; }

        public required BasApprovalApplier Applier { get; init; }
    }

    private static Harness Build(
        string? connectionString = null,
        InMemorySerialSequenceStore? serials = null,
        StubSystemParameterProvider? parameters = null,
        bool ensureCreated = true)
    {
        var basConnection = new SqliteConnection(connectionString ?? "DataSource=:memory:");
        basConnection.Open();
        var logConnection = new SqliteConnection("DataSource=:memory:");
        logConnection.Open();

        var resolver = new JobStubTenantDatabaseResolver();
        var db = new BasTenantDbContext(
            resolver,
            TenantId,
            new DbContextOptionsBuilder<BasTenantDbContext>().UseSqlite(basConnection).Options);
        if (ensureCreated)
        {
            _ = db.Database.EnsureCreated();
        }

        var logDb = new TenantLogDbContext(
            resolver,
            TenantId,
            new DbContextOptionsBuilder<TenantLogDbContext>().UseSqlite(logConnection).Options);
        _ = logDb.Database.EnsureCreated();

        var repo = new BasTenantRepository(db, logDb);
        var sharedSerials = serials ?? new InMemorySerialSequenceStore();
        var cache = new MemoryCacheService(Options.Create(new CacheOptions()));
        var audit = new InMemoryAuditService();
        var stubParameters = parameters ?? new StubSystemParameterProvider();
        var approval = new BasStubApprovalService();
        var tenantCtx = new TestTenantContext(TenantId);
        var userCtx = new TestCurrentUserContext(Operator);
        var snowflake = new BasStubSnowflakeIdGenerator();

        var codeGenerator = new BasCodeNumberGenerator(repo, sharedSerials);
        var orgService = new OrgMasterDataService(
            tenantCtx,
            repo,
            codeGenerator,
            approval,
            new BasRecordingEventBus(),
            cache,
            audit,
            snowflake,
            userCtx,
            new StubTenantMemberQuery());
        var positionService = new PositionMasterDataService(
            tenantCtx,
            repo,
            new EncodingGenerator(new PinyinConverter(), sharedSerials),
            approval,
            cache,
            audit,
            snowflake,
            userCtx);
        var categoryService = new ItemCategoryService(
            tenantCtx,
            repo,
            codeGenerator,
            approval,
            stubParameters,
            cache,
            audit,
            snowflake,
            userCtx);
        var unitService = new UnitService(
            tenantCtx,
            repo,
            new EncodingGenerator(new PinyinConverter(), sharedSerials),
            cache,
            audit,
            snowflake,
            userCtx);

        var supplierService = new SupplierService(
            tenantCtx,
            repo,
            codeGenerator,
            approval,
            cache,
            audit,
            snowflake,
            userCtx);
        var customerService = new CustomerService(
            tenantCtx,
            repo,
            codeGenerator,
            approval,
            stubParameters,
            cache,
            audit,
            snowflake,
            userCtx);

        return new Harness
        {
            Db = db,
            Repo = repo,
            Serials = sharedSerials,
            Cache = cache,
            Audit = audit,
            Parameters = stubParameters,
            Approval = approval,
            CategoryService = categoryService,
            UnitService = unitService,
            Applier = new BasApprovalApplier(
                tenantCtx,
                repo,
                orgService,
                positionService,
                categoryService,
                supplierService,
                customerService,
                new BasRecordingEventBus(),
                cache),
        };
    }

    private static Task<ItemCategoryRow> CreateCategoryAsync(
        Harness h, string name, long? parentId = null)
        => h.CategoryService.CreateAsync(
            new CreateItemCategoryPayload(name, parentId, UnitGroupId: null),
            TestContext.Current.CancellationToken);

    private static string BizId(long requestId) => requestId.ToString(CultureInfo.InvariantCulture);

    // ============ 分类：层级编码与并发唯一 ============

    [Fact]
    public async Task CategoryCodesFollowParentChainAcrossThreeLevels()
    {
        Harness h = Build();

        ItemCategoryRow root = await CreateCategoryAsync(h, "食品");
        ItemCategoryRow child = await CreateCategoryAsync(h, "饮料", root.Id);
        ItemCategoryRow grandchild = await CreateCategoryAsync(h, "碳酸饮料", child.Id);

        // 层级连续编码：父 01 → 子 0101 → 孙 010101（{parent}{seq:2}）
        Assert.Equal("01", root.Code);
        Assert.Equal("0101", child.Code);
        Assert.Equal("010101", grandchild.Code);
        Assert.Equal(1, root.LevelNo);
        Assert.Equal(2, child.LevelNo);
        Assert.Equal(3, grandchild.LevelNo);
        Assert.Equal(root.Id, child.ParentId);
        Assert.Equal(child.Id, grandchild.ParentId);
    }

    [Fact]
    public async Task ConcurrentCategoryCodeGenerationIsUnique()
    {
        // 共享内存库（同一进程多连接）+ 共享流水存储，模拟多实例并发取号
        string databaseName = $"qlx-bas-{Guid.NewGuid():N}";
        string connectionString = $"DataSource=file:{databaseName}?mode=memory&cache=shared";
        Harness seed = Build(connectionString);

        // 顺序生成一次：懒种子编码规则并占用序号 1
        string first = await new BasCodeNumberGenerator(seed.Repo, seed.Serials).NextAsync(
            "category", TenantId, null, null, (_, _) => Task.FromResult(false),
            TestContext.Current.CancellationToken);
        Assert.Equal("01", first);

        var used = new ConcurrentDictionary<string, byte>(StringComparer.Ordinal);
        _ = used.TryAdd(first, 0);

        const int parallel = 20;
        string[] codes = await Task.WhenAll(Enumerable.Range(0, parallel).Select(_ => Task.Run(async () =>
        {
            // EF 上下文非线程安全：每任务独立上下文，共享流水存储与唯一性集合
            Harness h = Build(connectionString, seed.Serials, ensureCreated: false);
            return await new BasCodeNumberGenerator(h.Repo, h.Serials).NextAsync(
                "category", TenantId, null, null,
                (candidate, _) => Task.FromResult(!used.TryAdd(candidate, 0)),
                TestContext.Current.CancellationToken);
        })));

        // 并发取号不重号：20 个编码互不相同且均为两位流水
        Assert.Equal(parallel, codes.Distinct(StringComparer.Ordinal).Count());
        Assert.All(codes, code => Assert.Matches("^[0-9]{2}$", code));
    }

    [Fact]
    public async Task CreateCategoryBeyondMaxLevelThrowsValidation()
    {
        Harness h = Build();
        h.Parameters.Ints[ItemCategoryService.MaxLevelParamKey] = 3;

        ItemCategoryRow root = await CreateCategoryAsync(h, "食品");
        ItemCategoryRow level2 = await CreateCategoryAsync(h, "饮料", root.Id);
        ItemCategoryRow level3 = await CreateCategoryAsync(h, "碳酸饮料", level2.Id);

        ValidationException ex = await Assert.ThrowsAsync<ValidationException>(
            () => CreateCategoryAsync(h, "第四级", level3.Id));

        // 层级超过参数上限：422 + 字段级错误定位到 levelNo
        Assert.Equal("levelNo", Assert.Single(ex.FieldErrors).FieldName);
        Assert.Equal(3, await h.Db.ItemCategories.CountAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task DeleteCategoryWithChildrenIsBlocked()
    {
        Harness h = Build();
        ItemCategoryRow root = await CreateCategoryAsync(h, "食品");
        _ = await CreateCategoryAsync(h, "饮料", root.Id);

        ConflictException ex = await Assert.ThrowsAsync<ConflictException>(
            () => h.CategoryService.DeleteAsync(root.Id, TestContext.Current.CancellationToken));

        Assert.Equal("CATEGORY_HAS_CHILDREN", ex.ErrorCode);
        ItemCategory reloaded = await h.Db.ItemCategories.AsNoTracking().SingleAsync(
            c => c.Id == root.Id, TestContext.Current.CancellationToken);
        Assert.False(reloaded.IsDeleted);
    }

    // ============ 计量单位：换算率变更审计 ============

    [Fact]
    public async Task ConversionRateChangeCapturesAuditWithOldAndNewValues()
    {
        Harness h = Build();
        UnitGroupRow group = await h.UnitService.SaveUnitGroupAsync(
            new SaveUnitGroupPayload(null, "重量"), TestContext.Current.CancellationToken);
        UnitRow kilogram = await h.UnitService.SaveUnitAsync(
            new SaveUnitPayload(null, "千克", group.Id, 3), TestContext.Current.CancellationToken);
        UnitRow box = await h.UnitService.SaveUnitAsync(
            new SaveUnitPayload(null, "箱", group.Id, 0), TestContext.Current.CancellationToken);

        // 单位组/单位编码由后端生成（N-1），前端不传码
        Assert.StartsWith("ug-", group.Code, StringComparison.Ordinal);
        Assert.StartsWith("u-", kilogram.Code, StringComparison.Ordinal);

        UnitConversionRow created = await h.UnitService.SaveConversionAsync(
            new SaveUnitConversionPayload(null, group.Id, kilogram.Id, box.Id, 10m),
            TestContext.Current.CancellationToken);
        _ = await h.UnitService.SaveConversionAsync(
            new SaveUnitConversionPayload(created.Id, group.Id, kilogram.Id, box.Id, 12.5m),
            TestContext.Current.CancellationToken);

        // 换算率变更：旧值/新值快照均落审计，历史单据不回溯
        AuditLogEntry audit = Assert.Single(
            h.Audit.GetCapturedChanges(),
            e => string.Equals(e.EntityType, nameof(UnitConversion), StringComparison.Ordinal)
                 && string.Equals(e.Operation, "UPDATE", StringComparison.Ordinal));
        Assert.Equal(created.Id, audit.EntityId);
        Assert.Equal(Operator, audit.ChangedBy);

        UnitConversionRow oldValues = Assert.IsType<UnitConversionRow>(
            JsonSerializer.Deserialize<UnitConversionRow>(audit.OldValues!, QlxJsonDefaults.PreserveNull));
        UnitConversionRow newValues = Assert.IsType<UnitConversionRow>(
            JsonSerializer.Deserialize<UnitConversionRow>(audit.NewValues!, QlxJsonDefaults.PreserveNull));
        Assert.Equal(10m, oldValues.Rate);
        Assert.Equal(12.5m, newValues.Rate);
    }

    // ============ 分类停用审批 ============

    [Fact]
    public async Task ApprovedCategoryDisableRequestDisablesCategoryAndIsIdempotent()
    {
        Harness h = Build();
        ItemCategoryRow category = await CreateCategoryAsync(h, "食品");

        ApprovalRequestRow submitted = await h.CategoryService.SubmitDisableRequestAsync(
            category.Id, TestContext.Current.CancellationToken);

        // 审批挂起：请求待审批、已回写实例 Id，业务类型为分类停用
        Assert.Equal(1, submitted.Status);
        Assert.NotNull(submitted.ApprovalInstanceId);
        Assert.Equal(ItemCategoryService.CategoryDisableBizType, submitted.BizType);
        Assert.Equal(ItemCategoryService.CategoryDisableBizType, h.Approval.Started[0].ApprovalDefinitionCode);

        var completed = new ApprovalCompletedEvent(
            submitted.ApprovalInstanceId!,
            ItemCategoryService.CategoryDisableBizType,
            ItemCategoryService.CategoryDisableBizType,
            BizId(submitted.BizId),
            ApprovalStatus.Approved,
            Operator,
            DateTimeOffset.UtcNow);

        await h.Applier.ApplyAsync(completed, TestContext.Current.CancellationToken);

        // 审批通过后分类停用（读路径已失效缓存，返回新状态）
        ItemCategoryRow reloaded = await h.CategoryService.GetAsync(
            category.Id, TestContext.Current.CancellationToken);
        Assert.Equal((int)EntityStatus.Disabled, reloaded.Status);

        BasApprovalRequest request = await h.Db.ApprovalRequests.SingleAsync(
            r => r.ApprovalInstanceId == submitted.ApprovalInstanceId, TestContext.Current.CancellationToken);
        Assert.Equal(ApprovalRequestStatus.Approved, request.Status);
        DateTimeOffset appliedAt = Assert.IsType<DateTimeOffset>(request.AppliedAt);

        // 同一 ApprovalInstanceId 二次事件不重复生效
        int auditCount = h.Audit.GetCapturedChanges().Count(
            e => string.Equals(e.EntityType, nameof(ItemCategory), StringComparison.Ordinal));
        await h.Applier.ApplyAsync(completed, TestContext.Current.CancellationToken);

        Assert.Equal(
            auditCount,
            h.Audit.GetCapturedChanges().Count(
                e => string.Equals(e.EntityType, nameof(ItemCategory), StringComparison.Ordinal)));
        BasApprovalRequest unchanged = await h.Db.ApprovalRequests.AsNoTracking().SingleAsync(
            r => r.ApprovalInstanceId == submitted.ApprovalInstanceId, TestContext.Current.CancellationToken);
        Assert.Equal(appliedAt, unchanged.AppliedAt);
    }
}
