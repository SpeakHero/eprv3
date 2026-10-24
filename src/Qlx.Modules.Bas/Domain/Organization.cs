/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-21
 * 描述：组织架构节点类型（公司/部门/团队）
 */

using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace Qlx.Modules.Bas.Domain;

/// <summary>组织架构节点类型。</summary>
public enum OrgNodeType
{
    /// <summary>公司（顶层节点）。</summary>
    Company = 1,

    /// <summary>部门（中层组织）。</summary>
    Department = 2,

    /// <summary>团队（最小执行单元）。</summary>
    Team = 3,
}

/// <summary>
/// 实体启用/停用状态（与前端 EntityStatus 字符串映射：1↔Enabled / 2↔Disabled）。
/// </summary>
public enum EntityStatus
{
    Enabled = 1,
    Disabled = 2,
}

/// <summary>
/// 组织机构实体（表前缀 bas_）。租户内组织架构树节点，自引用形成层级。
/// 根节点 ParentId 为 null 且 IsBuiltin=true。
/// </summary>
[Index(nameof(ParentId))]
[Index(nameof(Status))]
public sealed class Organization : Qlx.Abstractions.Domain.AuditableEntity, Qlx.Abstractions.Domain.ITenantEntity
{
    /// <summary>所属租户 Id。</summary>
    public long TenantId { get; internal set; }

    /// <summary>父节点 Id（null 表示根）。</summary>
    public long? ParentId { get; internal set; }

    /// <summary>组织名称。</summary>
    [MaxLength(100)]
    public string Name { get; internal set; } = string.Empty;

    /// <summary>节点类型。</summary>
    public OrgNodeType Type { get; internal set; }

    /// <summary>负责人关联 por_tenant_members.id（可为空）。</summary>
    public long? ManagerMemberId { get; internal set; }

    /// <summary>启停状态。</summary>
    public EntityStatus Status { get; private set; } = EntityStatus.Enabled;

    /// <summary>同级排序。</summary>
    public int SortOrder { get; internal set; }

    /// <summary>是否系统内置根节点（禁止删除）。</summary>
    public bool IsBuiltin { get; internal set; }

    /// <summary>软删标记。</summary>
    public bool IsDeleted { get; private set; }

    public Organization() { }

    public Organization(long tenantId, string name, OrgNodeType type, long? parentId, int sortOrder = 0)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        TenantId = tenantId;
        Name = name;
        Type = type;
        ParentId = parentId;
        SortOrder = sortOrder;
    }

    public void Disable() => Status = EntityStatus.Disabled;
    public void Enable() => Status = EntityStatus.Enabled;
    public void SoftDelete() => IsDeleted = true;
}
