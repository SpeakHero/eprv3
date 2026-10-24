/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-20 23:00:00 +08:00 中国标准时间 星期二
 * 修改时间：2026-10-22 16:30:00 +08:00 中国标准时间 星期四
 * 描述：门户管理库 DbContext，固定连接串；与 TenantDbContext（动态路由租户库）是两套独立体系
 */

using Microsoft.EntityFrameworkCore;
using Qlx.Abstractions.Domain;

namespace Qlx.Modules.Portal;

/// <summary>
/// 门户管理库 DbContext。承载租户注册、许可、模块定义等门户侧全局元数据，
/// 使用固定连接串（<c>config/portal/portal.json</c> 的 PortalConnectionString），
/// 与业务模块使用的 TenantDbContext 动态路由体系完全独立。
/// <para>本类按职责拆分为多个 partial 文件：主文件仅保留建模编排与保存行为，
/// DbSet 集合见 <c>PortalDbContext.DbSets.cs</c>，种子数据见 <c>PortalDbContext.SeedData.cs</c>，
/// 复杂映射见 <c>Configurations/</c> 目录。</para>
/// </summary>
/// <remarks>
/// 构造函数。拦截器由 AddDbContextFactory 注册阶段通过 DbContextOptionsBuilder.AddInterceptors 注入——
/// 不在 OnConfiguring 里用 <c>this</c> 拿 IServiceProvider，避免 EnsureCreatedAsync/MigrateAsync 触发时
/// DbContext 仍在配置中导致递归异常。
/// </remarks>
public sealed partial class PortalDbContext(DbContextOptions<PortalDbContext> options) : DbContext(options)
{


    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.HasDefaultSchema(null);

        // 实体映射：常规约束走实体数据注解，复杂关系 / 过滤索引 / OpenIddict 表名走配置类
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(PortalDbContext).Assembly);

        // 雪花主键：所有 long 主键均由应用层生成，禁止数据库自增（公共层统一约定）
        Qlx.BuildingBlocks.MultiTenancy.SnowflakeKeyConvention.Apply(modelBuilder, this);

        // ======= 种子数据：基础设施元数据（固定 ID，用 HasData 写入首次 Migration）=======
        // 注意：此处用固定整数 ID（1-99 范围内的模块定义 / 权限点），
        // 因为 Migration 运行时雪花生成器不可用；租户/许可等环境相关种子
        // 由 PortalSeedService BackgroundService 用正常 SaveChanges 路径 + 雪花拦截器填充。
        SeedModuleDefinitions(modelBuilder);
        SeedPermissionDefinitions(modelBuilder);
    }

    /// <summary>
    /// 保存前补齐审计时间字段。Portal 侧未注入 <c>AuditSaveChangesInterceptor</c>（1A 约定），
    /// 因此在此处统一填充 <see cref="AuditableEntity.CreatedAt"/>/<see cref="AuditableEntity.UpdatedAt"/>，
    /// 避免门户管理端新增/修改记录落库为默认时间（0001-01-01）。
    /// </summary>
    public override int SaveChanges()
    {
        StampAuditTimestamps();
        return base.SaveChanges();
    }

    /// <summary>
    /// 保存前补齐审计时间字段（异步路径）。
    /// </summary>
    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        StampAuditTimestamps();
        return base.SaveChangesAsync(cancellationToken);
    }

    private void StampAuditTimestamps()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var entry in ChangeTracker.Entries<AuditableEntity>())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    if (entry.Entity.CreatedAt == default)
                    {
                        entry.Property(nameof(AuditableEntity.CreatedAt)).CurrentValue = now;
                    }

                    entry.Property(nameof(AuditableEntity.UpdatedAt)).CurrentValue = now;
                    break;
                case EntityState.Modified:
                    entry.Property(nameof(AuditableEntity.UpdatedAt)).CurrentValue = now;
                    break;
            }
        }
    }
}
