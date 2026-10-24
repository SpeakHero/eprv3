/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-20 16:30:00 +08:00 中国标准时间 星期一
 * 修改时间：2026-10-20 16:30:00 +08:00 中国标准时间 星期一
 * 描述：PortalDbContext 设计时工厂，供 dotnet ef migrations add 使用
 */

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;
using Qlx.BuildingBlocks.Configuration;

namespace Qlx.Modules.Portal;

/// <summary>
/// PortalDbContext 设计时工厂。
/// 让 <c>dotnet ef migrations add</c> 在 Host 项目外部也能构造 PortalDbContext——
/// 读取 <c>config/portal/portal.json</c> 的 PortalConnectionString 键构建 DbContextOptions。
/// </summary>
public sealed class PortalDbContextFactory : IDesignTimeDbContextFactory<PortalDbContext>
{
    /// <inheritdoc />
    public PortalDbContext CreateDbContext(string[] args)
    {
        IConfigurationRoot configuration = QlxDesignTimeConfiguration.Build();

        var portal = configuration.GetSection(PortalConnectionOptions.SectionName).Get<PortalConnectionOptions>();
        if (portal is null || string.IsNullOrWhiteSpace(portal.PortalConnectionString))
        {
            throw new InvalidOperationException(
                $"配置节 {PortalConnectionOptions.SectionName}:{PortalConnectionOptions.PortalConnectionStringKey} 缺失。请检查 config/portal/portal.json。");
        }

        var optionsBuilder = new DbContextOptionsBuilder<PortalDbContext>();
        optionsBuilder.UseSqlServer(portal.PortalConnectionString).UseOpenIddict();

        return new PortalDbContext(optionsBuilder.Options);
    }
}
