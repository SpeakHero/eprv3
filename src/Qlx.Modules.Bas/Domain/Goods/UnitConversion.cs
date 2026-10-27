/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-25 16:00:00 +08:00 中国标准时间 星期日
 * 描述：计量单位换算关系实体（bas_unit_conversions）——组内「源单位 → 目标单位」换算率，变更只记录不回溯
 */

using Microsoft.EntityFrameworkCore;

namespace Qlx.Modules.Bas.Domain.Goods;

/// <summary>
/// 计量单位换算关系实体（表 bas_unit_conversions）。租户 + 单位组内维护唯一的「源单位 → 目标单位」换算率。
/// <para>换算率变更写入审计（旧值/新值），历史单据不回溯重算。</para>
/// </summary>
[Index(nameof(TenantId), nameof(UnitGroupId))]
[Index(nameof(TenantId), nameof(FromUnitId), nameof(ToUnitId), IsUnique = true)]
public sealed class UnitConversion : Qlx.Abstractions.Domain.AuditableEntity, Qlx.Abstractions.Domain.ITenantEntity
{
    /// <summary>所属租户 Id。</summary>
    public long TenantId { get; internal set; }

    /// <summary>所属计量单位组 Id。</summary>
    public long UnitGroupId { get; internal set; }

    /// <summary>源单位 Id。</summary>
    public long FromUnitId { get; internal set; }

    /// <summary>目标单位 Id。</summary>
    public long ToUnitId { get; internal set; }

    /// <summary>换算率（1 源单位 = Rate 目标单位，必须大于 0）。</summary>
    [Precision(18, 8)]
    public decimal Rate { get; private set; }

    /// <summary>启停状态。</summary>
    public EntityStatus Status { get; private set; } = EntityStatus.Enabled;

    public UnitConversion() { }

    public UnitConversion(long tenantId, long unitGroupId, long fromUnitId, long toUnitId, decimal rate)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(tenantId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(unitGroupId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(fromUnitId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(toUnitId);
        if (fromUnitId == toUnitId)
        {
            throw new ArgumentException("源单位与目标单位不能相同", nameof(toUnitId));
        }

        EnsureRate(rate);
        TenantId = tenantId;
        UnitGroupId = unitGroupId;
        FromUnitId = fromUnitId;
        ToUnitId = toUnitId;
        Rate = rate;
    }

    /// <summary>变更换算率。</summary>
    /// <param name="rate">新换算率（必须大于 0）。</param>
    public void ChangeRate(decimal rate)
    {
        EnsureRate(rate);
        Rate = rate;
    }

    /// <summary>停用换算关系。</summary>
    public void Disable() => Status = EntityStatus.Disabled;

    /// <summary>启用换算关系。</summary>
    public void Enable() => Status = EntityStatus.Enabled;

    private static void EnsureRate(decimal rate)
    {
        if (rate <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(rate), rate, "换算率必须大于 0");
        }
    }
}
