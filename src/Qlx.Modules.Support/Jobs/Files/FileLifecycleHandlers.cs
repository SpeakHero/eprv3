/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-23 01:15:00 +08:00 中国标准时间 星期五
 * 描述：文件生命周期任务处理器（SYNC）：软删进回收站（Delete 权限）与还原（Write 权限）
 */

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Qlx.Abstractions.Exceptions;
using Qlx.Abstractions.Files;
using Qlx.Abstractions.Jobs;
using Qlx.Abstractions.Tenancy;
using Qlx.Modules.Support.Domain;
using Qlx.Modules.Support.Options;

namespace Qlx.Modules.Support.Jobs.Files;

/// <summary>
/// 文件软删处理器（file.delete，SYNC）。规则 R3：NORMAL→RECYCLED，保留 RecycleDays 天；
/// 目录须无存活子级才允许删除。物理清理与引用计数扣减由 2C 清理任务执行。
/// </summary>
public sealed partial class FileSoftDeleteHandler(
    SupportTenantDbContext db,
    IServiceProvider serviceProvider,
    IOptions<SupportFilesOptions> filesOptions,
    ILogger<FileSoftDeleteHandler> logger) : IJobHandler
{
    private readonly ITenantSubjectQuery? _subjectQuery = serviceProvider.GetService<ITenantSubjectQuery>();
    private readonly SupportFilesOptions _filesOptions = filesOptions.Value;
    private readonly ILogger<FileSoftDeleteHandler> _logger = logger;

    /// <inheritdoc />
    public string TaskCode => FileTaskCodes.Delete;

    /// <inheritdoc />
    public async Task<CompletedResult> ExecuteAsync(JobContext context, CancellationToken cancellationToken)
    {
        var operatorId = FileJobHelpers.GetRequiredString(context, FileJobParams.OperatorId);
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        var target = await FileJobHelpers
            .LoadTargetAsync(db, context, tracked: true, cancellationToken)
            .ConfigureAwait(false);
        target = FileJobHelpers.RequireNormalTarget(target, FileJobHelpers.ResolveResourceId(context));

        var permission = await FileAclGuard
            .ComputeAsync(db, _subjectQuery, target, operatorId, cancellationToken)
            .ConfigureAwait(false);
        if (!permission.HasFlag(FilePermission.Delete))
        {
            LogDeleteDenied(target.Id, operatorId);
            throw new ForbiddenException($"无删除权限：{target.Name}");
        }

        if (target.ObjectType == StorageObjectType.Folder)
        {
            var hasChildren = await db.StorageObjects
                .AnyAsync(o => o.ParentId == target.Id && o.Status == StorageObjectStatus.Normal, cancellationToken)
                .ConfigureAwait(false);
            if (hasChildren)
            {
                throw new ConflictException($"目录 {target.Name} 下仍有未删除的子项，请先清空目录。");
            }
        }

        var now = DateTimeOffset.UtcNow;
        target.Recycle(now);
        db.FileOpLogs.Add(new FileOpLog(db.CurrentTenantId, target.Id, "RECYCLE", operatorId, null,
            $"移入回收站（保留 {_filesOptions.RecycleDays} 天）", now));
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        stopwatch.Stop();
        return new CompletedResult(
            new FileMetadataJobPayload(target.Id, target.Name, target.Mime ?? string.Empty, target.Size, target.Hash,
                target.Path, target.BizDomain, target.CurrentVersion, target.OwnerId, target.Status.ToString(),
                false, target.RecycledAt, target.CreatedAt, target.UpdatedAt),
            null, null, stopwatch.ElapsedMilliseconds, IsSuccess: true);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "文件软删被拒绝：{FileId} / {OperatorId}")]
    private partial void LogDeleteDenied(long fileId, string operatorId);
}

/// <summary>
/// 文件还原处理器（file.restore，SYNC）。RECYCLED→NORMAL，需 Write 权限。
/// </summary>
public sealed partial class FileRestoreHandler(
    SupportTenantDbContext db,
    IServiceProvider serviceProvider,
    ILogger<FileRestoreHandler> logger) : IJobHandler
{
    private readonly ITenantSubjectQuery? _subjectQuery = serviceProvider.GetService<ITenantSubjectQuery>();
    private readonly ILogger<FileRestoreHandler> _logger = logger;

    /// <inheritdoc />
    public string TaskCode => FileTaskCodes.Restore;

    /// <inheritdoc />
    public async Task<CompletedResult> ExecuteAsync(JobContext context, CancellationToken cancellationToken)
    {
        var operatorId = FileJobHelpers.GetRequiredString(context, FileJobParams.OperatorId);
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        var fileId = FileJobHelpers.GetOptionalLong(context, FileJobParams.FileId);
        var target = await db.StorageObjects
            .SingleOrDefaultAsync(o => o.Id == fileId, cancellationToken)
            .ConfigureAwait(false);

        if (target is not { Status: StorageObjectStatus.Recycled })
        {
            throw new NotFoundException("回收站文件", FileJobHelpers.ResolveResourceId(context));
        }

        var permission = await FileAclGuard
            .ComputeAsync(db, _subjectQuery, target, operatorId, cancellationToken)
            .ConfigureAwait(false);
        if (!permission.HasFlag(FilePermission.Write))
        {
            LogRestoreDenied(target.Id, operatorId);
            throw new ForbiddenException($"无还原权限：{target.Name}");
        }

        var now = DateTimeOffset.UtcNow;
        target.Restore();
        db.FileOpLogs.Add(new FileOpLog(db.CurrentTenantId, target.Id, "RESTORE", operatorId, null, "从回收站还原", now));
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        stopwatch.Stop();
        return new CompletedResult(
            new FileMetadataJobPayload(target.Id, target.Name, target.Mime ?? string.Empty, target.Size, target.Hash,
                target.Path, target.BizDomain, target.CurrentVersion, target.OwnerId, target.Status.ToString(),
                false, target.RecycledAt, target.CreatedAt, target.UpdatedAt),
            null, null, stopwatch.ElapsedMilliseconds, IsSuccess: true);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "文件还原被拒绝：{FileId} / {OperatorId}")]
    private partial void LogRestoreDenied(long fileId, string operatorId);
}
