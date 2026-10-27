/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.1.0 beta
 * 创建时间：2026-10-23 01:05:00 +08:00 中国标准时间 星期五
 * 修改时间：2026-10-23 02:50:00 +08:00 中国标准时间 星期五
 * 描述：文件查询任务处理器（SYNC）：元数据 / 目录列表 / 回收站列表（批量 IsProtected，避免 N+1）
 */

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Qlx.Abstractions.Exceptions;
using Qlx.Abstractions.Files;
using Qlx.Abstractions.Jobs;
using Qlx.Abstractions.Tenancy;
using Qlx.BuildingBlocks.Files;
using Qlx.Modules.Support.Domain;

namespace Qlx.Modules.Support.Jobs.Files;

/// <summary>
/// 文件元数据处理器（file.metadata，SYNC）。Read 权限校验后返回对象元数据载荷。
/// </summary>
public sealed partial class FileMetadataHandler(
    SupportTenantDbContext db,
    IServiceProvider serviceProvider,
    ILogger<FileMetadataHandler> logger) : IJobHandler
{
    private readonly ITenantSubjectQuery? _subjectQuery = serviceProvider.GetService<ITenantSubjectQuery>();
    private readonly ILogger<FileMetadataHandler> _logger = logger;

    /// <inheritdoc />
    public string TaskCode => FileTaskCodes.Metadata;

    /// <inheritdoc />
    public async Task<CompletedResult> ExecuteAsync(JobContext context, CancellationToken cancellationToken)
    {
        var operatorId = FileJobHelpers.GetRequiredString(context, FileJobParams.OperatorId);
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        var target = await FileJobHelpers
            .LoadTargetAsync(db, context, tracked: false, cancellationToken)
            .ConfigureAwait(false);
        target = FileJobHelpers.RequireNormalTarget(target, FileJobHelpers.ResolveResourceId(context));

        var permission = await FileAclGuard
            .ComputeAsync(db, _subjectQuery, target, operatorId, cancellationToken)
            .ConfigureAwait(false);
        if (!permission.HasFlag(FilePermission.Read))
        {
            LogMetadataDenied(target.Id, operatorId);
            throw new ForbiddenException($"无读取权限：{target.Name}");
        }

        var isProtected = await IsProtectedBatch(db, [target.Id], cancellationToken).ConfigureAwait(false);
        stopwatch.Stop();
        return new CompletedResult(ToPayload(target, isProtected.Contains(target.Id)), null, null,
            stopwatch.ElapsedMilliseconds, IsSuccess: true);
    }

    /// <summary>批量判定保护对象集合（存在通配 DENY 写/删条目即视为保护目录）。</summary>
    internal static async Task<HashSet<long>> IsProtectedBatch(
        SupportTenantDbContext db, IReadOnlyCollection<long> objectIds, CancellationToken cancellationToken)
    {
        if (objectIds.Count == 0)
        {
            return [];
        }

        var writeDeleteMask = (int)(FilePermission.Write | FilePermission.Delete);
        var protectedIds = await db.FileAclEntries
            .AsNoTracking()
            .Where(e => objectIds.Contains(e.ObjectId)
                && e.AceType == AceType.Deny
                && (e.PermMask & writeDeleteMask) != 0)
            .Select(e => e.ObjectId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return [.. protectedIds];
    }

    /// <summary>实体 → 元数据载荷（供列表处理器复用）。</summary>
    internal static FileMetadataJobPayload ToPayload(StorageObject o, bool isProtected)
        => new(o.Id, o.Name, o.Mime ?? string.Empty, o.Size, o.Hash, o.Path, o.BizDomain,
            o.CurrentVersion, o.OwnerId, o.Status.ToString(), isProtected, o.RecycledAt, o.CreatedAt, o.UpdatedAt);

    [LoggerMessage(Level = LogLevel.Warning, Message = "文件元数据查询被拒绝：{FileId} / {OperatorId}")]
    private partial void LogMetadataDenied(long fileId, string operatorId);
}

/// <summary>
/// 文件列表处理器（file.list，SYNC）。按父目录（校验父目录 Read 权限后列出子级）或关键字检索。
/// </summary>
public sealed class FileListHandler(SupportTenantDbContext db, IServiceProvider serviceProvider) : IJobHandler
{
    /// <summary>关键字检索单次返回上限（防止全表翻页）。</summary>
    private const int MaxKeywordResults = 200;

    private readonly ITenantSubjectQuery? _subjectQuery = serviceProvider.GetService<ITenantSubjectQuery>();

    /// <inheritdoc />
    public string TaskCode => FileTaskCodes.List;

    /// <inheritdoc />
    public async Task<CompletedResult> ExecuteAsync(JobContext context, CancellationToken cancellationToken)
    {
        var operatorId = FileJobHelpers.GetRequiredString(context, FileJobParams.OperatorId);
        var parentId = FileJobHelpers.GetOptionalLong(context, FileJobParams.ParentId);
        var keyword = FileJobHelpers.GetOptionalString(context, FileJobParams.Keyword);
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        List<StorageObject> items;
        if (parentId > 0)
        {
            var parent = await db.StorageObjects
                .AsNoTracking()
                .SingleOrDefaultAsync(o => o.Id == parentId, cancellationToken)
                .ConfigureAwait(false);
            parent = FileJobHelpers.RequireNormalTarget(parent, parentId.ToString(System.Globalization.CultureInfo.InvariantCulture));

            if (parent.ObjectType != StorageObjectType.Folder)
            {
                throw new ValidationException(FileJobParams.ParentId, "父级必须是目录");
            }

            var parentPermission = await FileAclGuard
                .ComputeAsync(db, _subjectQuery, parent, operatorId, cancellationToken)
                .ConfigureAwait(false);
            if (!parentPermission.HasFlag(FilePermission.Read))
            {
                throw new ForbiddenException($"无目录读取权限：{parent.Name}");
            }

            // 子级可见性随父目录（继承语义），不逐条计算 ACL（避免 N+1）
            items = await db.StorageObjects
                .AsNoTracking()
                .Where(o => o.ParentId == parentId && o.Status == StorageObjectStatus.Normal)
                .OrderBy(o => o.ObjectType)
                .ThenBy(o => o.Name)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        else
        {
            var query = db.StorageObjects.AsNoTracking();
            query = string.IsNullOrWhiteSpace(keyword)
                ? query.Where(o => o.ParentId == null && o.Status == StorageObjectStatus.Normal)
                : query.Where(o => o.Status == StorageObjectStatus.Normal && o.Name.Contains(keyword));

            items = await query
                .OrderBy(o => o.Path)
                .Take(MaxKeywordResults)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
        }

        var ids = items.Select(static o => o.Id).ToList();
        var protectedIds = await FileMetadataHandler.IsProtectedBatch(db, ids, cancellationToken).ConfigureAwait(false);
        var payloads = items
            .Select(o => FileMetadataHandler.ToPayload(o, protectedIds.Contains(o.Id)))
            .ToList();

        stopwatch.Stop();
        return new CompletedResult(new FileListJobPayload(payloads), null, null, stopwatch.ElapsedMilliseconds, IsSuccess: true);
    }
}

/// <summary>
/// 回收站列表处理器（file.recycle.list，SYNC）。租户管理员可见全部；普通用户仅见本人删除项。
/// </summary>
public sealed class FileRecycleListHandler(SupportTenantDbContext db, IServiceProvider serviceProvider) : IJobHandler
{
    private const int MaxResults = 500;

    private readonly ITenantSubjectQuery? _subjectQuery = serviceProvider.GetService<ITenantSubjectQuery>();

    /// <inheritdoc />
    public string TaskCode => FileTaskCodes.RecycleList;

    /// <inheritdoc />
    public async Task<CompletedResult> ExecuteAsync(JobContext context, CancellationToken cancellationToken)
    {
        var operatorId = FileJobHelpers.GetRequiredString(context, FileJobParams.OperatorId);
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        var subjects = await FileAclGuard
            .ResolveSubjectsAsync(_subjectQuery, db.CurrentTenantId, operatorId, cancellationToken)
            .ConfigureAwait(false);

        var query = db.StorageObjects.AsNoTracking().Where(o => o.Status == StorageObjectStatus.Recycled);
        if (!subjects.IsTenantAdmin)
        {
            query = query.Where(o => o.OwnerId == operatorId);
        }

        var items = await query
            .OrderByDescending(o => o.RecycledAt)
            .Take(MaxResults)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var ids = items.Select(static o => o.Id).ToList();
        var protectedIds = await FileMetadataHandler.IsProtectedBatch(db, ids, cancellationToken).ConfigureAwait(false);
        var payloads = items
            .Select(o => FileMetadataHandler.ToPayload(o, protectedIds.Contains(o.Id)))
            .ToList();

        stopwatch.Stop();
        return new CompletedResult(new FileListJobPayload(payloads), null, null, stopwatch.ElapsedMilliseconds, IsSuccess: true);
    }
}
