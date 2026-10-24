/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.1.0 beta
 * 创建时间：2026-10-21 04:00:00 +08:00 中国标准时间 星期三
 * 修改时间：2026-10-21 23:59:00 +08:00 中国标准时间 星期三
 * 描述：租户迁移执行后台服务：只处理独立库租户的预配、重试、激活；迁移原语复用 TenantMigrationRunner
 */

using System.Threading.Tasks.Dataflow;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Qlx.Abstractions.Tenancy;

namespace Qlx.BuildingBlocks.MultiTenancy;

/// <summary>
/// 租户迁移执行后台服务。定期扫描门户库中处于 <c>Provisioning</c> 且为**独立库**的租户，
/// 按模块依赖序完成迁移后标记租户 Active。
/// </summary>
/// <remarks>
/// <para>共享租户不进入本服务：共享库由启动装配按迁移计划去重迁移一次（B2-06），开通即 Active。</para>
/// <para>依赖注入图</para>
/// <list type="bullet">
/// <item><see cref="ITenantProvisioningRepository"/>：查询/更新租户生命周期状态（门户模块实现，BuildingBlocks 解耦依赖 Abstractions 契约）</item>
/// <item><see cref="ITenantDatabaseResolver"/>：获取租户数据库连接串（PortalTenantDatabaseResolver 为生产实现）</item>
/// <item><see cref="TenantMigrationRunner"/>：建库 + 应用锁 + 按模块迁移的通用原语</item>
/// </list>
/// </remarks>
/// <remarks>
/// 构造函数。
/// </remarks>
public sealed partial class TenantMigrationService(
    IServiceProvider serviceProvider,
    ITenantDatabaseResolver resolver,
    TenantMigrationRunner runner,
    IOptions<TenantMigrationOptions> options,
    ILogger<TenantMigrationService> logger) : BackgroundService
{
    private readonly IServiceProvider _serviceProvider = serviceProvider;
    private readonly ITenantDatabaseResolver _resolver = resolver;
    private readonly TenantMigrationRunner _runner = runner;
    private readonly IOptions<TenantMigrationOptions> _options = options;
    private readonly ILogger<TenantMigrationService> _logger = logger;


    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield();

        // 启动时先跑一次（立即处理等待中的预配）
        await ScanAndMigrateAsync(stoppingToken).ConfigureAwait(false);

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(_options.Value.ScanIntervalSeconds));
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            await ScanAndMigrateAsync(stoppingToken).ConfigureAwait(false);
        }
    }

    private async Task ScanAndMigrateAsync(CancellationToken ct)
    {
        try
        {
            // 用 Scope 拿 ITenantProvisioningRepository（Scoped）
            await using var scope = _serviceProvider.CreateAsyncScope();
            var repo = scope.ServiceProvider.GetService<ITenantProvisioningRepository>();
            if (repo is null)
            {
                // 门户模块未注册本接口（可能开发期），跳过
                return;
            }

            var tenantIds = await repo.GetProvisioningTenantIdsAsync(ct).ConfigureAwait(false);
            if (tenantIds.Count == 0)
            {
                return;
            }

            LogScanFound(tenantIds.Count);

            // 按 BatchSize 分批（灰度：每次只处理前 N 个，避免一次性压垮租户库）
            var batch = _options.Value.BatchSize > 0
                ? tenantIds.Take(_options.Value.BatchSize).ToList()
                : tenantIds;

            if (batch.Count < tenantIds.Count)
            {
                LogBatchLimitApplied(batch.Count, tenantIds.Count);
            }

            // 并发度控制（Dataflow ActionBlock）
            var options = new ExecutionDataflowBlockOptions
            {
                MaxDegreeOfParallelism = _options.Value.MigrationConcurrency,
                CancellationToken = ct,
            };

            var block = new ActionBlock<long>(
                tenantId => MigrateOneTenantAsync(tenantId, repo, ct),
                options);

            foreach (var id in batch)
            {
                block.Post(id);
            }

            block.Complete();
            await block.Completion.ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogScanError(ex);
        }
    }

    private async Task MigrateOneTenantAsync(
        long tenantId,
        ITenantProvisioningRepository repo,
        CancellationToken ct)
    {
        try
        {
            LogTenantStart(tenantId);

            // 1. 检查是否超过最大重试次数（超过则跳过，标记失败）
            var tenant = await FindTenantWithAttemptCountAsync(repo, tenantId, ct).ConfigureAwait(false);
            if (tenant.HasValue && tenant.Value.attemptCount >= _options.Value.MaxRetryCount)
            {
                LogTenantMaxRetryReached(tenantId, tenant.Value.attemptCount);
                await repo.MarkTenantProvisioningFailedAsync(
                    tenantId,
                    $"已达最大尝试次数 {tenant.Value.attemptCount}/{_options.Value.MaxRetryCount}，停止自动重试，等待人工介入",
                    ct).ConfigureAwait(false);
                return;
            }

            // 2. 解析连接并按模块迁移（建库 + 应用锁 + 依赖序；失败不删库）
            var connectionInfo = _resolver.Resolve(tenantId);
            try
            {
                await _runner.MigrateTenantAsync(tenantId, connectionInfo, ct).ConfigureAwait(false);
            }
            catch (TenantModuleMigrationException ex)
            {
                await HandleModuleFailureAsync(tenantId, connectionInfo, ex.Plan, repo, ex, ct).ConfigureAwait(false);
                return;
            }

            // 3. 全部成功
            await repo.MarkTenantActiveAsync(tenantId, ct).ConfigureAwait(false);
            LogTenantDone(tenantId);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogTenantError(tenantId, ex);
            try
            {
                await repo.MarkTenantProvisioningFailedAsync(tenantId, ex.Message, ct).ConfigureAwait(false);
            }
            catch
            {
                // 忽略
            }
        }
    }

    /// <summary>
    /// 模块迁移失败的处理：
    /// <para>
    /// ① 默认**保留**数据库与已迁移表（<see cref="TenantMigrationOptions.AllowDropOnFirstModuleFailure"/>=false），仅标记失败待重试/人工介入；
    /// ② 仅当显式开启开关且失败的是首个模块时，才清理独立租户库重来；
    /// ③ 共享库永不清理（本服务不处理共享租户，且连接信息带 <see cref="TenantConnectionInfo.IsShared"/> 时直接拒绝删库）。
    /// </para>
    /// </summary>
    private async Task HandleModuleFailureAsync(
        long tenantId,
        TenantConnectionInfo connectionInfo,
        ITenantMigrationPlan failedPlan,
        ITenantProvisioningRepository repo,
        Exception ex,
        CancellationToken ct)
    {
        try
        {
            if (!connectionInfo.IsShared && _options.Value.AllowDropOnFirstModuleFailure && IsFirstModule(failedPlan))
            {
                LogRollbackAttempt(tenantId, connectionInfo.DbName);
                await TryEnsureDeletedAsync(connectionInfo, ct).ConfigureAwait(false);
                LogRollbackDone(tenantId);

                // 清库后重置该租户的连接池闸门（信号量状态作废，下次重建）
                if (_serviceProvider.GetService<ITenantConnectionPool>() is { } pool)
                {
                    pool.Reset(tenantId);
                }
            }

            await repo.MarkTenantProvisioningFailedAsync(
                tenantId,
                $"模块 {failedPlan.ModuleCode} 迁移失败（已保留数据库与表，可重试）：{ex.Message}",
                ct).ConfigureAwait(false);
        }
        catch (Exception cleanupEx)
        {
            LogRollbackError(tenantId, cleanupEx);
            // 兜底：不管清理成功与否，标记失败
            await repo.MarkTenantProvisioningFailedAsync(
                tenantId,
                $"模块 {failedPlan.ModuleCode} 迁移失败 + 清理回滚异常：{cleanupEx.Message}",
                ct).ConfigureAwait(false);
        }
    }

    private static bool IsFirstModule(ITenantMigrationPlan plan) => plan.DependsOn.Count == 0;

    private static async Task TryEnsureDeletedAsync(TenantConnectionInfo connectionInfo, CancellationToken ct)
    {
        var masterConnString = connectionInfo.ToBuilder("master").ConnectionString;
        await using var connection = new SqlConnection(masterConnString);
        await connection.OpenAsync(ct).ConfigureAwait(false);

        // 置单用户模式后 DROP DATABASE
        var setSingle = connection.CreateCommand();
        setSingle.CommandText = $"ALTER DATABASE [{SqlEscape(connectionInfo.DbName)}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE";
        await setSingle.ExecuteNonQueryAsync(ct).ConfigureAwait(false);

        var dropCmd = connection.CreateCommand();
        dropCmd.CommandText = $"DROP DATABASE [{SqlEscape(connectionInfo.DbName)}]";
        await dropCmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private static string SqlEscape(string name) => name.Replace("]", "]]");

    /// <summary>
    /// 查找租户的预配尝试次数与最近失败原因（失败时用于判定是否超限）。
    /// BuildingBlocks 层不直接依赖 PortalDbContext，通过 <see cref="ITenantProvisioningRepository.GetTenantAttemptCountAsync"/> 契约获取。
    /// </summary>
    private static async Task<(int attemptCount, string? lastError)?> FindTenantWithAttemptCountAsync(
        ITenantProvisioningRepository repo, long tenantId, CancellationToken ct)
    {
        return await repo.GetTenantAttemptCountAsync(tenantId, ct).ConfigureAwait(false);
    }

    // LoggerMessage 日志（partial class）
    [LoggerMessage(Level = LogLevel.Information, Message = "租户预配扫描完成，发现 {Count} 个待迁移租户")]
    private partial void LogScanFound(int count);

    [LoggerMessage(Level = LogLevel.Trace, Message = "开始迁移租户 {TenantId}")]
    private partial void LogTenantStart(long tenantId);

    [LoggerMessage(Level = LogLevel.Information, Message = "租户 {TenantId} 全部模块迁移完成")]
    private partial void LogTenantDone(long tenantId);

    [LoggerMessage(Level = LogLevel.Error, Message = "租户 {TenantId} 迁移失败（详细异常见日志）")]
    private partial void LogTenantError(long tenantId, Exception ex);

    [LoggerMessage(Level = LogLevel.Warning, Message = "租户迁移扫描异常（详细异常见日志）")]
    private partial void LogScanError(Exception ex);

    [LoggerMessage(Level = LogLevel.Information, Message = "灰度分批：本次处理 {BatchSize}/{TotalCount} 个租户")]
    private partial void LogBatchLimitApplied(int batchSize, int totalCount);

    [LoggerMessage(Level = LogLevel.Warning, Message = "租户 {TenantId} 已达最大尝试次数 {AttemptCount}，停止自动重试")]
    private partial void LogTenantMaxRetryReached(long tenantId, int attemptCount);

    [LoggerMessage(Level = LogLevel.Information, Message = "租户 {TenantId} 首个模块迁移失败，尝试清理租户数据库 [{DbName}] 以便下次重试")]
    private partial void LogRollbackAttempt(long tenantId, string dbName);

    [LoggerMessage(Level = LogLevel.Information, Message = "租户 {TenantId} 数据库清理完成，下次扫描将重新创建")]
    private partial void LogRollbackDone(long tenantId);

    [LoggerMessage(Level = LogLevel.Error, Message = "租户 {TenantId} 清理回滚异常（详细异常见日志）")]
    private partial void LogRollbackError(long tenantId, Exception ex);
}
