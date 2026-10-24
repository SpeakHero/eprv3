/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-24
 * 修改时间：2026-10-24
 * 描述：大文件分片组装任务处理器（file.chunk.assemble，ASYNC）：
 *       按序读取分片 → 合并计算 SHA-256 → 校验期望哈希 → 配额校验 → 落盘为正式存储对象 → 清理分片临时文件
 */

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Qlx.Abstractions.Exceptions;
using Qlx.Abstractions.Jobs;
using Qlx.BuildingBlocks.Files;
using Qlx.Modules.Support.Domain;
using Qlx.Modules.Support.Options;

namespace Qlx.Modules.Support.Jobs.Files;

/// <summary>
/// 大文件分片组装处理器（file.chunk.assemble，ASYNC）。
/// <para>
/// 入参：<see cref="FileJobParams.ChunkKeys"/>（按上传顺序排列的分片存储键）、
/// <see cref="FileJobParams.FileName"/>、<see cref="FileJobParams.ContentType"/>、<see cref="FileJobParams.OperatorId"/>；
/// <see cref="FileJobParams.Hash"/>（客户端声明的 SHA-256，用于完整性校验）、
/// <see cref="FileJobParams.BizDomain"/>、<see cref="FileJobParams.TargetPath"/>（目标展示路径）。
/// </para>
/// <para>
/// 组装完成后：新建正式 <see cref="StorageObject"/> + <see cref="FileVersion"/> 并记录
/// <c>CHUNK_ASSEMBLE</c> 操作日志；随后删除全部分片临时文件（临时区清理，规则 R4）。
/// 哈希不符时不删除分片，任务失败可重试（避免「校验失败但临时件已被清空」）。
/// </para>
/// </summary>
/// <param name="db">租户上下文。</param>
/// <param name="storageBackend">物理存储后端（仅文件任务处理器可调用）。</param>
/// <param name="filesOptions">文件存储中心配置（配额规则 R7）。</param>
/// <param name="logger">日志器。</param>
public sealed partial class FileChunkAssembleHandler(
    SupportTenantDbContext db,
    IFileStorageBackend storageBackend,
    IOptions<SupportFilesOptions> filesOptions,
    ILogger<FileChunkAssembleHandler> logger) : IJobHandler
{
    private readonly SupportTenantDbContext _db = db;
    private readonly IFileStorageBackend _storageBackend = storageBackend;
    private readonly SupportFilesOptions _options = filesOptions.Value;
    private readonly ILogger<FileChunkAssembleHandler> _logger = logger;

    /// <inheritdoc />
    public string TaskCode => FileTaskCodes.ChunkAssemble;

    /// <inheritdoc />
    public async Task<CompletedResult> ExecuteAsync(JobContext context, CancellationToken cancellationToken)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        var chunkKeys = ReadChunkKeys(context);
        var fileName = FileJobHelpers.GetRequiredString(context, FileJobParams.FileName);
        var contentType = FileJobHelpers.GetRequiredString(context, FileJobParams.ContentType);
        var operatorId = FileJobHelpers.GetRequiredString(context, FileJobParams.OperatorId);
        var expectedHash = FileJobHelpers.GetOptionalString(context, FileJobParams.Hash);
        var bizDomain = FileJobHelpers.GetOptionalString(context, FileJobParams.BizDomain)
            ?? SupportFileDomains.Unclassified;
        var path = FileJobHelpers.GetOptionalString(context, FileJobParams.TargetPath)
            ?? $"/{bizDomain}/{fileName}";

        ValidateFileName(fileName);
        ValidateTargetPath(path);

        // 分片必须全部存在，缺片直接失败（不产生半截物理文件）
        foreach (var chunkKey in chunkKeys)
        {
            if (!await _storageBackend.ExistsAsync(chunkKey, cancellationToken).ConfigureAwait(false))
            {
                throw new ValidationException(FileJobParams.ChunkKeys,
                    $"分片 {chunkKey} 不存在或已被清理，无法组装");
            }
        }

        var tenantId = _db.CurrentTenantId;
        var now = DateTimeOffset.UtcNow;

        // 合并到进程临时文件（DeleteOnClose：成功/失败/异常均自动清理，不污染存储区）
        var tempPath = Path.Combine(Path.GetTempPath(), $"qlx-chunk-{Guid.NewGuid():N}.tmp");
        string hash;
        long totalBytes;
        string finalStorageKey;
        await using (var merged = new FileStream(
            tempPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 81920, FileOptions.DeleteOnClose))
        {
            foreach (var chunkKey in chunkKeys)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await using var part = await _storageBackend
                    .OpenReadAsync(chunkKey, cancellationToken).ConfigureAwait(false);
                await part.CopyToAsync(merged, cancellationToken).ConfigureAwait(false);
            }

            totalBytes = merged.Length;
            merged.Position = 0;
            hash = await FileJobHelpers.ComputeSha256HexAsync(merged, cancellationToken).ConfigureAwait(false);

            // 完整性校验（客户端声明哈希与合并结果不一致 → 失败且保留分片以便重传）
            if (!string.IsNullOrWhiteSpace(expectedHash)
                && !string.Equals(expectedHash, hash, StringComparison.OrdinalIgnoreCase))
            {
                LogHashMismatch(context.TaskCode, expectedHash, hash, chunkKeys.Count);
                stopwatch.Stop();
                return new CompletedResult(
                    null, null, null, stopwatch.ElapsedMilliseconds, IsSuccess: false,
                    ErrorMessage: $"分片合并哈希校验失败（期望 {expectedHash}，实际 {hash}），分片已保留可重试。");
            }

            // 配额校验（规则 R7）：与直传同口径，避免大文件经分片通道绕过配额阻断
            var usedBytes = await _db.StorageObjects
                .Where(o => o.Status == StorageObjectStatus.Normal)
                .SumAsync(o => (long?)o.Size, cancellationToken)
                .ConfigureAwait(false) ?? 0;
            var blockThreshold = _options.QuotaTotalBytes * _options.QuotaBlockPercent / 100;
            if (usedBytes + totalBytes > blockThreshold)
            {
                throw new ConflictException(
                    $"存储配额已满（已用 {usedBytes} / 上限 {blockThreshold} 字节），分片组装被阻断。", "QUOTA_EXCEEDED");
            }

            // 秒传命中：复用既有物理文件并递增引用计数（规则 R6）
            var existingVersion = await _db.FileVersions
                .AsNoTracking()
                .Where(v => v.Hash == hash)
                .OrderBy(v => v.CreatedAt)
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);

            var deduplicated = existingVersion is not null;
            if (existingVersion is { } hit)
            {
                finalStorageKey = hit.StorageKey;
                var owner = await _db.StorageObjects
                    .SingleAsync(o => o.Id == hit.ObjectId, cancellationToken)
                    .ConfigureAwait(false);
                owner.RefCount++;
            }
            else
            {
                finalStorageKey = $"{hash[..2]}/{hash[2..4]}/{hash}.bin";
                merged.Position = 0;
                await _storageBackend.SaveAsync(finalStorageKey, merged, setReadOnly: false, cancellationToken)
                    .ConfigureAwait(false);
            }

            var storageObject = new StorageObject(
                tenantId, null, fileName, bizDomain, path, hash, totalBytes, contentType, operatorId);
            _db.StorageObjects.Add(storageObject);
            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            _db.FileVersions.Add(new FileVersion(
                tenantId, storageObject.Id, 1, hash, totalBytes, finalStorageKey, operatorId, now));
            _db.FileOpLogs.Add(new FileOpLog(tenantId, storageObject.Id, "CHUNK_ASSEMBLE", operatorId, null,
                $"分片组装：{chunkKeys.Count} 个分片，{totalBytes} 字节", now));
            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            // 临时区清理：只删分片自身，绝不删正式存储键（同键即秒传复用场景）
            var removedChunks = await RemoveChunkFilesAsync(chunkKeys, finalStorageKey, cancellationToken)
                .ConfigureAwait(false);

            stopwatch.Stop();
            LogAssembleCompleted(tenantId, storageObject.Id, chunkKeys.Count, totalBytes, deduplicated, removedChunks);

            return new CompletedResult(
                new FileUploadJobPayload(
                    storageObject.Id, finalStorageKey, hash, totalBytes, storageObject.Path, bizDomain,
                    deduplicated, storageObject.RefCount, Warning: null),
                null, null, stopwatch.ElapsedMilliseconds, IsSuccess: true);
        }
    }

    /// <summary>读取并按序校验分片存储键集合（必填、非空、无空项、无重复）。</summary>
    private static List<string> ReadChunkKeys(JobContext context)
    {
        if (!context.Parameters.TryGetValue(FileJobParams.ChunkKeys, out var raw) || raw is null)
        {
            throw new ValidationException(FileJobParams.ChunkKeys,
                $"任务 {context.TaskCode} 缺少必填参数 {FileJobParams.ChunkKeys}");
        }

        var keys = raw switch
        {
            IReadOnlyList<string> list => list.ToList(),
            IEnumerable<string> sequence => sequence.ToList(),
            string single => [single],
            _ => throw new ValidationException(FileJobParams.ChunkKeys,
                $"任务 {context.TaskCode} 参数 {FileJobParams.ChunkKeys} 必须为字符串集合"),
        };

        if (keys.Count == 0 || keys.Any(string.IsNullOrWhiteSpace))
        {
            throw new ValidationException(FileJobParams.ChunkKeys, "分片列表不能为空且不允许空白存储键");
        }

        if (keys.Distinct(StringComparer.Ordinal).Count() != keys.Count)
        {
            throw new ValidationException(FileJobParams.ChunkKeys, "分片列表存在重复存储键，拒绝重复合并");
        }

        return keys;
    }

    /// <summary>文件名安全校验：禁止路径穿越片段与路径分隔符（V-1 输入校验）。</summary>
    private static void ValidateFileName(string fileName)
    {
        if (fileName.Contains("..", StringComparison.Ordinal)
            || fileName.Contains('/', StringComparison.Ordinal)
            || fileName.Contains('\\', StringComparison.Ordinal)
            || fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            throw new ValidationException(FileJobParams.FileName, $"文件名非法：{fileName}");
        }
    }

    /// <summary>展示路径安全校验：必须以 <c>/</c> 开头且不含路径穿越片段（V-1 输入校验）。</summary>
    private static void ValidateTargetPath(string path)
    {
        if (!path.StartsWith('/')
            || path.Contains("..", StringComparison.Ordinal)
            || path.Contains('\\', StringComparison.Ordinal)
            || path.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
        {
            throw new ValidationException(FileJobParams.TargetPath, $"目标路径非法：{path}");
        }
    }

    /// <summary>删除分片临时文件（跳过与正式存储键相同的键）；单文件删除失败仅记日志不回滚。</summary>
    private async Task<int> RemoveChunkFilesAsync(
        IReadOnlyList<string> chunkKeys, string finalStorageKey, CancellationToken cancellationToken)
    {
        var removed = 0;
        foreach (var chunkKey in chunkKeys)
        {
            if (string.Equals(chunkKey, finalStorageKey, StringComparison.Ordinal))
            {
                continue;
            }

            try
            {
                await _storageBackend.DeleteAsync(chunkKey, cancellationToken).ConfigureAwait(false);
                removed++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogChunkDeleteFailed(chunkKey, ex);
            }
        }

        return removed;
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "任务 {TaskCode} 哈希校验失败：期望 {Expected}，实际 {Actual}（{ChunkCount} 个分片保留）")]
    private partial void LogHashMismatch(string taskCode, string expected, string actual, int chunkCount);

    [LoggerMessage(Level = LogLevel.Warning, Message = "分片临时文件 {ChunkKey} 删除失败（正式文件已落库，不影响业务）")]
    private partial void LogChunkDeleteFailed(string chunkKey, Exception exception);

    [LoggerMessage(Level = LogLevel.Information, Message = "租户 {TenantId} 分片组装完成：对象 {ObjectId}，{ChunkCount} 个分片，{SizeBytes} 字节，秒传复用={Deduplicated}，清理分片 {RemovedChunks}")]
    private partial void LogAssembleCompleted(
        long tenantId, long objectId, int chunkCount, long sizeBytes, bool deduplicated, int removedChunks);
}
