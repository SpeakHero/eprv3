/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-25 18:00:00 +08:00 中国标准时间 星期日
 * 描述：客户信用变更历史实体（bas_customer_credit_logs）——旧/新等级与额度、理由、审批人
 */

using System.ComponentModel.DataAnnotations;

using Microsoft.EntityFrameworkCore;

namespace Qlx.Modules.Bas.Domain.Customer;

/// <summary>
/// 客户信用变更历史（表 bas_customer_credit_logs）。仅存主表外键标量 <see cref="CustomerId"/>，不配置 EF 导航属性。
/// </summary>
[Index(nameof(TenantId), nameof(CustomerId))]
public sealed class CustomerCreditLog : Qlx.Abstractions.Domain.AuditableEntity, Qlx.Abstractions.Domain.ITenantEntity
{
    /// <summary>所属租户 Id。</summary>
    public long TenantId { get; internal set; }

    /// <summary>所属客户 Id（bas_customers.id）。</summary>
    public long CustomerId { get; internal set; }

    /// <summary>变更前信用等级。</summary>
    public CreditGrade OldCreditGrade { get; internal set; }

    /// <summary>变更后信用等级。</summary>
    public CreditGrade NewCreditGrade { get; internal set; }

    /// <summary>变更前信用额度。</summary>
    [Precision(18, 2)]
    public decimal OldLimit { get; internal set; }

    /// <summary>变更后信用额度。</summary>
    [Precision(18, 2)]
    public decimal NewLimit { get; internal set; }

    /// <summary>变更原因。</summary>
    [MaxLength(256)]
    public string? Reason { get; internal set; }

    /// <summary>审批人（用户标识）。</summary>
    [MaxLength(64)]
    public string? ApprovedBy { get; internal set; }

    public CustomerCreditLog() { }

    public CustomerCreditLog(
        long tenantId,
        long customerId,
        CreditGrade oldCreditGrade,
        CreditGrade newCreditGrade,
        decimal oldLimit,
        decimal newLimit,
        string? reason,
        string? approvedBy)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(tenantId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(customerId);
        TenantId = tenantId;
        CustomerId = customerId;
        OldCreditGrade = oldCreditGrade;
        NewCreditGrade = newCreditGrade;
        OldLimit = oldLimit;
        NewLimit = newLimit;
        Reason = reason;
        ApprovedBy = approvedBy;
    }
}
