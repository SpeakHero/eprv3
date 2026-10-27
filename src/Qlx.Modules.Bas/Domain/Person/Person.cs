/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-25 21:50:00 +08:00 中国标准时间 星期日
 * 描述：人员主数据实体（bas_persons）——租户内编码/证件哈希唯一，在职状态机与再入职守卫，
 *       证件号/手机号/银行卡号仅存密文与哈希，禁止明文落库
 */

using System.ComponentModel.DataAnnotations;

using Microsoft.EntityFrameworkCore;

using Qlx.Abstractions.Exceptions;

namespace Qlx.Modules.Bas.Domain.Person;

/// <summary>人员类型（数值枚举，中文文案由前端公共层映射，B-2-5）：EMP 员工 / LAB 劳务人员。</summary>
public enum PersonType
{
    /// <summary>员工（EMP）。</summary>
    Employee = 1,

    /// <summary>劳务人员（LAB）。</summary>
    Labour = 2,
}

/// <summary>证件类型（数值枚举）：身份证 / 护照 / 其他。</summary>
public enum PersonIdType
{
    /// <summary>居民身份证。</summary>
    IdCard = 1,

    /// <summary>护照。</summary>
    Passport = 2,

    /// <summary>其他证件。</summary>
    Other = 3,
}

/// <summary>在职状态（数值枚举）：试用 / 在职 / 停职 / 离职（终态）。</summary>
public enum EmployStatus
{
    /// <summary>试用。</summary>
    Probation = 1,

    /// <summary>在职（正式）。</summary>
    Regular = 2,

    /// <summary>停职。</summary>
    Suspended = 3,

    /// <summary>离职（终态，回归须走再入职流程）。</summary>
    Left = 4,
}

/// <summary>
/// 人员主数据实体（表 bas_persons）。编码由 Bas 统一发号器生成（N-1，前端不传码）；
/// 证件号 / 手机号 / 银行卡号只存 Data Protection 密文，另存证件号 SHA-256 哈希用于防重与查找；
/// 离职为终态，再入职复用同一人员记录（<see cref="Rehire"/>）。
/// </summary>
[Index(nameof(TenantId), nameof(Code), IsUnique = true)]
[Index(nameof(TenantId), nameof(OrgId))]
[Index(nameof(TenantId), nameof(PositionId))]
[Index(nameof(TenantId), nameof(EmployStatus))]
public sealed class Person : Qlx.Abstractions.Domain.AuditableEntity, Qlx.Abstractions.Domain.ITenantEntity
{
    /// <summary>所属租户 Id。</summary>
    public long TenantId { get; internal set; }

    /// <summary>人员编码（工号，租户内唯一，统一发号器生成，不可修改）。</summary>
    [MaxLength(20)]
    public string Code { get; internal set; } = string.Empty;

    /// <summary>姓名。</summary>
    [MaxLength(64)]
    public string Name { get; internal set; } = string.Empty;

    /// <summary>人员类型。</summary>
    public PersonType PersonType { get; internal set; } = PersonType.Employee;

    /// <summary>证件类型。</summary>
    public PersonIdType IdType { get; internal set; } = PersonIdType.IdCard;

    /// <summary>证件号密文（Data Protection，禁止明文落库）。</summary>
    [MaxLength(512)]
    public string IdNoCipher { get; internal set; } = string.Empty;

    /// <summary>证件号 SHA-256 十六进制哈希（含租户盐，租户内唯一防重与查找）。</summary>
    [MaxLength(64)]
    public string IdNoHash { get; internal set; } = string.Empty;

    /// <summary>手机号密文（Data Protection）。</summary>
    [MaxLength(512)]
    public string MobileCipher { get; internal set; } = string.Empty;

    /// <summary>紧急联系人。</summary>
    [MaxLength(64)]
    public string? EmergencyContact { get; internal set; }

    /// <summary>紧急联系电话。</summary>
    [MaxLength(32)]
    public string? EmergencyPhone { get; internal set; }

    /// <summary>所属组织 Id（劳务人员可为空）。</summary>
    public long? OrgId { get; internal set; }

    /// <summary>所属岗位 Id（劳务人员可为空）。</summary>
    public long? PositionId { get; internal set; }

    /// <summary>在职状态。</summary>
    public EmployStatus EmployStatus { get; private set; } = EmployStatus.Probation;

    /// <summary>入职日期。</summary>
    public DateOnly EntryDate { get; internal set; }

    /// <summary>离职日期（在职为 null）。</summary>
    public DateOnly? LeaveDate { get; internal set; }

    /// <summary>银行卡号密文（Data Protection，可空）。</summary>
    [MaxLength(512)]
    public string? BankCardNoCipher { get; internal set; }

    /// <summary>软删标记。</summary>
    public bool IsDeleted { get; private set; }

    public Person() { }

    public Person(
        long tenantId,
        string code,
        string name,
        PersonType personType,
        PersonIdType idType,
        string idNoCipher,
        string idNoHash,
        string mobileCipher,
        DateOnly entryDate,
        string? bankCardNoCipher = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(idNoCipher);
        ArgumentException.ThrowIfNullOrWhiteSpace(idNoHash);
        ArgumentException.ThrowIfNullOrWhiteSpace(mobileCipher);
        TenantId = tenantId;
        Code = code;
        Name = name;
        PersonType = personType;
        IdType = idType;
        IdNoCipher = idNoCipher;
        IdNoHash = idNoHash;
        MobileCipher = mobileCipher;
        EntryDate = entryDate;
        BankCardNoCipher = bankCardNoCipher;
    }

    /// <summary>基础信息变更（姓名 / 人员类型 / 紧急联系人；编码与证件号不可经此变更）。</summary>
    public void UpdateProfile(
        string name, PersonType personType, string? emergencyContact, string? emergencyPhone)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name;
        PersonType = personType;
        EmergencyContact = emergencyContact;
        EmergencyPhone = emergencyPhone;
    }

    /// <summary>调整所属组织与岗位（任职记录由服务层同步维护）。</summary>
    public void Assign(long? orgId, long? positionId)
    {
        OrgId = orgId;
        PositionId = positionId;
    }

    /// <summary>
    /// 变更在职状态（非离职态之间流转）。离职为终态，必须走 <see cref="Resign"/> / <see cref="Rehire"/>。
    /// </summary>
    public void ChangeEmployStatus(EmployStatus target)
    {
        if (EmployStatus == EmployStatus.Left)
        {
            throw new ConflictException("人员已离职，状态不可变更（请走再入职流程）", "PERSON_STATUS_LEFT_TERMINAL");
        }

        if (target == EmployStatus.Left)
        {
            throw new ConflictException("离职请使用离职流程（需登记离职日期）", "PERSON_RESIGN_REQUIRED");
        }

        if (!Enum.IsDefined(target))
        {
            throw new ConflictException("未知的在职状态", "PERSON_STATUS_INVALID");
        }

        EmployStatus = target;
    }

    /// <summary>办理离职（终态，登记离职日期）。</summary>
    /// <param name="leaveDate">离职日期。</param>
    public void Resign(DateOnly leaveDate)
    {
        if (EmployStatus == EmployStatus.Left)
        {
            throw new ConflictException("人员已处于离职状态", "PERSON_ALREADY_LEFT");
        }

        if (leaveDate < EntryDate)
        {
            throw new ConflictException("离职日期不能早于入职日期", "PERSON_LEAVE_DATE_INVALID");
        }

        LeaveDate = leaveDate;
        EmployStatus = EmployStatus.Left;
    }

    /// <summary>再入职（仅离职状态允许；复用同一人员记录，清空离职日期）。</summary>
    /// <param name="entryDate">再入职日期。</param>
    /// <param name="orgId">所属组织 Id。</param>
    /// <param name="positionId">所属岗位 Id。</param>
    /// <param name="employStatus">再入职状态（试用 / 在职）。</param>
    public void Rehire(
        DateOnly entryDate, long? orgId, long? positionId, EmployStatus employStatus)
    {
        if (EmployStatus != EmployStatus.Left)
        {
            throw new ConflictException("仅在离职状态下可办理再入职", "PERSON_NOT_LEFT");
        }

        if (employStatus is not (EmployStatus.Probation or EmployStatus.Regular))
        {
            throw new ConflictException("再入职状态只能为试用或在职", "PERSON_REHIRE_STATUS_INVALID");
        }

        if (LeaveDate.HasValue && entryDate < LeaveDate.Value)
        {
            throw new ConflictException("再入职日期不能早于离职日期", "PERSON_REHIRE_DATE_INVALID");
        }

        EntryDate = entryDate;
        LeaveDate = null;
        EmployStatus = employStatus;
        OrgId = orgId;
        PositionId = positionId;
    }

    /// <summary>软删（仅未被业务引用时由服务层调用）。</summary>
    public void SoftDelete() => IsDeleted = true;
}
