/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-25 16:00:00 +08:00 中国标准时间 星期日
 * 描述：计量单位实体（bas_units）——隶属计量单位组，精度 0~8 位小数用于数量展示
 */

using System.ComponentModel.DataAnnotations;

using Microsoft.EntityFrameworkCore;

namespace Qlx.Modules.Bas.Domain.Goods;

/// <summary>
/// 计量单位实体（表 bas_units）。租户内编码唯一；<see cref="Precision"/> 表示数量保留小数位（0~8）。
/// </summary>
[Index(nameof(TenantId), nameof(Code), IsUnique = true)]
[Index(nameof(TenantId), nameof(UnitGroupId))]
public sealed class Unit : Qlx.Abstractions.Domain.AuditableEntity, Qlx.Abstractions.Domain.ITenantEntity
{
    /// <summary>精度允许的最大小数位。</summary>
    public const int MaxPrecision = 8;

    /// <summary>所属租户 Id。</summary>
    public long TenantId { get; internal set; }

    /// <summary>单位编码（租户内唯一，后端生成，不可修改）。</summary>
    [MaxLength(32)]
    public string Code { get; internal set; } = string.Empty;

    /// <summary>单位名称（如 千克 / 箱）。</summary>
    [MaxLength(64)]
    public string Name { get; internal set; } = string.Empty;

    /// <summary>所属计量单位组 Id。</summary>
    public long UnitGroupId { get; internal set; }

    /// <summary>数量精度（小数位 0~8）。</summary>
    public int Precision { get; internal set; }

    /// <summary>启停状态。</summary>
    public EntityStatus Status { get; private set; } = EntityStatus.Enabled;

    /// <summary>软删标记。</summary>
    public bool IsDeleted { get; private set; }

    public Unit() { }

    public Unit(long tenantId, string name, string code, long unitGroupId, int precision)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(unitGroupId);
        EnsurePrecision(precision);
        TenantId = tenantId;
        Name = name;
        Code = code;
        UnitGroupId = unitGroupId;
        Precision = precision;
    }

    /// <summary>重命名单位。</summary>
    /// <param name="name">新名称。</param>
    public void Rename(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name;
    }

    /// <summary>调整所属计量单位组。</summary>
    /// <param name="unitGroupId">新计量单位组 Id。</param>
    public void ChangeUnitGroup(long unitGroupId)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(unitGroupId);
        UnitGroupId = unitGroupId;
    }

    /// <summary>调整数量精度。</summary>
    /// <param name="precision">新精度（0~8）。</param>
    public void SetPrecision(int precision)
    {
        EnsurePrecision(precision);
        Precision = precision;
    }

    /// <summary>停用单位。</summary>
    public void Disable() => Status = EntityStatus.Disabled;

    /// <summary>启用单位。</summary>
    public void Enable() => Status = EntityStatus.Enabled;

    /// <summary>软删单位。</summary>
    public void SoftDelete() => IsDeleted = true;

    private static void EnsurePrecision(int precision)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(precision);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(precision, MaxPrecision);
    }
}
