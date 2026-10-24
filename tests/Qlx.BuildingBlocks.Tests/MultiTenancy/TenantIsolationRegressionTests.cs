/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-22 01:00:00 +08:00 中国标准时间 星期四
 * 修改时间：2026-10-22 01:00:00 +08:00 中国标准时间 星期四
 * 描述：B3-07 越权回归用例——共享库场景下跨租户读写隔离与裸 SQL 行为边界
 */

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Qlx.Abstractions.Domain;
using Qlx.Abstractions.Exceptions;
using Qlx.Abstractions.Tenancy;
using Qlx.BuildingBlocks.MultiTenancy;

namespace Qlx.BuildingBlocks.Tests.MultiTenancy;

/// <summary>
/// B3-07 越权回归：共享库（同库同表）场景下，
/// 验证「读空集 / 写 403 / 裸 SQL 行为边界」三条红线。
/// 独立库场景由路由层天然隔离，不在本用例重复。
/// </summary>
public sealed class TenantIsolationRegressionTests
{
    [Fact]
    public void CrossTenantReadReturnsEmptySet()
    {
        using var fixture = new DbFixture();
        SeedTenantData(fixture, tenantId: 7, name: "tenant-a-secret");
        SeedTenantData(fixture, tenantId: 9, name: "tenant-b-public");

        using var dbA = fixture.CreateContext(tenantId: 7);
        var visible = dbA.Things.ToList();

        var item = Assert.Single(visible);
        Assert.Equal(7, item.TenantId);
        Assert.Equal("tenant-a-secret", item.Name);
    }

    [Fact]
    public void CrossTenantInsertIsForbidden()
    {
        using var fixture = new DbFixture();
        using var dbA = fixture.CreateContext(tenantId: 7);

        dbA.Things.Add(new Thing { TenantId = 9, Name = "forged" });

        Assert.Throws<ForbiddenException>(() => dbA.SaveChanges());
        Assert.Equal(0, dbA.Things.IgnoreQueryFilters().Count());
    }

    [Fact]
    public void CrossTenantUpdateIsForbidden()
    {
        using var fixture = new DbFixture();
        SeedTenantData(fixture, tenantId: 9, name: "original");

        using var dbA = fixture.CreateContext(tenantId: 7);
        var row = dbA.Things.IgnoreQueryFilters().Single();
        row.Name = "hijacked";

        Assert.Throws<ForbiddenException>(() => dbA.SaveChanges());
    }

    [Fact]
    public void CrossTenantDeleteIsForbidden()
    {
        using var fixture = new DbFixture();
        SeedTenantData(fixture, tenantId: 9, name: "keep");

        using (var dbA = fixture.CreateContext(tenantId: 7))
        {
            var row = dbA.Things.IgnoreQueryFilters().Single();
            dbA.Things.Remove(row);

            Assert.Throws<ForbiddenException>(() => dbA.SaveChanges());
        }

        using var dbB = fixture.CreateContext(tenantId: 9);
        Assert.Single(dbB.Things.ToList());
    }

    [Fact]
    public void ModelCacheDoesNotFreezeFirstTenantFilter()
    {
        using var fixture = new DbFixture();
        SeedTenantData(fixture, tenantId: 7, name: "seven");
        SeedTenantData(fixture, tenantId: 9, name: "nine");

        using var first = fixture.CreateContext(tenantId: 7);
        Assert.Equal("seven", Assert.Single(first.Things.ToList()).Name);

        using var second = fixture.CreateContext(tenantId: 9);
        Assert.Equal("nine", Assert.Single(second.Things.ToList()).Name);
    }

    [Fact]
    public void RawSqlBypassesEfGlobalFilter()
    {
        using var fixture = new DbFixture();
        SeedTenantData(fixture, tenantId: 7, name: "a");
        SeedTenantData(fixture, tenantId: 9, name: "b");

        using var dbA = fixture.CreateContext(tenantId: 7);

        // EF 全局过滤生效
        Assert.Single(dbA.Things.ToList());

        // 裸 SQL 绕过 EF 过滤器，能看到全部租户数据。
        // 这正是 RLS 存在的意义：SqlServer 集成测试（TenantRlsSqlServerIntegrationTests）会断言裸 SQL 被 RLS 拦截。
        var rawCount = dbA.Database.SqlQueryRaw<long>("SELECT COUNT(*) AS Value FROM things").Single();
        Assert.Equal(2, rawCount);
    }

    // =============== 测试基建 ===============

    private sealed class Thing : AuditableEntity, ITenantEntity
    {
        public long TenantId { get; set; }

        public string Name { get; set; } = string.Empty;
    }

    private sealed class TestTenantDbContext(
        ITenantDatabaseResolver resolver,
        long tenantId,
        DbContextOptions options) : TenantDbContext(resolver, tenantId, options)
    {
        public DbSet<Thing> Things => Set<Thing>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<Thing>(b => b.ToTable("things"));
        }
    }

    private sealed class DbFixture : IDisposable
    {
        private readonly SqliteConnection _connection = new("DataSource=:memory:");

        public DbFixture()
        {
            _connection.Open();
        }

        public TestTenantDbContext CreateContext(long tenantId)
        {
            var interceptor = new TenantWriteGuardInterceptor(
                NullLogger<TenantWriteGuardInterceptor>.Instance);

            var options = new DbContextOptionsBuilder<TestTenantDbContext>()
                .UseSqlite(_connection)
                .AddInterceptors(interceptor)
                .Options;

            var db = new TestTenantDbContext(new StubTenantDatabaseResolver(), tenantId, options);
            db.Database.EnsureCreated();
            return db;
        }

        public void Dispose() => _connection.Dispose();
    }

    private static void SeedTenantData(DbFixture fixture, long tenantId, string name)
    {
        using var seed = fixture.CreateContext(tenantId);
        seed.Things.Add(new Thing { TenantId = tenantId, Name = name });
        seed.SaveChanges();
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
