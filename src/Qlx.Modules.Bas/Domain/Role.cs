/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-21
 * 描述：租户角色实体 + 数据范围枚举（roles + role_permissions + member_roles）
 */

using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace Qlx.Modules.Bas.Domain;

/// <summary>数据范围（与前端 DataScope 字符串映射）。</summary>
public enum DataScope
{
    All = 1,
    Custom = 2,
    DeptAndChildren = 3,
    Dept = 4,
    Self = 5,
}

/// <summary>
/// 租户角色实体（表前缀 bas_）。租户内自定义角色，关联权限点和成员。
/// </summary>
[Index(nameof(TenantId), nameof(Code), IsUnique = true)]
public sealed class Role : Qlx.Abstractions.Domain.AuditableEntity, Qlx.Abstractions.Domain.ITenantEntity
{
    /// <summary>所属租户 Id。</summary>
    public long TenantId { get; internal set; }

    /// <summary>角色编码（租户内唯一）。</summary>
    [MaxLength(50)]
    public string Code { get; internal set; } = string.Empty;

    /// <summary>角色名称。</summary>
    [MaxLength(100)]
    public string Name { get; internal set; } = string.Empty;

    /// <summary>角色说明。</summary>
    [MaxLength(500)]
    public string? Description { get; internal set; }

    /// <summary>是否系统内置角色（禁止删除、禁止修改编码、禁止修改关键权限）。</summary>
    public bool IsBuiltin { get; internal set; }

    /// <summary>默认数据范围。</summary>
    public DataScope DefaultDataScope { get; internal set; } = DataScope.Self;

    /// <summary>软删标记。</summary>
    public bool IsDeleted { get; private set; }

    public Role() { }

    public Role(long tenantId, string code, string name, bool isBuiltin = false)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        TenantId = tenantId;
        Code = code;
        Name = name;
        IsBuiltin = isBuiltin;
    }

    public void SoftDelete() => IsDeleted = true;
}

/// <summary>
/// 角色-权限点关联（表前缀 bas_）。复合主键 (RoleId, PermissionCode, TenantId)。
/// PermissionCode 与 Portal 库 por_permission_definitions 对齐。
/// </summary>
#pragma warning disable CA1711 // 类型名不以 "Permission" 结尾（业务语义需要，不可改名）
[PrimaryKey(nameof(RoleId), nameof(PermissionCode), nameof(TenantId))]
[Index(nameof(TenantId))]
public sealed class RolePermission : Qlx.Abstractions.Domain.Entity
{
    public long RoleId { get; internal set; }
    public long TenantId { get; internal set; }

    [MaxLength(100)]
    public string PermissionCode { get; internal set; } = string.Empty;

    public RolePermission() { }

    public RolePermission(long roleId, long tenantId, string permissionCode)
    {
        RoleId = roleId;
        TenantId = tenantId;
        PermissionCode = permissionCode;
    }
}
#pragma warning restore CA1711

/// <summary>
/// 成员-角色关联（表前缀 bas_）。复合主键 (TenantMemberId, RoleId, TenantId)。
/// TenantMemberId 关联 Portal 库 por_tenant_members.id——架构上为了禁止跨模块 Join，
/// 成员基础信息通过 Abstractions.Queries.ITenantMemberQuery 单独查询。
/// </summary>
[PrimaryKey(nameof(TenantMemberId), nameof(RoleId), nameof(TenantId))]
[Index(nameof(RoleId))]
[Index(nameof(TenantMemberId))]
public sealed class MemberRole : Qlx.Abstractions.Domain.Entity
{
    /// <summary>关联 por_tenant_members.id。</summary>
    public long TenantMemberId { get; internal set; }
    public long RoleId { get; internal set; }
    public long TenantId { get; internal set; }

    public MemberRole() { }

    public MemberRole(long tenantMemberId, long roleId, long tenantId)
    {
        TenantMemberId = tenantMemberId;
        RoleId = roleId;
        TenantId = tenantId;
    }
}
