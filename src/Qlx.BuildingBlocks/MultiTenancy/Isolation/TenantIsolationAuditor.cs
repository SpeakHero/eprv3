/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-22 01:25:00 +08:00 中国标准时间 星期四
 * 描述：租户隔离巡检服务——模型检查、策略覆盖、跨租户抽样，产出可读报告
 */

using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Qlx.Abstractions.Constants;
using Qlx.BuildingBlocks.MultiTenancy.Rls;

namespace Qlx.BuildingBlocks.MultiTenancy.Isolation;

/// <summary>
/// 租户隔离巡检器（B3-08）。手动触发执行，产出 <see cref="TenantIsolationAuditReport"/>：
/// ① 模型检查：实体是否含 TenantId、唯一索引是否含 TenantId；
/// ② 策略覆盖：模型推导的策略名 vs 数据库已安装策略名；
/// ③ 跨租户抽样：直连库统计非当前租户行数（RLS 生效时应为 0）。
/// <para>
/// 仅当 RLS 提供程序为 SQL Server 且能力为 <see cref="RlsCapability.Supported"/> 时执行库级检查；
/// 其他提供程序降级为仅模型检查。
/// </para>
/// </summary>
/// <remarks>构造函数。</remarks>
public sealed partial class TenantIsolationAuditor(
    IServiceProvider serviceProvider,
    ITenantDatabaseResolver resolver,
    IEnumerable<ITenantMigrationPlan> plans,
    ITenantRlsProvider rlsProvider,
    ILogger<TenantIsolationAuditor> logger)
{
    private const string PolicyPrefix = Rls.SqlServerTenantRlsProvider.PolicyNamePrefix;

    private readonly IServiceProvider _serviceProvider = serviceProvider;
    private readonly ITenantDatabaseResolver _resolver = resolver;
    private readonly IEnumerable<ITenantMigrationPlan> _plans = plans;
    private readonly ITenantRlsProvider _rlsProvider = rlsProvider;
    private readonly ILogger<TenantIsolationAuditor> _logger = logger;


    /// <summary>
    /// 对指定租户执行完整巡检。
    /// </summary>
    /// <param name="tenantId">租户 ID（必须大于 0）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>巡检报告。</returns>
    public async Task<TenantIsolationAuditReport> RunAsync(long tenantId, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(tenantId);

        var findings = new List<TenantIsolationFinding>();
        var expectedPolicies = new List<string>();
        var sampleTargets = new List<TenantRlsTable>();
        var connectionInfo = _resolver.Resolve(tenantId);

        // 1. 模型检查（所有租户模块）
        foreach (var plan in _plans)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var dbContext = CreateTenantDbContext(plan.DbContextType, tenantId, connectionInfo);
            try
            {
                var model = dbContext.Model;
                InspectModel(model, plan.ModuleCode, findings);

                var tables = TenantRlsPolicyCatalog.Build(model);
                expectedPolicies.AddRange(tables.Select(static t => SqlServerTenantRlsProvider.BuildPolicyName(t.TableName)));
                sampleTargets.AddRange(tables);
            }
            finally
            {
                if (dbContext is IAsyncDisposable asyncDisposable)
                {
                    await asyncDisposable.DisposeAsync().ConfigureAwait(false);
                }
                else
                {
                    dbContext.Dispose();
                }
            }
        }

        // 2. 库级检查（策略覆盖 + 跨租户抽样）
        if (_rlsProvider.Capability == RlsCapability.Supported)
        {
            try
            {
                await using var connection = new SqlConnection(connectionInfo.ToConnectionString());
                await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

                var installedPolicies = await QueryInstalledPoliciesAsync(connection, cancellationToken)
                    .ConfigureAwait(false);

                ComparePolicyCoverage(expectedPolicies, installedPolicies, findings);
                await SampleCrossTenantRowsAsync(connection, tenantId, sampleTargets, findings, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogDatabaseCheckFailed(ex);
                findings.Add(new TenantIsolationFinding(
                    "Connection",
                    connectionInfo.DbName,
                    $"数据库巡检连接失败：{ex.Message}",
                    TenantIsolationSeverity.Warning));
            }
        }
        else
        {
            findings.Add(new TenantIsolationFinding(
                "Policy",
                _rlsProvider.ProviderName,
                "RLS 未启用或提供程序不支持，跳过策略覆盖与跨租户抽样检查。",
                TenantIsolationSeverity.Info));
        }

        var report = new TenantIsolationAuditReport(
            DateTimeOffset.UtcNow,
            tenantId,
            connectionInfo.DbName,
            _rlsProvider.ProviderName,
            [.. findings.OrderByDescending(static f => f.Severity)]);

        LogAuditCompleted(tenantId, report.IsHealthy, report.HasCritical);
        return report;
    }

    /// <summary>检查 EF 模型：实体必须含 long TenantId，唯一索引必须包含 TenantId。</summary>
    internal static void InspectModel(IModel model, string moduleCode, List<TenantIsolationFinding> findings)
    {
        foreach (var entityType in model.GetEntityTypes())
        {
            var tableName = entityType.GetTableName();
            if (string.IsNullOrWhiteSpace(tableName))
            {
                continue;
            }

            var tenantIdProperty = entityType.FindProperty(QlxHostConstants.TenantEntityPropertyName);
            if (tenantIdProperty is null)
            {
                findings.Add(new TenantIsolationFinding(
                    "Model",
                    $"{moduleCode}:{entityType.ClrType.Name}",
                    $"实体缺少 TenantId 属性，共享库下会串数据。",
                    TenantIsolationSeverity.Critical));
                continue;
            }

            if (tenantIdProperty.ClrType != typeof(long))
            {
                findings.Add(new TenantIsolationFinding(
                    "Model",
                    $"{moduleCode}:{entityType.ClrType.Name}",
                    $"TenantId 属性类型必须是 long。",
                    TenantIsolationSeverity.Critical));
            }

            foreach (var index in entityType.GetIndexes())
            {
                if (!index.IsUnique)
                {
                    continue;
                }

                var containsTenantId = index.Properties.Any(
                    static p => p.Name == QlxHostConstants.TenantEntityPropertyName);
                if (!containsTenantId)
                {
                    findings.Add(new TenantIsolationFinding(
                        "Model",
                        $"{moduleCode}:{entityType.ClrType.Name}",
                        $"唯一索引缺少 TenantId，共享库下会跨租户冲突。",
                        TenantIsolationSeverity.Critical));
                }
            }
        }
    }

    /// <summary>比较期望策略与已安装策略。</summary>
    internal static void ComparePolicyCoverage(
        IReadOnlyCollection<string> expectedPolicies,
        IReadOnlyCollection<string> installedPolicies,
        List<TenantIsolationFinding> findings)
    {
        var missing = expectedPolicies.Except(installedPolicies, StringComparer.OrdinalIgnoreCase).ToList();
        foreach (var policy in missing)
        {
            findings.Add(new TenantIsolationFinding(
                "Policy",
                policy,
                "RLS 策略缺失，共享库下该表裸 SQL 无防护。",
                TenantIsolationSeverity.Critical));
        }

        var orphaned = installedPolicies.Except(expectedPolicies, StringComparer.OrdinalIgnoreCase).ToList();
        foreach (var policy in orphaned)
        {
            findings.Add(new TenantIsolationFinding(
                "Policy",
                policy,
                "数据库存在孤立 RLS 策略（模型中无对应租户表），建议清理。",
                TenantIsolationSeverity.Warning));
        }
    }

    private static async Task<IReadOnlyCollection<string>> QueryInstalledPoliciesAsync(
        SqlConnection connection,
        CancellationToken cancellationToken)
    {
        var result = new List<string>();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT name FROM sys.security_policies WHERE name LIKE @prefix + '%';";
        command.Parameters.Add(new SqlParameter("@prefix", PolicyPrefix));

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            result.Add(reader.GetString(0));
        }

        return result;
    }

    private static async Task SampleCrossTenantRowsAsync(
        SqlConnection connection,
        long tenantId,
        IReadOnlyList<TenantRlsTable> tables,
        List<TenantIsolationFinding> findings,
        CancellationToken cancellationToken)
    {
        foreach (var table in tables.DistinctBy(static t => t.QualifiedName))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var count = await CountForeignRowsAsync(connection, table, tenantId, cancellationToken)
                .ConfigureAwait(false);

            if (count > 0)
            {
                findings.Add(new TenantIsolationFinding(
                    "Sample",
                    table.QualifiedName,
                    $"检测到 {count} 条非当前租户（{tenantId}）数据，可能存在串库。",
                    TenantIsolationSeverity.Critical));
            }
        }
    }

    private static async Task<long> CountForeignRowsAsync(
        SqlConnection connection,
        TenantRlsTable table,
        long tenantId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"SELECT COUNT_BIG(*) FROM {Quote(table.Schema)}.{Quote(table.TableName)} WHERE {Quote(table.TenantColumn)} <> @tenantId;";
        command.Parameters.Add(new SqlParameter("@tenantId", tenantId));

        var scalar = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return scalar is long value ? value : 0L;
    }

    private DbContext CreateTenantDbContext(
        Type dbContextType,
        long tenantId,
        TenantConnectionInfo connectionInfo)
    {
        var optionsBuilder = new DbContextOptionsBuilder();
        optionsBuilder.UseSqlServer(connectionInfo.ToConnectionString());

        var snapshotResolver = new SnapshotTenantDatabaseResolver(tenantId, connectionInfo);
        var ctorArgs = new object[] { snapshotResolver, tenantId, optionsBuilder.Options };
        return (DbContext)ActivatorUtilities.CreateInstance(_serviceProvider, dbContextType, ctorArgs);
    }

    private static string Quote(string identifier)
        => $"[{identifier.Replace("]", "]]", StringComparison.Ordinal)}]";

    [LoggerMessage(Level = LogLevel.Warning, Message = "隔离巡检数据库检查失败（详细异常见日志）")]
    private partial void LogDatabaseCheckFailed(Exception ex);

    [LoggerMessage(Level = LogLevel.Information, Message = "隔离巡检完成（租户 {TenantId}）：健康={IsHealthy}，严重={HasCritical}")]
    private partial void LogAuditCompleted(long tenantId, bool isHealthy, bool hasCritical);

    /// <summary>迁移/巡检用快照解析器：直接返回已解析连接信息，不触发缓存查询。</summary>
    private sealed class SnapshotTenantDatabaseResolver(
        long tenantId,
        TenantConnectionInfo connectionInfo) : ITenantDatabaseResolver
    {
        public TenantConnectionInfo Resolve(long requestedTenantId)
        {
            if (requestedTenantId != tenantId)
            {
                throw new InvalidOperationException(
                    $"巡检/迁移快照上下文租户不匹配：期望 {tenantId}，实际 {requestedTenantId}");
            }

            return connectionInfo;
        }

        public Task PreloadAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public void Invalidate(long requestedTenantId)
        {
        }
    }
}
