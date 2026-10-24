/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-22 16:30:00 +08:00 中国标准时间 星期四
 * 修改时间：2026-10-22 16:30:00 +08:00 中国标准时间 星期四
 * 描述：共享库残留数据清理后台服务：升级完成且过保留期后，按任务断点逐表分批删除该租户共享库数据
 */

using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Qlx.Abstractions.Audit;
using Qlx.Abstractions.Constants;
using Qlx.Abstractions.Tenancy;
using Qlx.BuildingBlocks.Ids;

namespace Qlx.BuildingBlocks.MultiTenancy.Upgrade;

/// <summary>
/// 共享库残留数据清理后台服务。每 <see cref="TenantUpgradeOptions.CleanIntervalHours"/> 小时扫描一轮：
/// 对 Done 且完成时间超过 <see cref="TenantUpgradeOptions.RetentionDays"/> 保留期的升级任务，
/// 按该任务的表级断点（LastKey）逐表循环 <c>DELETE TOP(@batch) WHERE tenant_id AND id &lt;= LastKey</c>，
/// 全部表清理完毕后标记 Cleaned 并落审计。
/// <para>仅允许按断点 DELETE 行，禁止 DROP / 删库；模型中已不存在的表记警告后跳过。</para>
/// </summary>
internal sealed partial class TenantSharedDataCleanService(
    IServiceProvider serviceProvider,
    TenantMigrationRunner runner,
    TenantDataCopier copier,
    IOptions<TenantUpgradeOptions> options,
    IOptions<SharedDatabaseOptions> sharedDatabase,
    ILogger<TenantSharedDataCleanService> logger) : BackgroundService
{
    /// <summary>LoggerMessage 源生成器要求字段。</summary>
    private readonly ILogger<TenantSharedDataCleanService> _logger = logger;

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled)
        {
            return;
        }

        await Task.Yield();

        using var timer = new PeriodicTimer(TimeSpan.FromHours(options.Value.CleanIntervalHours));
        do
        {
            await CleanOnceAsync(stoppingToken).ConfigureAwait(false);
        }
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
    }

    private async Task CleanOnceAsync(CancellationToken ct)
    {
        try
        {
            await using var scope = serviceProvider.CreateAsyncScope();
            var repo = scope.ServiceProvider.GetService<ITenantUpgradeJobRepository>();
            if (repo is null)
            {
                return;
            }

            var threshold = DateTimeOffset.UtcNow.AddDays(-options.Value.RetentionDays);
            var jobs = await repo.GetJobsAwaitingCleanupAsync(threshold, ct).ConfigureAwait(false);
            if (jobs.Count == 0)
            {
                return;
            }

            LogCleanScanFound(jobs.Count);

            foreach (var job in jobs)
            {
                ct.ThrowIfCancellationRequested();
                await CleanJobAsync(job, repo, scope.ServiceProvider, ct).ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogCleanError(ex);
        }
    }

    private async Task CleanJobAsync(
        TenantUpgradeJobSnapshot job,
        ITenantUpgradeJobRepository repo,
        IServiceProvider scopeProvider,
        CancellationToken ct)
    {
        var checkpoints = await repo.GetCheckpointsAsync(job.JobId, ct).ConfigureAwait(false);
        if (checkpoints.Count == 0)
        {
            // 零数据任务（复制阶段未产生任何断点）直接标记 Cleaned，同样落审计保持轨迹完整
            await repo.MarkCleanedAsync(job.JobId, ct).ConfigureAwait(false);
            await CaptureCleanedAuditAsync(job, 0, scopeProvider, ct).ConfigureAwait(false);
            return;
        }

        var modules = runner.GetSortedPlans()
            .Select(static p => (p.DbContextType, p.ModuleCode))
            .ToList();
        var source = sharedDatabase.Value.ToConnectionInfo(job.TenantId);
        var tables = copier.CollectModuleTables(modules, job.TenantId, source)
            .ToDictionary(static t => t.Key, StringComparer.Ordinal);

        await using var connection = new SqlConnection(source.ToConnectionString());
        await connection.OpenAsync(ct).ConfigureAwait(false);

        var batchSize = options.Value.CleanBatchSize;
        long totalDeleted = 0;

        foreach (var checkpoint in checkpoints)
        {
            ct.ThrowIfCancellationRequested();

            var key = $"{checkpoint.ModuleCode}|{checkpoint.TableName}";
            if (!tables.TryGetValue(key, out var table))
            {
                LogTableMissing(checkpoint.ModuleCode, checkpoint.TableName);
                continue;
            }

            long tableDeleted = 0;
            while (true)
            {
                await using var command = connection.CreateCommand();
                command.CommandText = $"""
                    DELETE TOP (@batchSize) FROM {table.QualifiedName}
                    WHERE {TenantTableDescriptor.Quote(table.TenantColumn)} = @tenantId
                      AND {TenantTableDescriptor.Quote(table.PrimaryKeyColumn)} <= @maxKey;
                    """;
                command.Parameters.Add(new SqlParameter("@batchSize", batchSize));
                command.Parameters.Add(new SqlParameter("@tenantId", job.TenantId));
                command.Parameters.Add(new SqlParameter("@maxKey", checkpoint.LastKey));

                var affected = await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                tableDeleted += affected;
                if (affected < batchSize)
                {
                    break;
                }
            }

            totalDeleted += tableDeleted;
            LogTableCleaned(job.TenantId, checkpoint.TableName, tableDeleted);
        }

        await repo.MarkCleanedAsync(job.JobId, ct).ConfigureAwait(false);

        await CaptureCleanedAuditAsync(job, totalDeleted, scopeProvider, ct).ConfigureAwait(false);
    }

    private static async Task CaptureCleanedAuditAsync(
        TenantUpgradeJobSnapshot job,
        long totalDeleted,
        IServiceProvider scopeProvider,
        CancellationToken ct)
    {
        if (scopeProvider.GetService<IAuditService>() is { } audit)
        {
            // workerId 租约在宿主启动完成后才就绪，雪花生成器必须在运行期从作用域解析
            var snowflake = scopeProvider.GetRequiredService<ISnowflakeIdGenerator>();
            await audit.CaptureOperationAsync(new OperationLogEntry(
                snowflake.NextId(), QlxAuditConstants.PortalModuleCode, TenantUpgradeAuditOperations.SharedDataCleaned,
                $"租户升级保留期届满，共享库残留数据已清理（任务 {job.JobId}，共 {totalDeleted} 行）",
                QlxAuditConstants.SystemOperator, DateTimeOffset.UtcNow, job.TenantId,
                QlxAuditConstants.TargetTenant, job.TenantId),
                ct).ConfigureAwait(false);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "共享库残留清理扫描发现 {Count} 个待清理任务")]
    private partial void LogCleanScanFound(int count);

    [LoggerMessage(Level = LogLevel.Warning, Message = "共享库残留清理异常（详细异常见日志）")]
    private partial void LogCleanError(Exception ex);

    [LoggerMessage(Level = LogLevel.Warning, Message = "共享库残留清理跳过表 {ModuleCode}:{TableName}（模型中已不存在，需人工确认）")]
    private partial void LogTableMissing(string moduleCode, string tableName);

    [LoggerMessage(Level = LogLevel.Information, Message = "共享库残留清理：租户 {TenantId} 表 {TableName} 删除 {Rows} 行")]
    private partial void LogTableCleaned(long tenantId, string tableName, long rows);
}
