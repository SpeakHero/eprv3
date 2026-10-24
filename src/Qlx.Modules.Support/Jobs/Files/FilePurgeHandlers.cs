/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-23 02:40:00 +08:00 中国标准时间 星期五
 * 描述：文件物理清理任务处理器（ASYNC 周期调度）：临时区到期清理（file.temp.purge）
 *       与回收站到期清理（file.recycle.cleanup）。物理删除前必须通过同存储键存活引用检查（规则 R6）
 */

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Qlx.BuildingBlocks.Files;
using Qlx.Abstractions.Jobs;
using Qlx.Modules.Support.Domain;
using Qlx.Modules.Support.Options;

namespace Qlx.Modules.Support.Jobs.Files;

/// <summary>
/// 物理清理共享助手：同存储键存活引用检查（排除已物理清理对象后仍有其他版本引用则禁止删物理文件）。
/// </summary>
internal static class FilePurgeHelpers
{
    /// <summary>
    /// 判定存储键是否仍被其他存活对象引用（规则 R6）。引用判定基于
    /// 版本表联接存活对象（状态非 PURGED）——秒传复用同键的 Normal / Recycled 对象均算存活。
    /// </summary>
    /// <param name="db">租户上下文。</param>
    /// <param name="storageKey">存储键。</param>
    /// <param name="excludeObjectId">待清理对象 Id（自身版本不计入）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>仍有其他存活引用返回 true。</returns>
    public static Task<bool> HasOtherAliveReferenceAsync(
        SupportTenantDbContext db, string storageKey, long excludeObjectId, CancellationToken cancellationToken)
        => db.FileVersions
            .AsNoTracking()
            .Where(v => v.StorageKey == storageKey && v.ObjectId != excludeObjectId)
            .Join(
                db.StorageObjects.Where(o => o.Status != StorageObjectStatus.Purged),
                static v => v.ObjectId,
                static o => o.Id,
                static (v, o) => o.Id)
            .AnyAsync(cancellationToken);
}

/// <summary>
/// 临时区到期清理处理器（file.temp.purge，ASYNC）。规则 R4：过期未消费的临时件
/// 置 EXPIRED，关联对象标记 PURGED；同存储键无其他存活引用时物理删除文件内容。
/// 单文件物理删除失败仅记日志跳过（保持 Active 下轮重试），不中断整批清理。
/// </summary>
/// <param name="db">租户上下文。</param>
/// <param name="storageBackend">物理存储后端。</param>
/// <param name="logger">日志器。</param>
public sealed partial class FileTempPurgeHandler(
    SupportTenantDbContext db,
    IFileStorageBackend storageBackend,
    ILogger<FileTempPurgeHandler> logger) : IJobHandler
{
    /// <summary>单轮清理上限（调度周期性执行兜底剩余量，避免单次执行超出任务超时）。</summary>
    private const int MaxBatchSize = 500;

    private readonly SupportTenantDbContext _db = db;
    private readonly IFileStorageBackend _storageBackend = storageBackend;
    private readonly ILogger<FileTempPurgeHandler> _logger = logger;

    /// <inheritdoc />
    public string TaskCode => FileTaskCodes.TempPurge;

    /// <inheritdoc />
    public async Task<CompletedResult> ExecuteAsync(JobContext context, CancellationToken cancellationToken)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        // 参数 beforeTime 优先（运维手工指定清理截止）；缺省按当前时间（临时件到期即清）
        var now = DateTimeOffset.UtcNow;
        var before = context.Parameters.TryGetValue(JobTaskCodes.ParamKeyBeforeTime, out var rawValue)
            && rawValue is string beforeText
            && DateTimeOffset.TryParse(beforeText, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AssumeUniversal, out var parsed)
                ? parsed.ToUniversalTime()
                : now;

        var tempFiles = await _db.TempFiles
            .Where(t => t.Status == TempFileStatus.Active && t.ExpireAt <= before)
            .OrderBy(t => t.ExpireAt)
            .Take(MaxBatchSize)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        if (tempFiles.Count == 0)
        {
            return new CompletedResult(
                new { Scanned = 0, Purged = 0, PhysicallyDeleted = 0, Skipped = 0, Before = before },
                null, null, stopwatch.ElapsedMilliseconds, IsSuccess: true);
        }

        var objectIds = tempFiles.Select(static t => t.ObjectId).Distinct().ToList();

        // 批量拉取对象与当前版本（两条 SQL 预载，禁 N+1）
        var objects = await _db.StorageObjects
            .Where(o => objectIds.Contains(o.Id))
            .ToDictionaryAsync(static o => o.Id, cancellationToken)
            .ConfigureAwait(false);
        var currentVersionKeys = await _db.FileVersions
            .AsNoTracking()
            .Where(v => objectIds.Contains(v.ObjectId))
            .Select(v => new { v.ObjectId, v.VerNo, v.StorageKey })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var storageKeyByObjectId = currentVersionKeys
            .GroupBy(static v => v.ObjectId)
            .ToDictionary(static g => g.Key, static g => g.OrderByDescending(static v => v.VerNo).First().StorageKey);

        var purgedCount = 0;
        var physicallyDeleted = 0;
        var skipped = 0;
        foreach (var tempFile in tempFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!objects.TryGetValue(tempFile.ObjectId, out var target))
            {
                // 临时件关联对象缺失（脏数据）：仅置过期，避免每轮重复扫描
                tempFile.Status = TempFileStatus.Expired;
                purgedCount++;
                LogOrphanTempFile(tempFile.Id, tempFile.ObjectId);
                continue;
            }

            if (target.ObjectType == StorageObjectType.File
                && storageKeyByObjectId.TryGetValue(target.Id, out var storageKey))
            {
                var deleted = await TryDeletePhysicalFileAsync(target.Id, storageKey, cancellationToken).ConfigureAwait(false);
                if (!deleted)
                {
                    skipped++;
                    continue;
                }

                physicallyDeleted++;
            }

            // 目录无物理文件直接标记；文件物理删除成功后标记（失败保持原状下轮重试）
            target.MarkPurged();
            tempFile.Status = TempFileStatus.Expired;
            purgedCount++;
        }

        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        LogPurgeSummary(_db.CurrentTenantId, tempFiles.Count, purgedCount, physicallyDeleted, skipped);

        return new CompletedResult(
            new { Scanned = tempFiles.Count, Purged = purgedCount, PhysicallyDeleted = physicallyDeleted, Skipped = skipped, Before = before },
            null, null, stopwatch.ElapsedMilliseconds, IsSuccess: true);
    }

    /// <summary>物理删除单文件（存活引用检查 + 后端删除）；失败记日志返回 false（不中断整批）。</summary>
    /// <param name="objectId">对象 Id。</param>
    /// <param name="storageKey">存储键。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>物理文件已删除返回 true；被引用或删除失败返回 false。</returns>
    private async Task<bool> TryDeletePhysicalFileAsync(long objectId, string storageKey, CancellationToken cancellationToken)
    {
        if (await FilePurgeHelpers.HasOtherAliveReferenceAsync(_db, storageKey, objectId, cancellationToken).ConfigureAwait(false))
        {
            // 同键仍被其他存活对象复用：仅扣减逻辑引用，物理文件保留（规则 R6）
            return true;
        }

        try
        {
            await _storageBackend.DeleteAsync(storageKey, cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogPhysicalDeleteFailed(objectId, storageKey, ex);
            return false;
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "临时件 {TempFileId} 关联的存储对象 {ObjectId} 缺失（脏数据），仅置过期")]
    private partial void LogOrphanTempFile(long tempFileId, long objectId);

    [LoggerMessage(Level = LogLevel.Error, Message = "对象 {ObjectId} 物理文件删除失败（保留原状下轮重试）：{StorageKey}")]
    private partial void LogPhysicalDeleteFailed(long objectId, string storageKey, Exception exception);

    [LoggerMessage(Level = LogLevel.Information, Message = "租户 {TenantId} 临时区清理完成：扫描 {Scanned}，清理 {Purged}，物理删除 {Deleted}，跳过 {Skipped}")]
    private partial void LogPurgeSummary(long tenantId, int scanned, int purged, int deleted, int skipped);
}

/// <summary>
/// 回收站到期清理处理器（file.recycle.cleanup，ASYNC）。规则 R3：回收站保留 RecycleDays 天，
/// 到期对象标记 PURGED；文件对象在同存储键无其他存活引用时物理删除内容（规则 R6），
/// 目录对象无物理文件直接标记。单文件物理删除失败仅记日志跳过，不中断整批清理。
/// </summary>
/// <param name="db">租户上下文。</param>
/// <param name="storageBackend">物理存储后端。</param>
/// <param name="filesOptions">文件中心配置（回收站保留天数）。</param>
/// <param name="logger">日志器。</param>
public sealed partial class FileRecycleCleanupHandler(
    SupportTenantDbContext db,
    IFileStorageBackend storageBackend,
    IOptions<SupportFilesOptions> filesOptions,
    ILogger<FileRecycleCleanupHandler> logger) : IJobHandler
{
    /// <summary>单轮清理上限（调度周期性执行兜底剩余量，避免单次执行超出任务超时）。</summary>
    private const int MaxBatchSize = 500;

    private readonly SupportTenantDbContext _db = db;
    private readonly IFileStorageBackend _storageBackend = storageBackend;
    private readonly SupportFilesOptions _filesOptions = filesOptions.Value;
    private readonly ILogger<FileRecycleCleanupHandler> _logger = logger;

    /// <inheritdoc />
    public string TaskCode => FileTaskCodes.RecycleCleanup;

    /// <inheritdoc />
    public async Task<CompletedResult> ExecuteAsync(JobContext context, CancellationToken cancellationToken)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        // 参数 beforeTime 优先（运维手工指定清理截止）；缺省按保留期推算（RecycledAt + RecycleDays）
        var now = DateTimeOffset.UtcNow;
        var before = context.Parameters.TryGetValue(JobTaskCodes.ParamKeyBeforeTime, out var rawValue)
            && rawValue is string beforeText
            && DateTimeOffset.TryParse(beforeText, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AssumeUniversal, out var parsed)
                ? parsed.ToUniversalTime()
                : now - TimeSpan.FromDays(_filesOptions.RecycleDays);

        var targets = await _db.StorageObjects
            .Where(o => o.Status == StorageObjectStatus.Recycled
                && o.RecycledAt != null
                && o.RecycledAt <= before)
            .OrderBy(o => o.RecycledAt)
            .Take(MaxBatchSize)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        if (targets.Count == 0)
        {
            return new CompletedResult(
                new { Scanned = 0, Purged = 0, PhysicallyDeleted = 0, Skipped = 0, Before = before, RecycleDays = _filesOptions.RecycleDays },
                null, null, stopwatch.ElapsedMilliseconds, IsSuccess: true);
        }

        // 批量拉取文件对象的当前版本存储键（单条 SQL 预载，禁 N+1）
        var fileIds = targets
            .Where(static o => o.ObjectType == StorageObjectType.File)
            .Select(static o => o.Id)
            .ToList();
        var currentVersionKeys = await _db.FileVersions
            .AsNoTracking()
            .Where(v => fileIds.Contains(v.ObjectId))
            .Select(v => new { v.ObjectId, v.VerNo, v.StorageKey })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var storageKeyByObjectId = currentVersionKeys
            .GroupBy(static v => v.ObjectId)
            .ToDictionary(static g => g.Key, static g => g.OrderByDescending(static v => v.VerNo).First().StorageKey);

        var purgedCount = 0;
        var physicallyDeleted = 0;
        var skipped = 0;
        foreach (var target in targets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (target.ObjectType == StorageObjectType.File
                && storageKeyByObjectId.TryGetValue(target.Id, out var storageKey))
            {
                if (await FilePurgeHelpers
                        .HasOtherAliveReferenceAsync(_db, storageKey, target.Id, cancellationToken)
                        .ConfigureAwait(false))
                {
                    // 同键仍被其他存活对象复用：仅扣减逻辑引用，物理文件保留（规则 R6）
                    target.MarkPurged();
                    purgedCount++;
                    continue;
                }

                try
                {
                    await _storageBackend.DeleteAsync(storageKey, cancellationToken).ConfigureAwait(false);
                    physicallyDeleted++;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // 物理删除失败保留 Recycled 状态下轮重试（不标记 PURGED，避免产生孤儿物理文件）
                    skipped++;
                    LogPhysicalDeleteFailed(target.Id, storageKey, ex);
                    continue;
                }
            }

            // 目录无物理文件直接标记；文件物理删除成功后标记
            target.MarkPurged();
            purgedCount++;
        }

        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        LogRecycleCleanupSummary(_db.CurrentTenantId, targets.Count, purgedCount, physicallyDeleted, skipped);

        return new CompletedResult(
            new { Scanned = targets.Count, Purged = purgedCount, PhysicallyDeleted = physicallyDeleted, Skipped = skipped, Before = before, RecycleDays = _filesOptions.RecycleDays },
            null, null, stopwatch.ElapsedMilliseconds, IsSuccess: true);
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "对象 {ObjectId} 物理文件删除失败（保留回收站状态下轮重试）：{StorageKey}")]
    private partial void LogPhysicalDeleteFailed(long objectId, string storageKey, Exception exception);

    [LoggerMessage(Level = LogLevel.Information, Message = "租户 {TenantId} 回收站清理完成：扫描 {Scanned}，清理 {Purged}，物理删除 {Deleted}，跳过 {Skipped}")]
    private partial void LogRecycleCleanupSummary(long tenantId, int scanned, int purged, int deleted, int skipped);
}
