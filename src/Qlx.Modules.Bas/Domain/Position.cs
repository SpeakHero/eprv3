/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.1.0 beta
 * 创建时间：2026-10-21
 * 修改时间：2026-10-25 12:00:00 +08:00 中国标准时间 星期日
 * 描述：岗位实体（bas_positions）。租户内可按组织关联，编码租户内唯一；新增职责说明与状态行为方法。
 */

using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace Qlx.Modules.Bas.Domain;

/// <summary>
/// 岗位实体（表前缀 bas_）。租户内岗位档案，关联所属组织。
/// </summary>
[Index(nameof(TenantId))]
[Index(nameof(TenantId), nameof(Code), IsUnique = true)]
public sealed class Position : Qlx.Abstractions.Domain.AuditableEntity, Qlx.Abstractions.Domain.ITenantEntity
{
    /// <summary>所属租户 Id。</summary>
    public long TenantId { get; internal set; }

    /// <summary>所属组织 Id。</summary>
    public long OrgId { get; internal set; }

    /// <summary>岗位名称。</summary>
    [MaxLength(100)]
    public string Name { get; internal set; } = string.Empty;

    /// <summary>岗位编码（租户内唯一）。</summary>
    [MaxLength(50)]
    public string Code { get; internal set; } = string.Empty;

    /// <summary>编制人数。</summary>
    public int Headcount { get; internal set; }

    /// <summary>当前在岗人数（由成员分配维护）。</summary>
    public int CurrentCount { get; internal set; }

    /// <summary>启停状态。</summary>
    public EntityStatus Status { get; private set; } = EntityStatus.Enabled;

    /// <summary>岗位说明。</summary>
    [MaxLength(500)]
    public string? Description { get; internal set; }

    /// <summary>岗位职责。</summary>
    [MaxLength(500)]
    public string? Duty { get; internal set; }

    /// <summary>软删标记。</summary>
    public bool IsDeleted { get; private set; }

    public Position() { }

    public Position(long tenantId, long orgId, string name, string code, int headcount)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(tenantId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(orgId);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        TenantId = tenantId;
        OrgId = orgId;
        Name = name;
        Code = code;
        Headcount = headcount;
    }

    public void Disable() => Status = EntityStatus.Disabled;
    public void Enable() => Status = EntityStatus.Enabled;
    public void SoftDelete() => IsDeleted = true;

    /// <summary>重命名岗位。</summary>
    /// <param name="name">新名称。</param>
    public void Rename(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name;
    }

    /// <summary>调整所属组织。</summary>
    /// <param name="orgId">新组织 Id。</param>
    public void ChangeOrg(long orgId)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(orgId);
        OrgId = orgId;
    }

    /// <summary>调整编制人数。</summary>
    /// <param name="headcount">新编制人数。</param>
    public void SetHeadcount(int headcount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(headcount);
        Headcount = headcount;
    }

    /// <summary>回写在岗人数（由人员任职记录汇总维护）。</summary>
    /// <param name="currentCount">当前在岗人数。</param>
    public void SetCurrentCount(int currentCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(currentCount);
        CurrentCount = currentCount;
    }

    /// <summary>设置岗位说明与职责。</summary>
    /// <param name="description">岗位说明。</param>
    /// <param name="duty">岗位职责。</param>
    public void SetDescriptions(string? description, string? duty)
    {
        Description = description;
        Duty = duty;
    }
}
