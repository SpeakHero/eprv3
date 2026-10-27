/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-26 14:00:00 +08:00 中国标准时间 星期一
 * 描述：Stk 设计时 DbContext 工厂——供 dotnet ef migrations 使用（运行时由 AddQlxStk 工厂注册）
 */

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

using Qlx.BuildingBlocks.Configuration;
using Qlx.BuildingBlocks.MultiTenancy;

namespace Qlx.Modules.Stk;

/// <summary>Stk 设计时 DbContext 工厂。</summary>
public sealed class StkTenantDbContextFactory : IDesignTimeDbContextFactory<StkTenantDbContext>
{
    /// <inheritdoc />
    public StkTenantDbContext CreateDbContext(string[] args)
    {
        var configuration = QlxDesignTimeConfiguration.Build();
        string connectionString = QlxDesignTimeConfiguration.ResolveModelingConnectionString(configuration);

        var options = new DbContextOptionsBuilder<StkTenantDbContext>()
            .UseSqlServer(connectionString)
            .Options;

        return new StkTenantDbContext(new InMemoryTenantDatabaseResolver(), 0L, options);
    }
}
