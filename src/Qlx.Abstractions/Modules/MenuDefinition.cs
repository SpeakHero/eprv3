/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-20 22:00:00 +08:00 中国标准时间 星期二
 * 修改时间：2026-10-20 22:00:00 +08:00 中国标准时间 星期二
 * 描述：菜单项定义，模块经 Manifest 声明，前端单壳按许可下发后渲染
 */

namespace Qlx.Abstractions.Modules;

/// <summary>
/// 菜单项定义。<see cref="RequiredPermissionCode"/> 为空时仅需模块许可即可见。
/// </summary>
/// <param name="Code">菜单编码，模块内唯一。</param>
/// <param name="Name">菜单显示名。</param>
/// <param name="Route">前端路由地址。</param>
/// <param name="Icon">图标标识；无图标为 <see langword="null"/>。</param>
/// <param name="ParentCode">父菜单编码；顶级菜单为 <see langword="null"/>。</param>
/// <param name="SortOrder">同级排序号，越小越靠前。</param>
/// <param name="RequiredPermissionCode">访问所需权限点编码。</param>
public sealed record MenuDefinition(
    string Code,
    string Name,
    string Route,
    string? Icon,
    string? ParentCode,
    int SortOrder,
    string? RequiredPermissionCode);
