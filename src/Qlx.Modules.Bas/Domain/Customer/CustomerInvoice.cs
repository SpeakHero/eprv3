/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-25 18:00:00 +08:00 中国标准时间 星期日
 * 描述：客户开票信息实体（bas_customer_invoices）
 */

using System.ComponentModel.DataAnnotations;

using Microsoft.EntityFrameworkCore;

namespace Qlx.Modules.Bas.Domain.Customer;

/// <summary>
/// 客户开票信息（表 bas_customer_invoices）。仅存主表外键标量 <see cref="CustomerId"/>，不配置 EF 导航属性。
/// </summary>
[Index(nameof(TenantId), nameof(CustomerId))]
public sealed class CustomerInvoice : Qlx.Abstractions.Domain.AuditableEntity, Qlx.Abstractions.Domain.ITenantEntity
{
    /// <summary>所属租户 Id。</summary>
    public long TenantId { get; internal set; }

    /// <summary>所属客户 Id（bas_customers.id）。</summary>
    public long CustomerId { get; internal set; }

    /// <summary>发票抬头。</summary>
    [MaxLength(128)]
    public string Title { get; internal set; } = string.Empty;

    /// <summary>纳税人识别号。</summary>
    [MaxLength(32)]
    public string? TaxNo { get; internal set; }

    /// <summary>开户行及账号。</summary>
    [MaxLength(256)]
    public string? BankInfo { get; internal set; }

    /// <summary>地址电话。</summary>
    [MaxLength(256)]
    public string? AddrPhone { get; internal set; }

    /// <summary>开票类型（1 专票 / 2 普票 / 3 数电票）。</summary>
    public int InvoiceType { get; internal set; } = 1;

    /// <summary>启停状态。</summary>
    public EntityStatus Status { get; private set; } = EntityStatus.Enabled;

    public CustomerInvoice() { }

    public CustomerInvoice(
        long tenantId,
        long customerId,
        string title,
        string? taxNo,
        string? bankInfo,
        string? addrPhone,
        int invoiceType)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(tenantId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(customerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        TenantId = tenantId;
        CustomerId = customerId;
        Title = title;
        TaxNo = taxNo;
        BankInfo = bankInfo;
        AddrPhone = addrPhone;
        InvoiceType = invoiceType;
    }

    /// <summary>停用开票信息。</summary>
    public void Disable() => Status = EntityStatus.Disabled;

    /// <summary>启用开票信息。</summary>
    public void Enable() => Status = EntityStatus.Enabled;
}
