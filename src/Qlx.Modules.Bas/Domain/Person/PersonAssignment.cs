/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-25 21:50:00 +08:00 中国标准时间 星期日
 * 描述：人员任职记录实体（bas_person_assignments）——当前任职唯一，调岗/离职关闭旧记录并追加新记录
 */

using System.ComponentModel.DataAnnotations;

using Microsoft.EntityFrameworkCore;

namespace Qlx.Modules.Bas.Domain.Person;

/// <summary>
/// 人员任职记录（表 bas_person_assignments）。仅存主表外键标量 <see cref="PersonId"/>，不配置 EF 导航属性；
/// 同一人员仅一条 <see cref="IsCurrent"/>=true 的当前任职记录。
/// </summary>
[Index(nameof(TenantId), nameof(PersonId), nameof(IsCurrent))]
public sealed class PersonAssignment : Qlx.Abstractions.Domain.AuditableEntity, Qlx.Abstractions.Domain.ITenantEntity
{
    /// <summary>所属租户 Id。</summary>
    public long TenantId { get; internal set; }

    /// <summary>人员 Id（bas_persons.id）。</summary>
    public long PersonId { get; internal set; }

    /// <summary>所属组织 Id（劳务人员可为空）。</summary>
    public long? OrgId { get; internal set; }

    /// <summary>所属岗位 Id（劳务人员可为空）。</summary>
    public long? PositionId { get; internal set; }

    /// <summary>生效日期。</summary>
    public DateOnly StartDate { get; internal set; }

    /// <summary>结束日期（当前任职为 null）。</summary>
    public DateOnly? EndDate { get; internal set; }

    /// <summary>变更原因。</summary>
    [MaxLength(256)]
    public string? ChangeReason { get; internal set; }

    /// <summary>是否当前任职记录。</summary>
    public bool IsCurrent { get; private set; }

    public PersonAssignment() { }

    public PersonAssignment(
        long tenantId,
        long personId,
        long? orgId,
        long? positionId,
        DateOnly startDate,
        string? changeReason = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(tenantId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(personId);
        TenantId = tenantId;
        PersonId = personId;
        OrgId = orgId;
        PositionId = positionId;
        StartDate = startDate;
        ChangeReason = changeReason;
        IsCurrent = true;
    }

    /// <summary>关闭当前任职记录（调岗 / 离职）。</summary>
    /// <param name="endDate">结束日期。</param>
    /// <param name="changeReason">变更原因。</param>
    public void Close(DateOnly endDate, string? changeReason = null)
    {
        if (!IsCurrent)
        {
            return;
        }

        EndDate = endDate;
        ChangeReason = changeReason;
        IsCurrent = false;
    }
}
