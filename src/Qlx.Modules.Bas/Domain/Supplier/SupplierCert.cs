/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-25 18:00:00 +08:00 中国标准时间 星期日
 * 描述：供应商资质证照实体（bas_supplier_certs）——到期扫描与预警幂等标记
 */

using System.ComponentModel.DataAnnotations;

using Microsoft.EntityFrameworkCore;

namespace Qlx.Modules.Bas.Domain.Supplier;

/// <summary>资质证照状态。</summary>
public enum CertificateStatus
{
    /// <summary>有效。</summary>
    Valid = 1,

    /// <summary>已过期。</summary>
    Expired = 2,

    /// <summary>已作废。</summary>
    Revoked = 3,
}

/// <summary>
/// 供应商资质证照（表 bas_supplier_certs）。仅存主表外键标量 <see cref="SupplierId"/>，不配置 EF 导航属性；
/// <see cref="WarnedAt"/> 为到期预警幂等标记（同一资质在预警窗口内只推送一次）。
/// </summary>
[Index(nameof(TenantId), nameof(SupplierId))]
[Index(nameof(TenantId), nameof(ExpiryDate))]
public sealed class SupplierCert : Qlx.Abstractions.Domain.AuditableEntity, Qlx.Abstractions.Domain.ITenantEntity
{
    /// <summary>所属租户 Id。</summary>
    public long TenantId { get; internal set; }

    /// <summary>所属供应商 Id（bas_suppliers.id）。</summary>
    public long SupplierId { get; internal set; }

    /// <summary>资质类型（营业执照 / 生产许可证 / 安全资质等）。</summary>
    [MaxLength(32)]
    public string CertType { get; internal set; } = string.Empty;

    /// <summary>资质编号。</summary>
    [MaxLength(64)]
    public string CertNo { get; internal set; } = string.Empty;

    /// <summary>发证机构。</summary>
    [MaxLength(128)]
    public string? IssueOrg { get; internal set; }

    /// <summary>生效日期。</summary>
    public DateOnly? EffectDate { get; internal set; }

    /// <summary>失效日期（null 表示长期有效）。</summary>
    public DateOnly? ExpiryDate { get; internal set; }

    /// <summary>影像文件 Id（文件中心）。</summary>
    public long? FileId { get; internal set; }

    /// <summary>证照状态。</summary>
    public CertificateStatus Status { get; private set; } = CertificateStatus.Valid;

    /// <summary>最近一次到期预警时间（幂等标记，null 表示未预警）。</summary>
    public DateTimeOffset? WarnedAt { get; private set; }

    public SupplierCert() { }

    public SupplierCert(
        long tenantId,
        long supplierId,
        string certType,
        string certNo,
        string? issueOrg,
        DateOnly? effectDate,
        DateOnly? expiryDate,
        long? fileId)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(tenantId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(supplierId);
        ArgumentException.ThrowIfNullOrWhiteSpace(certType);
        ArgumentException.ThrowIfNullOrWhiteSpace(certNo);
        TenantId = tenantId;
        SupplierId = supplierId;
        CertType = certType;
        CertNo = certNo;
        IssueOrg = issueOrg;
        EffectDate = effectDate;
        ExpiryDate = expiryDate;
        FileId = fileId;
    }

    /// <summary>登记到期预警时间（避免同一资质重复推送）。</summary>
    /// <param name="warnedAt">预警时间。</param>
    public void MarkWarned(DateTimeOffset warnedAt) => WarnedAt = warnedAt;

    /// <summary>标记证照已过期。</summary>
    public void MarkExpired() => Status = CertificateStatus.Expired;

    /// <summary>作废证照。</summary>
    public void Revoke() => Status = CertificateStatus.Revoked;
}
