/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.1 beta
 * 创建时间：2026-10-23 23:50:00 +08:00 中国标准时间 星期四
 * 修改时间：2026-10-23 12:20:00 +08:00 中国标准时间 星期五
 * 描述：门户用户-角色双向分配服务实现：正向（角色→成员）+ 反向（用户→角色）全量替换
 */

using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Qlx.Abstractions.Audit;
using Qlx.Abstractions.Constants;
using Qlx.Abstractions.Domain;
using Qlx.Abstractions.Exceptions;
using Qlx.BuildingBlocks.Caching;
using Qlx.BuildingBlocks.Ids;
using Qlx.Modules.Portal.Domain.Identity;
using Qlx.Modules.Portal.Domain.Tenancy;
using Qlx.Modules.Portal.Dtos;

namespace Qlx.Modules.Portal.Services;

/// <summary>
/// <see cref="IPortalUserRoleAdminService"/> 实现。
/// 禁止跨模块 Join（规范 8.9）：成员查询采用 "先查 UserId 集合 → Batch 查 PortalUser" 两步法。
/// Replace 事务包裹：ExecuteDeleteAsync + AddRangeAsync。
/// </summary>
public sealed class PortalUserRoleAdminService(
    IDbContextFactory<PortalDbContext> dbFactory,
    ICacheService cacheService,
    IAuditService audit,
    ISnowflakeIdGenerator snowflake,
    ICurrentUserContext currentUser) : IPortalUserRoleAdminService
{
    /// <summary>缓存失效前缀：角色成员变更后（memberCount 影响 role:list）。</summary>
    private const string RoleCachePrefix = "_portal:role:";

    // ======= 正向：按角色 → 成员 =======

    /// <inheritdoc />
    public async Task<IReadOnlyList<UserRoleMember>> ListMembersAsync(long roleId, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);

        bool roleExists = await db.PortalRoles.AsNoTracking()
            .AnyAsync(r => r.Id == roleId, ct).ConfigureAwait(false);
        if (!roleExists)
        {
            throw new NotFoundException("门户角色", roleId.ToString(CultureInfo.InvariantCulture));
        }

        // 第一步：先查 UserId 集合（禁止 Include 导航属性按需访问 → 显式分离查询）
        var userIds = await db.PortalUserRoles.AsNoTracking()
            .Where(ur => ur.RoleId == roleId)
            .Select(ur => ur.UserId)
            .Distinct()
            .ToListAsync(ct)
            .ConfigureAwait(false);

        if (userIds.Count == 0)
        {
            return [];
        }

        // 第二步：Batch 查 PortalUser（禁止跨模块 Join，PortalUser 是本模块实体，允许单独查询）
        var users = await db.PortalUsers.AsNoTracking()
            .Where(u => userIds.Contains(u.Id))
            .Select(u => new { u.Id, u.LoginAccount, u.DisplayName })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        // 按原始 userIds 顺序组装（稳定顺序）
        var userMap = users.ToDictionary(u => u.Id, u => new UserRoleMember(u.Id, u.LoginAccount, u.DisplayName));
        var result = new List<UserRoleMember>(userIds.Count);
        foreach (var id in userIds)
        {
            if (userMap.TryGetValue(id, out var member))
            {
                result.Add(member);
            }
        }

        return result;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<UserOption>> GetUserOptionsAsync(CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);

        var options = await db.PortalUsers.AsNoTracking()
            .Where(u => u.Status == UserStatus.Enabled)
            .OrderBy(u => u.LoginAccount)
            .Select(u => new UserOption(u.Id, u.LoginAccount, u.DisplayName))
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return options;
    }

    /// <inheritdoc />
    public async Task ReplaceRoleMembersAsync(long roleId, ReplaceRoleMembersRequest req, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(req);

        await using var db = await dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);

        var role = await db.PortalRoles
            .FindAsync([roleId], ct)
            .ConfigureAwait(false)
            ?? throw new NotFoundException("门户角色", roleId.ToString(CultureInfo.InvariantCulture));

        // 所有 UserIds 必须存在于 PortalUser
        var submittedIds = req.UserIds.ToHashSet();
        var validUserIds = await db.PortalUsers.AsNoTracking()
            .Where(u => submittedIds.Contains(u.Id))
            .Select(u => u.Id)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var invalidIds = submittedIds.Except(validUserIds).ToList();
        if (invalidIds.Count > 0)
        {
            throw new ValidationException(
                nameof(ReplaceRoleMembersRequest.UserIds),
                $"以下用户 Id 不存在：{string.Join(", ", invalidIds)}");
        }

        // 事务：先删后插
        using var transaction = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
        try
        {
            await db.PortalUserRoles
                .Where(ur => ur.RoleId == roleId)
                .ExecuteDeleteAsync(ct)
                .ConfigureAwait(false);

            var newEntries = submittedIds
                .Select(userId => new PortalUserRole { RoleId = roleId, UserId = userId })
                .ToList();

            if (newEntries.Count > 0)
            {
                await db.PortalUserRoles.AddRangeAsync(newEntries, ct).ConfigureAwait(false);
            }

            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            await transaction.CommitAsync(ct).ConfigureAwait(false);
        }
        catch
        {
            await transaction.RollbackAsync(ct).ConfigureAwait(false);
            throw;
        }

        // 缓存失效：角色列表 memberCount 变更
        await cacheService.RemoveByPrefixAsync(RoleCachePrefix, ct).ConfigureAwait(false);

        await audit.CaptureOperationAsync(new OperationLogEntry(
            snowflake.NextId(), QlxAuditConstants.PortalModuleCode, PortalConstants.AuditOperations.ReplaceRoleMembers,
            $"全量替换角色 {role.RoleCode} 的成员（{submittedIds.Count} 名）",
            currentUser.UserName ?? QlxAuditConstants.PortalAdminOperatorName,
            DateTimeOffset.UtcNow, 0, QlxAuditConstants.TargetPortalRole, roleId), ct).ConfigureAwait(false);
    }

    // ======= 反向：按用户 → 角色 =======

    /// <inheritdoc />
    public async Task<IReadOnlyList<RoleMemberItem>> ListRolesForUserAsync(long userId, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);

        // 先查角色 Id 集合
        var roleIds = await db.PortalUserRoles.AsNoTracking()
            .Where(ur => ur.UserId == userId)
            .Select(ur => ur.RoleId)
            .Distinct()
            .ToListAsync(ct)
            .ConfigureAwait(false);

        if (roleIds.Count == 0)
        {
            return [];
        }

        var roles = await db.PortalRoles.AsNoTracking()
            .Where(r => roleIds.Contains(r.Id))
            .OrderBy(r => r.RoleCode)
            .Select(r => new RoleMemberItem(r.Id, r.RoleCode, r.RoleName, r.IsSystem))
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return roles;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<RoleOption>> GetRoleOptionsAsync(CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);

        var options = await db.PortalRoles.AsNoTracking()
            .OrderBy(r => r.RoleCode)
            .Select(r => new RoleOption(r.Id, r.RoleCode, r.RoleName, r.IsSystem))
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return options;
    }

    /// <inheritdoc />
    public async Task ReplaceRolesForUserAsync(long userId, ReplaceUserRolesRequest req, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(req);

        await using var db = await dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);

        // 用户必须存在
        bool userExists = await db.PortalUsers.AsNoTracking()
            .AnyAsync(u => u.Id == userId, ct).ConfigureAwait(false);
        if (!userExists)
        {
            throw new NotFoundException("门户用户", userId.ToString(CultureInfo.InvariantCulture));
        }

        // 所有 RoleIds 必须存在
        var submittedIds = req.RoleIds.ToHashSet();
        var validRoleIds = await db.PortalRoles.AsNoTracking()
            .Where(r => submittedIds.Contains(r.Id))
            .Select(r => r.Id)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var invalidIds = submittedIds.Except(validRoleIds).ToList();
        if (invalidIds.Count > 0)
        {
            throw new ValidationException(
                nameof(ReplaceUserRolesRequest.RoleIds),
                $"以下角色 Id 不存在：{string.Join(", ", invalidIds)}");
        }

        // 事务：先删后插
        using var transaction = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
        try
        {
            await db.PortalUserRoles
                .Where(ur => ur.UserId == userId)
                .ExecuteDeleteAsync(ct)
                .ConfigureAwait(false);

            var newEntries = submittedIds
                .Select(roleId => new PortalUserRole { UserId = userId, RoleId = roleId })
                .ToList();

            if (newEntries.Count > 0)
            {
                await db.PortalUserRoles.AddRangeAsync(newEntries, ct).ConfigureAwait(false);
            }

            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            await transaction.CommitAsync(ct).ConfigureAwait(false);
        }
        catch
        {
            await transaction.RollbackAsync(ct).ConfigureAwait(false);
            throw;
        }

        // 缓存失效：角色列表 memberCount 变更
        await cacheService.RemoveByPrefixAsync(RoleCachePrefix, ct).ConfigureAwait(false);

        await audit.CaptureOperationAsync(new OperationLogEntry(
            snowflake.NextId(), QlxAuditConstants.PortalModuleCode, PortalConstants.AuditOperations.ReplaceUserRoles,
            $"全量替换用户 {userId} 的角色分配（{submittedIds.Count} 项）",
            currentUser.UserName ?? QlxAuditConstants.PortalAdminOperatorName,
            DateTimeOffset.UtcNow, 0, QlxAuditConstants.TargetPortalUser, userId), ct).ConfigureAwait(false);
    }
}
