/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-22 23:40:00 +08:00 中国标准时间 星期四
 * 描述：本地文件存储后端：根目录 + hash 分片子目录（ab/cd/…）+ 路径穿越防护 + 只读保护
 */

using System.Security;
using Microsoft.Extensions.Options;

namespace Qlx.BuildingBlocks.Files;

/// <summary>
/// 本地文件存储后端实现。存储键格式约束为 <c>[0-9a-f]/</c> 分片子目录 + 文件名片段，
/// 按 hash 前两位/前四位散列到二级子目录；路径含 <c>..</c>、盘符、绝对路径等穿越片段一律拒绝。
/// </summary>
public sealed class LocalFileStorageBackend : IFileStorageBackend
{
    private readonly string _rootFullPath;

    /// <summary>
    /// 创建本地存储后端。
    /// </summary>
    /// <param name="options">存储配置（根目录）。</param>
    /// <param name="contentRootPath">宿主内容根（相对根目录基于此解析；测试可传临时目录）。</param>
    public LocalFileStorageBackend(IOptions<LocalFileStorageOptions> options, string contentRootPath)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentRootPath);

        var root = options.Value.Root;
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        _rootFullPath = Path.GetFullPath(Path.IsPathRooted(root) ? root : Path.Combine(contentRootPath, root));
        Directory.CreateDirectory(_rootFullPath);
    }

    /// <inheritdoc />
    public async Task SaveAsync(string storageKey, Stream content, bool setReadOnly, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(storageKey);
        ArgumentNullException.ThrowIfNull(content);
        var fullPath = ResolvePhysicalPath(storageKey);

        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        if (File.Exists(fullPath))
        {
            // 同 hash 秒传落盘并发：已存在即复用（内容一致性由哈希保证），且保护只读文件不被改写
            return;
        }

        await using (var target = new FileStream(fullPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            await content.CopyToAsync(target, cancellationToken).ConfigureAwait(false);
        }

        if (setReadOnly)
        {
            File.SetAttributes(fullPath, FileAttributes.ReadOnly);
        }
    }

    /// <inheritdoc />
    public Task<Stream> OpenReadAsync(string storageKey, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(storageKey);
        var fullPath = ResolvePhysicalPath(storageKey);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("物理文件不存在", storageKey);
        }

        Stream stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Task.FromResult(stream);
    }

    /// <inheritdoc />
    public Task DeleteAsync(string storageKey, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(storageKey);
        var fullPath = ResolvePhysicalPath(storageKey);
        if (File.Exists(fullPath))
        {
            File.SetAttributes(fullPath, FileAttributes.Normal);
            File.Delete(fullPath);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<bool> ExistsAsync(string storageKey, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(storageKey);
        return Task.FromResult(File.Exists(ResolvePhysicalPath(storageKey)));
    }

    /// <summary>
    /// 解析存储键对应的物理绝对路径，并做路径穿越防护：
    /// 拒绝 <c>..</c> 片段、盘符前缀、绝对路径、空段与非法字符；结果必须仍位于根目录之下。
    /// </summary>
    private string ResolvePhysicalPath(string storageKey)
    {
        if (storageKey.Contains("..", StringComparison.Ordinal)
            || Path.IsPathRooted(storageKey)
            || storageKey.Contains(':', StringComparison.Ordinal)
            || storageKey.Contains('\\', StringComparison.Ordinal))
        {
            throw new SecurityException($"非法存储键（疑似路径穿越）：{storageKey}");
        }

        var segments = storageKey.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Length == 0 || segments.Any(string.IsNullOrWhiteSpace))
        {
            throw new SecurityException($"非法存储键（空片段）：{storageKey}");
        }

        foreach (var segment in segments)
        {
            if (segment.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            {
                throw new SecurityException($"非法存储键（含非法字符）：{storageKey}");
            }
        }

        var fullPath = Path.GetFullPath(Path.Combine([_rootFullPath, .. segments]));
        if (!fullPath.StartsWith(_rootFullPath + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            && !string.Equals(fullPath, _rootFullPath, StringComparison.Ordinal))
        {
            throw new SecurityException($"存储键越界（逃出根目录）：{storageKey}");
        }

        return fullPath;
    }
}
