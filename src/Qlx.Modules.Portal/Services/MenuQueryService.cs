/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-24
 * 修改时间：2026-10-24
 * 描述：菜单扁平查询实现（阶段 R-3）：读取 por_navigation_groups + por_module_menus，
 *       门户级全量下发、租户级按模块许可裁剪；统一 ICacheService.GetOrCreateAsync（C-1/C-2/C-4）
 */

using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Qlx.Abstractions.Modules;
using Qlx.BuildingBlocks.Caching;
using Qlx.Modules.Portal.Domain.Tenancy;

namespace Qlx.Modules.Portal.Services;

/// <summary>
/// 菜单扁平查询服务。后端只做投影与许可裁剪，树组装由前端负责（B-2）。
/// <para>
/// 缓存键：门户级 <c>_portal:menu:portal</c>；租户级 <c>_portal:menu:tenant-{tenantId}</c>。
/// Manifest 同步完成后由 <see cref="PortalManifestSyncService"/> 按 <c>_portal:menu:</c> 前缀失效（C-5 失效优先）。
/// </para>
/// </summary>
/// <param name="dbFactory">Portal 库上下文工厂。</param>
/// <param name="cache">统一缓存服务。</param>
/// <param name="licenseService">模块许可查询（租户级裁剪用，自身已 Cache-Aside）。</param>
public sealed class MenuQueryService(
    IDbContextFactory<PortalDbContext> dbFactory,
    ICacheService cache,
    IModuleLicenseService licenseService) : ITenantMenuQuery
{
    /// <summary>菜单缓存键前缀（写路径按此前缀失效）。</summary>
    public const string CachePrefix = "_portal:menu:";

    /// <summary>门户级菜单缓存键。</summary>
    public static readonly string PortalCacheKey = QlxCacheKey.BuildPortalKey("menu", "portal");

    private static readonly CacheEntryOptions CacheOptions = new()
    {
        AbsoluteExpirationRelativeToNow = PortalConstants.CacheKeys.MenuTtl,
    };

    /// <summary>
    /// 构建租户级菜单缓存键（唯一入口，禁止散装拼 key，C-1-2）。
    /// </summary>
    /// <param name="tenantId">租户主键。</param>
    /// <returns>规范化缓存键。</returns>
    public static string BuildTenantCacheKey(long tenantId)
        => QlxCacheKey.BuildPortalKey("menu", $"tenant-{tenantId.ToString(CultureInfo.InvariantCulture)}");

    /// <summary>
    /// 获取门户级全量菜单快照（不裁剪，供门户管理端与身份级会话使用）。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>扁平菜单快照。</returns>
    public Task<MenuSnapshot> GetPortalAsync(CancellationToken cancellationToken)
        => cache.GetOrCreateAsync(
            PortalCacheKey,
            factory: LoadAllAsync,
            cancellationToken: cancellationToken,
            options: CacheOptions);

    /// <inheritdoc />
    public Task<MenuSnapshot> GetAsync(long tenantId, CancellationToken cancellationToken)
    {
        if (tenantId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(tenantId), tenantId, "租户 ID 必须大于 0");
        }

        return cache.GetOrCreateAsync(
            BuildTenantCacheKey(tenantId),
            factory: ct => LoadForTenantAsync(tenantId, ct),
            cancellationToken: cancellationToken,
            options: CacheOptions);
    }

    /// <summary>
    /// 查询全部门口的导航分组 + 菜单（扁平原始字段）。
    /// </summary>
    private async Task<MenuSnapshot> LoadAllAsync(CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);

        var navGroups = await QueryNavGroupsAsync(db, ct).ConfigureAwait(false);
        var menus = await QueryMenusAsync(db, ct).ConfigureAwait(false);
        return new MenuSnapshot(navGroups, menus);
    }

    /// <summary>
    /// 查询租户可见菜单：先取全量，再按租户许可模块裁剪（安全过滤留在后端，B-3）。
    /// </summary>
    private async Task<MenuSnapshot> LoadForTenantAsync(long tenantId, CancellationToken ct)
    {
        var all = await LoadAllAsync(ct).ConfigureAwait(false);

        var moduleCodes = all.Menus
            .Select(static m => m.ModuleCode)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var licensedSet = new HashSet<string>(StringComparer.Ordinal);
        foreach (var moduleCode in moduleCodes)
        {
            if (await licenseService.HasLicenseAsync(tenantId, moduleCode, ct).ConfigureAwait(false))
            {
                licensedSet.Add(moduleCode);
            }
        }

        var menus = all.Menus.Where(m => licensedSet.Contains(m.ModuleCode)).ToList();

        // 导航分组仅保留仍有菜单落入的模块（moduleCodes 为逗号分隔）
        var navGroups = all.NavGroups
            .Where(g => g.ModuleCodes
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Any(licensedSet.Contains))
            .ToList();

        return new MenuSnapshot(navGroups, menus);
    }

    /// <summary>查询导航分组扁平列表。</summary>
    private static async Task<IReadOnlyList<MenuNavGroupRow>> QueryNavGroupsAsync(
        PortalDbContext db, CancellationToken ct)
    {
        var rows = await db.NavigationGroups
            .AsNoTracking()
            .Where(g => !g.IsDeprecated)
            .OrderBy(g => g.SortOrder)
            .Select(g => new MenuNavGroupRow(g.Id, g.GroupCode, g.GroupName, g.ModuleCodes, g.SortOrder))
            .ToListAsync(ct).ConfigureAwait(false);

        return rows;
    }

    /// <summary>查询菜单扁平列表（仅可见且未软删）。</summary>
    private static async Task<IReadOnlyList<MenuRow>> QueryMenusAsync(
        PortalDbContext db, CancellationToken ct)
    {
        var rows = await db.ModuleMenus
            .AsNoTracking()
            .Where(m => m.IsVisible && !m.IsDeprecated)
            .OrderBy(m => m.ModuleCode)
            .ThenBy(m => m.ParentCode)
            .ThenBy(m => m.SortOrder)
            .Select(m => new MenuRow(
                m.Id,
                m.ModuleCode,
                m.MenuCode,
                m.ParentCode,
                m.GroupName,
                m.DisplayName,
                m.Route,
                m.Icon,
                m.RequiredPermissionCode,
                m.SortOrder,
                m.OpenMode))
            .ToListAsync(ct).ConfigureAwait(false);

        return rows;
    }
}
