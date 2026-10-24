/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-24
 * 修改时间：2026-10-24
 * 描述：MenuQueryService 单元测试（阶段 R-3）：门户全量下发 / 租户许可裁剪 / 读穿缓存命中
 */

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Qlx.Abstractions.Modules;
using Qlx.BuildingBlocks.Caching;
using Qlx.Modules.Portal;
using Qlx.Modules.Portal.Domain.Tenancy;
using Qlx.Modules.Portal.Services;

namespace Qlx.BuildingBlocks.Tests.Portal;

/// <summary>
/// 菜单扁平查询测试：验证后端只投影不组装、租户级按模块许可裁剪，且读路径走缓存（C-2）。
/// </summary>
public sealed class TenantMenuQueryTests
{
    [Fact]
    public async Task GetPortalReturnsAllVisibleMenusAndNavGroups()
    {
        await using var scope = await RepositoryScope.CreateAsync();
        await scope.SeedMenuAsync("bas.admin.org", "bas", "组织机构", visible: true);
        await scope.SeedMenuAsync("bas.admin.hidden", "bas", "隐藏菜单", visible: false);
        await scope.SeedNavGroupAsync("workbench", "工作台", "portal,bas", 0);

        using var cache = CreateCache();
        var service = new MenuQueryService(scope.Factory, cache, new StubLicenseService(["bas"]));

        var snapshot = await service.GetPortalAsync(TestContext.Current.CancellationToken);

        Assert.Single(snapshot.Menus);
        Assert.Equal("bas.admin.org", snapshot.Menus[0].MenuCode);
        Assert.Single(snapshot.NavGroups);
    }

    [Fact]
    public async Task GetTenantFiltersMenusByLicensedModules()
    {
        await using var scope = await RepositoryScope.CreateAsync();
        await scope.SeedMenuAsync("bas.admin.org", "bas", "组织机构", visible: true);
        await scope.SeedMenuAsync("sal.order.list", "sal", "销售订单", visible: true);
        await scope.SeedNavGroupAsync("workbench", "工作台", "portal,bas", 0);
        await scope.SeedNavGroupAsync("business", "业务管理", "sal", 1);

        using var cache = CreateCache();
        var license = new StubLicenseService(["bas"]);
        var service = new MenuQueryService(scope.Factory, cache, license);

        var snapshot = await service.GetAsync(9L, TestContext.Current.CancellationToken);

        Assert.Single(snapshot.Menus);
        Assert.Equal("bas.admin.org", snapshot.Menus[0].MenuCode);
        Assert.Single(snapshot.NavGroups);
        Assert.Equal("workbench", snapshot.NavGroups[0].GroupCode);
    }

    [Fact]
    public async Task GetTenantReusesCacheOnSecondCall()
    {
        await using var scope = await RepositoryScope.CreateAsync();
        await scope.SeedMenuAsync("bas.admin.org", "bas", "组织机构", visible: true);
        await scope.SeedNavGroupAsync("workbench", "工作台", "bas", 0);

        using var cache = CreateCache();
        var license = new StubLicenseService(["bas"]);
        var service = new MenuQueryService(scope.Factory, cache, license);

        await service.GetAsync(11L, TestContext.Current.CancellationToken);
        var firstCallCount = license.CallCount;
        await service.GetAsync(11L, TestContext.Current.CancellationToken);

        Assert.Equal(1, firstCallCount);
        Assert.Equal(firstCallCount, license.CallCount);
    }

    /// <summary>创建内存缓存服务（TTL 抖动等提供方行为由既有 Caching 测试覆盖）。</summary>
    private static MemoryCacheService CreateCache()
        => new(Options.Create(new CacheOptions()));

    /// <summary>桩许可服务：仅返回预设许可模块，并统计调用次数用于缓存断言。</summary>
    private sealed class StubLicenseService(HashSet<string> licensedModules) : IModuleLicenseService
    {
        public int CallCount { get; private set; }

        public Task<bool> HasLicenseAsync(long tenantId, string moduleCode, CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(licensedModules.Contains(moduleCode));
        }
    }

    /// <summary>Sqlite 门户库测试作用域（复用既有仓储测试的连通方式）。</summary>
    private sealed class RepositoryScope : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;

        private RepositoryScope(SqliteConnection connection, IDbContextFactory<PortalDbContext> factory)
        {
            _connection = connection;
            Factory = factory;
        }

        public IDbContextFactory<PortalDbContext> Factory { get; }

        public static async Task<RepositoryScope> CreateAsync()
        {
            var connection = new SqliteConnection("DataSource=:memory:");
            await connection.OpenAsync();
            var factory = new SqlitePortalDbContextFactory(connection);

            await using (var db = factory.CreateDbContext())
            {
                await db.Database.EnsureCreatedAsync();
            }

            return new RepositoryScope(connection, factory);
        }

        public async Task SeedMenuAsync(string menuCode, string moduleCode, string displayName, bool visible)
        {
            await using var db = Factory.CreateDbContext();
            var menu = new ModuleMenu
            {
                ModuleCode = moduleCode,
                MenuCode = menuCode,
                DisplayName = displayName,
                Route = $"/{menuCode}",
                IsVisible = visible,
                SortOrder = 10,
            };
            menu.AssignId(menuCode.GetHashCode(StringComparison.Ordinal) & 0x7FFFFFFFL);
            db.ModuleMenus.Add(menu);
            await db.SaveChangesAsync();
        }

        public async Task SeedNavGroupAsync(string groupCode, string groupName, string moduleCodes, int sortOrder)
        {
            await using var db = Factory.CreateDbContext();
            var group = new NavigationGroup
            {
                GroupCode = groupCode,
                GroupName = groupName,
                ModuleCodes = moduleCodes,
                SortOrder = sortOrder,
            };
            group.AssignId(groupCode.GetHashCode(StringComparison.Ordinal) & 0x7FFFFFFFL);
            db.NavigationGroups.Add(group);
            await db.SaveChangesAsync();
        }

        public async ValueTask DisposeAsync() => await _connection.DisposeAsync();
    }

    /// <summary>基于同一 Sqlite 连接的门户库上下文工厂。</summary>
    private sealed class SqlitePortalDbContextFactory(SqliteConnection connection)
        : IDbContextFactory<PortalDbContext>
    {
        public PortalDbContext CreateDbContext()
            => new(new DbContextOptionsBuilder<PortalDbContext>()
                .UseSqlite(connection)
                .Options);

        public Task<PortalDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(CreateDbContext());
        }
    }
}
