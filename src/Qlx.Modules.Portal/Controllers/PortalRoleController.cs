/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-24 00:00:00 +08:00 中国标准时间 星期五
 * 修改时间：2026-10-23 12:10:00 +08:00 中国标准时间 星期五
 * 描述：门户角色 CRUD 端点（api/portal/admin/roles）：门户管理员专属
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
/// 门户角色 CRUD 端点。契约与前端门户管理界面角色列表页对齐，
/// 仅门户管理员（身份级 Token 携带 is_portal_admin=1）可访问。
/// </summary>
[ApiController]
[Authorize(
    AuthenticationSchemes = OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme,
    Policy = QlxAuthConstants.Policies.PortalAdmin)]
[Route("api/portal/admin/roles")]
[Produces(MediaTypeNames.Application.Json)]
public sealed class PortalRoleController(IPortalRoleAdminService roleService) : ControllerBase
{
    /// <summary>
    /// 分页查询门户角色。
    /// </summary>
    /// <param name="keyword">关键字（可选，匹配 RoleCode / RoleName）。</param>
    /// <param name="page">页码（从 1 开始，默认 1）。</param>
    /// <param name="pageSize">每页大小（默认 20，上限 100）。</param>
    /// <param name="cancellationToken">请求中止令牌。</param>
    [HttpGet]
    public async Task<ApiResponse<PagedResult<PortalRoleListItem>>> List(
        [FromQuery] string? keyword = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var safePage = Math.Max(page, 1);
        var safePageSize = Math.Clamp(pageSize, 1, 100);

        var result = await roleService
            .ListAsync(keyword, safePage, safePageSize, cancellationToken)
            .ConfigureAwait(false);

        return ApiResponse<PagedResult<PortalRoleListItem>>.Ok(result);
    }

    /// <summary>
    /// 新建门户角色（角色编码由系统根据角色名称自动生成，IsSystem 强制 false）。
    /// </summary>
    /// <param name="req">新建请求。</param>
    /// <param name="cancellationToken">请求中止令牌。</param>
    [HttpPost]
    public async Task<ApiResponse<PortalRoleDetail>> Create(
        [FromBody] CreatePortalRoleRequest req, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(req);

        var detail = await roleService.CreateAsync(req, cancellationToken).ConfigureAwait(false);
        return ApiResponse<PortalRoleDetail>.Ok(detail, "角色创建成功");
    }

    /// <summary>
    /// 更新门户角色（IsSystem=true 角色拒绝修改，Conflict 409）。
    /// </summary>
    /// <param name="id">角色 Id。</param>
    /// <param name="req">更新请求（RoleCode 因唯一索引不可修改）。</param>
    /// <param name="cancellationToken">请求中止令牌。</param>
    [HttpPut("{id}")]
    public async Task<ApiResponse<PortalRoleDetail>> Update(
        long id, [FromBody] UpdatePortalRoleRequest req, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(req);

        var detail = await roleService.UpdateAsync(id, req, cancellationToken).ConfigureAwait(false);
        return ApiResponse<PortalRoleDetail>.Ok(detail, "角色已更新");
    }

    /// <summary>
    /// 删除门户角色（IsSystem=true 或有成员，Conflict 409）。
    /// </summary>
    /// <param name="id">角色 Id。</param>
    /// <param name="cancellationToken">请求中止令牌。</param>
    [HttpDelete("{id}")]
    public async Task<ApiResponse> Delete(long id, CancellationToken cancellationToken)
    {
        await roleService.DeleteAsync(id, cancellationToken).ConfigureAwait(false);
        return ApiResponse.Ok("角色已删除");
    }
}
