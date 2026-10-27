/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-25 16:00:00 +08:00 中国标准时间 星期日
 * 描述：商品分类实体（bas_item_categories）——自引用多级分类，编码按层级连续生成，仅允许停用不允许删除
 */

using System.ComponentModel.DataAnnotations;

using Microsoft.EntityFrameworkCore;

namespace Qlx.Modules.Bas.Domain.Goods;

/// <summary>
/// 商品分类实体（表 bas_item_categories）。租户内自引用形成多级分类；编码由统一发号器按
/// <c>{parent}{seq:2}</c> 规则生成（父 01 → 子 0101 → 孙 010101），一经生成不可修改。
/// </summary>
[Index(nameof(TenantId), nameof(Code), IsUnique = true)]
[Index(nameof(TenantId), nameof(ParentId))]
[Index(nameof(TenantId), nameof(Status))]
public sealed class ItemCategory : Qlx.Abstractions.Domain.AuditableEntity, Qlx.Abstractions.Domain.ITenantEntity
{
    /// <summary>所属租户 Id。</summary>
    public long TenantId { get; internal set; }

    /// <summary>父级分类 Id（null 表示顶级分类）。</summary>
    public long? ParentId { get; internal set; }

    /// <summary>分类编码（租户内唯一，层级连续性编码，不可修改）。</summary>
    [MaxLength(20)]
    public string Code { get; internal set; } = string.Empty;

    /// <summary>分类名称。</summary>
    [MaxLength(64)]
    public string Name { get; internal set; } = string.Empty;

    /// <summary>分类层级（顶级为 1）。</summary>
    public int LevelNo { get; internal set; }

    /// <summary>默认计量单位组 Id（可为空）。</summary>
    public long? UnitGroupId { get; internal set; }

    /// <summary>启停状态。</summary>
    public EntityStatus Status { get; private set; } = EntityStatus.Enabled;

    /// <summary>软删标记。</summary>
    public bool IsDeleted { get; private set; }

    public ItemCategory() { }

    public ItemCategory(long tenantId, string name, long? parentId, int levelNo, string code, long? unitGroupId)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentOutOfRangeException.ThrowIfLessThan(levelNo, 1);
        TenantId = tenantId;
        Name = name;
        ParentId = parentId;
        LevelNo = levelNo;
        Code = code;
        UnitGroupId = unitGroupId;
    }

    /// <summary>重命名分类。</summary>
    /// <param name="name">新名称。</param>
    public void Rename(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name;
    }

    /// <summary>设置默认计量单位组。</summary>
    /// <param name="unitGroupId">计量单位组 Id（null 表示清空）。</param>
    public void SetUnitGroup(long? unitGroupId) => UnitGroupId = unitGroupId;

    /// <summary>停用分类。</summary>
    public void Disable() => Status = EntityStatus.Disabled;

    /// <summary>启用分类。</summary>
    public void Enable() => Status = EntityStatus.Enabled;

    /// <summary>软删分类。</summary>
    public void SoftDelete() => IsDeleted = true;
}
