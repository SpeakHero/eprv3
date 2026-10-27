/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：2.0.0 beta
 * 创建时间：2026-10-21
 * 修改时间：2026-10-25 12:00:00 +08:00 中国标准时间 星期日
 * 描述：组织架构节点类型（集团/子公司/部门/项目部，D2 重定义）与组织机构实体
 */

using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace Qlx.Modules.Bas.Domain;

/// <summary>组织架构节点类型（D2：集团 / 子公司 / 部门 / 项目部）。</summary>
public enum OrgNodeType
{
    /// <summary>集团（顶层节点）。</summary>
    Group = 1,

    /// <summary>子公司。</summary>
    Subsidiary = 2,

    /// <summary>部门。</summary>
    Department = 3,

    /// <summary>项目部。</summary>
    Project = 4,
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
[Index(nameof(TenantId), nameof(Code), IsUnique = true)]
[Index(nameof(ParentId))]
[Index(nameof(Status))]
public sealed class Organization : Qlx.Abstractions.Domain.AuditableEntity, Qlx.Abstractions.Domain.ITenantEntity
{
    /// <summary>所属租户 Id。</summary>
    public long TenantId { get; internal set; }

    /// <summary>父节点 Id（null 表示根）。</summary>
    public long? ParentId { get; internal set; }

    /// <summary>组织编码（租户内唯一，经统一发号器生成，不可修改）。</summary>
    [MaxLength(20)]
    public string Code { get; internal set; } = string.Empty;

    /// <summary>组织名称。</summary>
    [MaxLength(100)]
    public string Name { get; internal set; } = string.Empty;

    /// <summary>节点类型。</summary>
    public OrgNodeType Type { get; internal set; }

    /// <summary>负责人（自然人 Id，可为空；旧负责人关联在迁移中置空）。</summary>
    public long? LeaderPersonId { get; internal set; }

    /// <summary>生效日期。</summary>
    public DateOnly EffectDate { get; internal set; }

    /// <summary>失效日期（null 表示长期有效）。</summary>
    public DateOnly? ExpiryDate { get; internal set; }

    /// <summary>数据版本号（每次写操作 +1，版本快照与历史回看使用）。</summary>
    public int Version { get; private set; } = 1;

    /// <summary>启停状态。</summary>
    public EntityStatus Status { get; private set; } = EntityStatus.Enabled;

    /// <summary>同级排序。</summary>
    public int SortOrder { get; internal set; }

    /// <summary>是否系统内置根节点（禁止删除）。</summary>
    public bool IsBuiltin { get; internal set; }

    /// <summary>软删标记。</summary>
    public bool IsDeleted { get; private set; }

    public Organization() { }

    public Organization(long tenantId, string name, OrgNodeType type, long? parentId, int sortOrder = 0, string? code = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        TenantId = tenantId;
        Name = name;
        Type = type;
        ParentId = parentId;
        SortOrder = sortOrder;
        Code = code ?? string.Empty;
        EffectDate = DateOnly.FromDateTime(DateTime.UtcNow);
    }

    /// <summary>重命名。</summary>
    /// <param name="name">新名称。</param>
    public void Rename(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name;
    }

    /// <summary>变更父级。</summary>
    /// <param name="parentId">新父级 Id（null 表示根）。</param>
    public void ChangeParent(long? parentId) => ParentId = parentId;

    /// <summary>变更节点类型（组织调级）。</summary>
    /// <param name="type">新节点类型。</param>
    public void ChangeType(OrgNodeType type) => Type = type;

    /// <summary>设置负责人。</summary>
    /// <param name="personId">负责人自然人 Id（null 表示清空）。</param>
    public void SetLeader(long? personId) => LeaderPersonId = personId;

    /// <summary>变更有效期。</summary>
    /// <param name="effectDate">生效日期。</param>
    /// <param name="expiryDate">失效日期（null 表示长期有效）。</param>
    public void ChangePeriod(DateOnly effectDate, DateOnly? expiryDate)
    {
        EffectDate = effectDate;
        ExpiryDate = expiryDate;
    }

    /// <summary>版本号 +1（每次写操作由服务层调用一次）。</summary>
    public void TouchVersion() => Version++;

    /// <summary>停用。</summary>
    public void Disable() => Status = EntityStatus.Disabled;

    /// <summary>启用。</summary>
    public void Enable() => Status = EntityStatus.Enabled;

    /// <summary>软删。</summary>
    public void SoftDelete() => IsDeleted = true;
}
