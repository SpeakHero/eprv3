/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-24
 * 修改时间：2026-10-24
 * 描述：升级 Cutover 落库测试（R-5/L-02）：数据库模式与路由行必须同事务同步、非法状态拒绝切换
 */

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Qlx.Abstractions.Tenancy;
using Qlx.BuildingBlocks.Tests.Modules;
using Qlx.Modules.Portal;
using Qlx.Modules.Portal.Domain.Tenancy;
using Qlx.Modules.Portal.Services;

namespace Qlx.BuildingBlocks.Tests.Portal;

/// <summary>
/// <see cref="PortalTenantUpgradeRepository.TryCutoverAsync"/> 测试：
/// 切换成功后 <c>tenants.database_mode</c> 与 <c>por_tenant_databases</c> 路由行一致（L-02）。
/// </summary>
public sealed class PortalTenantUpgradeRepositoryTests
{
    [Fact]
    public async Task TryCutoverAsyncSyncsDatabaseModeAndRouteRow()
    {
        await using var scope = await RepositoryScope.CreateAsync();
        await scope.SeedAsync(CreateTenant(501, "upgrade-pending", TenantDatabaseMode.Shared));

        await using (var db = scope.Factory.CreateDbContext())
        {
            var repo = CreateRepository(db);
            var cutover = await repo.TryCutoverAsync(
                501, "db-isolated", 1533, "qlx_tenant_upgrade_pending", TestContext.Current.CancellationToken);
            Assert.True(cutover);
        }

        await using var readDb = scope.Factory.CreateDbContext();
        var tenant = await readDb.Tenants.AsNoTracking()
            .Include(t => t.Database)
            .SingleAsync(t => t.Id == 501, TestContext.Current.CancellationToken);

        Assert.Equal(TenantDatabaseMode.Isolated, tenant.DatabaseMode);
        Assert.NotNull(tenant.Database);
        Assert.Equal("db-isolated", tenant.Database!.DbHost);
        Assert.Equal(1533, tenant.Database.DbPort);
        Assert.Equal("qlx_tenant_upgrade_pending", tenant.Database.DbName);
        Assert.Equal(TenantDatabaseMode.Isolated, tenant.Database.DatabaseMode);
    }

    [Fact]
    public async Task TryCutoverAsyncRejectsAlreadyIsolatedTenant()
    {
        await using var scope = await RepositoryScope.CreateAsync();
        await scope.SeedAsync(CreateTenant(601, "already-isolated", TenantDatabaseMode.Isolated));

        await using var db = scope.Factory.CreateDbContext();
        var repo = CreateRepository(db);

        var cutover = await repo.TryCutoverAsync(
            601, "db-isolated", 1533, "qlx_tenant_already_isolated", TestContext.Current.CancellationToken);

        Assert.False(cutover);
    }

    [Fact]
    public async Task TryCutoverAsyncRejectsUnknownTenant()
    {
        await using var scope = await RepositoryScope.CreateAsync();

        await using var db = scope.Factory.CreateDbContext();
        var repo = CreateRepository(db);

        var cutover = await repo.TryCutoverAsync(
            999, "db-isolated", 1533, "qlx_tenant_missing", TestContext.Current.CancellationToken);

        Assert.False(cutover);
    }

    private static PortalTenantUpgradeRepository CreateRepository(PortalDbContext db)
        => new(db, new StubSnowflakeIdGenerator(), Options.Create(new PortalProvisioningOptions()));

    private static Tenant CreateTenant(long id, string code, TenantDatabaseMode mode)
    {
        var tenant = new Tenant(code, "演示租户");
        tenant.AssignId(id);
        tenant.SetRegistrationProfile("91510100MA99999999", "standard", null);

        if (mode == TenantDatabaseMode.Isolated)
        {
            tenant.ChangeDatabaseMode(TenantDatabaseMode.Isolated);
        }

        var database = new TenantDatabase(tenant, mode, "(localdb)", 1433, "qlx_tenant_" + code.Replace('-', '_'));
        database.AssignId(id + 1);
        tenant.SetDatabase(database);
        return tenant;
    }

    /// <summary>Sqlite 门户库测试作用域。</summary>
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

        public async Task SeedAsync(Tenant tenant)
        {
            await using var db = Factory.CreateDbContext();
            db.Tenants.Add(tenant);
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
