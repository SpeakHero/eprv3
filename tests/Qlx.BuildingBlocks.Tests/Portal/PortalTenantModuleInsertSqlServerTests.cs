/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-24
 * 描述：P0-1 回归集成测试（LocalDB）：通过聚合集合追加模块必须生成 INSERT 而非 UPDATE。
 *       修复前 SaveLicenseAsync 显式 AssignId 后经导航集合加入，EF 判定为已存在实体生成 UPDATE，
 *       影响 0 行并抛 DbUpdateConcurrencyException；本测试守护该回归。LocalDB 不可用时跳过。
 */

using System;
using System.Linq;
using System.Threading.Tasks;

using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

using Qlx.Modules.Portal;
using Qlx.Modules.Portal.Domain.Tenancy;

namespace Qlx.BuildingBlocks.Tests.Portal;

/// <summary>
/// 租户模块落库回归测试：新模块仅经 <c>Tenant.AddModule</c> 加入跟踪图，不显式置键。
/// </summary>
public sealed class PortalTenantModuleInsertSqlServerTests
{
    private const string MasterConnectionString =
        "Server=(localdb)\\MSSQLLocalDB;Database=master;Integrated Security=true;TrustServerCertificate=true;";

    [Fact]
    public async Task ModuleAddedThroughAggregateCollectionIsInsertedWithoutConcurrencyError()
    {
        string databaseName = $"qlx_portal_it_{Guid.NewGuid():N}";
        string connectionString =
            $"Server=(localdb)\\MSSQLLocalDB;Database={databaseName};Integrated Security=true;TrustServerCertificate=true;Pooling=false;";

        try
        {
            await using var master = new SqlConnection(MasterConnectionString);
            await master.OpenAsync(TestContext.Current.CancellationToken);
            await using var create = master.CreateCommand();
            create.CommandText = $"CREATE DATABASE [{databaseName}];";
            await create.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        }
        catch (SqlException)
        {
            Assert.Skip("LocalDB 不可用，跳过 P0-1 集成回归测试");
            return;
        }

        var options = new DbContextOptionsBuilder<PortalDbContext>()
            .UseSqlServer(connectionString)
            .Options;

        try
        {
            long tenantId;
            await using (var db = new PortalDbContext(options))
            {
                await db.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
                var tenant = new Tenant("it-regress", "集成回归租户");
                db.Set<Tenant>().Add(tenant);
                db.Set<TenantModule>().Add(new TenantModule(tenant, "support", "1.0.0"));
                await db.SaveChangesAsync(TestContext.Current.CancellationToken);
                tenantId = tenant.Id;
            }

            // 第二个事务：复现 SaveLicenseAsync 的写入路径——只经聚合集合追加，不显式 AssignId
            await using (var db = new PortalDbContext(options))
            {
                Tenant tracked = await db.Set<Tenant>()
                    .Include(t => t.Modules)
                    .SingleAsync(t => t.Id == tenantId, TestContext.Current.CancellationToken);

                tracked.AddModule(new TenantModule(tracked, "bas", "1.0.0"));

                // 修复前：此处抛 DbUpdateConcurrencyException（expected 1 row, affected 0）
                await db.SaveChangesAsync(TestContext.Current.CancellationToken);
            }

            await using (var db = new PortalDbContext(options))
            {
                TenantModule[] modules = await db.Set<TenantModule>()
                    .Where(m => m.TenantId == tenantId)
                    .OrderBy(m => m.ModuleCode)
                    .ToArrayAsync(TestContext.Current.CancellationToken);

                Assert.Equal(2, modules.Length);
                Assert.Equal(["bas", "support"], modules.Select(m => m.ModuleCode).ToArray());
                Assert.All(modules, m => Assert.NotEqual(0, m.Id));
            }
        }
        finally
        {
            SqlConnection.ClearAllPools();
            await using var master = new SqlConnection(MasterConnectionString);
            await master.OpenAsync(TestContext.Current.CancellationToken);
            await using var drop = master.CreateCommand();
            drop.CommandText =
                $"ALTER DATABASE [{databaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{databaseName}];";
            await drop.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        }
    }
}
