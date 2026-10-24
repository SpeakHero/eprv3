/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-23
 * 修改时间：2026-10-23
 * 描述：门户菜单 API：返回导航分组 + 模块菜单扁平 JSON 结构。
 *       前端负责按 GroupCode / ParentCode 组装树形结构，后端只做投影。
 */

using System.Net.Mime;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OpenIddict.Validation.AspNetCore;
using Qlx.Modules.Portal.Domain.Tenancy;

namespace Qlx.Modules.Portal.Controllers;

/// <summary>
/// 门户菜单 Controller。
/// <para>
/// 数据源：Portal 库 por_navigation_groups + por_module_menus。
/// 返回形态：扁平 JSON（navGroups / menus 两个数组），前端负责组装树形。
/// </para>
/// </summary>
[ApiController]
[Authorize(AuthenticationSchemes = OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme)]
[Route("api/portal/menus")]
public sealed class PortalMenuController(
    IDbContextFactory<PortalDbContext> dbFactory) : ControllerBase
{
    /// <summary>
    /// 获取导航分组 + 菜单（扁平 JSON，前端负责组装）。
    /// </summary>
    /// <param name="cancellationToken">请求中止令牌。</param>
    [HttpGet]
    [Produces(MediaTypeNames.Application.Json)]
    public async Task<IActionResult> GetMenus(CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

        var navGroups = await db.Set<NavigationGroup>()
            .AsNoTracking()
            .Where(g => !g.IsDeprecated)
            .OrderBy(g => g.SortOrder)
            .Select(g => new
            {
                id = g.Id,
                groupCode = g.GroupCode,
                groupName = g.GroupName,
                moduleCodes = g.ModuleCodes,
                sortOrder = g.SortOrder,
            })
            .ToListAsync(cancellationToken);

        var menus = await db.Set<ModuleMenu>()
            .AsNoTracking()
            .Where(m => m.IsVisible && !m.IsDeprecated)
            .OrderBy(m => m.ModuleCode)
            .ThenBy(m => m.ParentCode)
            .ThenBy(m => m.SortOrder)
            .Select(m => new
            {
                id = m.Id,
                moduleCode = m.ModuleCode,
                menuCode = m.MenuCode,
                parentCode = m.ParentCode,
                groupName = m.GroupName,
                displayName = m.DisplayName,
                route = m.Route,
                icon = m.Icon,
                requiredPermissionCode = m.RequiredPermissionCode,
                sortOrder = m.SortOrder,
                openMode = m.OpenMode,
            })
            .ToListAsync(cancellationToken);

        return Ok(new { navGroups, menus });
    }
}
