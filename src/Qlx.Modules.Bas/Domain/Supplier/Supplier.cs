/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-25 18:00:00 +08:00 中国标准时间 星期日
 * 描述：供应商主数据实体（bas_suppliers）——租户内编码/统一社会信用代码唯一，准入/冻结/终止状态机守卫
 */

using System.ComponentModel.DataAnnotations;

using Microsoft.EntityFrameworkCore;

using Qlx.Abstractions.Exceptions;

namespace Qlx.Modules.Bas.Domain.Supplier;

/// <summary>供应商类型（数值枚举，中文文案由前端公共层映射，B-2-5）。</summary>
public enum SupplierType
{
    /// <summary>生产厂家。</summary>
    Manufacturer = 1,

    /// <summary>经销商。</summary>
    Dealer = 2,

    /// <summary>劳务分包商。</summary>
    LabourSubcontractor = 3,

    /// <summary>服务商。</summary>
    ServiceProvider = 4,

    /// <summary>其他。</summary>
    Other = 5,
}

/// <summary>供应商评级（数值枚举；黑名单全局禁采，需集团级终审解除）。</summary>
public enum SupplierGrade
{
    /// <summary>A 级。</summary>
    Excellent = 1,

    /// <summary>B 级。</summary>
    Good = 2,

    /// <summary>C 级（默认）。</summary>
    Normal = 3,

    /// <summary>黑名单。</summary>
    Blacklist = 4,
}

/// <summary>
/// 供应商主数据实体（表 bas_suppliers）。编码由 Bas 统一发号器生成（N-1，前端不传码）；
/// 状态机与评级变更经领域方法守卫（非法流转抛 <see cref="Qlx.Abstractions.Exceptions.ConflictException"/>）。
/// </summary>
[Index(nameof(TenantId), nameof(Code), IsUnique = true)]
[Index(nameof(TenantId), nameof(Uscc), IsUnique = true)]
[Index(nameof(TenantId), nameof(Status))]
[Index(nameof(TenantId), nameof(Grade))]
public sealed class Supplier : Qlx.Abstractions.Domain.AuditableEntity, Qlx.Abstractions.Domain.ITenantEntity
{
    /// <summary>所属租户 Id。</summary>
    public long TenantId { get; internal set; }

    /// <summary>供应商编码（租户内唯一，统一发号器生成，不可修改）。</summary>
    [MaxLength(20)]
    public string Code { get; internal set; } = string.Empty;

    /// <summary>供应商全称（与营业执照一致）。</summary>
    [MaxLength(128)]
    public string Name { get; internal set; } = string.Empty;

    /// <summary>统一社会信用代码（租户内唯一，18 位）。</summary>
    [MaxLength(18)]
    public string Uscc { get; internal set; } = string.Empty;

    /// <summary>供应商类型。</summary>
    public SupplierType SupplierType { get; internal set; } = SupplierType.Other;

    /// <summary>联系人。</summary>
    [MaxLength(64)]
    public string? Contact { get; internal set; }

    /// <summary>联系电话。</summary>
    [MaxLength(32)]
    public string? Phone { get; internal set; }

    /// <summary>评级（默认 C 级）。</summary>
    public SupplierGrade Grade { get; private set; } = SupplierGrade.Normal;

    /// <summary>合作状态（新建默认待准入）。</summary>
    public PartnerStatus Status { get; private set; } = PartnerStatus.Pending;

    /// <summary>软删标记。</summary>
    public bool IsDeleted { get; private set; }

    public Supplier() { }

    public Supplier(long tenantId, string code, string name, string uscc, SupplierType supplierType)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(uscc);
        TenantId = tenantId;
        Code = code;
        Name = name;
        Uscc = uscc;
        SupplierType = supplierType;
    }

    /// <summary>基础信息变更（编码不可变更）。</summary>
    public void UpdateProfile(string name, string uscc, SupplierType supplierType, string? contact, string? phone)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(uscc);
        Name = name;
        Uscc = uscc;
        SupplierType = supplierType;
        Contact = contact;
        Phone = phone;
    }

    /// <summary>提交（重新）准入审批（仅待准入 / 冻结状态允许）。</summary>
    public void SubmitAdmission()
        => Status = PartnerStatusGuard.Fire(Status, PartnerTrigger.SubmitAdmission, "SUPPLIER_STATE_INVALID");

    /// <summary>准入审批通过（待准入 / 冻结 → 合作中）。</summary>
    public void Approve()
        => Status = PartnerStatusGuard.Fire(Status, PartnerTrigger.Approve, "SUPPLIER_STATE_INVALID");

    /// <summary>冻结（仅合作中允许；资质到期自动冻结与黑名单处置共用）。</summary>
    public void Freeze()
        => Status = PartnerStatusGuard.Fire(Status, PartnerTrigger.Freeze, "SUPPLIER_STATE_INVALID");

    /// <summary>终止合作（终态，不可再流转）。</summary>
    public void Close()
        => Status = PartnerStatusGuard.Fire(Status, PartnerTrigger.Close, "SUPPLIER_STATE_INVALID");

    /// <summary>变更评级（已终止供应商不可变更）。</summary>
    /// <param name="grade">新评级。</param>
    public void ChangeGrade(SupplierGrade grade)
    {
        if (Status == PartnerStatus.Closed)
        {
            throw new ConflictException("已终止供应商不可变更评级", "SUPPLIER_CLOSED");
        }

        Grade = grade;
    }

    /// <summary>软删（仅未被业务引用时由服务层调用）。</summary>
    public void SoftDelete() => IsDeleted = true;
}
