/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-22 00:05:00 +08:00 中国标准时间 星期四
 * 修改时间：2026-10-22 00:05:00 +08:00 中国标准时间 星期四
 * 描述：租户预配扫描过滤测试：仅独立库且 Provisioning 的租户进入预配
 */

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Qlx.Abstractions.Tenancy;
using Qlx.Modules.Portal;
using Qlx.Modules.Portal.Domain.Tenancy;
using Qlx.Modules.Portal.Services;

namespace Qlx.BuildingBlocks.Tests.Portal;

/// <summary>
/// <see cref="PortalTenantProvisioningRepository"/> 扫描过滤测试（B2-02）：
/// 共享租户不进入预配，避免被误建库。
/// </summary>
public sealed class PortalTenantProvisioningRepositoryTests
{
    [Fact]
    public async Task GetProvisioningTenantIdsOnlyReturnsIsolatedTenants()
    {
        await using var scope = await RepositoryScope.CreateAsync();

        // 共享租户即使处于 Provisioning 也不应被扫描（正常链路不会产生该组合，此处防御性验证）
        await scope.SeedAsync(CreateTenant(101, "shared-pending", TenantDatabaseMode.Shared, active: false));
        await scope.SeedAsync(CreateTenant(201, "isolated-pending", TenantDatabaseMode.Isolated, active: false));
        await scope.SeedAsync(CreateTenant(301, "isolated-active", TenantDatabaseMode.Isolated, active: true));

        await using var db = scope.Factory.CreateDbContext();
        var repo = new PortalTenantProvisioningRepository(db);

        var ids = await repo.GetProvisioningTenantIdsAsync(TestContext.Current.CancellationToken);

        Assert.Equal([201L], ids);
    }

    [Fact]
    public async Task MarkTenantActiveActivatesIsolatedTenant()
    {
        await using var scope = await RepositoryScope.CreateAsync();
        await scope.SeedAsync(CreateTenant(401, "isolated-pending", TenantDatabaseMode.Isolated, active: false));

        await using (var db = scope.Factory.CreateDbContext())
        {
            var repo = new PortalTenantProvisioningRepository(db);
            await repo.MarkTenantActiveAsync(401, TestContext.Current.CancellationToken);
        }

        await using var readDb = scope.Factory.CreateDbContext();
        var tenant = await readDb.Tenants.AsNoTracking()
            .SingleAsync(t => t.Id == 401, TestContext.Current.CancellationToken);
        Assert.Equal(TenantStatus.Active, tenant.Status);
    }

    private static Tenant CreateTenant(long id, string code, TenantDatabaseMode mode, bool active)
    {
        var tenant = new Tenant(code, "演示租户");
        tenant.AssignId(id);
        tenant.SetRegistrationProfile("91510100MA99999999", "standard", null);

        if (mode == TenantDatabaseMode.Isolated)
        {
            tenant.ChangeDatabaseMode(TenantDatabaseMode.Isolated);
        }

        var database = new TenantDatabase(
            tenant, mode, "(localdb)", 1433, "qlx_tenant_" + code.Replace('-', '_'));
        database.AssignId(id + 1);
        tenant.SetDatabase(database);

        if (active)
        {
            tenant.Activate();
        }

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
