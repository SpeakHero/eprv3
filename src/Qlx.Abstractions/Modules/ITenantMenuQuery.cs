/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-24
 * 修改时间：2026-10-24
 * 描述：菜单扁平查询契约（阶段 R-3）：门户/租户侧共用扁平 DTO，后端只投影不组装树（B-2）
 */

namespace Qlx.Abstractions.Modules;

/// <summary>
/// 导航分组扁平行（por_navigation_groups 投影）。
/// </summary>
/// <param name="Id">分组主键（对外经 Hashids 混淆）。</param>
/// <param name="GroupCode">分组编码（如 workbench）。</param>
/// <param name="GroupName">分组显示名。</param>
/// <param name="ModuleCodes">逗号分隔的模块编码，如 <c>portal,bas</c>。</param>
/// <param name="SortOrder">同级排序号，越小越靠前。</param>
public sealed record MenuNavGroupRow(
    long Id,
    string GroupCode,
    string GroupName,
    string ModuleCodes,
    int SortOrder);

/// <summary>
/// 菜单扁平行（por_module_menus 投影）。前端按 <see cref="ParentCode"/> 组装树。
/// </summary>
/// <param name="Id">菜单主键（对外经 Hashids 混淆）。</param>
/// <param name="ModuleCode">所属模块编码。</param>
/// <param name="MenuCode">菜单编码（模块内唯一）。</param>
/// <param name="ParentCode">父菜单编码；顶级为 <see langword="null"/>。</param>
/// <param name="GroupName">菜单所属导航分组名（可空）。</param>
/// <param name="DisplayName">菜单显示名。</param>
/// <param name="Route">前端路由地址。</param>
/// <param name="Icon">图标标识；无图标为 <see langword="null"/>。</param>
/// <param name="RequiredPermissionCode">访问所需权限码；<see langword="null"/> 表示仅需模块许可。</param>
/// <param name="SortOrder">同级排序号。</param>
/// <param name="OpenMode">打开方式：0=当前页 / 1=新标签页 / 2=外部浏览器。</param>
public sealed record MenuRow(
    long Id,
    string ModuleCode,
    string MenuCode,
    string? ParentCode,
    string? GroupName,
    string DisplayName,
    string Route,
    string? Icon,
    string? RequiredPermissionCode,
    int SortOrder,
    byte OpenMode);

/// <summary>
/// 菜单快照：导航分组 + 扁平菜单列表（后端不组装树）。
/// </summary>
/// <param name="NavGroups">导航分组扁平列表。</param>
/// <param name="Menus">菜单扁平列表。</param>
public sealed record MenuSnapshot(
    IReadOnlyList<MenuNavGroupRow> NavGroups,
    IReadOnlyList<MenuRow> Menus);

/// <summary>
/// 租户菜单查询契约（由 Portal 模块实现，Bas 租户自管理域消费）。
/// 实现侧负责按租户模块许可裁剪，并按 C-1/C-2 走统一缓存。
/// </summary>
public interface ITenantMenuQuery
{
    /// <summary>
    /// 获取指定租户可见的菜单快照（仅含已许可模块的菜单与相关导航分组）。
    /// </summary>
    /// <param name="tenantId">租户主键（真实 long）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>扁平菜单快照，前端负责树组装。</returns>
    Task<MenuSnapshot> GetAsync(long tenantId, CancellationToken cancellationToken);
}
