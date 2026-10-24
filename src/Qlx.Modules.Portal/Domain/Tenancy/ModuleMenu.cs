/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-23 22:30:00 +08:00 中国标准时间 星期四
 * 修改时间：2026-10-23 22:30:00 +08:00 中国标准时间 星期四
 * 描述：导航菜单定义实体，门户侧登记所有模块导出的前端菜单树
 */

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Qlx.Modules.Portal.Domain.Tenancy;

/// <summary>
/// 导航菜单定义（表前缀 por_）。门户侧登记所有模块 Manifest 导出的前端菜单，
/// 形成跨模块的菜单树，供租户侧前端按角色权限裁剪后下发。
/// 继承 <see cref="Qlx.Abstractions.Domain.Entity"/>：雪花 Id 主键统一。
/// </summary>
[Table("por_module_menus")]
[Index(nameof(ModuleCode), nameof(MenuCode), IsUnique = true)]
[Index(nameof(ModuleCode), nameof(ParentCode), nameof(SortOrder))]
[Index(nameof(RequiredPermissionCode))]
public sealed class ModuleMenu : Qlx.Abstractions.Domain.Entity
{
    /// <summary>所属模块编码（外键 → por_module_definitions.module_code）。</summary>
    [MaxLength(64)]
    public string ModuleCode { get; init; } = string.Empty;

    /// <summary>菜单编码（模块内唯一，如 purchase.order.list）。</summary>
    [MaxLength(64)]
    public string MenuCode { get; init; } = string.Empty;

    /// <summary>父级菜单编码（顶级菜单为 null）。</summary>
    [MaxLength(64)]
    public string? ParentCode { get; init; }

    /// <summary>导航分组名（顶层菜单按组折叠展示，如 采购管理 / 销售管理）。</summary>
    [MaxLength(128)]
    public string? GroupName { get; init; }

    /// <summary>菜单显示名称（中文）。</summary>
    [MaxLength(128)]
    public string DisplayName { get; init; } = string.Empty;

    /// <summary>前端路由路径（相对路径，如 /purchase/order/list）。</summary>
    [MaxLength(256)]
    public string Route { get; init; } = string.Empty;

    /// <summary>菜单图标标识（前端 Icon 组件名）。</summary>
    [MaxLength(64)]
    public string? Icon { get; init; }

    /// <summary>访问该菜单所需权限点编码（外键 → por_permission_definitions.permission_code）。</summary>
    [MaxLength(128)]
    public string? RequiredPermissionCode { get; init; }

    /// <summary>菜单排序（同级内升序）。</summary>
    public int SortOrder { get; init; }

    /// <summary>是否在菜单中可见（隐藏后可通过权限单独授权）。</summary>
    public bool IsVisible { get; init; } = true;

    /// <summary>打开方式：0=当前标签页 / 1=新标签页 / 2=外部浏览器。</summary>
    public byte OpenMode { get; init; } = 1;

    /// <summary>Manifest 已移除标记（软删，不物理删除）。</summary>
    public bool IsDeprecated { get; set; }
}
