/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-21
 * 描述：Bas 模块仓储 EF 实现——纯数据访问，AsNoTracking 读、Find 跟踪态写。
 */

using Microsoft.EntityFrameworkCore;
using Qlx.BuildingBlocks.Support.Logs;
using Qlx.Modules.Bas.Domain;

namespace Qlx.Modules.Bas.Repositories;

internal sealed class BasTenantRepository(BasTenantDbContext db, TenantLogDbContext logDb) : IBasTenantRepository
{
    public Task<int> SaveChangesAsync(CancellationToken ct) => db.SaveChangesAsync(ct);

    // ============ Organization ============

    public async Task<IReadOnlyList<Organization>> ListAllOrgsAsync(CancellationToken ct)
        => await db.Organizations.AsNoTracking().OrderBy(o => o.SortOrder).ToListAsync(ct);

    public Task<Organization?> FindOrgAsync(long id, CancellationToken ct)
        => db.Organizations.FindAsync([id], ct).AsTask();

    public Task AddOrgAsync(Organization org, CancellationToken ct)
    {
        db.Organizations.Add(org);
        return Task.CompletedTask;
    }

    public Task UpdateOrgAsync(Organization org, CancellationToken ct)
    {
        db.Organizations.Update(org);
        return Task.CompletedTask;
    }

    public Task RemoveOrgAsync(Organization org, CancellationToken ct)
    {
        db.Organizations.Remove(org);
        return Task.CompletedTask;
    }

    public async Task<bool> HasChildOrgsAsync(long parentId, CancellationToken ct)
        => await db.Organizations.AnyAsync(o => o.ParentId == parentId && !o.IsDeleted, ct);

    // 注：成员 OrgId 存在 Portal 库 por_tenant_members，需 Service 层通过 ITenantMemberQuery 查询
    // 此处保留接口占位，返回 false 由消费方自行跨库判断
    public Task<bool> HasMembersUnderOrgAsync(long orgId, CancellationToken ct)
        => Task.FromResult(false);

    // ============ Position ============

    public async Task<(IReadOnlyList<Position> Items, int Total)> QueryPositionsAsync(
        IReadOnlyCollection<long>? orgIds, string? keyword, int skip, int take, CancellationToken ct)
    {
        IQueryable<Position> q = db.Positions.AsNoTracking();
        // 组织子树筛选（服务层已把「选中组织 + 全部下级」展开为 Id 集合，单条 IN 查询）
        if (orgIds is { Count: > 0 }) q = q.Where(p => orgIds.Contains(p.OrgId));
        if (!string.IsNullOrWhiteSpace(keyword))
        {
            q = q.Where(p =>
                p.Name.Contains(keyword) || p.Code.Contains(keyword)
                || (p.Description != null && p.Description.Contains(keyword)));
        }
        var total = await q.CountAsync(ct);
        var items = await q.OrderBy(p => p.OrgId).ThenBy(p => p.Name)
            .Skip(skip).Take(take).ToListAsync(ct);
        return (items, total);
    }

    public Task<Position?> FindPositionAsync(long id, CancellationToken ct)
        => db.Positions.FindAsync([id], ct).AsTask();

    public async Task<bool> IsPositionCodeExistsAsync(string code, long? excludeId, CancellationToken ct)
    {
        // 注：EF Core 翻译限制——
        //   ① 无法翻译 string.Equals(code1, code2, StringComparison) 重载（CA1862 要求的 OrdinalIgnoreCase 不可用）
        //   ② 无法翻译 string.ToLowerInvariant()（EF 10 不支持 invariant 方法映射）
        //   ③ ToLower() 会触发一串全球化分析器（CA1304/CA1311 等），但 SQL 端 LOWER(col) 默认是 CI，语义正确
        // 唯一可行方案：ToLower() + == ，SQL 端翻译为 LOWER(col) = @lower ，与数据库默认 CI 排序规则语义等价
#pragma warning disable CA1304, CA1311, CA1862
        var lower = code.ToLower();
        return await db.Positions.AnyAsync(p =>
            p.Code.ToLower() == lower
            && (!excludeId.HasValue || p.Id != excludeId.Value), ct);
#pragma warning restore CA1304, CA1311, CA1862
    }

    public Task AddPositionAsync(Position position, CancellationToken ct)
    {
        db.Positions.Add(position);
        return Task.CompletedTask;
    }

    Task IBasTenantRepository.UpdatePositionAsync(Position position, CancellationToken ct)
    {
        db.Positions.Update(position);
        return Task.CompletedTask;
    }

    Task IBasTenantRepository.RemovePositionAsync(Position position, CancellationToken ct)
    {
        db.Positions.Remove(position);
        return Task.CompletedTask;
    }

    // ============ Role ============

    public async Task<IReadOnlyList<Role>> ListRolesAsync(CancellationToken ct)
        => await db.Roles.AsNoTracking().OrderBy(r => r.IsBuiltin).ThenBy(r => r.Name).ToListAsync(ct);

    public async Task<IReadOnlyList<(Role Role, int MemberCount, int PermissionCount)>> ListRolesWithCountsAsync(CancellationToken ct)
    {
        // 单次往返：相关标量子查询投影为 SELECT ..., (SELECT COUNT...)（禁 N+1）；
        // 双 GroupJoin 投影在部分提供程序（Sqlite）不可翻译，子查询形式全提供程序可用。
        // MemberRoles/RolePermissions 与 Roles 同租户全局过滤
        var rows = await db.Roles.AsNoTracking()
            .OrderBy(r => r.IsBuiltin).ThenBy(r => r.Name)
            .Select(r => new
            {
                Role = r,
                MemberCount = db.MemberRoles.Count(mr => mr.RoleId == r.Id),
                PermissionCount = db.RolePermissions.Count(rp => rp.RoleId == r.Id),
            })
            .ToListAsync(ct);
        return [.. rows.Select(x => (x.Role, x.MemberCount, x.PermissionCount))];
    }

    public async Task<(int MemberCount, int PermissionCount)> GetRoleCountsAsync(long roleId, CancellationToken ct)
    {
        int memberCount = await db.MemberRoles.CountAsync(mr => mr.RoleId == roleId, ct);
        int permissionCount = await db.RolePermissions.CountAsync(rp => rp.RoleId == roleId, ct);
        return (memberCount, permissionCount);
    }

    public Task<Role?> FindRoleAsync(long id, CancellationToken ct)
        => db.Roles.FindAsync([id], ct).AsTask();

    public async Task<bool> IsRoleCodeExistsAsync(string code, long? excludeId, CancellationToken ct)
    {
        // 注：EF Core 翻译限制——string.Equals + StringComparison / ToLowerInvariant 均不可翻译，
        // ToLower() 触发全球化分析器（CA1304/CA1311/CA1862），但 SQL LOWER 本身是 CI，语义正确
#pragma warning disable CA1304, CA1311, CA1862
        var lower = code.ToLower();
        return await db.Roles.AnyAsync(r =>
            r.Code.ToLower() == lower
            && (!excludeId.HasValue || r.Id != excludeId.Value), ct);
#pragma warning restore CA1304, CA1311, CA1862
    }

    public Task AddRoleAsync(Role role, CancellationToken ct) { db.Roles.Add(role); return Task.CompletedTask; }
    public Task UpdateRoleAsync(Role role, CancellationToken ct) { db.Roles.Update(role); return Task.CompletedTask; }
    public Task RemoveRoleAsync(Role role, CancellationToken ct) { db.Roles.Remove(role); return Task.CompletedTask; }

    // ============ RolePermission ============

    public async Task<IReadOnlyList<string>> ListRolePermissionCodesAsync(long roleId, CancellationToken ct)
        => await db.RolePermissions.AsNoTracking()
            .Where(rp => rp.RoleId == roleId)
            .Select(rp => rp.PermissionCode)
            .ToListAsync(ct);

    public async Task ReplaceRolePermissionsAsync(long roleId, long tenantId, IReadOnlyList<string> codes, CancellationToken ct)
    {
        // Entity 基类（无审计字段 / 无软删），ExecuteDelete 绕过拦截器语义一致；TenantDbContext 全局 TenantId 过滤自动生效
        await db.RolePermissions.Where(rp => rp.RoleId == roleId).ExecuteDeleteAsync(ct).ConfigureAwait(false);

        var distinct = codes.Distinct(StringComparer.Ordinal).Select(code => new RolePermission(roleId, tenantId, code)).ToList();
        if (distinct.Count > 0)
        {
            await db.RolePermissions.AddRangeAsync(distinct, ct).ConfigureAwait(false);
        }
    }

    // ============ MemberRole ============

    public async Task<IReadOnlyList<long>> ListRoleMemberIdsAsync(long roleId, CancellationToken ct)
        => await db.MemberRoles.AsNoTracking()
            .Where(mr => mr.RoleId == roleId)
            .Select(mr => mr.TenantMemberId).ToListAsync(ct);

    public async Task<IReadOnlyList<string>> ListMemberRoleCodesAsync(long tenantMemberId, CancellationToken ct)
    {
        var roleIds = await db.MemberRoles.AsNoTracking()
            .Where(mr => mr.TenantMemberId == tenantMemberId)
            .Select(mr => mr.RoleId).ToListAsync(ct);
        if (roleIds.Count == 0) return [];

        return await db.Roles.AsNoTracking()
            .Where(r => roleIds.Contains(r.Id))
            .Select(r => r.Code).ToListAsync(ct);
    }

    public async Task ReplaceMemberRolesAsync(
        long tenantMemberId, long tenantId, IReadOnlyList<(long RoleId, string RoleCode)> roles, CancellationToken ct)
    {
        // Entity 基类（无审计字段 / 无软删），ExecuteDelete 绕过拦截器语义一致；TenantDbContext 全局 TenantId 过滤自动生效
        await db.MemberRoles.Where(mr => mr.TenantMemberId == tenantMemberId).ExecuteDeleteAsync(ct).ConfigureAwait(false);

        var distinct = roles.DistinctBy(x => x.RoleId).Select(x => new MemberRole(tenantMemberId, x.RoleId, tenantId)).ToList();
        if (distinct.Count > 0)
        {
            await db.MemberRoles.AddRangeAsync(distinct, ct).ConfigureAwait(false);
        }
    }

    public async Task RemoveMemberRoleLinksAsync(long tenantMemberId, CancellationToken ct)
        => await db.MemberRoles.Where(mr => mr.TenantMemberId == tenantMemberId).ExecuteDeleteAsync(ct).ConfigureAwait(false);

    public async Task RemoveMemberRoleLinksByRoleAsync(long roleId, CancellationToken ct)
        => await db.MemberRoles.Where(mr => mr.RoleId == roleId).ExecuteDeleteAsync(ct).ConfigureAwait(false);

    public async Task ReplaceRoleMemberLinksAsync(
        long roleId, IReadOnlyList<long> tenantMemberIds, long tenantId, CancellationToken ct)
    {
        // Entity 基类（无审计字段 / 无软删），ExecuteDelete 绕过拦截器语义一致；TenantDbContext 全局 TenantId 过滤自动生效
        await db.MemberRoles.Where(mr => mr.RoleId == roleId).ExecuteDeleteAsync(ct).ConfigureAwait(false);

        var distinct = tenantMemberIds.Distinct().Select(memberId => new MemberRole(memberId, roleId, tenantId)).ToList();
        if (distinct.Count > 0)
        {
            await db.MemberRoles.AddRangeAsync(distinct, ct).ConfigureAwait(false);
        }
    }

    public async Task<IReadOnlyList<(long TenantMemberId, long RoleId, string RoleCode)>> ListAllMemberRoleCodesFlatAsync(
        long tenantId, CancellationToken ct)
    {
        var raw = await (
            from mr in db.MemberRoles.AsNoTracking()
            join r in db.Roles.AsNoTracking() on mr.RoleId equals r.Id
            where mr.TenantId == tenantId
            select new { mr.TenantMemberId, mr.RoleId, RoleCode = r.Code })
            .ToListAsync(ct);
        return raw.Select(x => (x.TenantMemberId, x.RoleId, x.RoleCode)).ToList();
    }

    // ============ 登录日志（租户库 log_login）============

    public async Task<(IReadOnlyList<TenantLogLoginEntity> Items, int Total)> QueryLoginLogsAsync(
        string loginAccount, int skip, int take, CancellationToken ct)
    {
        // TenantLogDbContext 基类全局 TenantId 过滤自动生效，仅需按账号过滤；
        // EventId 为雪花 Id（时间单调递增），按 EventId 倒序与按 LoginAt 倒序语义等价，
        // 且规避 Sqlite 不支持 DateTimeOffset 排序的提供程序差异（SQL Server 两者皆可）
        IQueryable<TenantLogLoginEntity> q = logDb.LoginLogs.AsNoTracking()
            .Where(l => l.LoginAccount == loginAccount);
        int total = await q.CountAsync(ct);
        List<TenantLogLoginEntity> items = await q.OrderByDescending(l => l.EventId)
            .Skip(skip).Take(take).ToListAsync(ct);
        return (items, total);
    }
}
