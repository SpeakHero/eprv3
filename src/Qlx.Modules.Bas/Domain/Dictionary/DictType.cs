/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-25 23:30:00 +08:00 中国标准时间 星期日
 * 描述：数据字典类型实体（bas_dict_types）——集团级 / 子公司级两级归属，租户内类型编码唯一
 */

using System.ComponentModel.DataAnnotations;

using Microsoft.EntityFrameworkCore;

namespace Qlx.Modules.Bas.Domain.Dictionary;

/// <summary>字典归属层级（数值枚举，中文文案由前端公共层映射，B-2-5）。</summary>
public enum DictOwnerLevel
{
    /// <summary>集团级（租户内全部成员可见，仅租户根组织可维护）。</summary>
    Group = 1,

    /// <summary>子公司级（仅所属子公司可见、可维护）。</summary>
    Subsidiary = 2,
}

/// <summary>
/// 数据字典类型实体（表 bas_dict_types）。类型编码一经创建不可修改（系统内置类型也不可删除），
/// 两级可见性由服务层在 SQL 层过滤（集团级全租户可见 / 子公司级仅本组织可见）。
/// </summary>
[Index(nameof(TenantId), nameof(TypeCode), IsUnique = true)]
[Index(nameof(TenantId), nameof(OwnerLevel))]
[Index(nameof(TenantId), nameof(OrgId))]
public sealed class DictType : Qlx.Abstractions.Domain.AuditableEntity, Qlx.Abstractions.Domain.ITenantEntity
{
    /// <summary>所属租户 Id。</summary>
    public long TenantId { get; internal set; }

    /// <summary>字典类型编码（租户内唯一，不可修改）。</summary>
    [MaxLength(64)]
    public string TypeCode { get; internal set; } = string.Empty;

    /// <summary>字典类型名称。</summary>
    [MaxLength(64)]
    public string TypeName { get; internal set; } = string.Empty;

    /// <summary>归属层级（集团级 / 子公司级）。</summary>
    public DictOwnerLevel OwnerLevel { get; internal set; }

    /// <summary>所属组织 Id（子公司级必填，集团级为空）。</summary>
    public long? OrgId { get; internal set; }

    /// <summary>启停状态（新建默认启用）。</summary>
    public EntityStatus Status { get; private set; } = EntityStatus.Enabled;

    /// <summary>是否系统内置（内置类型不可删除，仅可停用 / 启用）。</summary>
    public bool IsSystem { get; internal set; }

    public DictType() { }

    public DictType(
        long tenantId, string typeCode, string typeName, DictOwnerLevel ownerLevel, long? orgId, bool isSystem = false)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(typeCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(typeName);
        TenantId = tenantId;
        TypeCode = typeCode;
        TypeName = typeName;
        OwnerLevel = ownerLevel;
        OrgId = ownerLevel == DictOwnerLevel.Subsidiary ? orgId : null;
        IsSystem = isSystem;
    }

    /// <summary>重命名字典类型（编码不可变更）。</summary>
    /// <param name="typeName">新名称。</param>
    public void Rename(string typeName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(typeName);
        TypeName = typeName;
    }

    /// <summary>停用字典类型。</summary>
    public void Disable() => Status = EntityStatus.Disabled;

    /// <summary>启用字典类型。</summary>
    public void Enable() => Status = EntityStatus.Enabled;
}
