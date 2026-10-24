/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.1 beta
 * 创建时间：2026-10-23 23:30:00 +08:00 中国标准时间 星期四
 * 修改时间：2026-10-23 11:55:00 +08:00 中国标准时间 星期五
 * 描述：门户角色管理服务实现：CRUD + 缓存 + IsSystem 防护 + Delete 前成员检查
 */

using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Qlx.Abstractions.Audit;
using Qlx.Abstractions.Constants;
using Qlx.Abstractions.Domain;
using Qlx.Abstractions.Exceptions;
using Qlx.BuildingBlocks.Api;
using Qlx.BuildingBlocks.Caching;
using Qlx.BuildingBlocks.Ids;
using Qlx.BuildingBlocks.Numbering;
using Qlx.Modules.Portal.Domain.Tenancy;
using Qlx.Modules.Portal.Dtos;

namespace Qlx.Modules.Portal.Services;

/// <summary>
/// <see cref="IPortalRoleAdminService"/> 实现。门户角色 CRUD + Cache-Aside + IsSystem 防护。
/// </summary>
/// <remarks>
/// 依赖 <see cref="IDbContextFactory{PortalDbContext}"/>：每次方法调用从工厂创建独立 DbContext，
/// 方法结束时 <c>await using</c> 自动释放，避免 DbContext 长期持有连接。
/// </remarks>
public sealed class PortalRoleAdminService(
    IDbContextFactory<PortalDbContext> dbFactory,
    ICacheService cacheService,
    IAuditService audit,
    ISnowflakeIdGenerator snowflake,
    ICurrentUserContext currentUser,
    IEncodingGenerator encodingGenerator) : IPortalRoleAdminService
{
    /// <summary>缓存键：门户角色列表（_portal:role:list）。</summary>
    private static readonly string RoleListCacheKey = QlxCacheKey.BuildPortalKey("role", "list");

    /// <summary>缓存失效前缀（写路径后 RemoveByPrefixAsync）。</summary>
    private const string RoleCachePrefix = "_portal:role:";

    /// <inheritdoc />
    public Task<PagedResult<PortalRoleListItem>> ListAsync(
        string? keyword, int page, int pageSize, CancellationToken ct)
    {
        // keyword 非空时直接查库（不走缓存，避免 keyword 组合爆炸）；空 keyword 走缓存
        if (string.IsNullOrWhiteSpace(keyword))
        {
            return cacheService.GetOrCreateAsync(
                RoleListCacheKey,
                factory: async ct2 =>
                {
                    await using var db = await dbFactory.CreateDbContextAsync(ct2).ConfigureAwait(false);
                    return await QueryRolesPaginatedAsync(db, null, page, pageSize, ct2).ConfigureAwait(false);
                },
                cancellationToken: ct);
        }

        // keyword 非空 → 直接查库（不缓存）
        return QueryRolesPaginatedDirectAsync(keyword, page, pageSize, ct);
    }

    /// <inheritdoc />
    public async Task<PortalRoleDetail> CreateAsync(CreatePortalRoleRequest req, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(req);

        await using var db = await dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);

        // 角色编码由系统自动生成：portal. 前缀 + 角色名拼音首字母，冲突追加 001/002...
        string roleCode = await encodingGenerator.GenerateAsync(
            req.RoleName,
            scope: "portal-role",
            uniquenessChecker: async (code, ct2) =>
                await db.PortalRoles.AsNoTracking().AnyAsync(r => r.RoleCode == code, ct2).ConfigureAwait(false),
            options: new EncodingGenerationOptions
            {
                Prefix = "portal.",
                PinyinMode = PinyinMode.Initials,
                SequenceDigits = 3,
                MaxLength = 64,
            },
            cancellationToken: ct).ConfigureAwait(false);

        // IsSystem 强制 false：新建角色一律业务角色
        var role = new PortalRole
        {
            RoleCode = roleCode,
            RoleName = req.RoleName,
            Description = req.Description,
            IsSystem = false,
        };

        db.PortalRoles.Add(role);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        // 缓存失效
        await cacheService.RemoveByPrefixAsync(RoleCachePrefix, ct).ConfigureAwait(false);

        await audit.CaptureOperationAsync(new OperationLogEntry(
            snowflake.NextId(), QlxAuditConstants.PortalModuleCode, PortalConstants.AuditOperations.CreatePortalRole,
            $"新建门户角色 {role.RoleCode}",
            currentUser.UserName ?? QlxAuditConstants.PortalAdminOperatorName,
            DateTimeOffset.UtcNow, 0, QlxAuditConstants.TargetPortalRole, role.Id), ct).ConfigureAwait(false);

        return new PortalRoleDetail(
            role.Id, role.RoleCode, role.RoleName, role.Description,
            role.IsSystem, role.UpdatedAt);
    }

    /// <inheritdoc />
    public async Task<PortalRoleDetail> UpdateAsync(long roleId, UpdatePortalRoleRequest req, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(req);

        await using var db = await dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);

        var role = await db.PortalRoles
            .FindAsync([roleId], ct)
            .ConfigureAwait(false)
            ?? throw new NotFoundException("门户角色", roleId.ToString(CultureInfo.InvariantCulture));

        if (role.IsSystem)
        {
            throw new ConflictException("系统内置角色禁止修改", "SYSTEM_ROLE_PROTECTED");
        }

        role.RoleName = req.RoleName;
        role.Description = req.Description;

        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        // 缓存失效
        await cacheService.RemoveByPrefixAsync(RoleCachePrefix, ct).ConfigureAwait(false);

        await audit.CaptureOperationAsync(new OperationLogEntry(
            snowflake.NextId(), QlxAuditConstants.PortalModuleCode, PortalConstants.AuditOperations.UpdatePortalRole,
            $"更新门户角色 {role.RoleCode}",
            currentUser.UserName ?? QlxAuditConstants.PortalAdminOperatorName,
            DateTimeOffset.UtcNow, 0, QlxAuditConstants.TargetPortalRole, role.Id), ct).ConfigureAwait(false);

        return new PortalRoleDetail(
            role.Id, role.RoleCode, role.RoleName, role.Description,
            role.IsSystem, role.UpdatedAt);
    }

    /// <inheritdoc />
    public async Task DeleteAsync(long roleId, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);

        var role = await db.PortalRoles
            .FindAsync([roleId], ct)
            .ConfigureAwait(false)
            ?? throw new NotFoundException("门户角色", roleId.ToString(CultureInfo.InvariantCulture));

        if (role.IsSystem)
        {
            throw new ConflictException("系统内置角色禁止删除", "SYSTEM_ROLE_PROTECTED");
        }

        // 有成员则拒绝
        bool hasMembers = await db.PortalUserRoles
            .AsNoTracking()
            .AnyAsync(ur => ur.RoleId == roleId, ct)
            .ConfigureAwait(false);
        if (hasMembers)
        {
            throw new ConflictException("角色存在成员，禁止删除", "ROLE_HAS_MEMBERS");
        }

        // 事务：先删 PortalRolePermissions → 再删 PortalRole
        using var transaction = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
        try
        {
            await db.PortalRolePermissions
                .Where(rp => rp.RoleId == roleId)
                .ExecuteDeleteAsync(ct)
                .ConfigureAwait(false);

            db.PortalRoles.Remove(role);
            await db.SaveChangesAsync(ct).ConfigureAwait(false);

            await transaction.CommitAsync(ct).ConfigureAwait(false);
        }
        catch
        {
            await transaction.RollbackAsync(ct).ConfigureAwait(false);
            throw;
        }

        // 缓存失效
        await cacheService.RemoveByPrefixAsync(RoleCachePrefix, ct).ConfigureAwait(false);

        await audit.CaptureOperationAsync(new OperationLogEntry(
            snowflake.NextId(), QlxAuditConstants.PortalModuleCode, PortalConstants.AuditOperations.DeletePortalRole,
            $"删除门户角色 {role.RoleCode}",
            currentUser.UserName ?? QlxAuditConstants.PortalAdminOperatorName,
            DateTimeOffset.UtcNow, 0, QlxAuditConstants.TargetPortalRole, role.Id), ct).ConfigureAwait(false);
    }

    // ======= 私有方法 =======

    /// <summary>
    /// 带分页 + keyword 过滤的角色列表查询（直接查库版本，不走缓存）。
    /// </summary>
    private async Task<PagedResult<PortalRoleListItem>> QueryRolesPaginatedDirectAsync(
        string keyword, int page, int pageSize, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        return await QueryRolesPaginatedAsync(db, keyword, page, pageSize, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// 核心分页查询：GroupJoin 一次拿到权限数和成员数，避免 N+1。
    /// </summary>
    private static async Task<PagedResult<PortalRoleListItem>> QueryRolesPaginatedAsync(
        PortalDbContext db, string? keyword, int page, int pageSize, CancellationToken ct)
    {
        IQueryable<PortalRole> roles = db.PortalRoles.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            roles = roles.Where(r =>
                EF.Functions.Like(r.RoleCode, $"%{keyword}%")
                || EF.Functions.Like(r.RoleName, $"%{keyword}%"));
        }

        roles = roles.OrderBy(r => r.RoleCode);

        // 相关子查询一次聚合 PermissionCount + MemberCount（单条 SQL，无 N+1；
        // GroupJoin + 分组 Count 形态 EF Core 无法翻译，见启动冒烟 500 根因）
        var query = roles.Select(r => new PortalRoleListItem(
            r.Id,
            r.RoleCode,
            r.RoleName,
            r.Description,
            r.IsSystem,
            db.PortalRolePermissions.Count(rp => rp.RoleId == r.Id),
            db.PortalUserRoles.Count(ur => ur.RoleId == r.Id),
            r.UpdatedAt));

        var pagedRequest = new PagedRequest { Page = page, PageSize = pageSize };
        return await query.ToPagedAsync(pagedRequest, ct).ConfigureAwait(false);
    }
}
