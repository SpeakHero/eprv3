/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-22 00:20:00 +08:00 中国标准时间 星期四
 * 修改时间：2026-10-22 00:20:00 +08:00 中国标准时间 星期四
 * 描述：RLS 提供程序与策略目录单测：能力标记、工厂选择、脚本生成、模型推导
 */

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Qlx.Abstractions.Domain;
using Qlx.Abstractions.Tenancy;
using Qlx.BuildingBlocks.MultiTenancy;
using Qlx.BuildingBlocks.MultiTenancy.Rls;

namespace Qlx.BuildingBlocks.Tests.MultiTenancy;

/// <summary>
/// B3-04 / B3-05 单测：RLS 能力标记、提供程序工厂、SQL Server 脚本生成、策略目录推导。
/// </summary>
public sealed class TenantRlsTests
{
    [Fact]
    public void MySqlProviderIsUnsupportedAndDegrades()
    {
        var provider = new MySqlTenantRlsProvider();

        Assert.Equal(RlsCapability.Unsupported, provider.Capability);
        Assert.Empty(provider.BuildInstallStatements([new TenantRlsTable("dbo", "bas_org", "tenant_id")]));
    }

    [Fact]
    public void SkeletonProvidersAreUnsupported()
    {
        Assert.Equal(RlsCapability.Unsupported, new PostgreSqlTenantRlsProvider().Capability);
        Assert.Equal(RlsCapability.Unsupported, new OracleTenantRlsProvider().Capability);
        Assert.Equal(RlsCapability.Unsupported, new NullTenantRlsProvider().Capability);
    }

    [Fact]
    public void FactoryReturnsNullProviderWhenDisabled()
    {
        var provider = TenantRlsProviderFactory.Create(new TenantRlsOptions { Enabled = false, Provider = "SqlServer" });

        Assert.IsType<NullTenantRlsProvider>(provider);
    }

    [Theory]
    [InlineData("SqlServer", typeof(SqlServerTenantRlsProvider))]
    [InlineData("mysql", typeof(MySqlTenantRlsProvider))]
    [InlineData("PostgreSql", typeof(PostgreSqlTenantRlsProvider))]
    [InlineData("Oracle", typeof(OracleTenantRlsProvider))]
    [InlineData("unknown", typeof(NullTenantRlsProvider))]
    public void FactorySelectsProviderByName(string providerName, Type expected)
    {
        var provider = TenantRlsProviderFactory.Create(new TenantRlsOptions { Enabled = true, Provider = providerName });

        Assert.IsType(expected, provider);
    }

    [Fact]
    public void SqlServerInstallStatementsContainFunctionAndPolicyPerTable()
    {
        var provider = new SqlServerTenantRlsProvider();
        var statements = provider.BuildInstallStatements(
        [
            new TenantRlsTable("dbo", "bas_org", "tenant_id"),
            new TenantRlsTable("dbo", "bas_user", "tenant_id"),
        ]);

        Assert.Equal(3, statements.Count);
        Assert.Contains("CREATE OR ALTER FUNCTION [dbo].[fn_qlx_tenant_rls_predicate]", statements[0]);
        Assert.Contains("SESSION_CONTEXT(N'qlx_tenant_id')", statements[0]);
        Assert.Contains("qlx_rls_bas_org", statements[1]);
        Assert.Contains("ADD FILTER PREDICATE", statements[1]);
        Assert.Contains("AFTER INSERT", statements[1]);
        Assert.Contains("AFTER UPDATE", statements[1]);
        Assert.Contains("qlx_rls_bas_user", statements[2]);
    }

    [Fact]
    public void PolicyNameIsSanitized()
    {
        Assert.Equal("qlx_rls_bas_org", SqlServerTenantRlsProvider.BuildPolicyName("bas_org"));
        Assert.Equal("qlx_rls_a_b", SqlServerTenantRlsProvider.BuildPolicyName("a-b"));
    }

    [Fact]
    public void PolicyCatalogPicksOnlyTenantTables()
    {
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();

        var options = new DbContextOptionsBuilder<CatalogDbContext>()
            .UseSqlite(connection)
            .Options;

        using var db = new CatalogDbContext(new StubTenantDatabaseResolver(), 7, options);
        var tables = TenantRlsPolicyCatalog.Build(db.Model);

        var table = Assert.Single(tables);
        Assert.Equal("things", table.TableName);
        Assert.Equal("tenant_id", table.TenantColumn);
    }

    // =============== 测试基建 ===============

    private sealed class Thing : AuditableEntity, ITenantEntity
    {
        public long TenantId { get; set; }

        public string Name { get; set; } = string.Empty;
    }

    private sealed class Plain : AuditableEntity
    {
        public string Name { get; set; } = string.Empty;
    }

    private sealed class CatalogDbContext(
        ITenantDatabaseResolver resolver,
        long tenantId,
        DbContextOptions options) : TenantDbContext(resolver, tenantId, options)
    {
        public DbSet<Thing> Things => Set<Thing>();

        public DbSet<Plain> Plains => Set<Plain>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<Thing>(b => b.ToTable("things"));
            modelBuilder.Entity<Plain>(b => b.ToTable("plains"));
        }
    }

    private sealed class StubTenantDatabaseResolver : ITenantDatabaseResolver
    {
        public TenantConnectionInfo Resolve(long tenantId)
            => new(TenantId: tenantId, DbHost: "stub", DbPort: 0, DbName: "stub", DbUser: string.Empty, DbPassword: string.Empty);

        public Task PreloadAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public void Invalidate(long tenantId)
        {
        }
    }
}
