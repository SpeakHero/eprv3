/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-20 23:00:00 +08:00 中国标准时间 星期二
 * 修改时间：2026-10-23 22:30:00 +08:00 中国标准时间 星期四
 * 描述：权限点定义实体，门户侧登记所有菜单/按钮/数据/字段权限点（PermissionKind 枚举统一引用 Abstractions）
 */

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using Qlx.Abstractions.Modules;

namespace Qlx.Modules.Portal.Domain.Tenancy;

/// <summary>
/// 权限点定义（表前缀 por_）。门户侧登记所有模块导出的权限点，
/// 为租户角色授权与 Token 签发提供权威数据源。
/// 继承 <see cref="Qlx.Abstractions.Domain.Entity"/>：雪花 Id 主键统一。
/// 原业务唯一键 PermissionCode 改为唯一索引。
/// <para><b>PermissionKind 枚举已统一</b>：删除本模块本地定义的 PermissionKind，
/// 统一使用 Abstractions 层 <see cref="Qlx.Abstractions.Modules.PermissionKind"/>（Menu=0/Action=1/Data=2/Field=3）。
/// 数据库存储为整数，与原本地 PermissionKind.DataScope=2 数值一致，无迁移需求。</para>
/// </summary>
[Table("por_permission_definitions")]
[Index(nameof(PermissionCode), IsUnique = true)]
[Index(nameof(ModuleCode))]
[Index(nameof(ModuleCode), nameof(Kind))]
public sealed class PermissionDefinition : Qlx.Abstractions.Domain.Entity
{
    /// <summary>权限点唯一标识（业务唯一键，如 bas.supplier.edit）。</summary>
    [MaxLength(128)]
    public string PermissionCode { get; init; } = string.Empty;

    /// <summary>所属模块编码（外键 → por_module_definitions）。</summary>
    [MaxLength(64)]
    public string ModuleCode { get; init; } = string.Empty;

    /// <summary>权限点类型。</summary>
    public PermissionKind Kind { get; init; }

    /// <summary>权限点显示名称（中文）。</summary>
    [MaxLength(128)]
    public string DisplayName { get; init; } = string.Empty;

    /// <summary>权限点分组（同一模块内按功能分组展示，如 供应商管理 / 客户管理）。</summary>
    [MaxLength(128)]
    public string? GroupName { get; init; }

    /// <summary>权限点排序（前端展示顺序）。</summary>
    public int SortOrder { get; init; }

    /// <summary>权限点描述。</summary>
    [MaxLength(256)]
    public string? Description { get; init; }

    /// <summary>Manifest 已移除标记（软删，不物理删除）。</summary>
    public bool IsDeprecated { get; set; }
}
