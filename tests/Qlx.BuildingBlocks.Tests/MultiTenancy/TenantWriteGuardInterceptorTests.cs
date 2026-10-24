/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-21 00:40:00 +08:00 中国标准时间 星期三
 * 修改时间：2026-10-21 00:40:00 +08:00 中国标准时间 星期三
 * 描述：租户写入守卫测试：新增自动填充、篡改拒绝、跨租户改删拒绝、批量整批拒绝、无租户上下文
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
/// <see cref="TenantWriteGuardInterceptor"/> 行为测试（B3-01）。
/// 使用 Sqlite 内存库验证 L1 写入侧隔离：填充、禁止篡改、拒绝跨租户修改/删除、整批回滚。
/// </summary>
public sealed class TenantWriteGuardInterceptorTests
{
    [Fact]
    public void AddedEntityWithoutTenantIdIsFilledWithCurrentTenant()
    {
        using var fixture = new DbFixture();
        using var db = fixture.CreateContext(tenantId: 7);

        db.Things.Add(new Thing { TenantId = 0, Name = "auto" });
        db.SaveChanges();

        Assert.Equal(7, db.Things.Single().TenantId);
    }

    [Fact]
    public void AddedEntityWithOtherTenantIdIsRejected()
    {
        using var fixture = new DbFixture();
        using var db = fixture.CreateContext(tenantId: 7);

        db.Things.Add(new Thing { TenantId = 8, Name = "cross" });

        Assert.Throws<ForbiddenException>(() => db.SaveChanges());
        Assert.Equal(0, db.Things.IgnoreQueryFilters().Count());
    }

    [Fact]
    public void ModifyingTenantIdIsRejected()
    {
        using var fixture = new DbFixture();
        using (var seed = fixture.CreateContext(tenantId: 7))
        {
            seed.Things.Add(new Thing { TenantId = 7, Name = "own" });
            seed.SaveChanges();
        }

        using var db = fixture.CreateContext(tenantId: 7);
        var thing = db.Things.Single();
        thing.TenantId = 8;

        Assert.Throws<ForbiddenException>(() => db.SaveChanges());
    }

    [Fact]
    public void UpdatingRowOfAnotherTenantIsRejected()
    {
        using var fixture = new DbFixture();
        using (var seed = fixture.CreateContext(tenantId: 9))
        {
            seed.Things.Add(new Thing { TenantId = 9, Name = "other" });
            seed.SaveChanges();
        }

        using var db = fixture.CreateContext(tenantId: 7);
        var thing = db.Things.IgnoreQueryFilters().Single();
        thing.Name = "hijacked";

        Assert.Throws<ForbiddenException>(() => db.SaveChanges());
    }

    [Fact]
    public void DeletingRowOfAnotherTenantIsRejected()
    {
        using var fixture = new DbFixture();
        using (var seed = fixture.CreateContext(tenantId: 9))
        {
            seed.Things.Add(new Thing { TenantId = 9, Name = "keep" });
            seed.SaveChanges();
        }

        using (var db = fixture.CreateContext(tenantId: 7))
        {
            var thing = db.Things.IgnoreQueryFilters().Single();
            db.Things.Remove(thing);

            Assert.Throws<ForbiddenException>(() => db.SaveChanges());
        }

        // 行仍存在（整批未落库）
        using var verify = fixture.CreateContext(tenantId: 9);
        var row = Assert.Single(verify.Things.IgnoreQueryFilters().ToList());
        Assert.Equal(9, row.TenantId);
    }

    [Fact]
    public void BatchWithOneViolationRejectsAllChanges()
    {
        using var fixture = new DbFixture();
        using var db = fixture.CreateContext(tenantId: 7);

        db.Things.Add(new Thing { TenantId = 0, Name = "ok" });
        db.Things.Add(new Thing { TenantId = 8, Name = "bad" });

        Assert.Throws<ForbiddenException>(() => db.SaveChanges());
        Assert.Equal(0, db.Things.IgnoreQueryFilters().Count());
    }

    [Fact]
    public void GlobalFilterUsesCurrentContextTenant()
    {
        using var fixture = new DbFixture();
        using (var seed7 = fixture.CreateContext(tenantId: 7))
        {
            seed7.Things.Add(new Thing { TenantId = 7, Name = "a" });
            seed7.SaveChanges();
        }

        using (var seed9 = fixture.CreateContext(tenantId: 9))
        {
            seed9.Things.Add(new Thing { TenantId = 9, Name = "b" });
            seed9.SaveChanges();
        }

        // 同一 DbContext 类型共享 EF 模型：过滤条件必须随当前上下文实例切换租户
        using var db7 = fixture.CreateContext(tenantId: 7);
        Assert.Equal("a", Assert.Single(db7.Things.ToList()).Name);

        using var db9 = fixture.CreateContext(tenantId: 9);
        Assert.Equal("b", Assert.Single(db9.Things.ToList()).Name);
    }

    [Fact]
    public async Task NoTenantContextLeavesEntityUntouched()
    {
        using var fixture = new DbFixture();
        await using var db = fixture.CreateContext(tenantId: 0);

        db.Things.Add(new Thing { TenantId = 0, Name = "system" });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        Assert.Equal(0, db.Things.IgnoreQueryFilters().Single().TenantId);
    }

    // =============== 测试基建 ===============

    /// <summary>租户业务实体（可显式指定 TenantId 以模拟越权写入）。</summary>
    private sealed class Thing : AuditableEntity, ITenantEntity
    {
        public long TenantId { get; set; }

        public string Name { get; set; } = string.Empty;
    }

    /// <summary>测试用租户库：仅承载一张测试表。</summary>
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

    /// <summary>共享同一 Sqlite 内存连接的上下文夹具（多上下文验证同一数据库）。</summary>
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

    /// <summary>租户连接解析桩（测试不触达真实数据库路由）。</summary>
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
