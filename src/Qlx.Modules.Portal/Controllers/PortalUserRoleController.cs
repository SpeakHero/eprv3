/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-24 00:15:00 +08:00 中国标准时间 星期五
 * 修改时间：2026-10-23 12:10:00 +08:00 中国标准时间 星期五
 * 描述：门户用户-角色双向分配端点（正向：roles/{roleId}/members；反向：users/{userId}/roles）
 */

using System.Net.Mime;
using Microsoft.AspNetCore.Mvc;
using Qlx.BuildingBlocks.Api;
using Qlx.Modules.Portal.Auth;
using Qlx.Modules.Portal.Dtos;
using Qlx.Modules.Portal.Services;

namespace Qlx.Modules.Portal.Controllers;

/// <summary>
/// 门户用户-角色双向分配端点。正向路由（roles/{roleId}/members）：按角色 → 成员；
/// 反向路由（users/{userId}/roles）：按用户 → 角色。全部复用 <see cref="IPortalUserRoleAdminService"/>。
/// </summary>
[ApiController]
[Produces(MediaTypeNames.Application.Json)]
public sealed class PortalUserRoleController(IPortalUserRoleAdminService userRoleService) : ControllerBase
{
    // ======= 正向路由：api/portal/admin/roles/{roleId}/members =======

    /// <summary>
    /// 查询指定角色的成员列表。
    /// </summary>
    /// <param name="roleId">角色 Id。</param>
    /// <param name="cancellationToken">请求中止令牌。</param>
    [HttpGet("api/portal/admin/roles/{roleId}/members")]
    public async Task<ApiResponse<IReadOnlyList<UserRoleMember>>> ListMembers(
        long roleId, CancellationToken cancellationToken)
    {
        var members = await userRoleService.ListMembersAsync(roleId, cancellationToken)
            .ConfigureAwait(false);
        return ApiResponse<IReadOnlyList<UserRoleMember>>.Ok(members);
    }

    /// <summary>
    /// 查询可选用户清单（所有已启用 PortalUser）。
    /// </summary>
    /// <param name="cancellationToken">请求中止令牌。</param>
    [HttpGet("api/portal/admin/roles/{roleId}/member-options")]
    public async Task<ApiResponse<IReadOnlyList<UserOption>>> GetMemberOptions(
        CancellationToken cancellationToken)
    {
        var options = await userRoleService.GetUserOptionsAsync(cancellationToken)
            .ConfigureAwait(false);
        return ApiResponse<IReadOnlyList<UserOption>>.Ok(options);
    }

    /// <summary>
    /// 全量替换指定角色的成员（事务包裹，先删后插）。
    /// </summary>
    /// <param name="roleId">角色 Id。</param>
    /// <param name="req">新的成员用户 Id 集合（以本次提交为准）。</param>
    /// <param name="cancellationToken">请求中止令牌。</param>
    [HttpPut("api/portal/admin/roles/{roleId}/members")]
    public async Task<ApiResponse> ReplaceRoleMembers(
        long roleId, [FromBody] ReplaceRoleMembersRequest req, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(req);

        await userRoleService.ReplaceRoleMembersAsync(roleId, req, cancellationToken).ConfigureAwait(false);
        return ApiResponse.Ok("角色成员已更新");
    }

    // ======= 反向路由：api/portal/admin/users/{userId}/roles =======

    /// <summary>
    /// 查询指定用户已分配的门户角色列表。
    /// </summary>
    /// <param name="userId">用户 Id。</param>
    /// <param name="cancellationToken">请求中止令牌。</param>
    [HttpGet("api/portal/admin/users/{userId}/roles")]
    public async Task<ApiResponse<IReadOnlyList<RoleMemberItem>>> ListRolesForUser(
        long userId, CancellationToken cancellationToken)
    {
        var roles = await userRoleService.ListRolesForUserAsync(userId, cancellationToken)
            .ConfigureAwait(false);
        return ApiResponse<IReadOnlyList<RoleMemberItem>>.Ok(roles);
    }

    /// <summary>
    /// 查询可选角色清单（所有门户角色）。
    /// </summary>
    /// <param name="cancellationToken">请求中止令牌。</param>
    [HttpGet("api/portal/admin/users/{userId}/role-options")]
    public async Task<ApiResponse<IReadOnlyList<RoleOption>>> GetRoleOptions(
        CancellationToken cancellationToken)
    {
        var options = await userRoleService.GetRoleOptionsAsync(cancellationToken)
            .ConfigureAwait(false);
        return ApiResponse<IReadOnlyList<RoleOption>>.Ok(options);
    }

    /// <summary>
    /// 全量替换指定用户的角色分配（事务包裹，先删后插）。
    /// </summary>
    /// <param name="userId">用户 Id。</param>
    /// <param name="req">新的角色 Id 集合（以本次提交为准）。</param>
    /// <param name="cancellationToken">请求中止令牌。</param>
    [HttpPut("api/portal/admin/users/{userId}/roles")]
    public async Task<ApiResponse> ReplaceRolesForUser(
        long userId, [FromBody] ReplaceUserRolesRequest req, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(req);

        await userRoleService.ReplaceRolesForUserAsync(userId, req, cancellationToken).ConfigureAwait(false);
        return ApiResponse.Ok("用户角色已更新");
    }
}
