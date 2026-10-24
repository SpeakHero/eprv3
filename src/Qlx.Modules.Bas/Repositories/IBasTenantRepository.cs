/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-21
 * 描述：Bas 模块仓储接口——纯数据访问层，禁止业务逻辑。
 */

using Qlx.Modules.Bas.Domain;

namespace Qlx.Modules.Bas.Repositories;

/// <summary>
/// Bas 租户域仓储——封装对 BasTenantDbContext 的访问。
/// 读路径统一 AsNoTracking；写路径 FindTracked 保持跟踪态。
/// </summary>
internal interface IBasTenantRepository
{
    // === Organization ===
    Task<IReadOnlyList<Organization>> ListAllOrgsAsync(CancellationToken ct);
    Task<Organization?> FindOrgAsync(long id, CancellationToken ct);
    Task AddOrgAsync(Organization org, CancellationToken ct);
    Task UpdateOrgAsync(Organization org, CancellationToken ct);
    Task RemoveOrgAsync(Organization org, CancellationToken ct);
    Task<bool> HasChildOrgsAsync(long parentId, CancellationToken ct);
    Task<bool> HasMembersUnderOrgAsync(long orgId, CancellationToken ct);

    // === Position ===
    /// <summary>岗位分页查询；orgIds 为组织子树 Id 集合（null/空 表示不限组织）。</summary>
    Task<(IReadOnlyList<Position> Items, int Total)> QueryPositionsAsync(
        IReadOnlyCollection<long>? orgIds, string? keyword, int skip, int take, CancellationToken ct);
    Task<Position?> FindPositionAsync(long id, CancellationToken ct);
    Task<bool> IsPositionCodeExistsAsync(string code, long? excludeId, CancellationToken ct);
    Task AddPositionAsync(Position position, CancellationToken ct);
    Task UpdatePositionAsync(Position position, CancellationToken ct);
    Task RemovePositionAsync(Position position, CancellationToken ct);

    // === Role ===
    Task<IReadOnlyList<Role>> ListRolesAsync(CancellationToken ct);

    /// <summary>角色列表 + 成员数/权限点聚合计数（单次往返，相关标量子查询投影，禁 N+1）。</summary>
    Task<IReadOnlyList<(Role Role, int MemberCount, int PermissionCount)>> ListRolesWithCountsAsync(CancellationToken ct);

    /// <summary>单角色成员数/权限点计数（更新回包用，两条 COUNT）。</summary>
    Task<(int MemberCount, int PermissionCount)> GetRoleCountsAsync(long roleId, CancellationToken ct);

    Task<Role?> FindRoleAsync(long id, CancellationToken ct);
    Task<bool> IsRoleCodeExistsAsync(string code, long? excludeId, CancellationToken ct);
    Task AddRoleAsync(Role role, CancellationToken ct);
    Task UpdateRoleAsync(Role role, CancellationToken ct);
    Task RemoveRoleAsync(Role role, CancellationToken ct);

    // === RolePermission ===
    Task<IReadOnlyList<string>> ListRolePermissionCodesAsync(long roleId, CancellationToken ct);
    Task ReplaceRolePermissionsAsync(long roleId, long tenantId, IReadOnlyList<string> codes, CancellationToken ct);

    // === MemberRole ===
    Task<IReadOnlyList<long>> ListRoleMemberIdsAsync(long roleId, CancellationToken ct);
    Task<IReadOnlyList<string>> ListMemberRoleCodesAsync(long tenantMemberId, CancellationToken ct);
    Task ReplaceMemberRolesAsync(long tenantMemberId, long tenantId, IReadOnlyList<(long RoleId, string RoleCode)> roles, CancellationToken ct);
    Task RemoveMemberRoleLinksAsync(long tenantMemberId, CancellationToken ct);
    Task RemoveMemberRoleLinksByRoleAsync(long roleId, CancellationToken ct);
    Task ReplaceRoleMemberLinksAsync(long roleId, IReadOnlyList<long> tenantMemberIds, long tenantId, CancellationToken ct);
    Task<IReadOnlyList<(long TenantMemberId, long RoleId, string RoleCode)>> ListAllMemberRoleCodesFlatAsync(long tenantId, CancellationToken ct);

    // === 通用 ===
    Task<int> SaveChangesAsync(CancellationToken ct);

    // === 登录日志（租户库 log_login，TenantLogDbContext 全局 TenantId 过滤） ===

    /// <summary>按登录账号分页查询登录日志（按雪花 EventId 倒序，与 LoginAt 倒序语义等价）。</summary>
    Task<(IReadOnlyList<Qlx.BuildingBlocks.Support.Logs.TenantLogLoginEntity> Items, int Total)> QueryLoginLogsAsync(
        string loginAccount, int skip, int take, CancellationToken ct);
}
