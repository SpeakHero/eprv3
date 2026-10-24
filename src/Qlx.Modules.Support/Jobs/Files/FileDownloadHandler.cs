/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.1.0 beta
 * 创建时间：2026-10-23 00:25:00 +08:00 中国标准时间 星期五
 * 修改时间：2026-10-23 01:00:00 +08:00 中国标准时间 星期五
 * 描述：文件下载/预览任务处理器（SYNC）：ACL 权限校验（Read）+ 保护目录只读流打开；业务错误抛 BusinessException
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
/// 文件下载处理器（file.download，SYNC）。校验当前用户 Read 权限后打开物理流（进程内传递）。
/// 支持 fileId（主通道）与 storageKey（IFileStorageService 契约）双通道定位。
/// </summary>
public sealed partial class FileDownloadHandler(
    SupportTenantDbContext db,
    IFileStorageBackend storageBackend,
    IServiceProvider serviceProvider,
    ILogger<FileDownloadHandler> logger) : IJobHandler
{
    private readonly ITenantSubjectQuery? _subjectQuery = serviceProvider.GetService<ITenantSubjectQuery>();
    private readonly ILogger<FileDownloadHandler> _logger = logger;

    /// <inheritdoc />
    public string TaskCode => FileTaskCodes.Download;

    /// <inheritdoc />
    public async Task<CompletedResult> ExecuteAsync(JobContext context, CancellationToken cancellationToken)
        => await ReadContentAsync(context, FileTaskCodes.Download, cancellationToken).ConfigureAwait(false);

    /// <summary>下载/预览共用读流逻辑。</summary>
    internal async Task<CompletedResult> ReadContentAsync(JobContext context, string action, CancellationToken cancellationToken)
    {
        var operatorId = FileJobHelpers.GetRequiredString(context, FileJobParams.OperatorId);
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        var target = await FileJobHelpers
            .LoadTargetAsync(db, context, tracked: false, cancellationToken)
            .ConfigureAwait(false);
        target = FileJobHelpers.RequireNormalTarget(target, FileJobHelpers.ResolveResourceId(context));

        if (target.ObjectType == StorageObjectType.Folder)
        {
            throw new ValidationException(FileJobParams.FileId, "目录不支持下载");
        }

        var permission = await FileAclGuard
            .ComputeAsync(db, _subjectQuery, target, operatorId, cancellationToken)
            .ConfigureAwait(false);
        if (!permission.HasFlag(FilePermission.Read))
        {
            LogReadDenied(target.Id, operatorId);
            throw new ForbiddenException($"无读取权限：{target.Name}");
        }

        var currentVersion = await db.FileVersions
            .AsNoTracking()
            .Where(v => v.ObjectId == target.Id)
            .OrderByDescending(v => v.VerNo)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false)
            ?? throw new NotFoundException("文件版本", FileJobHelpers.ResolveResourceId(context));

        var stream = await storageBackend.OpenReadAsync(currentVersion.StorageKey, cancellationToken).ConfigureAwait(false);
        stopwatch.Stop();
        return new CompletedResult(
            new FileContentJobPayload(target.Id, stream, target.Name, target.Mime ?? "application/octet-stream", target.Size),
            null, null, stopwatch.ElapsedMilliseconds, IsSuccess: true);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "文件读取被拒绝：{FileId} / {OperatorId}")]
    private partial void LogReadDenied(long fileId, string operatorId);
}

/// <summary>
/// 文件预览处理器（file.preview，SYNC 流式）。2B 与下载同权限（Read）同流；2C 接入格式转换预览。
/// </summary>
public sealed class FilePreviewHandler(FileDownloadHandler downloadHandler) : IJobHandler
{
    /// <inheritdoc />
    public string TaskCode => FileTaskCodes.Preview;

    /// <inheritdoc />
    public Task<CompletedResult> ExecuteAsync(JobContext context, CancellationToken cancellationToken)
        => downloadHandler.ReadContentAsync(context, FileTaskCodes.Preview, cancellationToken);
}
