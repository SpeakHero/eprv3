/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-23 23:30:00 +08:00 中国标准时间 星期四
 * 修改时间：2026-10-23 23:30:00 +08:00 中国标准时间 星期四
 * 描述：选择角色步骤端点（api/portal/tenants/{tenantId}/member-roles）：
 *       凭身份级 Token 按企业懒加载当前账号可进入的角色
 */

using System.Net.Mime;
using Microsoft.AspNetCore.Mvc;
using Qlx.Abstractions.Exceptions;
using Qlx.BuildingBlocks.Api;
using Qlx.Modules.Portal.Auth;
using Qlx.Modules.Portal.Services;

namespace Qlx.Modules.Portal.Controllers;

/// <summary>
/// 企业成员角色端点。选择角色步骤使用，仅接受身份级 Token。
/// <para>路由参数不使用 <c>:long</c> 约束：tenantId 为 Hashids 混淆串，加约束会导致 404。</para>
/// </summary>
[ApiController]
[Route("api/portal/tenants")]
public sealed class TenantMemberRoleController(ITenantMemberRoleService tenantMemberRoleService) : ControllerBase
{
    /// <summary>
    /// 查询当前账号在指定企业下可进入的角色。
    /// </summary>
    /// <param name="tenantId">企业 Id（Hashids 混淆串）。</param>
    /// <param name="cancellationToken">请求中止令牌。</param>
    [HttpGet("{tenantId}/member-roles")]
    [Produces(MediaTypeNames.Application.Json)]
    public async Task<IActionResult> MemberRoles(string tenantId, CancellationToken cancellationToken)
    {
        var items = await tenantMemberRoleService
            .GetMemberRolesAsync(User, tenantId, cancellationToken)
            .ConfigureAwait(false);

        if (items is null)
        {
            // 非成员 / 租户不可用 / Token 用途不符：统一 403，由全局异常中间件封装响应
            throw new ForbiddenException("无权查看该企业的角色列表");
        }

        return Ok(ApiResponse<IReadOnlyList<MemberRoleOptionItem>>.Ok(items));
    }
}
