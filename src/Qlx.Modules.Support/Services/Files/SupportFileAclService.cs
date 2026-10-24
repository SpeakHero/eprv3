/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-23 01:55:00 +08:00 中国标准时间 星期五
 * 描述：文件 ACL 服务实现（IFileAclService）：授予/撤销任务化（file.acl.set），列表/权限判定直读（只读不触存储后端）
 */

using Microsoft.EntityFrameworkCore;
using Qlx.Abstractions.Domain;
using Qlx.Abstractions.Exceptions;
using Qlx.Abstractions.Files;
using Qlx.Abstractions.Jobs;
using Qlx.Abstractions.Tenancy;
using Qlx.BuildingBlocks.Files;
using Qlx.BuildingBlocks.Ids;
using Qlx.Modules.Support.Domain;
using Qlx.Modules.Support.Jobs.Files;

namespace Qlx.Modules.Support.Services.Files;

/// <summary>
/// 文件 ACL 服务实现。
/// 授予/撤销属变更操作，统一走 <see cref="FileTaskCodes.AclSet"/> 任务（与文件操作同一任务化通道）；
/// 条目列表与权限判定为只读查询，直接读 fil_acl_entries 并调用 <see cref="FileAclEngine"/> 纯函数（不触存储后端，不违反 S18/D22）。
/// fileId 为混淆字符串，经 <see cref="IIdObfuscator"/> 解码为真实 long。
/// </summary>
public sealed class SupportFileAclService(
    SupportTenantDbContext db,
    IJobScheduler scheduler,
    ITenantContext tenantContext,
    ICurrentUserContext currentUser,
    IIdObfuscator idObfuscator) : IFileAclService
{
    private readonly SupportTenantDbContext _db = db;
    private readonly IJobScheduler _scheduler = scheduler;
    private readonly ITenantContext _tenantContext = tenantContext;
    private readonly ICurrentUserContext _currentUser = currentUser;
    private readonly IIdObfuscator _idObfuscator = idObfuscator;

    /// <inheritdoc />
    public Task GrantAsync(string fileId, FileSubjectType subjectType, string subjectId, FilePermission permission, CancellationToken cancellationToken)
        => SubmitAclSetAsync(fileId, FileAclSetHandler.OpGrant, subjectType, subjectId, permission, cancellationToken);

    /// <inheritdoc />
    public Task RevokeAsync(string fileId, FileSubjectType subjectType, string subjectId, CancellationToken cancellationToken)
        => SubmitAclSetAsync(fileId, FileAclSetHandler.OpRevoke, subjectType, subjectId, FilePermission.None, cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<FileAclEntryInfo>> ListAclAsync(string fileId, CancellationToken cancellationToken)
    {
        var objectId = DecodeFileId(fileId);
        var entries = await _db.FileAclEntries
            .AsNoTracking()
            .Where(e => e.ObjectId == objectId)
            .OrderBy(e => e.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return entries
            .Select(e => new FileAclEntryInfo(
                _idObfuscator.Encode(e.Id), fileId, e.SubjectType, e.SubjectId,
                (FilePermission)e.PermMask, e.GrantedBy ?? string.Empty, e.GrantedAt))
            .ToList();
    }

    /// <inheritdoc />
    public async Task<bool> HasPermissionAsync(string fileId, FileSubjectType subjectType, string subjectId, FilePermission permission, CancellationToken cancellationToken)
    {
        var objectId = DecodeFileId(fileId);
        var target = await _db.StorageObjects
            .AsNoTracking()
            .SingleOrDefaultAsync(o => o.Id == objectId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new NotFoundException("文件或目录", fileId);

        var entries = await FileAclGuard.LoadEntriesAsync(_db, target, cancellationToken).ConfigureAwait(false);

        // 显式主体判定：按主体类型构造引擎入参（非当前登录人语义）
        var currentUserId = subjectType == FileSubjectType.User ? subjectId : string.Empty;
        var subjects = subjectType switch
        {
            FileSubjectType.Role => new TenantSubjects([], [subjectId], [], IsTenantAdmin: false),
            FileSubjectType.Org => new TenantSubjects([], [], [subjectId], IsTenantAdmin: false),
            _ => new TenantSubjects([], [], [], IsTenantAdmin: false),
        };

        var effective = FileAclEngine.ComputeEffective(
            entries, target.Id, target.OwnerId, subjects, currentUserId,
            isTenantAdmin: false, target.InheritAcl);
        return effective.HasFlag(permission);
    }

    /// <summary>提交 ACL 变更任务（file.acl.set，SYNC）并断言成功。</summary>
    private async Task SubmitAclSetAsync(string fileId, string op, FileSubjectType subjectType, string subjectId,
        FilePermission permission, CancellationToken cancellationToken)
    {
        var objectId = DecodeFileId(fileId);
        var tenantId = _tenantContext.TenantId is > 0
            ? _tenantContext.TenantId.Value
            : throw new UnauthorizedException("缺少租户上下文");
        if (string.IsNullOrWhiteSpace(_currentUser.UserId))
        {
            throw new UnauthorizedException();
        }

        var response = await _scheduler
            .SubmitAsync(new TaskSubmitRequest(
                FileTaskCodes.AclSet,
                GuidIdMapper.ToGuid(tenantId),
                ExecutionMode.Sync,
                InitiatorUserId: _currentUser.UserId,
                Parameters: new Dictionary<string, object?>
                {
                    [FileJobParams.FileId] = objectId,
                    [FileJobParams.OperatorId] = _currentUser.UserId,
                    [FileJobParams.Op] = op,
                    [FileJobParams.SubjectType] = (long)subjectType,
                    [FileJobParams.SubjectId] = subjectId,
                    [FileJobParams.PermissionMask] = (long)permission,
                }), cancellationToken)
            .ConfigureAwait(false);

        if (response.CompletedResult is not { IsSuccess: true })
        {
            throw new ConflictException(
                response.CompletedResult?.ErrorMessage ?? $"ACL 变更任务执行失败（{response.InitialStatus}）。",
                "FILE_TASK_FAILED");
        }
    }

    /// <summary>混淆 fileId 解码为真实 long（非法串抛校验异常）。</summary>
    private long DecodeFileId(string fileId)
    {
        if (!_idObfuscator.TryDecode(fileId, out var objectId) || objectId <= 0)
        {
            throw new ValidationException("fileId", "文件标识非法");
        }

        return objectId;
    }
}
