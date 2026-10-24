/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-23 22:30:00 +08:00 中国标准时间 星期四
 * 修改时间：2026-10-24 00:30:00 +08:00 中国标准时间 星期五
 * 描述：门户侧角色 + 角色权限 + 用户角色关联三表实体（角色在 Portal 库，租户内角色在 Bas 库）
 */

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore;

namespace Qlx.Modules.Portal.Domain.Tenancy;

/// <summary>
/// 门户角色（表前缀 por_）。平台级角色，与租户内角色（<c>bas_roles</c>）形成两级授权体系：
/// 门户角色用于租户管理员 / 平台运维等跨租户身份授权。
/// 继承 <see cref="Qlx.Abstractions.Domain.AuditableEntity"/>：雪花 Id + CreatedAt/UpdatedAt 审计（门户级，无租户隔离）。
/// 属性为 <c>internal init / internal set</c>：同 assembly（Portal 模块）Service 层可访问，外部模块只读。
/// </summary>
[Table("por_roles")]
[Index(nameof(RoleCode), IsUnique = true)]
public sealed class PortalRole : Qlx.Abstractions.Domain.AuditableEntity
{
    /// <summary>角色编码（业务唯一键，如 portal.admin / portal.ops）。唯一索引禁止修改，故为 internal init。</summary>
    [MaxLength(64)]
    public string RoleCode { get; internal init; } = string.Empty;

    /// <summary>角色显示名称（中文）。</summary>
    [MaxLength(64)]
    public string RoleName { get; internal set; } = string.Empty;

    /// <summary>角色说明。</summary>
    [MaxLength(256)]
    public string? Description { get; internal set; }

    /// <summary>是否系统内置角色（禁止删除、禁止修改编码 / IsSystem 标记）。</summary>
    public bool IsSystem { get; internal init; }
}

/// <summary>
/// 门户角色-权限点关联（表前缀 por_）。为门户角色分配权限点，
/// PermissionCode 与 <c>por_permission_definitions.permission_code</c> 对齐。
/// 继承 <see cref="Qlx.Abstractions.Domain.Entity"/>：雪花 Id 主键统一。
/// </summary>
[Table("por_role_permissions")]
[Index(nameof(RoleId))]
[Index(nameof(RoleId), nameof(PermissionCode), IsUnique = true)]
[SuppressMessage("Naming", "CA1711", Justification = "RolePermission 是业务领域中间表命名约定，与 bas_role_permissions / por_role_permissions 表名对齐")]
public sealed class PortalRolePermission : Qlx.Abstractions.Domain.Entity
{
    /// <summary>关联 por_roles.id。</summary>
    public long RoleId { get; internal init; }

    /// <summary>权限点编码（外键 → por_permission_definitions.permission_code）。</summary>
    [MaxLength(128)]
    public string PermissionCode { get; internal init; } = string.Empty;
}

/// <summary>
/// 门户用户-角色关联（表前缀 por_）。将门户侧用户（跨租户身份）关联到门户角色，
/// 用户基础信息通过单独查询获取，禁止跨模块 Join。
/// 继承 <see cref="Qlx.Abstractions.Domain.Entity"/>：雪花 Id 主键统一。
/// </summary>
[Table("por_user_roles")]
[Index(nameof(RoleId))]
[Index(nameof(UserId), nameof(RoleId), IsUnique = true)]
public sealed class PortalUserRole : Qlx.Abstractions.Domain.Entity
{
    /// <summary>关联门户侧用户表 Id。</summary>
    public long UserId { get; internal init; }

    /// <summary>关联 por_roles.id。</summary>
    public long RoleId { get; internal init; }
}
