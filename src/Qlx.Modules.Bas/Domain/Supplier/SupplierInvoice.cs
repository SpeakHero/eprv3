/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-25 18:00:00 +08:00 中国标准时间 星期日
 * 描述：供应商开票信息实体（bas_supplier_invoices）——专票/普票/数电票，变更经审批留痕
 */

using System.ComponentModel.DataAnnotations;

using Microsoft.EntityFrameworkCore;

namespace Qlx.Modules.Bas.Domain.Supplier;

/// <summary>开票类型。</summary>
public enum InvoiceType
{
    /// <summary>增值税专用发票。</summary>
    Special = 1,

    /// <summary>增值税普通发票。</summary>
    General = 2,

    /// <summary>数电票。</summary>
    Digital = 3,
}

/// <summary>
/// 供应商开票信息（表 bas_supplier_invoices）。仅存主表外键标量 <see cref="SupplierId"/>，不配置 EF 导航属性。
/// </summary>
[Index(nameof(TenantId), nameof(SupplierId))]
public sealed class SupplierInvoice : Qlx.Abstractions.Domain.AuditableEntity, Qlx.Abstractions.Domain.ITenantEntity
{
    /// <summary>所属租户 Id。</summary>
    public long TenantId { get; internal set; }

    /// <summary>所属供应商 Id（bas_suppliers.id）。</summary>
    public long SupplierId { get; internal set; }

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

    /// <summary>开票类型。</summary>
    public InvoiceType InvoiceType { get; internal set; } = InvoiceType.Special;

    /// <summary>启停状态。</summary>
    public EntityStatus Status { get; private set; } = EntityStatus.Enabled;

    public SupplierInvoice() { }

    public SupplierInvoice(
        long tenantId,
        long supplierId,
        string title,
        string? taxNo,
        string? bankInfo,
        string? addrPhone,
        InvoiceType invoiceType)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(tenantId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(supplierId);
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        TenantId = tenantId;
        SupplierId = supplierId;
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
