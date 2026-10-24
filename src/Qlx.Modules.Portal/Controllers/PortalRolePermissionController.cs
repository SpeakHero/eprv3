/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-24 00:10:00 +08:00 中国标准时间 星期五
 * 修改时间：2026-10-23 12:10:00 +08:00 中国标准时间 星期五
 * 描述：门户角色权限分配端点（api/portal/admin/roles/{roleId}/permissions）：全量替换 + 可选权限清单
 */

using System.Net.Mime;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OpenIddict.Validation.AspNetCore;
using Qlx.BuildingBlocks.Api;
using Qlx.Modules.Portal.Auth;
using Qlx.Modules.Portal.Dtos;
using Qlx.Modules.Portal.Services;

namespace Qlx.Modules.Portal.Controllers;

/// <summary>
/// 门户角色权限分配端点。提供角色已分配权限码查询、可选权限码清单、全量替换权限三个操作。
/// IsSystem=true 角色禁止修改权限（Service 层校验，Conflict 409）。
/// </summary>
[ApiController]
[Authorize(
    AuthenticationSchemes = OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme,
    Policy = QlxAuthConstants.Policies.PortalAdmin)]
[Route("api/portal/admin/roles/{roleId}/permissions")]
[Produces(MediaTypeNames.Application.Json)]
public sealed class PortalRolePermissionController(
    IPortalRolePermissionAdminService permissionService) : ControllerBase
{
    /// <summary>
    /// 查询指定角色已分配的权限码列表。
    /// </summary>
    /// <param name="roleId">角色 Id。</param>
    /// <param name="cancellationToken">请求中止令牌。</param>
    [HttpGet]
    public async Task<ApiResponse<IReadOnlyList<string>>> GetPermissionCodes(
        long roleId, CancellationToken cancellationToken)
    {
        var codes = await permissionService.GetPermissionCodesAsync(roleId, cancellationToken)
            .ConfigureAwait(false);
        return ApiResponse<IReadOnlyList<string>>.Ok(codes);
    }

    /// <summary>
    /// 查询可选权限码清单（por_permission_definitions.IsDeprecated=false）。
    /// </summary>
    /// <param name="cancellationToken">请求中止令牌。</param>
    [HttpGet("permission-options")]
    public async Task<ApiResponse<IReadOnlyList<PermissionOption>>> GetPermissionOptions(
        CancellationToken cancellationToken)
    {
        var options = await permissionService.GetPermissionOptionsAsync(cancellationToken)
            .ConfigureAwait(false);
        return ApiResponse<IReadOnlyList<PermissionOption>>.Ok(options);
    }

    /// <summary>
    /// 全量替换指定角色的权限码（事务包裹，先删后插；IsSystem=true 拒绝，Conflict 409）。
    /// </summary>
    /// <param name="roleId">角色 Id。</param>
    /// <param name="req">新的权限码集合（以本次提交为准）。</param>
    /// <param name="cancellationToken">请求中止令牌。</param>
    [HttpPut]
    public async Task<ApiResponse> Replace(
        long roleId, [FromBody] ReplaceRolePermissionsRequest req, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(req);

        await permissionService.ReplaceAsync(roleId, req, cancellationToken).ConfigureAwait(false);
        return ApiResponse.Ok("角色权限已更新");
    }
}
