/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-21
 * 描述：Bas 模块租户自管理域 Service 接口——按前端 api/tenantAdmin.ts 契约对齐 18 个端点。
 */

using Qlx.BuildingBlocks.Api;
using Qlx.Modules.Bas.Dtos;

namespace Qlx.Modules.Bas.Services;

/// <summary>
/// 租户自管理域服务接口。
/// 后端聚焦原子查询 + 业务校验，树构建/跨表显示名填充等复杂组装由前端完成。
/// 路由 / 查询中的业务 Id 由全局 ModelBinder 解码后以真实 <see cref="long"/> 传入；
/// JSON 请求体中符合 Id 命名约定的 long 属性由全局 JSON 元数据约定自动解码。
/// </summary>
public interface ITenantAdminService
{
    // ============ Organization ============
    Task<IReadOnlyList<OrganizationFlatRow>> ListAllOrgsAsync(CancellationToken ct);
    Task<OrganizationFlatRow> CreateOrgAsync(SaveOrganizationPayload payload, CancellationToken ct);
    Task<OrganizationFlatRow> UpdateOrgAsync(long id, SaveOrganizationPayload payload, CancellationToken ct);
    Task<OrganizationFlatRow> ToggleOrgStatusAsync(long id, CancellationToken ct);

    // ============ Position ============
    Task<PagedResult<PositionRow>> QueryPositionsAsync(long? orgId, string? keyword, PagedRequest request, CancellationToken ct);
    Task<PositionRow> SavePositionAsync(SavePositionPayload payload, CancellationToken ct);
    Task<PositionRow> TogglePositionStatusAsync(long id, CancellationToken ct);

    // ============ Member ============
    Task<PagedResult<MemberRow>> QueryMembersAsync(long? orgId, int? memberRole, int? memberStatus, string? keyword, PagedRequest request, CancellationToken ct);
    Task<MemberRow> GetMemberDetailAsync(long tenantMemberId, CancellationToken ct);
    Task<(long TenantMemberId, long PortalUserId)?> InviteMemberAsync(InviteMemberPayload payload, CancellationToken ct);
    Task<ImportMembersResult> ImportMembersAsync(IReadOnlyList<ImportMemberRow> rows, CancellationToken ct);
    Task ResendMemberInviteAsync(long tenantMemberId, CancellationToken ct);

    /// <summary>成员在本租户的登录日志（租户库 log_login，按账号过滤 + 全局租户过滤，页大小上限 20）。</summary>
    Task<PagedResult<MemberLoginLogRow>> GetMemberLoginLogsAsync(long tenantMemberId, PagedRequest request, CancellationToken ct);
    Task ToggleMemberStatusAsync(long tenantMemberId, CancellationToken ct);
    Task ResetMemberPasswordAsync(long tenantMemberId, string newPassword, CancellationToken ct);
    Task RemoveMemberAsync(long tenantMemberId, CancellationToken ct);

    // ============ Role ============
    Task<IReadOnlyList<RoleRow>> ListRolesAsync(CancellationToken ct);
    Task<RoleRow> CreateRoleAsync(SaveRolePayload payload, CancellationToken ct);
    Task<RoleRow> UpdateRoleAsync(long id, SaveRolePayload payload, CancellationToken ct);
    Task DeleteRoleAsync(long id, CancellationToken ct);
    Task SaveRolePermissionsAsync(long id, IReadOnlyList<string> permissionCodes, CancellationToken ct);

    /// <summary>角色已分配权限码回显（角色不存在 404）。</summary>
    Task<IReadOnlyList<string>> GetRolePermissionCodesAsync(long id, CancellationToken ct);

    /// <summary>角色成员 Id 回显（对外混淆串；角色不存在 404）。</summary>
    Task<IReadOnlyList<string>> GetRoleMemberIdsAsync(long id, CancellationToken ct);

    /// <summary>
    /// 保存角色成员。请求体为顶层字符串数组（无属性元数据，JSON 约定不生效），
    /// 由 Service 统一解码为真实成员 Id；其余入参均为已解码的真实 Id。
    /// </summary>
    Task SaveRoleMembersAsync(long id, IReadOnlyList<string> tenantMemberIds, CancellationToken ct);

    // ============ 只读（来自 Portal 库）============
    Task<IReadOnlyList<PermissionGroupRow>> ListPermissionGroupsAsync(CancellationToken ct);
    Task<IReadOnlyList<TenantModuleRow>> ListTenantModulesAsync(CancellationToken ct);
}
