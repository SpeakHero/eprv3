/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-25 23:30:00 +08:00 中国标准时间 星期日
 * 描述：数据字典项实体（bas_dict_items）——按类型编码归属，类型内项编码唯一，系统内置项仅可停用
 */

using System.ComponentModel.DataAnnotations;

using Microsoft.EntityFrameworkCore;

namespace Qlx.Modules.Bas.Domain.Dictionary;

/// <summary>
/// 数据字典项实体（表 bas_dict_items）。项编码在类型内唯一且不可修改；
/// 系统内置项禁止删除（<see cref="IsSystem"/>），被业务引用时由服务层禁止删除、仅允许停用。
/// </summary>
[Index(nameof(TenantId), nameof(TypeCode), nameof(ItemCode), IsUnique = true)]
[Index(nameof(TenantId), nameof(TypeCode), nameof(SortNo))]
public sealed class DictItem : Qlx.Abstractions.Domain.AuditableEntity, Qlx.Abstractions.Domain.ITenantEntity
{
    /// <summary>所属租户 Id。</summary>
    public long TenantId { get; internal set; }

    /// <summary>所属字典类型编码。</summary>
    [MaxLength(64)]
    public string TypeCode { get; internal set; } = string.Empty;

    /// <summary>字典项编码（类型内唯一，不可修改）。</summary>
    [MaxLength(64)]
    public string ItemCode { get; internal set; } = string.Empty;

    /// <summary>字典项名称。</summary>
    [MaxLength(64)]
    public string ItemName { get; internal set; } = string.Empty;

    /// <summary>类型内排序号（升序）。</summary>
    public int SortNo { get; internal set; }

    /// <summary>启停状态（新建默认启用）。</summary>
    public EntityStatus Status { get; private set; } = EntityStatus.Enabled;

    /// <summary>是否系统内置（内置项不可删除，仅可停用 / 启用）。</summary>
    public bool IsSystem { get; internal set; }

    public DictItem() { }

    public DictItem(
        long tenantId, string typeCode, string itemCode, string itemName, int sortNo, bool isSystem = false)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(typeCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(itemCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(itemName);
        TenantId = tenantId;
        TypeCode = typeCode;
        ItemCode = itemCode;
        ItemName = itemName;
        SortNo = sortNo;
        IsSystem = isSystem;
    }

    /// <summary>更新名称与排序号（项编码不可变更）。</summary>
    /// <param name="itemName">新名称。</param>
    /// <param name="sortNo">新排序号。</param>
    public void UpdateDetails(string itemName, int sortNo)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(itemName);
        ItemName = itemName;
        SortNo = sortNo;
    }

    /// <summary>停用字典项。</summary>
    public void Disable() => Status = EntityStatus.Disabled;

    /// <summary>启用字典项。</summary>
    public void Enable() => Status = EntityStatus.Enabled;
}
