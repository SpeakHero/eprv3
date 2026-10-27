/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-25 21:55:00 +08:00 中国标准时间 星期日
 * 描述：Bas 人员主数据 DTO——仅扁平原始字段与数值枚举（B-2），证件/手机/银行卡一律脱敏下发；
 *       新建/更新入参不含 Code（编码由后端发号器生成，N-1），业务 Id 由全局 Hashids 元数据自动混淆
 */

namespace Qlx.Modules.Bas.Dtos;

// ================ 人员主档 ================

/// <summary>
/// 人员扁平行（原始字段）。<c>PersonType</c>/<c>IdType</c>/<c>EmployStatus</c> 为数值枚举；
/// 证件号 / 手机号 / 银行卡号为脱敏值，响应不含任何密文与明文。
/// </summary>
/// <param name="Id">人员 Id。</param>
/// <param name="Code">人员编码（后端生成，不可修改）。</param>
/// <param name="Name">姓名。</param>
/// <param name="PersonType">人员类型（1=员工 / 2=劳务人员）。</param>
/// <param name="IdType">证件类型（1=身份证 / 2=护照 / 3=其他）。</param>
/// <param name="IdNoMasked">脱敏证件号（前 6 后 4）。</param>
/// <param name="MobileMasked">脱敏手机号（前 3 后 4）。</param>
/// <param name="EmergencyContact">紧急联系人。</param>
/// <param name="EmergencyPhone">紧急联系电话。</param>
/// <param name="OrgId">所属组织 Id。</param>
/// <param name="PositionId">所属岗位 Id。</param>
/// <param name="EmployStatus">在职状态（1=试用 / 2=在职 / 3=停职 / 4=离职）。</param>
/// <param name="EntryDate">入职日期。</param>
/// <param name="LeaveDate">离职日期。</param>
/// <param name="BankCardNoMasked">脱敏银行卡号（后 4 位）。</param>
/// <param name="UpdatedAt">更新时间。</param>
public sealed record PersonRow(
    long Id,
    string Code,
    string Name,
    int PersonType,
    int IdType,
    string? IdNoMasked,
    string? MobileMasked,
    string? EmergencyContact,
    string? EmergencyPhone,
    long? OrgId,
    long? PositionId,
    int EmployStatus,
    DateOnly EntryDate,
    DateOnly? LeaveDate,
    string? BankCardNoMasked,
    DateTimeOffset UpdatedAt);

/// <summary>人员详情（主档 + 当前任职 + 任职历史 + 证件列表，全部为扁平集合，不做层级组装）。</summary>
/// <param name="Master">主档扁平行（敏感字段已脱敏）。</param>
/// <param name="CurrentAssignment">当前任职记录（无则 null）。</param>
/// <param name="Assignments">任职历史扁平集合（含当前记录）。</param>
/// <param name="Certs">证件档案扁平集合。</param>
public sealed record PersonDetailRow(
    PersonRow Master,
    PersonAssignmentRow? CurrentAssignment,
    IReadOnlyList<PersonAssignmentRow> Assignments,
    IReadOnlyList<PersonCertRow> Certs);

/// <summary>人员新建入参（无编码字段；证件/手机/银行卡为明文入参，后端加密落库）。</summary>
/// <param name="Name">姓名。</param>
/// <param name="PersonType">人员类型数值。</param>
/// <param name="IdType">证件类型数值。</param>
/// <param name="IdNo">证件号明文（仅入参，不落库）。</param>
/// <param name="Mobile">手机号明文（仅入参，不落库）。</param>
/// <param name="EmergencyContact">紧急联系人。</param>
/// <param name="EmergencyPhone">紧急联系电话。</param>
/// <param name="OrgId">所属组织 Id（员工必填）。</param>
/// <param name="PositionId">所属岗位 Id（员工必填）。</param>
/// <param name="EmployStatus">初始在职状态数值（不得为离职）。</param>
/// <param name="EntryDate">入职日期（null 取当天）。</param>
/// <param name="BankCardNo">银行卡号明文（仅入参，不落库）。</param>
public sealed record SavePersonPayload(
    string Name,
    int PersonType,
    int IdType,
    string IdNo,
    string Mobile,
    string? EmergencyContact,
    string? EmergencyPhone,
    long? OrgId,
    long? PositionId,
    int EmployStatus,
    DateOnly? EntryDate,
    string? BankCardNo);

/// <summary>人员信息更新入参（无编码与证件号字段：编码不可变，证件号变更走独立流程）。</summary>
/// <param name="Name">姓名。</param>
/// <param name="PersonType">人员类型数值。</param>
/// <param name="EmergencyContact">紧急联系人。</param>
/// <param name="EmergencyPhone">紧急联系电话。</param>
/// <param name="OrgId">所属组织 Id。</param>
/// <param name="PositionId">所属岗位 Id。</param>
/// <param name="EffectiveDate">调岗/换组织生效日期（null 取当天）。</param>
/// <param name="ChangeReason">调岗原因。</param>
public sealed record UpdatePersonPayload(
    string Name,
    int PersonType,
    string? EmergencyContact,
    string? EmergencyPhone,
    long? OrgId,
    long? PositionId,
    DateOnly? EffectiveDate,
    string? ChangeReason);

/// <summary>人员离职入参。</summary>
/// <param name="LeaveDate">离职日期（null 取当天）。</param>
/// <param name="Reason">离职原因（写入任职记录变更原因）。</param>
public sealed record ResignPersonPayload(
    DateOnly? LeaveDate,
    string? Reason);

/// <summary>人员再入职入参（复用原人员记录）。</summary>
/// <param name="EntryDate">再入职日期（null 取当天）。</param>
/// <param name="OrgId">所属组织 Id（员工必填）。</param>
/// <param name="PositionId">所属岗位 Id（员工必填）。</param>
/// <param name="EmployStatus">再入职状态（1=试用 / 2=在职）。</param>
public sealed record RehirePersonPayload(
    DateOnly? EntryDate,
    long? OrgId,
    long? PositionId,
    int? EmployStatus);

/// <summary>
/// 人员敏感字段明文行（仅经独立授权端点下发，接口必须挂 <c>bas.person.field.plaintext</c> 权限并写审计）。
/// </summary>
/// <param name="Id">人员 Id。</param>
/// <param name="Code">人员编码。</param>
/// <param name="IdNo">证件号明文。</param>
/// <param name="Mobile">手机号明文。</param>
/// <param name="BankCardNo">银行卡号明文。</param>
public sealed record PersonPlaintextRow(
    long Id,
    string Code,
    string IdNo,
    string Mobile,
    string? BankCardNo);

// ================ 人员子表 ================

/// <summary>任职记录扁平行。</summary>
/// <param name="Id">记录 Id。</param>
/// <param name="PersonId">人员 Id。</param>
/// <param name="OrgId">所属组织 Id。</param>
/// <param name="PositionId">所属岗位 Id。</param>
/// <param name="StartDate">生效日期。</param>
/// <param name="EndDate">结束日期（当前任职为 null）。</param>
/// <param name="ChangeReason">变更原因。</param>
/// <param name="IsCurrent">是否当前任职。</param>
public sealed record PersonAssignmentRow(
    long Id,
    long PersonId,
    long? OrgId,
    long? PositionId,
    DateOnly StartDate,
    DateOnly? EndDate,
    string? ChangeReason,
    bool IsCurrent);

/// <summary>证件档案扁平行。</summary>
/// <param name="Id">记录 Id。</param>
/// <param name="PersonId">人员 Id。</param>
/// <param name="CertType">证书类型。</param>
/// <param name="CertNo">证书编号。</param>
/// <param name="IssuedOn">发证日期。</param>
/// <param name="ExpiresOn">到期日期（null 表示长期有效）。</param>
/// <param name="Remark">备注。</param>
public sealed record PersonCertRow(
    long Id,
    long PersonId,
    string CertType,
    string? CertNo,
    DateOnly? IssuedOn,
    DateOnly? ExpiresOn,
    string? Remark);

/// <summary>证件档案新增 / 更新入参。</summary>
/// <param name="CertType">证书类型。</param>
/// <param name="CertNo">证书编号。</param>
/// <param name="IssuedOn">发证日期。</param>
/// <param name="ExpiresOn">到期日期。</param>
/// <param name="Remark">备注。</param>
public sealed record SavePersonCertPayload(
    string CertType,
    string? CertNo,
    DateOnly? IssuedOn,
    DateOnly? ExpiresOn,
    string? Remark);
