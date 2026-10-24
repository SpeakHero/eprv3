/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-22 16:30:00 +08:00 中国标准时间 星期四
 * 修改时间：2026-10-22 16:30:00 +08:00 中国标准时间 星期四
 * 描述：升级任务仓储与隔离巡检落库的门户库实现（ITenantUpgradeJobRepository + ITenantIsolationAuditRepository）
 */

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Qlx.Abstractions.Tenancy;
using Qlx.BuildingBlocks.Ids;
using Qlx.Modules.Portal.Domain.Tenancy;

namespace Qlx.Modules.Portal.Services;

/// <summary>
/// <see cref="ITenantUpgradeJobRepository"/> 与 <see cref="ITenantIsolationAuditRepository"/> 的门户库实现。
/// 仅依赖 <see cref="PortalDbContext"/> 固定连接串门户库；同实例注册两个接口（Scoped）。
/// </summary>
public sealed class PortalTenantUpgradeRepository(
    PortalDbContext db,
    ISnowflakeIdGenerator snowflake,
    IOptions<PortalProvisioningOptions> provisioning)
    : ITenantUpgradeJobRepository, ITenantIsolationAuditRepository
{
    /// <summary>进行中 / 待重试之外视为历史任务的终态集合（Done / Cleaned）。</summary>
    private static readonly TenantUpgradeStatus[] HistoricalStatuses =
        [TenantUpgradeStatus.Done, TenantUpgradeStatus.Cleaned];

    /// <inheritdoc />
    public async Task<long?> TryCreateJobAsync(long tenantId, string operatorName, CancellationToken cancellationToken)
    {
        // Failed 未重试也视为占用（必须显式重试，避免失败现场被新任务覆盖）
        var hasActive = await db.TenantUpgradeJobs
            .AsNoTracking()
            .AnyAsync(j => j.TenantId == tenantId && !HistoricalStatuses.Contains(j.Status), cancellationToken)
            .ConfigureAwait(false);
        if (hasActive)
        {
            return null;
        }

        var job = new TenantUpgradeJob(tenantId, operatorName);
        job.AssignId(snowflake.NextId());
        db.TenantUpgradeJobs.Add(job);

        try
        {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return job.Id;
        }
        catch (DbUpdateException)
        {
            // 并发登记命中唯一过滤索引兜底：视为已存在进行中任务
            db.Entry(job).State = EntityState.Detached;
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<long>> GetPendingJobIdsAsync(CancellationToken cancellationToken)
    {
        return await db.TenantUpgradeJobs
            .AsNoTracking()
            .Where(j => j.Status == TenantUpgradeStatus.Pending)
            .OrderBy(j => j.Id)
            .Select(j => j.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<TenantUpgradeJobSnapshot?> GetJobSnapshotAsync(long jobId, CancellationToken cancellationToken)
    {
        return await db.TenantUpgradeJobs
            .AsNoTracking()
            .Where(j => j.Id == jobId)
            .Select(j => new TenantUpgradeJobSnapshot(
                j.Id, j.TenantId, j.Status, j.ModuleCode, j.CurrentTable, j.RowsCopied,
                j.ErrorMessage, j.RetryCount, j.OperatorName, j.StartedAt, j.CompletedAt))
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task AdvanceStatusAsync(
        long jobId, TenantUpgradeStatus toStatus, string? moduleCode, string? currentTable,
        long rowsCopied, CancellationToken cancellationToken)
    {
        var job = await db.TenantUpgradeJobs.FindAsync([jobId], cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"升级任务 {jobId} 不存在");

        job.MarkStatus(toStatus, moduleCode, currentTable, rowsCopied);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task MarkFailedAsync(long jobId, string errorMessage, CancellationToken cancellationToken)
    {
        var job = await db.TenantUpgradeJobs.FindAsync([jobId], cancellationToken).ConfigureAwait(false);
        if (job is null)
        {
            return;
        }

        job.MarkFailed(errorMessage);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<bool> RetryJobAsync(long jobId, CancellationToken cancellationToken)
    {
        var job = await db.TenantUpgradeJobs.FindAsync([jobId], cancellationToken).ConfigureAwait(false);
        if (job is null || job.Status != TenantUpgradeStatus.Failed)
        {
            return false;
        }

        job.MarkRetry();
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    /// <inheritdoc />
    public async Task UpsertCheckpointAsync(
        long jobId, string moduleCode, string tableName, long lastKey, long rowsCopied, CancellationToken cancellationToken)
    {
        var checkpoint = await db.TenantUpgradeCheckpoints
            .FirstOrDefaultAsync(
                c => c.JobId == jobId && c.ModuleCode == moduleCode && c.TableName == tableName,
                cancellationToken)
            .ConfigureAwait(false);

        if (checkpoint is null)
        {
            checkpoint = new TenantUpgradeCheckpoint(jobId, moduleCode, tableName, lastKey, rowsCopied);
            checkpoint.AssignId(snowflake.NextId());
            db.TenantUpgradeCheckpoints.Add(checkpoint);
        }
        else
        {
            checkpoint.Update(lastKey, rowsCopied);
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<TenantUpgradeCheckpointSnapshot>> GetCheckpointsAsync(
        long jobId, CancellationToken cancellationToken)
    {
        return await db.TenantUpgradeCheckpoints
            .AsNoTracking()
            .Where(c => c.JobId == jobId)
            .Select(c => new TenantUpgradeCheckpointSnapshot(c.ModuleCode, c.TableName, c.LastKey, c.RowsCopied))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IsolatedTargetSnapshot?> GetIsolatedTargetAsync(long tenantId, CancellationToken cancellationToken)
    {
        var tenantCode = await db.Tenants
            .AsNoTracking()
            .Where(t => t.Id == tenantId)
            .Select(t => t.TenantCode)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (tenantCode is null)
        {
            return null;
        }

        var options = provisioning.Value;
        return new IsolatedTargetSnapshot(
            options.DbHost,
            options.DbPort,
            options.DbNamePrefix + tenantCode.Replace('-', '_'));
    }

    /// <inheritdoc />
    public async Task<bool> TryCutoverAsync(
        long tenantId, string dbHost, int dbPort, string dbName, CancellationToken cancellationToken)
    {
        var tenant = await db.Tenants
            .Include(t => t.Database)
            .FirstOrDefaultAsync(t => t.Id == tenantId, cancellationToken)
            .ConfigureAwait(false);

        if (tenant?.Database is null || tenant.DatabaseMode != TenantDatabaseMode.Shared)
        {
            return false;
        }

        tenant.ChangeDatabaseMode(TenantDatabaseMode.Isolated);
        tenant.Database.RelocateTo(dbHost, dbPort, dbName);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<TenantUpgradeJobSnapshot>> GetJobsAwaitingCleanupAsync(
        DateTimeOffset completedBefore, CancellationToken cancellationToken)
    {
        return await db.TenantUpgradeJobs
            .AsNoTracking()
            .Where(j => j.Status == TenantUpgradeStatus.Done && j.CompletedAt < completedBefore)
            .OrderBy(j => j.Id)
            .Select(j => new TenantUpgradeJobSnapshot(
                j.Id, j.TenantId, j.Status, j.ModuleCode, j.CurrentTable, j.RowsCopied,
                j.ErrorMessage, j.RetryCount, j.OperatorName, j.StartedAt, j.CompletedAt))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task MarkCleanedAsync(long jobId, CancellationToken cancellationToken)
    {
        var job = await db.TenantUpgradeJobs.FindAsync([jobId], cancellationToken).ConfigureAwait(false);
        if (job is null)
        {
            return;
        }

        job.MarkStatus(TenantUpgradeStatus.Cleaned, null, null, job.RowsCopied);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<TenantUpgradeJobSnapshot?> GetLatestJobByTenantAsync(long tenantId, CancellationToken cancellationToken)
    {
        return await db.TenantUpgradeJobs
            .AsNoTracking()
            .Where(j => j.TenantId == tenantId)
            .OrderByDescending(j => j.Id)
            .Select(j => new TenantUpgradeJobSnapshot(
                j.Id, j.TenantId, j.Status, j.ModuleCode, j.CurrentTable, j.RowsCopied,
                j.ErrorMessage, j.RetryCount, j.OperatorName, j.StartedAt, j.CompletedAt))
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<long>> GetActiveTenantIdsAsync(CancellationToken cancellationToken)
    {
        return await db.Tenants
            .AsNoTracking()
            .Where(t => t.Status == TenantStatus.Active)
            .Select(t => t.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task SaveReportAsync(
        long tenantId, bool isHealthy, bool hasCritical, int findingCount,
        string findingsJson, string reportText, CancellationToken cancellationToken)
    {
        var record = new TenantIsolationAuditRecord(
            tenantId, isHealthy, hasCritical, findingCount, findingsJson, reportText);
        record.AssignId(snowflake.NextId());
        db.TenantIsolationAuditRecords.Add(record);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
