/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-23 01:25:00 +08:00 中国标准时间 星期五
 * 描述：文件 ACL 变更任务处理器（file.acl.set，SYNC）：Manage 权限校验 + 单主体 Allow 授予/撤销（幂等合并）
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
/// 文件 ACL 变更处理器（file.acl.set，SYNC）。
/// 按（对象, 主体类型, 主体 Id）幂等合并 Allow 条目：授予 PermMask 并集；撤销差集，清零即删行。
/// DENY 条目（模板快照/保护目录）不由此处理器维护。
/// </summary>
public sealed partial class FileAclSetHandler(
    SupportTenantDbContext db,
    IServiceProvider serviceProvider,
    ILogger<FileAclSetHandler> logger) : IJobHandler
{
    /// <summary>操作类型：授予。</summary>
    public const string OpGrant = "grant";

    /// <summary>操作类型：撤销。</summary>
    public const string OpRevoke = "revoke";

    private readonly ITenantSubjectQuery? _subjectQuery = serviceProvider.GetService<ITenantSubjectQuery>();
    private readonly ILogger<FileAclSetHandler> _logger = logger;

    /// <inheritdoc />
    public string TaskCode => FileTaskCodes.AclSet;

    /// <inheritdoc />
    public async Task<CompletedResult> ExecuteAsync(JobContext context, CancellationToken cancellationToken)
    {
        var operatorId = FileJobHelpers.GetRequiredString(context, FileJobParams.OperatorId);
        var op = FileJobHelpers.GetRequiredString(context, FileJobParams.Op);
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        var target = await FileJobHelpers
            .LoadTargetAsync(db, context, tracked: true, cancellationToken)
            .ConfigureAwait(false);
        target = FileJobHelpers.RequireNormalTarget(target, FileJobHelpers.ResolveResourceId(context));

        var permission = await FileAclGuard
            .ComputeAsync(db, _subjectQuery, target, operatorId, cancellationToken)
            .ConfigureAwait(false);
        if (!permission.HasFlag(FilePermission.Manage))
        {
            LogAclChangeDenied(target.Id, operatorId);
            throw new ForbiddenException($"无 ACL 管理权限：{target.Name}");
        }

        var subjectType = (FileSubjectType)FileJobHelpers.GetOptionalLong(context, FileJobParams.SubjectType);
        var subjectId = FileJobHelpers.GetRequiredString(context, FileJobParams.SubjectId);
        var permMask = (FilePermission)FileJobHelpers.GetOptionalLong(context, FileJobParams.PermissionMask);

        var existing = await db.FileAclEntries
            .SingleOrDefaultAsync(e => e.ObjectId == target.Id
                && e.SubjectType == subjectType
                && e.SubjectId == subjectId
                && e.AceType == AceType.Allow, cancellationToken)
            .ConfigureAwait(false);

        var now = DateTimeOffset.UtcNow;
        if (string.Equals(op, OpGrant, StringComparison.Ordinal))
        {
            if (existing is { } entry)
            {
                entry.PermMask |= (int)permMask;
            }
            else
            {
                db.FileAclEntries.Add(new FileAclEntry(db.CurrentTenantId, target.Id, subjectType, subjectId,
                    permMask, AceType.Allow, propagate: target.ObjectType == StorageObjectType.Folder,
                    inheritSrc: null, operatorId, now));
            }
        }
        else if (existing is { } entry)
        {
            entry.PermMask &= ~(int)permMask;
            if (entry.PermMask == 0)
            {
                db.FileAclEntries.Remove(entry);
            }
        }
        else
        {
            throw new NotFoundException("ACL 条目", $"{target.Id}/{subjectType}/{subjectId}");
        }

        db.FileOpLogs.Add(new FileOpLog(db.CurrentTenantId, target.Id, "ACL_SET", operatorId, null,
            $"{op} {subjectType}/{subjectId} 权限位 {(int)permMask}", now));
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        stopwatch.Stop();
        return new CompletedResult(
            new FileAclSetResultPayload(target.Id, op, (int)subjectType, subjectId, (int)permMask),
            null, null, stopwatch.ElapsedMilliseconds, IsSuccess: true);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "ACL 变更被拒绝：{FileId} / {OperatorId}")]
    private partial void LogAclChangeDenied(long fileId, string operatorId);
}

/// <summary>ACL 变更结果载荷。</summary>
public sealed record FileAclSetResultPayload(long FileId, string Op, int SubjectType, string SubjectId, int PermMask);
