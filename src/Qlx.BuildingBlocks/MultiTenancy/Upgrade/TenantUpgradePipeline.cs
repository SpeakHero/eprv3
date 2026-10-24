/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-22 16:30:00 +08:00 中国标准时间 星期四
 * 修改时间：2026-10-22 16:30:00 +08:00 中国标准时间 星期四
 * 描述：租户升级流水线状态机：Preparing 建库迁移 → Copying 全量复制 → Verifying 校验 → Cutover 补差切换，支持断点续跑
 */

using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Qlx.Abstractions.Audit;
using Qlx.Abstractions.Constants;
using Qlx.Abstractions.Serialization;
using Qlx.Abstractions.Tenancy;
using Qlx.BuildingBlocks.Ids;

namespace Qlx.BuildingBlocks.MultiTenancy.Upgrade;

/// <summary>
/// 租户升级流水线（单任务执行体，由 <see cref="TenantUpgradeJobDispatcher"/> 串行调度）。
/// <para>
/// 按任务当前状态断点续跑：Pending → Preparing（建独立库 + 按模块迁移）→ Copying（全量复制，
/// 表级断点）→ Verifying（行数 + 抽样哈希）→ Cutover（增量补差 + 快速复核 + 门户路由切换 + 缓存刷新）→ Done。
/// </para>
/// <para>
/// 任一阶段异常：标记任务 Failed 并保留独立库现场（不删库），人工修复后可经管理 API 重试；
/// 重试后按当前状态继续（复制阶段凭断点跳过已复制批次）。
/// </para>
/// </summary>
internal sealed partial class TenantUpgradePipeline(
    TenantMigrationRunner runner,
    ITenantDatabaseResolver resolver,
    TenantDataCopier copier,
    TenantDataVerifier verifier,
    IOptions<TenantUpgradeOptions> options,
    IOptions<SharedDatabaseOptions> sharedDatabase,
    ISnowflakeIdGenerator snowflake,
    ILogger<TenantUpgradePipeline> logger)
{
    /// <summary>LoggerMessage 源生成器要求字段。</summary>
    private readonly ILogger<TenantUpgradePipeline> _logger = logger;

    /// <summary>
    /// 执行升级任务（按当前状态断点续跑至终态或失败）。
    /// </summary>
    /// <param name="jobId">任务 ID。</param>
    /// <param name="scopeProvider">当前作用域服务提供器（解析 Scoped 仓储与审计服务）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    internal async Task ExecuteAsync(long jobId, IServiceProvider scopeProvider, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scopeProvider);

        var repo = scopeProvider.GetRequiredService<ITenantUpgradeJobRepository>();
        var audit = scopeProvider.GetService<IAuditService>();
        var stopwatch = Stopwatch.StartNew();

        var snapshot = await repo.GetJobSnapshotAsync(jobId, cancellationToken).ConfigureAwait(false);
        if (snapshot is null || snapshot.Status is TenantUpgradeStatus.Done
            or TenantUpgradeStatus.Failed or TenantUpgradeStatus.Cleaned)
        {
            return;
        }

        var tenantId = snapshot.TenantId;
        try
        {
            await ExecuteCoreAsync(jobId, snapshot, repo, cancellationToken).ConfigureAwait(false);

            stopwatch.Stop();
            LogUpgradeDone(tenantId, jobId, stopwatch.ElapsedMilliseconds);

            if (audit is not null)
            {
                var final = await repo.GetJobSnapshotAsync(jobId, cancellationToken).ConfigureAwait(false);
                await audit.CaptureOperationAsync(new OperationLogEntry(
                    snowflake.NextId(), QlxAuditConstants.PortalModuleCode, TenantUpgradeAuditOperations.UpgradeDone,
                    $"租户升级独立库完成（任务 {jobId}，耗时 {stopwatch.ElapsedMilliseconds}ms）",
                    snapshot.OperatorName, DateTimeOffset.UtcNow, tenantId, QlxAuditConstants.TargetTenant, tenantId,
                    JsonSerializer.Serialize(new
                    {
                        jobId,
                        elapsedMs = stopwatch.ElapsedMilliseconds,
                        rowsCopied = final?.RowsCopied ?? 0,
                    }, QlxJsonDefaults.Default)), cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            stopwatch.Stop();
            LogUpgradeFailed(tenantId, jobId, ex);

            try
            {
                await repo.MarkFailedAsync(jobId, ex.Message, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception markEx) when (markEx is not OperationCanceledException)
            {
                LogMarkFailedError(jobId, markEx);
            }

            if (audit is not null)
            {
                try
                {
                    await audit.CaptureOperationAsync(new OperationLogEntry(
                        snowflake.NextId(), QlxAuditConstants.PortalModuleCode, TenantUpgradeAuditOperations.UpgradeFailed,
                        $"租户升级独立库失败（任务 {jobId}）：{ex.Message}",
                        snapshot.OperatorName, DateTimeOffset.UtcNow, tenantId, QlxAuditConstants.TargetTenant, tenantId),
                        cancellationToken).ConfigureAwait(false);
                }
                catch (Exception auditEx) when (auditEx is not OperationCanceledException)
                {
                    LogMarkFailedError(jobId, auditEx);
                }
            }
        }
    }

    /// <summary>状态机主体：逐阶段推进，任一阶段完成后即推进状态，崩溃可从下一阶段续跑。</summary>
    private async Task ExecuteCoreAsync(
        long jobId,
        TenantUpgradeJobSnapshot snapshot,
        ITenantUpgradeJobRepository repo,
        CancellationToken ct)
    {
        var tenantId = snapshot.TenantId;
        var status = snapshot.Status;
        var rowsCopied = snapshot.RowsCopied;

        var target = await repo.GetIsolatedTargetAsync(tenantId, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"租户 {tenantId} 不存在，无法解析升级目标库");

        var source = sharedDatabase.Value.ToConnectionInfo(tenantId);
        var targetInfo = new TenantConnectionInfo(
            tenantId, target.DbHost, target.DbPort, target.DbName, string.Empty, string.Empty);

        var modules = runner.GetSortedPlans()
            .Select(static p => (p.DbContextType, p.ModuleCode))
            .ToList();

        IReadOnlyList<TenantTableDescriptor>? tables = null;

        if (status is TenantUpgradeStatus.Pending)
        {
            await repo.AdvanceStatusAsync(jobId, TenantUpgradeStatus.Preparing, null, null, rowsCopied, ct)
                .ConfigureAwait(false);
            status = TenantUpgradeStatus.Preparing;
        }

        if (status is TenantUpgradeStatus.Preparing)
        {
            await runner.MigrateTenantAsync(tenantId, targetInfo, ct).ConfigureAwait(false);
            await repo.AdvanceStatusAsync(jobId, TenantUpgradeStatus.Copying, null, null, rowsCopied, ct)
                .ConfigureAwait(false);
            status = TenantUpgradeStatus.Copying;
        }

        if (status is TenantUpgradeStatus.Copying)
        {
            tables ??= copier.CollectModuleTables(modules, tenantId, source);
            var checkpoints = await LoadCheckpointMapAsync(repo, jobId, ct).ConfigureAwait(false);
            rowsCopied = await copier.CopyTenantDataAsync(
                tenantId, source, targetInfo, tables, checkpoints,
                (module, table, lastKey, rows, token) =>
                    repo.UpsertCheckpointAsync(jobId, module, table, lastKey, rows, token),
                ct).ConfigureAwait(false);
            await repo.AdvanceStatusAsync(jobId, TenantUpgradeStatus.Verifying, null, null, rowsCopied, ct)
                .ConfigureAwait(false);
            status = TenantUpgradeStatus.Verifying;
        }

        if (status is TenantUpgradeStatus.Verifying)
        {
            tables ??= copier.CollectModuleTables(modules, tenantId, source);
            await verifier.VerifyAsync(tenantId, source, targetInfo, tables, options.Value.VerifySampleSize, ct)
                .ConfigureAwait(false);
            await repo.AdvanceStatusAsync(jobId, TenantUpgradeStatus.Cutover, null, null, rowsCopied, ct)
                .ConfigureAwait(false);
            status = TenantUpgradeStatus.Cutover;
        }

        if (status is TenantUpgradeStatus.Cutover)
        {
            tables ??= copier.CollectModuleTables(modules, tenantId, source);
            var checkpoints = await LoadCheckpointMapAsync(repo, jobId, ct).ConfigureAwait(false);

            // 增量补差覆盖复制窗口内的写入，随后仅做行数复核（抽样哈希已在 Verifying 完成）
            await copier.CatchUpAsync(
                tenantId, source, targetInfo, tables, checkpoints,
                snapshot.StartedAt ?? DateTimeOffset.UtcNow, ct).ConfigureAwait(false);
            await verifier.VerifyAsync(tenantId, source, targetInfo, tables, sampleSize: 0, ct)
                .ConfigureAwait(false);

            var cutover = await repo.TryCutoverAsync(tenantId, target.DbHost, target.DbPort, target.DbName, ct)
                .ConfigureAwait(false);
            if (!cutover)
            {
                throw new InvalidOperationException(
                    $"租户 {tenantId} 门户路由切换失败：当前数据库模式不允许升级为独立库");
            }

            await resolver.RefreshAsync(tenantId, ct).ConfigureAwait(false);
            await repo.AdvanceStatusAsync(jobId, TenantUpgradeStatus.Done, null, null, rowsCopied, ct)
                .ConfigureAwait(false);
        }
    }

    private static async Task<IReadOnlyDictionary<string, TenantUpgradeCheckpointSnapshot>> LoadCheckpointMapAsync(
        ITenantUpgradeJobRepository repo, long jobId, CancellationToken ct)
    {
        var checkpoints = await repo.GetCheckpointsAsync(jobId, ct).ConfigureAwait(false);
        return checkpoints.ToDictionary(
            static c => $"{c.ModuleCode}|{c.TableName}", StringComparer.Ordinal);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "租户 {TenantId} 升级独立库完成（任务 {JobId}，耗时 {ElapsedMs}ms）")]
    private partial void LogUpgradeDone(long tenantId, long jobId, long elapsedMs);

    [LoggerMessage(Level = LogLevel.Error, Message = "租户 {TenantId} 升级独立库失败（任务 {JobId}，详细异常见日志）")]
    private partial void LogUpgradeFailed(long tenantId, long jobId, Exception ex);

    [LoggerMessage(Level = LogLevel.Error, Message = "升级任务 {JobId} 失败标记 / 审计写入异常（详细异常见日志）")]
    private partial void LogMarkFailedError(long jobId, Exception ex);
}
