/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-22 00:25:00 +08:00 中国标准时间 星期四
 * 修改时间：2026-10-22 00:25:00 +08:00 中国标准时间 星期四
 * 描述：SQL Server RLS 集成测试（LocalDB）：裸 SQL 只返回当前租户、跨租户写入被阻断
 */

using Microsoft.Data.SqlClient;
using Qlx.BuildingBlocks.MultiTenancy.Rls;

namespace Qlx.BuildingBlocks.Tests.MultiTenancy;

/// <summary>
/// B3-04 验收：SQL Server（LocalDB）安装 RLS 后，裸 SQL 只返回当前租户行；
/// 跨租户 INSERT 被 BLOCK 谓词拒绝。LocalDB 不可用时跳过。
/// </summary>
public sealed class TenantRlsSqlServerIntegrationTests
{
    private const string MasterConnectionString =
        "Server=(localdb)\\MSSQLLocalDB;Database=master;Integrated Security=true;TrustServerCertificate=true;";

    [Fact]
    public async Task RawSqlReturnsOnlyCurrentTenantRows()
    {
        var databaseName = $"qlx_rls_it_{Guid.NewGuid():N}";
        var connectionString =
            $"Server=(localdb)\\MSSQLLocalDB;Database={databaseName};Integrated Security=true;TrustServerCertificate=true;Pooling=false;";

        try
        {
            await CreateDatabaseAsync(databaseName);
        }
        catch (SqlException)
        {
            Assert.Skip("LocalDB 不可用，跳过 RLS 集成测试");
            return;
        }

        try
        {
            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync(TestContext.Current.CancellationToken);

            await ExecuteAsync(connection, """
                CREATE TABLE things (
                    id BIGINT IDENTITY(1,1) PRIMARY KEY,
                    tenant_id BIGINT NOT NULL,
                    name NVARCHAR(64) NOT NULL);
                """);

            var provider = new SqlServerTenantRlsProvider();
            foreach (var statement in provider.BuildInstallStatements(
                         [new TenantRlsTable("dbo", "things", "tenant_id")]))
            {
                await ExecuteAsync(connection, statement);
            }

            // 写入：按会话租户各插一行
            await provider.SetSessionTenantAsync(connection, 101, TestContext.Current.CancellationToken);
            await ExecuteAsync(connection, "INSERT INTO things (tenant_id, name) VALUES (101, N'A');");

            await provider.SetSessionTenantAsync(connection, 202, TestContext.Current.CancellationToken);
            await ExecuteAsync(connection, "INSERT INTO things (tenant_id, name) VALUES (202, N'B');");

            // 裸 SQL：租户 101 只能读到自己的行
            await provider.SetSessionTenantAsync(connection, 101, TestContext.Current.CancellationToken);
            var rows = await QueryTenantIdsAsync(connection);
            var row = Assert.Single(rows);
            Assert.Equal(101, row);

            // 跨租户写入被 BLOCK 谓词拒绝
            await Assert.ThrowsAsync<SqlException>(
                () => ExecuteAsync(connection, "INSERT INTO things (tenant_id, name) VALUES (999, N'X');"));
        }
        finally
        {
            await DropDatabaseAsync(databaseName);
        }
    }

    private static async Task CreateDatabaseAsync(string databaseName)
    {
        await using var connection = new SqlConnection(MasterConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await ExecuteAsync(connection, $"CREATE DATABASE [{databaseName}];");
    }

    private static async Task DropDatabaseAsync(string databaseName)
    {
        try
        {
            await using var connection = new SqlConnection(MasterConnectionString);
            await connection.OpenAsync(TestContext.Current.CancellationToken);
            await ExecuteAsync(connection, $"""
                ALTER DATABASE [{databaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
                DROP DATABASE [{databaseName}];
                """);
        }
        catch (SqlException)
        {
            // 清理失败不掩盖测试结果
        }
    }

    private static async Task ExecuteAsync(SqlConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }

    private static async Task<List<long>> QueryTenantIdsAsync(SqlConnection connection)
    {
        var result = new List<long>();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT tenant_id FROM things ORDER BY id;";

        await using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        while (await reader.ReadAsync(TestContext.Current.CancellationToken))
        {
            result.Add(reader.GetInt64(0));
        }

        return result;
    }
}
