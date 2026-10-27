/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-26 10:00:00 +08:00 中国标准时间 星期一
 * 描述：WmsTenantDbContext 设计时工厂——仅 dotnet ef migrations add 时使用（提供程序必须与运行时一致 SqlServer）
 */

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

using Qlx.BuildingBlocks.Configuration;
using Qlx.BuildingBlocks.MultiTenancy;

namespace Qlx.Modules.Wms;

internal sealed class WmsTenantDbContextFactory : IDesignTimeDbContextFactory<WmsTenantDbContext>
{
    public WmsTenantDbContext CreateDbContext(string[] args)
    {
        var configuration = QlxDesignTimeConfiguration.Build();
        var connectionString = QlxDesignTimeConfiguration.ResolveModelingConnectionString(configuration);

        var resolver = new InMemoryTenantDatabaseResolver();
        var opts = new DbContextOptionsBuilder().UseSqlServer(connectionString).Options;
        return new WmsTenantDbContext(resolver, QlxDesignTimeConfiguration.DesignTimeTenantId, opts);
    }
}
