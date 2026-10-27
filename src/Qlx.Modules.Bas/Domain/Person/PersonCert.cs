/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-25 21:50:00 +08:00 中国标准时间 星期日
 * 描述：人员证件档案实体（bas_person_certs）——证书类型/编号与有效期，仅存外键标量不配导航
 */

using System.ComponentModel.DataAnnotations;

using Microsoft.EntityFrameworkCore;

namespace Qlx.Modules.Bas.Domain.Person;

/// <summary>
/// 人员证件档案（表 bas_person_certs）。仅存主表外键标量 <see cref="PersonId"/>，不配置 EF 导航属性。
/// </summary>
[Index(nameof(TenantId), nameof(PersonId))]
public sealed class PersonCert : Qlx.Abstractions.Domain.AuditableEntity, Qlx.Abstractions.Domain.ITenantEntity
{
    /// <summary>所属租户 Id。</summary>
    public long TenantId { get; internal set; }

    /// <summary>人员 Id（bas_persons.id）。</summary>
    public long PersonId { get; internal set; }

    /// <summary>证书类型（职称 / 特种作业 / 学历等）。</summary>
    [MaxLength(32)]
    public string CertType { get; internal set; } = string.Empty;

    /// <summary>证书编号。</summary>
    [MaxLength(64)]
    public string? CertNo { get; internal set; }

    /// <summary>发证日期。</summary>
    public DateOnly? IssuedOn { get; internal set; }

    /// <summary>到期日期（null 表示长期有效）。</summary>
    public DateOnly? ExpiresOn { get; internal set; }

    /// <summary>备注。</summary>
    [MaxLength(256)]
    public string? Remark { get; internal set; }

    public PersonCert() { }

    public PersonCert(
        long tenantId,
        long personId,
        string certType,
        string? certNo,
        DateOnly? issuedOn,
        DateOnly? expiresOn,
        string? remark)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(tenantId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(personId);
        ArgumentException.ThrowIfNullOrWhiteSpace(certType);
        TenantId = tenantId;
        PersonId = personId;
        CertType = certType;
        CertNo = certNo;
        IssuedOn = issuedOn;
        ExpiresOn = expiresOn;
        Remark = remark;
    }

    /// <summary>更新证件档案。</summary>
    public void UpdateDetails(
        string certType, string? certNo, DateOnly? issuedOn, DateOnly? expiresOn, string? remark)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(certType);
        CertType = certType;
        CertNo = certNo;
        IssuedOn = issuedOn;
        ExpiresOn = expiresOn;
        Remark = remark;
    }
}
