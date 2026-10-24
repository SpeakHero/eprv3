/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-23 00:40:00 +08:00 中国标准时间 星期五
 * 描述：文件任务处理器共享助手：参数读取（可选/必填）+ 目标对象解析（fileId 或 storageKey 双通道）
 */

using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Qlx.Abstractions.Exceptions;
using Qlx.Abstractions.Jobs;
using Qlx.Modules.Support.Domain;

namespace Qlx.Modules.Support.Jobs.Files;

/// <summary>
/// 文件任务处理器共享助手。目标对象支持 fileId（ISupportFileService 主通道）与
/// storageKey（IFileStorageService 契约按存储键操作）双通道解析，二者必传其一。
/// </summary>
internal static class FileJobHelpers
{
    /// <summary>
    /// 计算流内容 SHA-256 十六进制串（内容 hash 与秒传键的公共实现）。
    /// </summary>
    /// <param name="content">内容流（调用方自行管理 Position）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>64 位小写十六进制 hash 串。</returns>
    public static async Task<string> ComputeSha256HexAsync(Stream content, CancellationToken cancellationToken)
    {
        var hashBytes = await SHA256.HashDataAsync(content, cancellationToken).ConfigureAwait(false);
        var builder = new StringBuilder(hashBytes.Length * 2);
        foreach (var b in hashBytes)
        {
            builder.Append(b.ToString("x2", System.Globalization.CultureInfo.InvariantCulture));
        }

        return builder.ToString();
    }

    /// <summary>读取可选 long 参数（缺失或类型不符返回 0）。</summary>
    public static long GetOptionalLong(JobContext context, string key)
        => context.Parameters.TryGetValue(key, out var value) && value is long typed ? typed : 0;

    /// <summary>读取可选字符串参数（缺失返回 null）。</summary>
    public static string? GetOptionalString(JobContext context, string key)
        => context.Parameters.TryGetValue(key, out var value) && value is string typed ? typed : null;

    /// <summary>读取必填字符串参数（缺失抛参数异常）。</summary>
    public static string GetRequiredString(JobContext context, string key)
    {
        if (!context.Parameters.TryGetValue(key, out var value) || value is not string typed
            || string.IsNullOrWhiteSpace(typed))
        {
            throw new ValidationException(key, $"任务 {context.TaskCode} 缺少必填参数 {key}");
        }

        return typed;
    }

    /// <summary>
    /// 解析目标存储对象：fileId 优先；否则按 storageKey 定位最新版本反查对象。
    /// </summary>
    /// <param name="db">租户 DbContext。</param>
    /// <param name="context">任务上下文。</param>
    /// <param name="tracked">是否跟踪查询（变更类任务传 true）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>目标对象；不存在返回 null。</returns>
    public static async Task<StorageObject?> LoadTargetAsync(
        SupportTenantDbContext db, JobContext context, bool tracked, CancellationToken cancellationToken)
    {
        var fileId = GetOptionalLong(context, FileJobParams.FileId);
        if (fileId > 0)
        {
            IQueryable<StorageObject> query = tracked ? db.StorageObjects : db.StorageObjects.AsNoTracking();
            return await query.SingleOrDefaultAsync(o => o.Id == fileId, cancellationToken).ConfigureAwait(false);
        }

        var storageKey = GetOptionalString(context, FileJobParams.StorageKey);
        if (string.IsNullOrWhiteSpace(storageKey))
        {
            throw new ValidationException(FileJobParams.FileId, "必须提供 fileId 或 storageKey 之一");
        }

        var version = await db.FileVersions
            .AsNoTracking()
            .Where(v => v.StorageKey == storageKey)
            .OrderByDescending(v => v.VerNo)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        if (version is null)
        {
            return null;
        }

        return await db.StorageObjects
            .AsNoTracking()
            .SingleOrDefaultAsync(o => o.Id == version.ObjectId, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>断言目标对象存在且处于正常态（否则抛 NotFound）。</summary>
    public static StorageObject RequireNormalTarget(StorageObject? target, string resourceId)
    {
        if (target is not { Status: StorageObjectStatus.Normal })
        {
            throw new NotFoundException("文件或目录", resourceId);
        }

        return target;
    }

    /// <summary>目标对象资源展示标识（fileId 优先，回落 storageKey）。</summary>
    public static string ResolveResourceId(JobContext context)
    {
        var fileId = GetOptionalLong(context, FileJobParams.FileId);
        return fileId > 0
            ? fileId.ToString(System.Globalization.CultureInfo.InvariantCulture)
            : GetOptionalString(context, FileJobParams.StorageKey) ?? string.Empty;
    }
}
