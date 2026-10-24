/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-20 16:30:00 +08:00 中国标准时间 星期一
 * 修改时间：2026-10-20 16:30:00 +08:00 中国标准时间 星期一
 * 描述：AuditDbContext 设计时工厂，供 dotnet ef migrations add 使用
 */

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;
using Qlx.BuildingBlocks.Configuration;

namespace Qlx.BuildingBlocks.Audit;

/// <summary>
/// AuditDbContext 设计时工厂。
/// 让 <c>dotnet ef migrations add</c> 构造 AuditDbContext——
/// 读取 <c>config/common/audit.json</c> 的 Qlx:Audit:ConnectionString 键。
/// </summary>
public sealed class AuditDbContextFactory : IDesignTimeDbContextFactory<AuditDbContext>
{
    /// <inheritdoc />
    public AuditDbContext CreateDbContext(string[] args)
    {
        IConfigurationRoot configuration = QlxDesignTimeConfiguration.Build();

        var audit = configuration.GetSection(AuditOptions.SectionName).Get<AuditOptions>();
        if (audit is null || string.IsNullOrWhiteSpace(audit.ConnectionString))
        {
            throw new InvalidOperationException(
                $"配置节 {AuditOptions.SectionName}:ConnectionString 缺失。请检查 config/common/audit.json。");
        }

        var optionsBuilder = new DbContextOptionsBuilder<AuditDbContext>();
        optionsBuilder.UseSqlServer(audit.ConnectionString);

        return new AuditDbContext(optionsBuilder.Options);
    }
}
