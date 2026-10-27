/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-25 18:00:00 +08:00 中国标准时间 星期日
 * 描述：客户主数据实体（bas_customers）——信用等级/额度/占用额度，准入与信用校验
 */

using System.ComponentModel.DataAnnotations;

using Microsoft.EntityFrameworkCore;

using Qlx.Abstractions.Exceptions;

namespace Qlx.Modules.Bas.Domain.Customer;

/// <summary>客户信用等级（数值枚举；下调触发在售订单复检由下游模块消费）。</summary>
public enum CreditGrade
{
    /// <summary>A 级。</summary>
    A = 1,

    /// <summary>B 级。</summary>
    B = 2,

    /// <summary>C 级。</summary>
    C = 3,

    /// <summary>D 级。</summary>
    D = 4,
}

/// <summary>
/// 客户主数据实体（表 bas_customers）。编码由 Bas 统一发号器生成（N-1，前端不传码）；
/// 信用额度为原始 decimal 金额，不做显示格式化（B-2-2）。
/// </summary>
[Index(nameof(TenantId), nameof(Code), IsUnique = true)]
[Index(nameof(TenantId), nameof(Uscc), IsUnique = true)]
[Index(nameof(TenantId), nameof(Status))]
[Index(nameof(TenantId), nameof(CreditGrade))]
public sealed class Customer : Qlx.Abstractions.Domain.AuditableEntity, Qlx.Abstractions.Domain.ITenantEntity
{
    /// <summary>所属租户 Id。</summary>
    public long TenantId { get; internal set; }

    /// <summary>客户编码（租户内唯一，统一发号器生成，不可修改）。</summary>
    [MaxLength(20)]
    public string Code { get; internal set; } = string.Empty;

    /// <summary>客户全称。</summary>
    [MaxLength(128)]
    public string Name { get; internal set; } = string.Empty;

    /// <summary>统一社会信用代码（租户内唯一，18 位）。</summary>
    [MaxLength(18)]
    public string Uscc { get; internal set; } = string.Empty;

    /// <summary>客户类型（沿用供应商类型枚举语义：1生产厂家/2经销商/3劳务分包商/4服务商/5其他）。</summary>
    public int CustomerType { get; internal set; } = 5;

    /// <summary>联系人。</summary>
    [MaxLength(64)]
    public string? Contact { get; internal set; }

    /// <summary>联系电话。</summary>
    [MaxLength(32)]
    public string? Phone { get; internal set; }

    /// <summary>信用等级（默认 C）。</summary>
    public CreditGrade CreditGrade { get; private set; } = CreditGrade.C;

    /// <summary>信用额度（默认 0）。</summary>
    [Precision(18, 2)]
    public decimal CreditLimit { get; private set; }

    /// <summary>已占用信用额度（在途应收实时汇总，本模块只读展示）。</summary>
    [Precision(18, 2)]
    public decimal CreditUsed { get; private set; }

    /// <summary>默认回款条款（下单带出）。</summary>
    [MaxLength(256)]
    public string? PaymentTerms { get; internal set; }

    /// <summary>合作状态（新建默认待准入）。</summary>
    public PartnerStatus Status { get; private set; } = PartnerStatus.Pending;

    /// <summary>软删标记。</summary>
    public bool IsDeleted { get; private set; }

    public Customer() { }

    public Customer(long tenantId, string code, string name, string uscc, int customerType)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(uscc);
        TenantId = tenantId;
        Code = code;
        Name = name;
        Uscc = uscc;
        CustomerType = customerType;
    }

    /// <summary>基础信息变更（编码不可变更）。</summary>
    public void UpdateProfile(
        string name, string uscc, int customerType, string? contact, string? phone, string? paymentTerms)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(uscc);
        Name = name;
        Uscc = uscc;
        CustomerType = customerType;
        Contact = contact;
        Phone = phone;
        PaymentTerms = paymentTerms;
    }

    /// <summary>提交（重新）准入审批（仅待准入 / 冻结状态允许）。</summary>
    public void SubmitAdmission()
        => Status = PartnerStatusGuard.Fire(Status, PartnerTrigger.SubmitAdmission, "CUSTOMER_STATE_INVALID");

    /// <summary>准入审批通过（待准入 / 冻结 → 合作中）。</summary>
    public void Approve()
        => Status = PartnerStatusGuard.Fire(Status, PartnerTrigger.Approve, "CUSTOMER_STATE_INVALID");

    /// <summary>冻结。</summary>
    public void Freeze()
        => Status = PartnerStatusGuard.Fire(Status, PartnerTrigger.Freeze, "CUSTOMER_STATE_INVALID");

    /// <summary>终止合作（终态）。</summary>
    public void Close()
        => Status = PartnerStatusGuard.Fire(Status, PartnerTrigger.Close, "CUSTOMER_STATE_INVALID");

    /// <summary>调整信用额度与等级（已终止客户不可调整；额度不得为负）。</summary>
    /// <param name="creditGrade">新的信用等级。</param>
    /// <param name="creditLimit">新的信用额度。</param>
    public void AdjustCredit(CreditGrade creditGrade, decimal creditLimit)
    {
        if (Status == PartnerStatus.Closed)
        {
            throw new ConflictException("已终止客户不可调整信用额度", "CUSTOMER_CLOSED");
        }

        if (creditLimit < 0)
        {
            throw new Qlx.Abstractions.Exceptions.ValidationException("creditLimit", "信用额度不能为负数");
        }

        CreditGrade = creditGrade;
        CreditLimit = creditLimit;
    }

    /// <summary>软删。</summary>
    public void SoftDelete() => IsDeleted = true;
}
