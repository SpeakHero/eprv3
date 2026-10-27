/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-26 13:30:00 +08:00 中国标准时间 星期一
 * 描述：Wms 仓库主数据测试——编码后端生成（N-1）、停用前置（有库存/有未完成作业）与审批挂起、
 *       写后缓存失效（含跨模块查询）、子级存在删除阻断、库区编码唯一、租户隔离、长度/范围校验
 */

using System.Data;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

using Qlx.Abstractions.Exceptions;
using Qlx.Abstractions.Queries;
using Qlx.BuildingBlocks.Caching;
using Qlx.BuildingBlocks.Numbering;
using Qlx.BuildingBlocks.Tests.Bas;
using Qlx.BuildingBlocks.Tests.Files;
using Qlx.BuildingBlocks.Tests.Jobs;
using Qlx.Modules.Wms;
using Qlx.Modules.Wms.Domain.Inbound;
using Qlx.Modules.Wms.Domain.Warehouse;
using Qlx.Modules.Wms.Dtos;
using Qlx.Modules.Wms.Repositories;
using Qlx.Modules.Wms.Services;

namespace Qlx.BuildingBlocks.Tests.Wms;

/// <summary>仓库主数据测试。</summary>
public sealed class WarehouseTests
{
    private const long TenantId = 42L;
    private const string Operator = "user-9";

    /// <summary>测试夹具：SQLite 库 + 仓库服务 + 跨模块查询（全部桩依赖）。</summary>
    private sealed class Harness
    {
        public required WmsTenantDbContext Db { get; init; }

        public required WarehouseService Service { get; init; }

        public required WarehouseQueryService Query { get; init; }

        public required InMemoryAuditService Audit { get; init; }

        public required StubStockQueryService Stock { get; init; }

        public required BasStubApprovalService Approval { get; init; }
    }

    private static Harness Build(
        SqliteConnection? connection = null,
        long tenantId = TenantId,
        bool ensureCreated = true,
        bool hasStock = false)
    {
        SqliteConnection ownedConnection = connection ?? new SqliteConnection("DataSource=:memory:");
        if (connection is null || ownedConnection.State != ConnectionState.Open)
        {
            ownedConnection.Open();
        }

        var db = new WmsTenantDbContext(
            new JobStubTenantDatabaseResolver(),
            tenantId,
            new DbContextOptionsBuilder<WmsTenantDbContext>().UseSqlite(ownedConnection).Options);
        if (ensureCreated)
        {
            _ = db.Database.EnsureCreated();
        }

        var repo = new WmsTenantRepository(db);
        var cache = new MemoryCacheService(Options.Create(new CacheOptions()));
        var audit = new InMemoryAuditService();
        var stock = new StubStockQueryService { HasStock = hasStock };
        var approval = new BasStubApprovalService();
        var tenantCtx = new TestTenantContext(tenantId);

        return new Harness
        {
            Db = db,
            Audit = audit,
            Stock = stock,
            Approval = approval,
            Service = new WarehouseService(
                tenantCtx,
                repo,
                new StubDocumentNumberGenerator(),
                new StubOrgQueryService(),
                approval,
                stock,
                cache,
                audit,
                new BasStubSnowflakeIdGenerator(),
                new TestCurrentUserContext(Operator)),
            Query = new WarehouseQueryService(tenantCtx, repo, cache),
        };
    }

    private static Task<WarehouseRow> CreateWarehouseAsync(
        Harness h, string name, int whType = 1, long? orgId = null)
        => h.Service.CreateAsync(
            new CreateWarehousePayload(name, whType, orgId, null, null, null),
            TestContext.Current.CancellationToken);

    // ============ 编码：后端生成且前端不传码（N-1） ============

    [Fact]
    public async Task CreateWarehouseGeneratesBackendCodePerOrgAndUnique()
    {
        Harness h = Build();

        WarehouseRow first = await CreateWarehouseAsync(h, "一号仓", whType: 1, orgId: 9001L);
        WarehouseRow second = await CreateWarehouseAsync(h, "二号仓", whType: 2);

        // 组织编码参与仓库编码；未绑组织回落 SYS；流水由发号器递增
        Assert.StartsWith("WH-HQ-", first.Code, StringComparison.Ordinal);
        Assert.StartsWith("WH-SYS-", second.Code, StringComparison.Ordinal);
        Assert.NotEqual(first.Code, second.Code, StringComparer.Ordinal);

        // 新建审计留痕
        Assert.Contains(
            h.Audit.GetCapturedChanges(),
            e => string.Equals(e.EntityType, nameof(Warehouse), StringComparison.Ordinal)
                 && string.Equals(e.Operation, "INSERT", StringComparison.Ordinal)
                 && string.Equals(e.ChangedBy, Operator, StringComparison.Ordinal));
    }

    // ============ 缓存：写后按前缀失效（C-5） ============

    [Fact]
    public async Task CreateWarehouseInvalidatesListCachesOfServiceAndQuery()
    {
        Harness h = Build();

        // 先读穿建立两份缓存（主数据列表 + 跨模块查询列表）
        Assert.Empty(await h.Service.ListAsync(TestContext.Current.CancellationToken));
        Assert.Empty(await h.Query.ListAllAsync(TestContext.Current.CancellationToken));

        WarehouseRow created = await CreateWarehouseAsync(h, "一号仓");

        // 写路径只删缓存：后续读重建，两个缓存键均已失效
        WarehouseRow listed = Assert.Single(await h.Service.ListAsync(TestContext.Current.CancellationToken));
        WarehouseQueryRow queryRow = Assert.Single(await h.Query.ListAllAsync(TestContext.Current.CancellationToken));
        Assert.Equal(created.Id, listed.Id);
        Assert.Equal(created.Id, queryRow.Id);
    }

    // ============ 停用前置：有库存阻断（V-1-2） ============

    [Fact]
    public async Task DisablingWarehouseWithStockIsBlocked()
    {
        Harness h = Build(hasStock: true);
        WarehouseRow warehouse = await CreateWarehouseAsync(h, "一号仓");

        ConflictException ex = await Assert.ThrowsAsync<ConflictException>(
            () => h.Service.ToggleAsync(warehouse.Id, TestContext.Current.CancellationToken));

        Assert.Equal("WMS_WAREHOUSE_HAS_STOCK", ex.ErrorCode);

        // 停用被阻断：状态保持启用
        WarehouseRow reloaded = await h.Service.GetAsync(warehouse.Id, TestContext.Current.CancellationToken);
        Assert.Equal((int)WarehouseStatus.Enabled, reloaded.Status);
    }

    [Fact]
    public async Task DisablingWarehouseStagesApprovalThenAppliesAndEnablingIsImmediate()
    {
        Harness h = Build();
        WarehouseRow warehouse = await CreateWarehouseAsync(h, "一号仓");

        // 先读穿缓存详情，停用审批通过后写路径失效
        _ = await h.Service.GetAsync(warehouse.Id, TestContext.Current.CancellationToken);

        // 停用：前置通过 → 转审批挂起，状态保持启用，仅回传挂起标记
        WarehouseRow staged = await h.Service.ToggleAsync(
            warehouse.Id, TestContext.Current.CancellationToken);
        Assert.True(staged.ApprovalRequired);
        Assert.Equal((int)WarehouseStatus.Enabled, staged.Status);
        Assert.Equal(
            WarehouseService.WarehouseDisableFlowCode,
            Assert.Single(h.Approval.Started).ApprovalDefinitionCode);

        // 审批通过 → Applier 落地停用（幂等）
        await h.Service.ApplyDisableApprovalAsync(
            warehouse.Id, approved: true, TestContext.Current.CancellationToken);
        Assert.Equal(
            (int)WarehouseStatus.Disabled,
            (await h.Service.GetAsync(warehouse.Id, TestContext.Current.CancellationToken)).Status);

        // 启用无需审批，直接生效
        WarehouseRow enabled = await h.Service.ToggleAsync(
            warehouse.Id, TestContext.Current.CancellationToken);
        Assert.False(enabled.ApprovalRequired);
        Assert.Equal((int)WarehouseStatus.Enabled, enabled.Status);
    }

    [Fact]
    public async Task DisablingWarehouseWithUnfinishedOperationsIsBlocked()
    {
        Harness h = Build();
        WarehouseRow warehouse = await CreateWarehouseAsync(h, "一号仓");
        h.Db.StockInTasks.Add(new StockInTask(
            TenantId, "RK-T25-001", 1, "SRC-T25-001", warehouse.Id, null));
        _ = await h.Db.SaveChangesAsync(TestContext.Current.CancellationToken);

        ConflictException ex = await Assert.ThrowsAsync<ConflictException>(
            () => h.Service.ToggleAsync(warehouse.Id, TestContext.Current.CancellationToken));

        Assert.Equal("WMS_WAREHOUSE_HAS_OPEN_TASKS", ex.ErrorCode);
        Assert.Empty(h.Approval.Started);
    }

    // ============ 删除前置：子级存在阻断 ============

    [Fact]
    public async Task DeletingWarehouseWithZonesIsBlocked()
    {
        Harness h = Build();
        WarehouseRow warehouse = await CreateWarehouseAsync(h, "一号仓");
        _ = await h.Service.CreateZoneAsync(
            warehouse.Id,
            new CreateZonePayload("A", "A 区", 1),
            TestContext.Current.CancellationToken);

        ConflictException ex = await Assert.ThrowsAsync<ConflictException>(
            () => h.Service.DeleteAsync(warehouse.Id, TestContext.Current.CancellationToken));

        Assert.Equal("WMS_WAREHOUSE_HAS_CHILDREN", ex.ErrorCode);
    }

    // ============ 库区编码：同仓库内唯一 ============

    [Fact]
    public async Task DuplicateZoneCodeInSameWarehouseIsRejected()
    {
        Harness h = Build();
        WarehouseRow warehouse = await CreateWarehouseAsync(h, "一号仓");
        _ = await h.Service.CreateZoneAsync(
            warehouse.Id,
            new CreateZonePayload("A", "A 区", 1),
            TestContext.Current.CancellationToken);

        ConflictException ex = await Assert.ThrowsAsync<ConflictException>(
            () => h.Service.CreateZoneAsync(
                warehouse.Id,
                new CreateZonePayload("A", "重复 A 区", 2),
                TestContext.Current.CancellationToken));

        Assert.Equal("WMS_ZONE_CODE_EXISTS", ex.ErrorCode);
    }

    // ============ 库区/库位删除（子级/在途前置） ============

    [Fact]
    public async Task DeletingZoneWithLocationsIsBlocked()
    {
        Harness h = Build();
        WarehouseRow warehouse = await CreateWarehouseAsync(h, "一号仓");
        ZoneRow zone = await h.Service.CreateZoneAsync(
            warehouse.Id, new CreateZonePayload("A", "A 区", 1), TestContext.Current.CancellationToken);
        _ = await h.Service.CreateLocationAsync(
            warehouse.Id, zone.Id, new CreateLocationPayload("A-01", null, 1),
            TestContext.Current.CancellationToken);

        ConflictException ex = await Assert.ThrowsAsync<ConflictException>(
            () => h.Service.DeleteZoneAsync(zone.Id, TestContext.Current.CancellationToken));

        Assert.Equal("WMS_ZONE_HAS_LOCATIONS", ex.ErrorCode);
    }

    [Fact]
    public async Task DeletingZoneWithoutLocationsSucceedsAndHidesItFromList()
    {
        Harness h = Build();
        WarehouseRow warehouse = await CreateWarehouseAsync(h, "一号仓");
        ZoneRow zone = await h.Service.CreateZoneAsync(
            warehouse.Id, new CreateZonePayload("B", "B 区", 2), TestContext.Current.CancellationToken);

        await h.Service.DeleteZoneAsync(zone.Id, TestContext.Current.CancellationToken);

        Assert.Empty(await h.Service.ListZonesAsync(warehouse.Id, TestContext.Current.CancellationToken));
        Assert.Contains(
            h.Audit.GetCapturedChanges(),
            e => string.Equals(e.EntityType, nameof(WarehouseZone), StringComparison.Ordinal)
                 && string.Equals(e.Operation, "SOFT_DELETE", StringComparison.Ordinal));
    }

    [Fact]
    public async Task DeletingLocationReferencedByOpenTaskIsBlocked()
    {
        Harness h = Build();
        WarehouseRow warehouse = await CreateWarehouseAsync(h, "一号仓");
        ZoneRow zone = await h.Service.CreateZoneAsync(
            warehouse.Id, new CreateZonePayload("A", "A 区", 1), TestContext.Current.CancellationToken);
        LocationRow location = await h.Service.CreateLocationAsync(
            warehouse.Id, zone.Id, new CreateLocationPayload("A-01", null, 1),
            TestContext.Current.CancellationToken);

        StockInTask task = new(TenantId, "RK-T25-002", 1, "SRC-T25-002", warehouse.Id, null);
        task.AddLine(new StockInTaskLine(TenantId, 1, 7001L, 1m, location.Id, null, 1m));
        h.Db.StockInTasks.Add(task);
        _ = await h.Db.SaveChangesAsync(TestContext.Current.CancellationToken);

        ConflictException ex = await Assert.ThrowsAsync<ConflictException>(
            () => h.Service.DeleteLocationAsync(location.Id, TestContext.Current.CancellationToken));

        Assert.Equal("WMS_LOCATION_HAS_OPEN_TASKS", ex.ErrorCode);
    }

    [Fact]
    public async Task DeletingLocationWithoutOpenTasksSucceedsAndHidesItFromList()
    {
        Harness h = Build();
        WarehouseRow warehouse = await CreateWarehouseAsync(h, "一号仓");
        ZoneRow zone = await h.Service.CreateZoneAsync(
            warehouse.Id, new CreateZonePayload("A", "A 区", 1), TestContext.Current.CancellationToken);
        LocationRow location = await h.Service.CreateLocationAsync(
            warehouse.Id, zone.Id, new CreateLocationPayload("A-01", null, 1),
            TestContext.Current.CancellationToken);

        await h.Service.DeleteLocationAsync(location.Id, TestContext.Current.CancellationToken);

        Assert.Empty(await h.Service.ListLocationsAsync(
            warehouse.Id, TestContext.Current.CancellationToken));
    }

    // ============ 长度/范围校验（V-1：400 而非落库 500） ============

    [Fact]
    public async Task WarehouseNameLongerThanColumnIsRejected()
    {
        Harness h = Build();

        ValidationException ex = await Assert.ThrowsAsync<ValidationException>(
            () => h.Service.CreateAsync(
                new CreateWarehousePayload(new string('仓', 65), 1, null, null, null, null),
                TestContext.Current.CancellationToken));

        Assert.Equal("name", ex.FieldErrors[0].FieldName);
    }

    [Fact]
    public async Task ZoneCodeLongerThanColumnIsRejected()
    {
        Harness h = Build();
        WarehouseRow warehouse = await CreateWarehouseAsync(h, "一号仓");

        ValidationException ex = await Assert.ThrowsAsync<ValidationException>(
            () => h.Service.CreateZoneAsync(
                warehouse.Id,
                new CreateZonePayload(new string('Z', 33), "A 区", 1),
                TestContext.Current.CancellationToken));

        Assert.Equal("code", ex.FieldErrors[0].FieldName);
    }

    [Fact]
    public async Task NegativeSortOrderIsRejected()
    {
        Harness h = Build();
        WarehouseRow warehouse = await CreateWarehouseAsync(h, "一号仓");

        ValidationException ex = await Assert.ThrowsAsync<ValidationException>(
            () => h.Service.CreateZoneAsync(
                warehouse.Id, new CreateZonePayload("A", "A 区", -1),
                TestContext.Current.CancellationToken));

        Assert.Equal("sortOrder", ex.FieldErrors[0].FieldName);
    }

    // ============ 租户隔离 ============

    [Fact]
    public async Task WarehousesOfOtherTenantAreInvisible()
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();

        Harness tenant42 = Build(connection);
        WarehouseRow warehouse = await CreateWarehouseAsync(tenant42, "一号仓");

        // 另一租户上下文（同库不同租户）：列表为空、详情为空、越权读取按不存在处理
        Harness tenant43 = Build(connection, tenantId: 43L, ensureCreated: false);

        Assert.Empty(await tenant43.Service.ListAsync(TestContext.Current.CancellationToken));
        Assert.Null(await tenant43.Query.FindAsync(warehouse.Id, TestContext.Current.CancellationToken));
        _ = await Assert.ThrowsAsync<NotFoundException>(
            () => tenant43.Service.GetAsync(warehouse.Id, TestContext.Current.CancellationToken));
    }
}

/// <summary>单据号生成桩：<c>{prefix}-{orgCode}-202610-{seq:000000}</c>，流水单调递增。</summary>
internal sealed class StubDocumentNumberGenerator : IDocumentNumberGenerator
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

/// <summary>组织只读查询桩：仅 9001 号组织存在（编码 HQ），其余返回 null。</summary>
internal sealed class StubOrgQueryService : IOrgQueryService
{
    private const long KnownOrgId = 9001L;

    public Task<IReadOnlyList<OrgQueryRow>> ListAllAsync(CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<OrgQueryRow>>(
            [new OrgQueryRow(KnownOrgId, null, "HQ", "总部", 1, 1, 1)]);

    public Task<OrgQueryRow?> FindAsync(long id, CancellationToken cancellationToken)
        => Task.FromResult(id == KnownOrgId
            ? new OrgQueryRow(KnownOrgId, null, "HQ", "总部", 1, 1, 1)
            : (OrgQueryRow?)null);
}

/// <summary>库存只读查询桩：<see cref="HasStock"/> 参与停用前置分支，<see cref="Batches"/> 供 FEFO 用例配置。</summary>
internal sealed class StubStockQueryService : IStockQueryService
{
    public bool HasStock { get; set; }

    public List<StockBatchRow> Batches { get; } = [];

    public Task<bool> HasStockInWarehouseAsync(long warehouseId, CancellationToken cancellationToken)
        => Task.FromResult(HasStock);

    public Task<StockBalanceRow?> FindBalanceAsync(long itemId, long warehouseId, CancellationToken cancellationToken)
        => Task.FromResult<StockBalanceRow?>(null);

    public Task<IReadOnlyList<StockBalanceRow>> ListBalancesByItemAsync(long itemId, CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<StockBalanceRow>>([]);

    public Task<IReadOnlyList<StockBalanceRow>> ListBalancesByWarehouseAsync(long warehouseId, CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<StockBalanceRow>>([]);

    public Task<IReadOnlyList<StockBatchRow>> ListBatchesAsync(long itemId, long warehouseId, CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<StockBatchRow>>(
            [.. Batches.Where(b => b.ItemId == itemId && b.WarehouseId == warehouseId)]);
}
