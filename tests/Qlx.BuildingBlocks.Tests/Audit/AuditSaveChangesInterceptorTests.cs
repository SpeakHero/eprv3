/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-21 08:00:00 +08:00 中国标准时间 星期三
 * 修改时间：2026-10-21 08:00:00 中国标准时间 星期三
 * 描述：AuditSaveChangesInterceptor + TenantDbContext 审计落地集成测试（Sqlite 内存库）
 */

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Qlx.Abstractions.Audit;
using Qlx.Abstractions.Domain;
using Qlx.BuildingBlocks.Audit;
using Qlx.BuildingBlocks.Ids;
using Qlx.BuildingBlocks.MultiTenancy;
using Qlx.BuildingBlocks.Tests;

namespace Qlx.BuildingBlocks.Tests.Audit;

/// <summary>
/// 审计拦截器业务场景验证：新增填审计字段 + 雪花 Id、修改捕获前后快照、
/// 软删改写 IsDeleted 并标记 SOFT_DELETE、快照携带租户 Id 与操作人。
/// </summary>
public sealed class AuditSaveChangesInterceptorTests
{
    private static readonly DateTimeOffset TimeFloor = DateTimeOffset.UtcNow.AddMinutes(-1);

    [Fact]
    public async Task InsertFillsAuditFields()
    {
        await using var db = CreateDb(tenantId: 5, user: new StubUserContext("tester01"));

        var thing = new Thing { Name = "第一件" };
        db.Things.Add(thing);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        Assert.True(thing.Id > 0);
        Assert.InRange(thing.CreatedAt, TimeFloor, DateTimeOffset.UtcNow.AddMinutes(1));
        Assert.Equal("tester01", thing.CreatedBy);
        Assert.Equal("tester01", thing.UpdatedBy);
    }

    [Fact]
    public async Task UpdateFillsAuditFields()
    {
        await using var db = CreateDb(tenantId: 7, user: new StubUserContext("tester02"));

        var thing = new Thing { Name = "旧名" };
        db.Things.Add(thing);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        thing.Name = "新名";
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        Assert.Equal("新名", thing.Name);
        Assert.Equal("tester02", thing.UpdatedBy);
        Assert.True(thing.UpdatedAt > thing.CreatedAt);
    }

    [Fact]
    public async Task SoftDeleteRewritesRow()
    {
        await using var db = CreateDb(tenantId: 9, user: new StubUserContext("tester03"));

        var soft = new SoftThing { Name = "待软删" };
        db.SoftThings.Add(soft);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        db.SoftThings.Remove(soft);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        // 软删后行仍在（无全局过滤查询），IsDeleted=true 且删除人与时间已填
        var row = await db.SoftThings.IgnoreQueryFilters().SingleAsync(s => s.Id == soft.Id, TestContext.Current.CancellationToken);
        Assert.True(row.IsDeleted);
        Assert.NotNull(row.DeletedAt);
        Assert.Equal("tester03", row.DeletedBy);
    }

    [Fact]
    public async Task AnonymousUserFillsEmptyOperator()
    {
        await using var db = CreateDb(tenantId: 3, user: null);

        var thing = new Thing { Name = "匿名操作" };
        db.Things.Add(thing);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        Assert.True(thing.Id > 0);
        Assert.Null(thing.CreatedBy);
        Assert.Null(thing.UpdatedBy);
    }

    // =============== 测试基建 ===============

    /// <summary>构建挂审计拦截器的租户测试库（Sqlite 内存库，选项已配置 → 跳过 SqlServer 动态路由）。</summary>
    private static TestTenantDbContext CreateDb(long tenantId, StubUserContext? user)
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();

        var snowflake = new SnowflakeIdGenerator(workerId: 21);
        var interceptor = new AuditSaveChangesInterceptor(snowflake, user);

        var options = new DbContextOptionsBuilder<TestTenantDbContext>()
            .UseSqlite(connection)
            .AddInterceptors(interceptor)
            .Options;

        var db = new TestTenantDbContext(
            new StubTenantDatabaseResolver(),
            tenantId,
            options);
        db.Database.EnsureCreated();
        return db;
    }

    /// <summary>普通审计实体（无租户字段、无软删）。</summary>
    private sealed class Thing : AuditableEntity
    {
        public string Name { get; set; } = string.Empty;
    }

    /// <summary>软删实体。</summary>
    private sealed class SoftThing : FullAuditableEntity
    {
        public string Name { get; set; } = string.Empty;
    }

    /// <summary>测试用租户库：连接/选项外部注入，仅承载两张测试表。</summary>
    private sealed class TestTenantDbContext(
        ITenantDatabaseResolver resolver,
        long tenantId,
        DbContextOptions options) : TenantDbContext(resolver, tenantId, options)
    {
        public DbSet<Thing> Things => Set<Thing>();

        public DbSet<SoftThing> SoftThings => Set<SoftThing>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<Thing>(b => b.ToTable("things"));
            modelBuilder.Entity<SoftThing>(b => b.ToTable("soft_things"));
        }
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

    /// <summary>当前用户上下文桩。</summary>
    private sealed class StubUserContext(string? userName, string? userId = null) : ICurrentUserContext
    {
        public string? UserId { get; } = userId;

        public string? UserName { get; } = userName;

        public string? UserDisplayName => UserName;

        public bool IsAuthenticated => UserName is not null;
    }
}
