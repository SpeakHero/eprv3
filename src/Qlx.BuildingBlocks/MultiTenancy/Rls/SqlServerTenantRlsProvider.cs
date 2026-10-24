/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-21 01:15:00 +08:00 中国标准时间 星期三
 * 修改时间：2026-10-21 01:15:00 +08:00 中国标准时间 星期三
 * 描述：SQL Server 租户 RLS 实现：SESSION_CONTEXT 会话上下文 + Security Policy 过滤/阻断谓词
 */

using System.Data;
using System.Data.Common;
using Microsoft.Data.SqlClient;

namespace Qlx.BuildingBlocks.MultiTenancy.Rls;

/// <summary>
/// SQL Server 租户行级安全实现。
/// 会话上下文键 <c>qlx_tenant_id</c> 由连接打开时写入（见 <see cref="TenantSessionInterceptor"/>）；
/// 每张租户表安装 <c>FILTER</c>（读）+ <c>BLOCK AFTER INSERT/UPDATE</c>（写）谓词，裸 SQL 同样受控。
/// </summary>
public sealed class SqlServerTenantRlsProvider : ITenantRlsProvider
{
    /// <summary>会话上下文键名。</summary>
    public const string SessionContextKey = "qlx_tenant_id";

    /// <summary>过滤/阻断谓词函数名（dbo 架构）。</summary>
    public const string PredicateFunctionName = "fn_qlx_tenant_rls_predicate";

    /// <summary>每表安全策略名前缀（隔离巡检按此前缀识别 RLS 策略）。</summary>
    public const string PolicyNamePrefix = "qlx_rls_";

    /// <inheritdoc />
    public string ProviderName => "SqlServer";

    /// <inheritdoc />
    public RlsCapability Capability => RlsCapability.Supported;

    /// <inheritdoc />
    public async Task SetSessionTenantAsync(DbConnection connection, long tenantId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);

        if (connection is not SqlConnection sqlConnection)
        {
            return;
        }

        if (tenantId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(tenantId), tenantId, "租户 ID 必须大于 0");
        }

        await using var command = sqlConnection.CreateCommand();
        command.CommandText = $"""
            EXEC sys.sp_set_session_context
                @key = N'{SessionContextKey}',
                @value = @tenantId,
                @read_only = 0;
            """;
        command.Parameters.Add(new SqlParameter("@tenantId", SqlDbType.BigInt) { Value = tenantId });
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public IReadOnlyList<string> BuildInstallStatements(IReadOnlyCollection<TenantRlsTable> tables)
    {
        ArgumentNullException.ThrowIfNull(tables);

        if (tables.Count == 0)
        {
            return [];
        }

        var statements = new List<string>(tables.Count + 1)
        {
            $"""
            CREATE OR ALTER FUNCTION [dbo].[{PredicateFunctionName}](@tenant_id BIGINT)
            RETURNS TABLE
            WITH SCHEMABINDING
            AS RETURN
                SELECT 1 AS fn_result
                WHERE @tenant_id = CAST(SESSION_CONTEXT(N'{SessionContextKey}') AS BIGINT);
            """,
        };

        foreach (var table in tables)
        {
            var policyName = BuildPolicyName(table.TableName);
            var qualifiedTable = $"{Quote(table.Schema)}.{Quote(table.TableName)}";
            var predicate = $"[dbo].[{PredicateFunctionName}]({Quote(table.TenantColumn)})";

            statements.Add($"""
                IF EXISTS (SELECT 1 FROM sys.security_policies WHERE name = N'{policyName}')
                    DROP SECURITY POLICY {Quote(table.Schema)}.{Quote(policyName)};

                CREATE SECURITY POLICY {Quote(table.Schema)}.{Quote(policyName)}
                    ADD FILTER PREDICATE {predicate} ON {qualifiedTable},
                    ADD BLOCK PREDICATE {predicate} ON {qualifiedTable} AFTER INSERT,
                    ADD BLOCK PREDICATE {predicate} ON {qualifiedTable} AFTER UPDATE
                    WITH (STATE = ON);
                """);
        }

        return statements;
    }

    /// <summary>每表策略名（<c>qlx_rls_{table}</c>，超长截断以符合 128 字符限制）。</summary>
    public static string BuildPolicyName(string tableName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tableName);

        var sanitized = tableName.Replace('-', '_').Replace(' ', '_');
        var name = PolicyNamePrefix + sanitized;
        return name.Length <= 120 ? name : name[..120];
    }

    private static string Quote(string identifier)
        => $"[{identifier.Replace("]", "]]", StringComparison.Ordinal)}]";
}
