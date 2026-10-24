/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-22 20:40:00 +08:00 中国标准时间 星期四
 * 描述：PortalUser 实体 Fluent 配置：可空列过滤唯一索引 + 成员/密码历史一对多关系
 */

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Qlx.Modules.Portal.Domain.Identity;

namespace Qlx.Modules.Portal.Configurations;

/// <summary>
/// 门户用户实体映射配置。表名、登录账号唯一索引与长度约束已由数据注解声明；
/// 手机号 / 邮箱 / 证件号的「过滤唯一索引」与一对多关系无法用数据注解表达，故在此声明。
/// </summary>
public sealed class PortalUserConfiguration : IEntityTypeConfiguration<PortalUser>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<PortalUser> builder)
    {
        // 手机号 / 邮箱 / 证件号是验证码登录的唯一标识：可空列用过滤唯一索引（允许多个 NULL）
        builder.HasIndex(u => u.Phone).IsUnique().HasFilter("[Phone] IS NOT NULL");
        builder.HasIndex(u => u.Email).IsUnique().HasFilter("[Email] IS NOT NULL");
        builder.HasIndex(u => u.IdCard).IsUnique().HasFilter("[IdCard] IS NOT NULL");

        builder.HasMany(u => u.Members)
            .WithOne()
            .HasForeignKey(m => m.PortalUserId)
            .IsRequired();

        builder.HasMany(u => u.PasswordHistories)
            .WithOne()
            .HasForeignKey(h => h.PortalUserId)
            .IsRequired();
    }
}
