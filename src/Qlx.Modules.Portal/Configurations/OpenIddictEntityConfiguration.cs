/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-22 20:40:00 +08:00 中国标准时间 星期四
 * 描述：OpenIddict 默认实体表名映射（por_oidc_*），实体本身由 UseOpenIddict() 注册
 */

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenIddict.EntityFrameworkCore.Models;

namespace Qlx.Modules.Portal.Configurations;

/// <summary>
/// OpenIddict 7 默认实体（string 主键）映射到 <c>por_oidc_*</c> 表。
/// 实体注册与属性配置由 <c>UseOpenIddict()</c> 注入，此处仅覆写表名与索引——
/// 第三方类型无法添加数据注解，故使用独立配置类。
/// </summary>
public sealed class OpenIddictEntityConfiguration :
    IEntityTypeConfiguration<OpenIddictEntityFrameworkCoreApplication>,
    IEntityTypeConfiguration<OpenIddictEntityFrameworkCoreAuthorization>,
    IEntityTypeConfiguration<OpenIddictEntityFrameworkCoreScope>,
    IEntityTypeConfiguration<OpenIddictEntityFrameworkCoreToken>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<OpenIddictEntityFrameworkCoreApplication> builder)
    {
        builder.ToTable("por_oidc_applications");
        builder.HasIndex(a => a.ClientId).IsUnique();
    }

    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<OpenIddictEntityFrameworkCoreAuthorization> builder)
    {
        builder.ToTable("por_oidc_authorizations");
    }

    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<OpenIddictEntityFrameworkCoreScope> builder)
    {
        builder.ToTable("por_oidc_scopes");
    }

    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<OpenIddictEntityFrameworkCoreToken> builder)
    {
        builder.ToTable("por_oidc_tokens");
    }
}
