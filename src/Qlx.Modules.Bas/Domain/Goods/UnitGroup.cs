/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-25 16:00:00 +08:00 中国标准时间 星期日
 * 描述：计量单位组实体（bas_unit_groups）——一组可互相换算的计量单位归类
 */

using System.ComponentModel.DataAnnotations;

using Microsoft.EntityFrameworkCore;

namespace Qlx.Modules.Bas.Domain.Goods;

/// <summary>
/// 计量单位组实体（表 bas_unit_groups）。租户内编码唯一；组内容纳多个单位与换算关系。
/// </summary>
[Index(nameof(TenantId), nameof(Code), IsUnique = true)]
[Index(nameof(TenantId), nameof(Status))]
public sealed class UnitGroup : Qlx.Abstractions.Domain.AuditableEntity, Qlx.Abstractions.Domain.ITenantEntity
{
    /// <summary>所属租户 Id。</summary>
    public long TenantId { get; internal set; }

    /// <summary>单位组编码（租户内唯一，后端生成，不可修改）。</summary>
    [MaxLength(32)]
    public string Code { get; internal set; } = string.Empty;

    /// <summary>单位组名称。</summary>
    [MaxLength(64)]
    public string Name { get; internal set; } = string.Empty;

    /// <summary>启停状态。</summary>
    public EntityStatus Status { get; private set; } = EntityStatus.Enabled;

    /// <summary>软删标记。</summary>
    public bool IsDeleted { get; private set; }

    public UnitGroup() { }

    public UnitGroup(long tenantId, string name, string code)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        TenantId = tenantId;
        Name = name;
        Code = code;
    }

    /// <summary>重命名单位组。</summary>
    /// <param name="name">新名称。</param>
    public void Rename(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name;
    }

    /// <summary>停用单位组。</summary>
    public void Disable() => Status = EntityStatus.Disabled;

    /// <summary>启用单位组。</summary>
    public void Enable() => Status = EntityStatus.Enabled;

    /// <summary>软删单位组。</summary>
    public void SoftDelete() => IsDeleted = true;
}
