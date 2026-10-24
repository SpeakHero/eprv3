/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-22 01:35:00 +08:00 中国标准时间 星期四
 * 描述：TenantIsolationAuditor 单测——模型检查、策略覆盖比对、报告格式化
 */

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Qlx.Abstractions.Domain;
using Qlx.Abstractions.Tenancy;
using Qlx.BuildingBlocks.MultiTenancy;
using Qlx.BuildingBlocks.MultiTenancy.Isolation;

namespace Qlx.BuildingBlocks.Tests.MultiTenancy;

/// <summary>
/// B3-08 单测：巡检器纯逻辑（不依赖真实数据库连接）。
/// </summary>
public sealed class TenantIsolationAuditorTests
{
    [Fact]
    public void InspectModelDetectsMissingTenantId()
    {
        using var fixture = new ModelFixture();
        var model = fixture.BuildModel<MissingTenantDbContext>();

        var findings = new List<TenantIsolationFinding>();
        TenantIsolationAuditor.InspectModel(model, "bas", findings);

        var finding = Assert.Single(findings, static f => f.Severity == TenantIsolationSeverity.Critical);
        Assert.Equal("Model", finding.Category);
        Assert.Contains("缺少 TenantId", finding.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void InspectModelDetectsNonLongTenantId()
    {
        using var fixture = new ModelFixture();
        var model = fixture.BuildPlainModel<WrongTenantTypeDbContext>();

        var findings = new List<TenantIsolationFinding>();
        TenantIsolationAuditor.InspectModel(model, "bas", findings);

        var finding = Assert.Single(findings, static f => f.Severity == TenantIsolationSeverity.Critical);
        Assert.Contains("必须是 long", finding.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void InspectModelDetectsUniqueIndexWithoutTenantId()
    {
        using var fixture = new ModelFixture();
        var model = fixture.BuildModel<BadUniqueIndexDbContext>();

        var findings = new List<TenantIsolationFinding>();
        TenantIsolationAuditor.InspectModel(model, "bas", findings);

        var finding = Assert.Single(findings, static f => f.Severity == TenantIsolationSeverity.Critical);
        Assert.Contains("唯一索引缺少 TenantId", finding.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void InspectModelAcceptsHealthyModel()
    {
        using var fixture = new ModelFixture();
        var model = fixture.BuildModel<HealthyDbContext>();

        var findings = new List<TenantIsolationFinding>();
        TenantIsolationAuditor.InspectModel(model, "bas", findings);

        Assert.DoesNotContain(findings, static f => f.Severity == TenantIsolationSeverity.Critical);
    }

    [Fact]
    public void ComparePolicyCoverageDetectsMissingAndOrphaned()
    {
        var expected = new List<string> { "qlx_rls_bas_org", "qlx_rls_bas_user" };
        var installed = new List<string> { "qlx_rls_bas_org", "qlx_rls_legacy" };

        var findings = new List<TenantIsolationFinding>();
        TenantIsolationAuditor.ComparePolicyCoverage(expected, installed, findings);

        Assert.Equal(2, findings.Count);
        Assert.Contains(findings, static f =>
            f.Severity == TenantIsolationSeverity.Critical
            && f.Target == "qlx_rls_bas_user"
            && f.Message.Contains("策略缺失", StringComparison.Ordinal));
        Assert.Contains(findings, static f =>
            f.Severity == TenantIsolationSeverity.Warning
            && f.Target == "qlx_rls_legacy"
            && f.Message.Contains("孤立", StringComparison.Ordinal));
    }

    [Fact]
    public void ComparePolicyCoverageIsCaseInsensitive()
    {
        var expected = new List<string> { "qlx_rls_bas_org" };
        var installed = new List<string> { "QLX_RLS_BAS_ORG" };

        var findings = new List<TenantIsolationFinding>();
        TenantIsolationAuditor.ComparePolicyCoverage(expected, installed, findings);

        Assert.Empty(findings);
    }

    [Fact]
    public void ReportFormatsReadableText()
    {
        var report = new TenantIsolationAuditReport(
            new DateTimeOffset(2026, 10, 22, 1, 0, 0, TimeSpan.Zero),
            42,
            "qlx_dev_v3",
            "SqlServer",
            [
                new TenantIsolationFinding("Model", "bas:Thing", "实体缺少 TenantId 属性", TenantIsolationSeverity.Critical),
                new TenantIsolationFinding("Policy", "qlx_rls_bas_org", "RLS 策略缺失", TenantIsolationSeverity.Warning),
            ]);

        var text = report.ToReadableText();

        Assert.Contains("租户隔离巡检报告（租户 42）", text, StringComparison.Ordinal);
        Assert.Contains("qlx_dev_v3", text, StringComparison.Ordinal);
        Assert.Contains("SqlServer", text, StringComparison.Ordinal);
        Assert.Contains("[Critical] Model | bas:Thing", text, StringComparison.Ordinal);
        Assert.Contains("[Warning] Policy | qlx_rls_bas_org", text, StringComparison.Ordinal);
        Assert.Contains("存在严重风险", text, StringComparison.Ordinal);
        Assert.True(report.HasCritical);
        Assert.False(report.IsHealthy);
    }

    [Fact]
    public void HealthyReportHasNoFindings()
    {
        var report = new TenantIsolationAuditReport(
            DateTimeOffset.UtcNow,
            7,
            "qlx_dev_v3",
            "SqlServer",
            []);

        Assert.True(report.IsHealthy);
        Assert.False(report.HasCritical);
        Assert.Contains("健康", report.ToReadableText(), StringComparison.Ordinal);
    }

    // =============== 测试基建 ===============

    private sealed class ModelFixture : IDisposable
    {
        private readonly SqliteConnection _connection = new("DataSource=:memory:");

        public ModelFixture()
        {
            _connection.Open();
        }

        public IModel BuildModel<TContext>()
            where TContext : DbContext
        {
            var options = new DbContextOptionsBuilder<TContext>()
                .UseSqlite(_connection)
                .Options;

            using var context = (TContext)Activator.CreateInstance(
                typeof(TContext),
                new StubTenantDatabaseResolver(),
                7L,
                options)!;
            return context.Model;
        }

        public IModel BuildPlainModel<TContext>()
            where TContext : DbContext
        {
            var options = new DbContextOptionsBuilder<TContext>()
                .UseSqlite(_connection)
                .Options;

            using var context = (TContext)Activator.CreateInstance(typeof(TContext), options)!;
            return context.Model;
        }

        public void Dispose() => _connection.Dispose();
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

    // --- 缺 TenantId ---
    private sealed class NoTenantEntity : AuditableEntity
    {
        public string Name { get; set; } = string.Empty;
    }

    private sealed class MissingTenantDbContext(
        ITenantDatabaseResolver resolver,
        long tenantId,
        DbContextOptions options) : TenantDbContext(resolver, tenantId, options)
    {
        public DbSet<NoTenantEntity> Items => Set<NoTenantEntity>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<NoTenantEntity>(b => b.ToTable("items"));
        }
    }

    // --- TenantId 类型错误 ---
    private sealed class WrongTenantEntity : AuditableEntity
    {
        public int TenantId { get; set; }

        public string Name { get; set; } = string.Empty;
    }

    private sealed class WrongTenantTypeDbContext(DbContextOptions<WrongTenantTypeDbContext> options) : DbContext(options)
    {
        public DbSet<WrongTenantEntity> Items => Set<WrongTenantEntity>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<WrongTenantEntity>(b => b.ToTable("items"));
        }
    }

    // --- 唯一索引缺 TenantId ---
    private sealed class BadIndexEntity : AuditableEntity, ITenantEntity
    {
        public long TenantId { get; set; }

        public string Code { get; set; } = string.Empty;
    }

    private sealed class BadUniqueIndexDbContext(
        ITenantDatabaseResolver resolver,
        long tenantId,
        DbContextOptions options) : TenantDbContext(resolver, tenantId, options)
    {
        public DbSet<BadIndexEntity> Items => Set<BadIndexEntity>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<BadIndexEntity>(b =>
            {
                b.ToTable("items");
                b.HasIndex(e => e.Code).IsUnique();
            });
        }
    }

    // --- 健康模型 ---
    private sealed class HealthyEntity : AuditableEntity, ITenantEntity
    {
        public long TenantId { get; set; }

        public string Code { get; set; } = string.Empty;
    }

    private sealed class HealthyDbContext(
        ITenantDatabaseResolver resolver,
        long tenantId,
        DbContextOptions options) : TenantDbContext(resolver, tenantId, options)
    {
        public DbSet<HealthyEntity> Items => Set<HealthyEntity>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<HealthyEntity>(b =>
            {
                b.ToTable("items");
                b.HasIndex(e => new { e.TenantId, e.Code }).IsUnique();
            });
        }
    }
}
