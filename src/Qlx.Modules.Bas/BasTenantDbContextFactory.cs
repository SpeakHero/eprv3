/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.1 beta
 * 创建时间：2026-10-21
 * 修改时间：2026-10-22 15:30:00 +08:00 中国标准时间 星期四
 * 描述：BasTenantDbContext 设计时工厂——仅 dotnet ef migrations add 时使用。
 *       提供内存 resolver + tenantId=1 + 显式 SqlServer 连接（仅建模、不连接）走 IsConfigured 跳过分支。
 *       提供程序必须与运行时一致（SqlServer）：若用 Sqlite 建模，快照注解与运行时模型不匹配，
 *       EF Core 10 的 pending-changes 检查会在 MigrateAsync 内抛异常导致租户迁移静默失败。
 */

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Qlx.BuildingBlocks.Configuration;
using Qlx.BuildingBlocks.MultiTenancy;

namespace Qlx.Modules.Bas;

internal sealed class BasTenantDbContextFactory : IDesignTimeDbContextFactory<BasTenantDbContext>
{
    public BasTenantDbContext CreateDbContext(string[] args)
    {
        var configuration = QlxDesignTimeConfiguration.Build();
        var connectionString = QlxDesignTimeConfiguration.ResolveModelingConnectionString(configuration);

        var resolver = new InMemoryTenantDatabaseResolver();
        var opts = new DbContextOptionsBuilder().UseSqlServer(connectionString).Options;
        return new BasTenantDbContext(resolver, QlxDesignTimeConfiguration.DesignTimeTenantId, opts);
    }
}
