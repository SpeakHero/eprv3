/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.1 beta
 * 创建时间：2026-10-23 23:45:00 +08:00 中国标准时间 星期四
 * 修改时间：2026-10-23 12:15:00 +08:00 中国标准时间 星期五
 * 描述：门户角色权限分配服务实现：角色权限码查询 + 可选权限清单 + 全量替换（事务）
 */

using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Qlx.Abstractions.Audit;
using Qlx.Abstractions.Constants;
using Qlx.Abstractions.Domain;
using Qlx.Abstractions.Exceptions;
using Qlx.BuildingBlocks.Caching;
using Qlx.BuildingBlocks.Ids;
using Qlx.Modules.Portal.Domain.Tenancy;
using Qlx.Modules.Portal.Dtos;

namespace Qlx.Modules.Portal.Services;

/// <summary>
/// <see cref="IPortalRolePermissionAdminService"/> 实现。
/// IsSystem=true 角色禁止修改权限；ReplaceAsync 事务包裹（ExecuteDeleteAsync + AddRangeAsync）。
/// </summary>
public sealed class PortalRolePermissionAdminService(
    IDbContextFactory<PortalDbContext> dbFactory,
    ICacheService cacheService,
    IAuditService audit,
    ISnowflakeIdGenerator snowflake,
    ICurrentUserContext currentUser) : IPortalRolePermissionAdminService
{
    /// <summary>缓存失效前缀：角色权限变更后 RemoveByPrefixAsync（_portal:perm:role-permissions）。</summary>
    private const string RolePermissionsCachePrefix = "_portal:perm:role-permissions";

    /// <summary>
    /// 缓存失效前缀：门户角色列表（与 PortalRoleAdminService.RoleCachePrefix 同前缀）。
    /// 权限变更会改变列表中的 permissionCount，必须同步失效，否则列表/页头计数长期为旧值。
    /// </summary>
    private const string RoleListCachePrefix = "_portal:role:";

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> GetPermissionCodesAsync(long roleId, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);

        bool roleExists = await db.PortalRoles.AsNoTracking()
            .AnyAsync(r => r.Id == roleId, ct).ConfigureAwait(false);
        if (!roleExists)
        {
            throw new NotFoundException("门户角色", roleId.ToString(CultureInfo.InvariantCulture));
        }

        var codes = await db.PortalRolePermissions.AsNoTracking()
            .Where(rp => rp.RoleId == roleId)
            .Select(rp => rp.PermissionCode)
            .Distinct()
            .OrderBy(c => c)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return codes;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<PermissionOption>> GetPermissionOptionsAsync(CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);

        var options = await db.PermissionDefinitions.AsNoTracking()
            .Where(p => !p.IsDeprecated)
            // 排序必须在 Select 投影之前（EF 不支持按投影类型属性翻译 OrderBy）
            .OrderBy(p => p.ModuleCode)
            .ThenBy(p => p.DisplayName)
            .Select(p => new PermissionOption(
                p.PermissionCode,
                p.ModuleCode,
                p.DisplayName,
                p.GroupName))
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return options;
    }

    /// <inheritdoc />
    public async Task ReplaceAsync(long roleId, ReplaceRolePermissionsRequest req, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(req);

        await using var db = await dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);

        // 角色必须存在
        var role = await db.PortalRoles
            .FindAsync([roleId], ct)
            .ConfigureAwait(false)
            ?? throw new NotFoundException("门户角色", roleId.ToString(CultureInfo.InvariantCulture));

        if (role.IsSystem)
        {
            throw new ConflictException("系统内置角色禁止修改权限", "SYSTEM_ROLE_PROTECTED");
        }

        // 所有传入 PermissionCodes 必须存在于 por_permission_definitions 且 IsDeprecated=false
        var submittedCodes = req.PermissionCodes.ToHashSet(StringComparer.Ordinal);
        var validCodes = await db.PermissionDefinitions.AsNoTracking()
            .Where(p => submittedCodes.Contains(p.PermissionCode) && !p.IsDeprecated)
            .Select(p => p.PermissionCode)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var invalidCodes = submittedCodes.Except(validCodes, StringComparer.Ordinal).ToList();
        if (invalidCodes.Count > 0)
        {
            throw new ValidationException(
                nameof(ReplaceRolePermissionsRequest.PermissionCodes),
                $"以下权限码不存在或已废弃：{string.Join(", ", invalidCodes)}");
        }

        // 事务：先删后插
        using var transaction = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
        try
        {
            await db.PortalRolePermissions
                .Where(rp => rp.RoleId == roleId)
                .ExecuteDeleteAsync(ct)
                .ConfigureAwait(false);

            var newEntries = submittedCodes
                .Select(code => new PortalRolePermission { RoleId = roleId, PermissionCode = code })
                .ToList();

            if (newEntries.Count > 0)
            {
                await db.PortalRolePermissions.AddRangeAsync(newEntries, ct).ConfigureAwait(false);
            }

            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            await transaction.CommitAsync(ct).ConfigureAwait(false);
        }
        catch
        {
            await transaction.RollbackAsync(ct).ConfigureAwait(false);
            throw;
        }

        // 缓存失效：角色权限相关
        await cacheService.RemoveAsync(
                QlxCacheKey.BuildPortalKey("perm", $"role:{role.RoleCode}"), ct)
            .ConfigureAwait(false);
        await cacheService.RemoveByPrefixAsync(RolePermissionsCachePrefix, ct).ConfigureAwait(false);
        // 列表缓存内嵌 permissionCount，必须一并失效
        await cacheService.RemoveByPrefixAsync(RoleListCachePrefix, ct).ConfigureAwait(false);

        await audit.CaptureOperationAsync(new OperationLogEntry(
            snowflake.NextId(), QlxAuditConstants.PortalModuleCode, PortalConstants.AuditOperations.ReplaceRolePermissions,
            $"全量替换角色 {role.RoleCode} 的权限（{submittedCodes.Count} 项）",
            currentUser.UserName ?? QlxAuditConstants.PortalAdminOperatorName,
            DateTimeOffset.UtcNow, 0, QlxAuditConstants.TargetPortalRole, roleId), ct).ConfigureAwait(false);
    }
}
