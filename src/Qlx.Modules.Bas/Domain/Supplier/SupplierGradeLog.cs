/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-25 18:00:00 +08:00 中国标准时间 星期日
 * 描述：供应商评级历史实体（bas_supplier_grade_logs）——旧/新评级与审批人全程留痕
 */

using System.ComponentModel.DataAnnotations;

using Microsoft.EntityFrameworkCore;

namespace Qlx.Modules.Bas.Domain.Supplier;

/// <summary>
/// 供应商评级历史（表 bas_supplier_grade_logs）。仅存主表外键标量 <see cref="SupplierId"/>，不配置 EF 导航属性。
/// </summary>
[Index(nameof(TenantId), nameof(SupplierId))]
public sealed class SupplierGradeLog : Qlx.Abstractions.Domain.AuditableEntity, Qlx.Abstractions.Domain.ITenantEntity
{
    /// <summary>所属租户 Id。</summary>
    public long TenantId { get; internal set; }

    /// <summary>所属供应商 Id（bas_suppliers.id）。</summary>
    public long SupplierId { get; internal set; }

    /// <summary>变更前评级。</summary>
    public SupplierGrade OldGrade { get; internal set; }

    /// <summary>变更后评级。</summary>
    public SupplierGrade NewGrade { get; internal set; }

    /// <summary>变更原因。</summary>
    [MaxLength(256)]
    public string? Reason { get; internal set; }

    /// <summary>审批人（用户标识）。</summary>
    [MaxLength(64)]
    public string? ApprovedBy { get; internal set; }

    public SupplierGradeLog() { }

    public SupplierGradeLog(
        long tenantId,
        long supplierId,
        SupplierGrade oldGrade,
        SupplierGrade newGrade,
        string? reason,
        string? approvedBy)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(tenantId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(supplierId);
        TenantId = tenantId;
        SupplierId = supplierId;
        OldGrade = oldGrade;
        NewGrade = newGrade;
        Reason = reason;
        ApprovedBy = approvedBy;
    }
}
