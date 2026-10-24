/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-23 22:30:00 +08:00 中国标准时间 星期四
 * 修改时间：2026-10-23 22:30:00 +08:00 中国标准时间 星期四
 * 描述：导航分组定义实体，跨模块菜单顶层分组配置
 */

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Qlx.Modules.Portal.Domain.Tenancy;

/// <summary>
/// 导航分组定义（表前缀 por_）。跨模块菜单顶层分组，
/// 用于前端侧边栏折叠展示。一个导航分组可聚合多个模块的菜单树。
/// 继承 <see cref="Qlx.Abstractions.Domain.Entity"/>：雪花 Id 主键统一。
/// </summary>
[Table("por_navigation_groups")]
[Index(nameof(GroupCode), IsUnique = true)]
public sealed class NavigationGroup : Qlx.Abstractions.Domain.Entity
{
    /// <summary>分组编码（业务唯一键，如 biz / support / portal）。</summary>
    [MaxLength(32)]
    public string GroupCode { get; init; } = string.Empty;

    /// <summary>分组显示名称（中文，如 业务 / 支撑 / 门户）。</summary>
    [MaxLength(64)]
    public string GroupName { get; init; } = string.Empty;

    /// <summary>聚合的模块编码列表（逗号分隔，如 pur,sal,bas）。</summary>
    [MaxLength(512)]
    public string ModuleCodes { get; init; } = string.Empty;

    /// <summary>分组排序（升序）。</summary>
    public int SortOrder { get; init; }

    /// <summary>Manifest 已移除标记（软删，不物理删除）。</summary>
    public bool IsDeprecated { get; set; }
}
