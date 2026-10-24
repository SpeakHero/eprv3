/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-21 23:59:00 +08:00 中国标准时间 星期三
 * 修改时间：2026-10-21 23:59:00 +08:00 中国标准时间 星期三
 * 描述：TenantDbContext 连接解析测试：TenantId>0 走 Resolver、TenantId=0 仅显式启用的开发兜底
 */

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Qlx.BuildingBlocks.MultiTenancy;

namespace Qlx.BuildingBlocks.Tests.MultiTenancy;

/// <summary>
/// <see cref="TenantDbContext"/> 去硬编码后的连接来源测试（B1-03）。
/// </summary>
public sealed class TenantDbContextConnectionFallbackTests
{
    private const string FallbackConnection =
        "Server=(localdb)\\MSSQLLocalDB;Database=qlx_dev_v3;Trusted_Connection=True;TrustServerCertificate=True;";

    [Fact]
    public void PositiveTenantResolvesThroughResolver()
    {
        using var services = BuildServices(new TenantConnectionFallbackOptions());
        var expected = new TenantConnectionInfo(
            77, "sql-host", 1433, "qlx_tenant_77", "user77", "pass77");
        using var db = CreateContext(services, tenantId: 77, new StubResolver(expected));

        var actual = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(
            db.Database.GetConnectionString()!);

        Assert.Equal("sql-host,1433", actual.DataSource);
        Assert.Equal("qlx_tenant_77", actual.InitialCatalog);
        Assert.Equal("user77", actual.UserID);
        Assert.Equal("pass77", actual.Password);
    }

    [Fact]
    public void ZeroTenantUsesEnabledDevelopmentFallback()
    {
        using var services = BuildServices(new TenantConnectionFallbackOptions
        {
            Enabled = true,
            ConnectionString = FallbackConnection,
        });
        using var db = CreateContext(services, tenantId: 0, new StubResolver(null));

        var actual = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(
            db.Database.GetConnectionString()!);

        Assert.Equal(@"(localdb)\MSSQLLocalDB", actual.DataSource);
        Assert.Equal("qlx_dev_v3", actual.InitialCatalog);
        Assert.True(actual.IntegratedSecurity);
    }

    [Fact]
    public void ZeroTenantThrowsWhenFallbackDisabled()
    {
        using var services = BuildServices(new TenantConnectionFallbackOptions { Enabled = false });
        using var db = CreateContext(services, tenantId: 0, new StubResolver(null));

        var ex = Assert.Throws<InvalidOperationException>(() => db.Database.GetConnectionString());
        Assert.Contains(TenantConnectionFallbackOptions.SectionName, ex.Message);
    }

    [Fact]
    public void ZeroTenantIgnoresFallbackOutsideDevelopment()
    {
        using var services = BuildServices(
            new TenantConnectionFallbackOptions
            {
                Enabled = true,
                ConnectionString = FallbackConnection,
            },
            Environments.Production);
        using var db = CreateContext(services, tenantId: 0, new StubResolver(null));

        Assert.Throws<InvalidOperationException>(() => db.Database.GetConnectionString());
    }

    private static ServiceProvider BuildServices(
        TenantConnectionFallbackOptions options,
        string? environmentName = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IOptions<TenantConnectionFallbackOptions>>(Options.Create(options));
        if (environmentName is not null)
        {
            services.AddSingleton<IHostEnvironment>(
                new StubHostEnvironment { EnvironmentName = environmentName });
        }

        return services.BuildServiceProvider();
    }

    private static TestTenantDbContext CreateContext(
        IServiceProvider applicationServices,
        long tenantId,
        ITenantDatabaseResolver resolver)
    {
        var options = new DbContextOptionsBuilder<TestTenantDbContext>()
            .UseApplicationServiceProvider(applicationServices)
            .Options;

        return new TestTenantDbContext(resolver, tenantId, options);
    }

    private sealed class TestTenantDbContext(
        ITenantDatabaseResolver resolver,
        long tenantId,
        DbContextOptions options) : TenantDbContext(resolver, tenantId, options);

    private sealed class StubResolver(TenantConnectionInfo? info) : ITenantDatabaseResolver
    {
        public TenantConnectionInfo Resolve(long tenantId)
            => info ?? throw new InvalidOperationException($"未注册租户 {tenantId}");

        public Task PreloadAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public void Invalidate(long tenantId) { }
    }

    private sealed class StubHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;

        public string ApplicationName { get; set; } = "Qlx.Tests";

        public string ContentRootPath { get; set; } = Directory.GetCurrentDirectory();

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
