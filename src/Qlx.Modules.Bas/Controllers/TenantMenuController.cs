/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-24
 * 修改时间：2026-10-24
 * 描述：租户菜单 API（GET /api/tenant/menus）：按租户模块许可下发的扁平菜单快照。
 *       树组装由前端负责（B-2）；Controller 只做租户上下文解析与 ApiResponse 包装，不含业务计算。
 */

using System.Net.Mime;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OpenIddict.Validation.AspNetCore;
using Qlx.Abstractions.Constants;
using Qlx.Abstractions.Exceptions;
using Qlx.Abstractions.Modules;
using Qlx.BuildingBlocks.Api;
using Qlx.BuildingBlocks.Ids;

namespace Qlx.Modules.Bas.Controllers;

/// <summary>
/// 租户自管理域菜单控制器。需租户级 Token（租户上下文 claim 携带 tenant_id）。
/// </summary>
/// <param name="tenantMenuQuery">租户菜单查询（Portal 模块实现，模块间只依赖 Abstractions）。</param>
/// <param name="obfuscator">Id 混淆器（解码 tenant_id claim）。</param>
[ApiController]
[Authorize(AuthenticationSchemes = OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme)]
[Route("api/tenant/menus")]
public sealed class TenantMenuController(
    ITenantMenuQuery tenantMenuQuery,
    IIdObfuscator obfuscator) : ControllerBase
{
    /// <summary>
    /// 获取当前租户可见的扁平菜单快照（仅含已许可模块）。
    /// </summary>
    /// <param name="cancellationToken">请求中止令牌。</param>
    /// <returns>导航分组 + 扁平菜单列表。</returns>
    [HttpGet]
    [Produces(MediaTypeNames.Application.Json)]
    public async Task<ApiResponse<MenuSnapshot>> GetMenus(CancellationToken cancellationToken)
    {
        var tenantIdRaw = User.FindFirst(QlxClaimConstants.TenantId)?.Value;
        if (tenantIdRaw is null || !obfuscator.TryDecode(tenantIdRaw, out var tenantId))
        {
            // 身份级 Token 无租户上下文：租户菜单不可用
            throw new UnauthorizedException("当前会话不包含租户上下文，请先进入租户");
        }

        return ApiResponse<MenuSnapshot>.Ok(
            await tenantMenuQuery.GetAsync(tenantId, cancellationToken));
    }
}
