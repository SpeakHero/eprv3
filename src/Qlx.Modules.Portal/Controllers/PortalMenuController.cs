/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.1.0 beta
 * 创建时间：2026-10-23
 * 修改时间：2026-10-24
 * 描述：门户菜单 API：返回导航分组 + 模块菜单扁平 JSON 结构（ApiResponse 统一契约，G-1）。
 *       前端负责按 GroupCode / ParentCode 组装树形结构，后端只做投影（B-2）；
 *       读路径经 ICacheService.GetOrCreateAsync（C-2），Manifest 同步后按前缀失效（G-4）。
 */

using System.Net.Mime;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OpenIddict.Validation.AspNetCore;
using Qlx.Abstractions.Modules;
using Qlx.BuildingBlocks.Api;
using Qlx.Modules.Portal.Services;

namespace Qlx.Modules.Portal.Controllers;

/// <summary>
/// 门户菜单 Controller。
/// <para>
/// 数据源：Portal 库 por_navigation_groups + por_module_menus（经 <see cref="MenuQueryService"/> 缓存）。
/// 返回形态：扁平 JSON（navGroups / menus 两个数组），前端负责组装树形。
/// </para>
/// </summary>
/// <param name="menuQuery">菜单扁平查询服务。</param>
[ApiController]
[Authorize(AuthenticationSchemes = OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme)]
[Route("api/portal/menus")]
public sealed class PortalMenuController(MenuQueryService menuQuery) : ControllerBase
{
    /// <summary>
    /// 获取导航分组 + 菜单（扁平 JSON，前端负责组装）。
    /// </summary>
    /// <param name="cancellationToken">请求中止令牌。</param>
    [HttpGet]
    [Produces(MediaTypeNames.Application.Json)]
    public async Task<ApiResponse<MenuSnapshot>> GetMenus(CancellationToken cancellationToken)
    {
        return ApiResponse<MenuSnapshot>.Ok(await menuQuery.GetPortalAsync(cancellationToken));
    }
}
