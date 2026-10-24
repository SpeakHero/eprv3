/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-21 23:59:00 +08:00 中国标准时间 星期三
 * 修改时间：2026-10-21 23:59:00 +08:00 中国标准时间 星期三
 * 描述：生产版租户数据库路由解析器测试：共享/独立/未命中三分支 + 路由行模式优先 + 统一刷新
 */

using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Qlx.Abstractions.Constants;
using Qlx.Abstractions.Tenancy;
using Qlx.BuildingBlocks.MultiTenancy;
using Qlx.Modules.Portal;
using Qlx.Modules.Portal.Domain.Tenancy;
using Qlx.Modules.Portal.Services;

namespace Qlx.BuildingBlocks.Tests.Portal;

/// <summary>
/// <see cref="PortalTenantDatabaseResolver"/> 双分支预载测试（B1-01 / B1-04）。
/// 使用 Sqlite 内存门户库 + 临时 Data Protection 密钥，验证共享连接来自配置、独立连接解密凭据。
/// </summary>
public sealed class PortalTenantDatabaseResolverTests
{
    private const string SharedDbName = "qlx_tenant_shared";

    private static readonly SharedDatabaseOptions SharedOptions = new()
    {
        DbHost = "(localdb)",
        DbPort = 1433,
        DbName = SharedDbName,
        DbUser = string.Empty,
        DbPassword = string.Empty,
        ConnectTimeoutSeconds = 15,
        MinPoolSize = 3,
        MaxPoolSize = 220,
    };

    [Fact]
    public async Task PreloadResolvesSharedTenantFromOptionsAndIgnoresRowCredentials()
    {
        await using var scope = await ResolverScope.CreateAsync();
        var protector = scope.CreateProtector();

        // 共享行即使残留凭据也应被忽略（连接统一来自 SharedDatabaseOptions）
        await scope.SeedAsync(CreateTenant(
            id: 101,
            code: "shared-a",
            mode: TenantDatabaseMode.Shared,
            dbName: SharedDbName,
            encryptedUser: protector.Protect("row-user"),
            encryptedPassword: protector.Protect("row-secret"),
            activate: false));

        await scope.Resolver.PreloadAsync(CancellationToken.None);
        var info = scope.Resolver.Resolve(101);

        Assert.True(info.IsShared);
        Assert.Equal(SharedDbName, info.DbName);
        Assert.Equal(string.Empty, info.DbUser);
        Assert.Equal(string.Empty, info.DbPassword);
        Assert.Equal(15, info.ConnectTimeoutSeconds);
        Assert.Equal(220, info.MaxPoolSize);
    }

    [Fact]
    public async Task PreloadDecryptsIsolatedCredentials()
    {
        await using var scope = await ResolverScope.CreateAsync();
        var protector = scope.CreateProtector();

        await scope.SeedAsync(CreateTenant(
            id: 201,
            code: "isolated-b",
            mode: TenantDatabaseMode.Isolated,
            dbName: "qlx_tenant_isolated_b",
            encryptedUser: protector.Protect("db_user_b"),
            encryptedPassword: protector.Protect("db_pass_b"),
            activate: true));

        await scope.Resolver.PreloadAsync(CancellationToken.None);
        var info = scope.Resolver.Resolve(201);

        Assert.False(info.IsShared);
        Assert.Equal("qlx_tenant_isolated_b", info.DbName);
        Assert.Equal("db_user_b", info.DbUser);
        Assert.Equal("db_pass_b", info.DbPassword);
    }

    [Fact]
    public async Task RoutingRowModeWinsOverTenantAggregateField()
    {
        await using var scope = await ResolverScope.CreateAsync();
        var protector = scope.CreateProtector();

        // 租户聚合仍为默认 Shared，但路由行为 Isolated（升级过渡态）：按路由行判定
        await scope.SeedAsync(CreateTenant(
            id: 301,
            code: "row-isolated",
            mode: TenantDatabaseMode.Isolated,
            dbName: "qlx_tenant_row_isolated",
            encryptedUser: protector.Protect("row_user"),
            encryptedPassword: protector.Protect("row_pass"),
            activate: true));

        await scope.Resolver.PreloadAsync(CancellationToken.None);
        var info = scope.Resolver.Resolve(301);

        Assert.False(info.IsShared);
        Assert.Equal("row_user", info.DbUser);
    }

    [Fact]
    public async Task PreloadSkipsDisabledIsolatedTenant()
    {
        await using var scope = await ResolverScope.CreateAsync();
        var protector = scope.CreateProtector();

        await scope.SeedAsync(CreateTenant(
            id: 401,
            code: "disabled-c",
            mode: TenantDatabaseMode.Isolated,
            dbName: "qlx_tenant_disabled_c",
            encryptedUser: protector.Protect("db_user_c"),
            encryptedPassword: protector.Protect("db_pass_c"),
            activate: true,
            disableAfterActivate: true));

        await scope.Resolver.PreloadAsync(CancellationToken.None);

        Assert.Throws<InvalidOperationException>(() => scope.Resolver.Resolve(401));
    }

    [Fact]
    public async Task ResolveThrowsWhenTenantNotCached()
    {
        await using var scope = await ResolverScope.CreateAsync();

        await scope.Resolver.PreloadAsync(CancellationToken.None);

        Assert.Throws<InvalidOperationException>(() => scope.Resolver.Resolve(999_999));
    }

    [Fact]
    public async Task RefreshAsyncInvalidatesAndLoadsNewTenant()
    {
        await using var scope = await ResolverScope.CreateAsync();
        ITenantDatabaseResolver resolver = scope.Resolver;

        await resolver.PreloadAsync(CancellationToken.None);
        Assert.Throws<InvalidOperationException>(() => resolver.Resolve(501));

        await scope.SeedAsync(CreateTenant(
            id: 501,
            code: "shared-new",
            mode: TenantDatabaseMode.Shared,
            dbName: SharedDbName,
            encryptedUser: null,
            encryptedPassword: null,
            activate: false));

        await resolver.RefreshAsync(501, CancellationToken.None);

        var info = resolver.Resolve(501);
        Assert.True(info.IsShared);
        Assert.Equal(SharedDbName, info.DbName);
    }

    private static Tenant CreateTenant(
        long id,
        string code,
        TenantDatabaseMode mode,
        string dbName,
        string? encryptedUser,
        string? encryptedPassword,
        bool activate,
        bool disableAfterActivate = false)
    {
        var tenant = new Tenant(code, $"测试租户 {code}");
        tenant.AssignId(id);

        var database = new TenantDatabase(tenant, mode, "(localdb)", 1433, dbName);
        database.AssignId(id + 10_000);

        if (!string.IsNullOrEmpty(encryptedUser) && !string.IsNullOrEmpty(encryptedPassword))
        {
            database.SetEncryptedCredentials(encryptedUser, encryptedPassword);
        }

        tenant.SetDatabase(database);

        if (activate)
        {
            tenant.Activate();
            if (disableAfterActivate)
            {
                tenant.Disable();
            }
        }

        return tenant;
    }

    /// <summary>Sqlite 门户库测试作用域（连接、工厂、解析器）。</summary>
    private sealed class ResolverScope : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;

        private readonly IDataProtectionProvider _protection;

        private ResolverScope(
            SqliteConnection connection,
            IDbContextFactory<PortalDbContext> factory,
            PortalTenantDatabaseResolver resolver,
            IDataProtectionProvider protection)
        {
            _connection = connection;
            Factory = factory;
            Resolver = resolver;
            _protection = protection;
        }

        public IDbContextFactory<PortalDbContext> Factory { get; }

        public PortalTenantDatabaseResolver Resolver { get; }

        /// <summary>使用与解析器相同的密钥创建加密器（测试独立库凭据）。</summary>
        public IDataProtector CreateProtector()
            => _protection.CreateProtector(QlxDataProtectionPurposes.TenantDatabaseCredentials);

        public static async Task<ResolverScope> CreateAsync()
        {
            var connection = new SqliteConnection("DataSource=:memory:");
            await connection.OpenAsync();
            var factory = new SqlitePortalDbContextFactory(connection);

            await using (var db = factory.CreateDbContext())
            {
                await db.Database.EnsureCreatedAsync();
            }

            var protection = new EphemeralDataProtectionProvider();
            var resolver = new PortalTenantDatabaseResolver(
                factory,
                protection,
                Options.Create(SharedOptions));

            return new ResolverScope(connection, factory, resolver, protection);
        }

        public async Task SeedAsync(Tenant tenant)
        {
            await using var db = Factory.CreateDbContext();
            db.Tenants.Add(tenant);
            await db.SaveChangesAsync();
        }

        public async ValueTask DisposeAsync()
        {
            await _connection.DisposeAsync();
        }
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
