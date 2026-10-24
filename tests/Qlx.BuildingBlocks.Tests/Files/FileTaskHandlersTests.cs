/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-24
 * 修改时间：2026-10-24
 * 描述：文件域真实任务处理器测试（R-7）：分片组装（合并/哈希校验/缺片拒绝/分片清理）
 *       与格式转换（目标格式校验、对象存在性、无引擎显式失败）
 */

using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Qlx.Abstractions.Exceptions;
using Qlx.Abstractions.Jobs;
using Qlx.BuildingBlocks.Files;
using Qlx.BuildingBlocks.Tests.Jobs;
using Qlx.Modules.Support;
using Qlx.Modules.Support.Domain;
using Qlx.Modules.Support.Jobs.Files;
using Qlx.Modules.Support.Options;

namespace Qlx.BuildingBlocks.Tests.Files;

/// <summary>
/// 分片组装 / 格式转换任务处理器测试。使用真实 <see cref="LocalFileStorageBackend"/>（临时目录）
/// 与 InMemory 租户库，验证落盘、落库、临时区清理与失败分支。
/// </summary>
public sealed class FileTaskHandlersTests : IDisposable
{
    private const long TenantId = 13L;
    private const string Operator = "9001";

    private readonly string _rootPath = Path.Combine(
        Path.GetTempPath(), $"qlx-file-tests-{Guid.NewGuid():N}");

    /// <inheritdoc />
    public void Dispose()
    {
        if (Directory.Exists(_rootPath))
        {
            Directory.Delete(_rootPath, recursive: true);
        }
    }

    private static SupportTenantDbContext CreateDbContext()
        => new(
            new JobStubTenantDatabaseResolver(),
            TenantId,
            new DbContextOptionsBuilder<SupportTenantDbContext>()
                .UseInMemoryDatabase($"file-handlers-{Guid.NewGuid():N}")
                .ConfigureWarnings(static w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                .Options);

    private LocalFileStorageBackend CreateBackend()
        => new(
            Options.Create(new LocalFileStorageOptions { Root = "store" }),
            _rootPath);

    private static FileChunkAssembleHandler CreateChunkHandler(
        SupportTenantDbContext db, IFileStorageBackend backend, SupportFilesOptions? options = null)
        => new(db, backend, Options.Create(options ?? new SupportFilesOptions()),
            NullLogger<FileChunkAssembleHandler>.Instance);

    private static JobContext CreateContext(string taskCode, Dictionary<string, object?> parameters)
        => new()
        {
            ExecutionId = Guid.NewGuid(),
            TaskCode = taskCode,
            TenantId = Guid.NewGuid(),
            Mode = ExecutionMode.Async,
            Parameters = parameters,
        };

    private static string Sha256Hex(byte[] content)
        => Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();

    // ============ 分片组装 ============

    [Fact]
    public async Task ChunkAssembleMergesChunksAndCleansTempFiles()
    {
        var backend = CreateBackend();
        string[] chunkKeys = ["tmp/chunk-1", "tmp/chunk-2", "tmp/chunk-3"];
        byte[][] parts = ["hello "u8.ToArray(), "chunked "u8.ToArray(), "world"u8.ToArray()];
        for (var i = 0; i < chunkKeys.Length; i++)
        {
            await using var content = new MemoryStream(parts[i]);
            await backend.SaveAsync(chunkKeys[i], content, setReadOnly: false, TestContext.Current.CancellationToken);
        }

        var mergedBytes = parts.SelectMany(static p => p).ToArray();
        var db = CreateDbContext();
        var handler = CreateChunkHandler(db, backend);

        var result = await handler.ExecuteAsync(
            CreateContext(FileTaskCodes.ChunkAssemble, new Dictionary<string, object?>
            {
                [FileJobParams.ChunkKeys] = chunkKeys,
                [FileJobParams.FileName] = "big.bin",
                [FileJobParams.ContentType] = "application/octet-stream",
                [FileJobParams.OperatorId] = Operator,
                [FileJobParams.Hash] = Sha256Hex(mergedBytes),
            }),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess, result.ErrorMessage);
        var payload = Assert.IsType<FileUploadJobPayload>(result.ResultJson);
        Assert.Equal(mergedBytes.Length, payload.SizeBytes);
        Assert.Equal(Sha256Hex(mergedBytes), payload.Hash);
        Assert.False(payload.Deduplicated);

        // 落库：正式对象 + 版本 + 操作日志
        Assert.Single(db.StorageObjects);
        Assert.Single(db.FileVersions);
        Assert.Contains(db.FileOpLogs, l => l.Action == "CHUNK_ASSEMBLE");

        // 物理：正式文件存在且内容正确，分片临时文件已清理
        var stored = await backend.OpenReadAsync(payload.StorageKey, TestContext.Current.CancellationToken);
        await using (stored)
        {
            using var buffer = new MemoryStream();
            await stored.CopyToAsync(buffer, TestContext.Current.CancellationToken);
            Assert.Equal(mergedBytes, buffer.ToArray());
        }

        foreach (var chunkKey in chunkKeys)
        {
            Assert.False(await backend.ExistsAsync(chunkKey, TestContext.Current.CancellationToken));
        }
    }

    [Fact]
    public async Task ChunkAssembleRejectsHashMismatchAndKeepsChunks()
    {
        var backend = CreateBackend();
        string[] chunkKeys = ["tmp/a", "tmp/b"];
        await using (var part = new MemoryStream("part-a"u8.ToArray()))
        {
            await backend.SaveAsync(chunkKeys[0], part, setReadOnly: false, TestContext.Current.CancellationToken);
        }

        await using (var part = new MemoryStream("part-b"u8.ToArray()))
        {
            await backend.SaveAsync(chunkKeys[1], part, setReadOnly: false, TestContext.Current.CancellationToken);
        }

        var db = CreateDbContext();
        var handler = CreateChunkHandler(db, backend);

        var result = await handler.ExecuteAsync(
            CreateContext(FileTaskCodes.ChunkAssemble, new Dictionary<string, object?>
            {
                [FileJobParams.ChunkKeys] = chunkKeys,
                [FileJobParams.FileName] = "broken.bin",
                [FileJobParams.ContentType] = "application/octet-stream",
                [FileJobParams.OperatorId] = Operator,
                [FileJobParams.Hash] = new string('0', 64),
            }),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Contains("哈希校验失败", result.ErrorMessage, StringComparison.Ordinal);
        Assert.Empty(db.StorageObjects);

        // 失败保留分片，允许重传/重试
        foreach (var chunkKey in chunkKeys)
        {
            Assert.True(await backend.ExistsAsync(chunkKey, TestContext.Current.CancellationToken));
        }
    }

    [Fact]
    public async Task ChunkAssembleRejectsMissingChunk()
    {
        var backend = CreateBackend();
        var db = CreateDbContext();
        var handler = CreateChunkHandler(db, backend);

        await Assert.ThrowsAsync<ValidationException>(() => handler.ExecuteAsync(
            CreateContext(FileTaskCodes.ChunkAssemble, new Dictionary<string, object?>
            {
                [FileJobParams.ChunkKeys] = new[] { "tmp/missing" },
                [FileJobParams.FileName] = "x.bin",
                [FileJobParams.ContentType] = "application/octet-stream",
                [FileJobParams.OperatorId] = Operator,
            }),
            TestContext.Current.CancellationToken));
        Assert.Empty(db.StorageObjects);
    }

    [Fact]
    public async Task ChunkAssembleRejectsDuplicateChunkKeys()
    {
        var backend = CreateBackend();
        var db = CreateDbContext();
        var handler = CreateChunkHandler(db, backend);

        await Assert.ThrowsAsync<ValidationException>(() => handler.ExecuteAsync(
            CreateContext(FileTaskCodes.ChunkAssemble, new Dictionary<string, object?>
            {
                [FileJobParams.ChunkKeys] = new[] { "tmp/dup", "tmp/dup" },
                [FileJobParams.FileName] = "x.bin",
                [FileJobParams.ContentType] = "application/octet-stream",
                [FileJobParams.OperatorId] = Operator,
            }),
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ChunkAssembleRejectsUnsafeFileName()
    {
        var backend = CreateBackend();
        var db = CreateDbContext();
        var handler = CreateChunkHandler(db, backend);

        await Assert.ThrowsAsync<ValidationException>(() => handler.ExecuteAsync(
            CreateContext(FileTaskCodes.ChunkAssemble, new Dictionary<string, object?>
            {
                [FileJobParams.ChunkKeys] = new[] { "tmp/any" },
                [FileJobParams.FileName] = "../escape.bin",
                [FileJobParams.ContentType] = "application/octet-stream",
                [FileJobParams.OperatorId] = Operator,
            }),
            TestContext.Current.CancellationToken));
    }

    // ============ 配额（规则 R7） ============

    [Fact]
    public async Task ChunkAssembleRejectsWhenQuotaBlocked()
    {
        var backend = CreateBackend();
        string[] chunkKeys = ["tmp/q1"];
        await using (var part = new MemoryStream(new byte[16]))
        {
            await backend.SaveAsync(chunkKeys[0], part, setReadOnly: false, TestContext.Current.CancellationToken);
        }

        var db = CreateDbContext();
        var handler = CreateChunkHandler(db, backend,
            new SupportFilesOptions { QuotaTotalBytes = 8, QuotaBlockPercent = 100 });

        await Assert.ThrowsAsync<ConflictException>(() => handler.ExecuteAsync(
            CreateContext(FileTaskCodes.ChunkAssemble, new Dictionary<string, object?>
            {
                [FileJobParams.ChunkKeys] = chunkKeys,
                [FileJobParams.FileName] = "quota.bin",
                [FileJobParams.ContentType] = "application/octet-stream",
                [FileJobParams.OperatorId] = Operator,
            }),
            TestContext.Current.CancellationToken));

        Assert.Empty(db.StorageObjects);
        Assert.True(await backend.ExistsAsync(chunkKeys[0], TestContext.Current.CancellationToken));
    }

    // ============ 格式转换 ============

    [Fact]
    public async Task FormatConvertFailsExplicitlyWhenEngineMissing()
    {
        var db = CreateDbContext();
        var target = new StorageObject(TenantId, null, "photo.png", "unclassified", "/unclassified/photo.png",
            "abc", 12, "image/png", Operator);
        target.AssignId(7001);
        db.StorageObjects.Add(target);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var handler = new FileFormatConvertHandler(db, NullLogger<FileFormatConvertHandler>.Instance);
        var result = await handler.ExecuteAsync(
            CreateContext(FileTaskCodes.FormatConvert, new Dictionary<string, object?>
            {
                [FileJobParams.FileId] = 7001L,
                [FileJobParams.TargetFormat] = ".jpg",
            }),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Contains("未启用文件格式转换引擎", result.ErrorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FormatConvertRejectsUnsupportedTargetFormat()
    {
        var db = CreateDbContext();
        var handler = new FileFormatConvertHandler(db, NullLogger<FileFormatConvertHandler>.Instance);

        await Assert.ThrowsAsync<ValidationException>(() => handler.ExecuteAsync(
            CreateContext(FileTaskCodes.FormatConvert, new Dictionary<string, object?>
            {
                [FileJobParams.FileId] = 1L,
                [FileJobParams.TargetFormat] = "exe",
            }),
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task FormatConvertRejectsMissingTarget()
    {
        var db = CreateDbContext();
        var handler = new FileFormatConvertHandler(db, NullLogger<FileFormatConvertHandler>.Instance);

        await Assert.ThrowsAsync<NotFoundException>(() => handler.ExecuteAsync(
            CreateContext(FileTaskCodes.FormatConvert, new Dictionary<string, object?>
            {
                [FileJobParams.FileId] = 999_999L,
                [FileJobParams.TargetFormat] = "png",
            }),
            TestContext.Current.CancellationToken));
    }
}
