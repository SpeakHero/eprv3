/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-22 20:40:00 +08:00 中国标准时间 星期四
 * 描述：Tenant 实体 Fluent 配置：一对一数据库路由行 + 一对多模块许可关系
 */

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Qlx.Modules.Portal.Domain.Tenancy;

namespace Qlx.Modules.Portal.Configurations;

/// <summary>
/// 租户实体映射配置。表名、唯一索引与长度约束已由实体数据注解声明，
/// 此处仅补充数据注解无法表达的一对一 / 一对多关系。
/// </summary>
public sealed class TenantConfiguration : IEntityTypeConfiguration<Tenant>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<Tenant> builder)
    {
        builder.HasOne(t => t.Database)
            .WithOne(d => d.Tenant)
            .HasForeignKey<TenantDatabase>(d => d.TenantId)
            .IsRequired();

        builder.HasMany(t => t.Modules)
            .WithOne(m => m.Tenant)
            .HasForeignKey(m => m.TenantId)
            .IsRequired();
    }
}
