/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-25 18:10:00 +08:00 中国标准时间 星期日
 * 描述：Bas 供应商主数据 DTO——仅扁平原始字段与数值枚举（B-2），中文映射与展示排序由前端完成；
 *       新建/更新入参不含 Code（编码由后端发号器生成，N-1），业务 Id 由全局 Hashids 元数据自动混淆
 */

namespace Qlx.Modules.Bas.Dtos;

// ================ 供应商主档 ================

/// <summary>
/// 供应商扁平行（原始字段）。<c>SupplierType</c>/<c>Grade</c>/<c>Status</c> 为数值枚举。
/// </summary>
/// <param name="Id">供应商 Id。</param>
/// <param name="Code">供应商编码（后端生成，不可修改）。</param>
/// <param name="Name">供应商全称。</param>
/// <param name="Uscc">统一社会信用代码。</param>
/// <param name="SupplierType">供应商类型（1生产厂家/2经销商/3劳务分包商/4服务商/5其他）。</param>
/// <param name="Contact">联系人。</param>
/// <param name="Phone">联系电话。</param>
/// <param name="Grade">评级（1=A / 2=B / 3=C / 4=黑名单）。</param>
/// <param name="Status">合作状态（1=待准入 / 2=合作中 / 3=冻结 / 4=终止）。</param>
/// <param name="UpdatedAt">更新时间。</param>
public sealed record SupplierRow(
    long Id,
    string Code,
    string Name,
    string Uscc,
    int SupplierType,
    string? Contact,
    string? Phone,
    int Grade,
    int Status,
    DateTimeOffset UpdatedAt);

/// <summary>供应商详情（主档 + 各子表扁平集合，不做层级组装）。</summary>
/// <param name="Master">主档扁平行。</param>
/// <param name="Certs">资质证照扁平集合。</param>
/// <param name="Banks">银行账户扁平集合。</param>
/// <param name="Invoices">开票信息扁平集合。</param>
/// <param name="GradeLogs">评级历史扁平集合。</param>
public sealed record SupplierDetailRow(
    SupplierRow Master,
    IReadOnlyList<SupplierCertRow> Certs,
    IReadOnlyList<SupplierBankRow> Banks,
    IReadOnlyList<SupplierInvoiceRow> Invoices,
    IReadOnlyList<SupplierGradeLogRow> GradeLogs);

/// <summary>供应商新建/更新入参（无编码字段）。</summary>
/// <param name="Name">供应商全称。</param>
/// <param name="Uscc">统一社会信用代码。</param>
/// <param name="SupplierType">供应商类型数值。</param>
/// <param name="Contact">联系人。</param>
/// <param name="Phone">联系电话。</param>
public sealed record SaveSupplierPayload(
    string Name,
    string Uscc,
    int SupplierType,
    string? Contact,
    string? Phone);

/// <summary>供应商评级变更入参。</summary>
/// <param name="Grade">新评级数值（1=A / 2=B / 3=C / 4=黑名单）。</param>
/// <param name="Reason">变更原因。</param>
public sealed record ChangeSupplierGradePayload(
    int Grade,
    string? Reason);

/// <summary>供应商准入审批挂起载荷。</summary>
/// <param name="SupplierId">供应商 Id。</param>
/// <param name="Reason">申请理由。</param>
public sealed record SupplierAdmissionPayload(long SupplierId, string? Reason);

/// <summary>供应商准入通过后的事件构造结果（由审批应用器在事务提交后持久发布）。</summary>
/// <param name="SupplierId">供应商 Id。</param>
/// <param name="SupplierCode">供应商编码。</param>
/// <param name="Grade">评级数值。</param>
public sealed record SupplierAdmissionApplied(long SupplierId, string SupplierCode, int Grade);

// ================ 供应商子表 ================

/// <summary>供应商资质证照扁平行。</summary>
/// <param name="Id">证照 Id。</param>
/// <param name="SupplierId">供应商 Id。</param>
/// <param name="CertType">资质类型。</param>
/// <param name="CertNo">资质编号。</param>
/// <param name="IssueOrg">发证机构。</param>
/// <param name="EffectDate">生效日期。</param>
/// <param name="ExpiryDate">失效日期（null 表示长期有效）。</param>
/// <param name="FileId">影像文件 Id。</param>
/// <param name="Status">证照状态（1=有效 / 2=已过期 / 3=已作废）。</param>
public sealed record SupplierCertRow(
    long Id,
    long SupplierId,
    string CertType,
    string CertNo,
    string? IssueOrg,
    DateOnly? EffectDate,
    DateOnly? ExpiryDate,
    long? FileId,
    int Status);

/// <summary>供应商银行账户扁平行（账号明文仅本接口下发，审计快照脱敏）。</summary>
/// <param name="Id">账户 Id。</param>
/// <param name="SupplierId">供应商 Id。</param>
/// <param name="BankName">开户银行。</param>
/// <param name="AccountName">账户名称。</param>
/// <param name="AccountNo">银行账号。</param>
/// <param name="IsDefault">是否默认账户。</param>
/// <param name="Status">启停状态（1=启用 / 2=停用）。</param>
public sealed record SupplierBankRow(
    long Id,
    long SupplierId,
    string BankName,
    string AccountName,
    string AccountNo,
    bool IsDefault,
    int Status);

/// <summary>供应商开票信息扁平行。</summary>
/// <param name="Id">开票信息 Id。</param>
/// <param name="SupplierId">供应商 Id。</param>
/// <param name="Title">发票抬头。</param>
/// <param name="TaxNo">纳税人识别号。</param>
/// <param name="BankInfo">开户行及账号。</param>
/// <param name="AddrPhone">地址电话。</param>
/// <param name="InvoiceType">开票类型（1专票 / 2普票 / 3数电票）。</param>
/// <param name="Status">启停状态（1=启用 / 2=停用）。</param>
public sealed record SupplierInvoiceRow(
    long Id,
    long SupplierId,
    string Title,
    string? TaxNo,
    string? BankInfo,
    string? AddrPhone,
    int InvoiceType,
    int Status);

/// <summary>供应商评级历史扁平行。</summary>
/// <param name="Id">历史行 Id。</param>
/// <param name="SupplierId">供应商 Id。</param>
/// <param name="OldGrade">变更前评级数值。</param>
/// <param name="NewGrade">变更后评级数值。</param>
/// <param name="Reason">变更原因。</param>
/// <param name="ApprovedBy">审批人。</param>
/// <param name="CreatedAt">记录时间。</param>
public sealed record SupplierGradeLogRow(
    long Id,
    long SupplierId,
    int OldGrade,
    int NewGrade,
    string? Reason,
    string? ApprovedBy,
    DateTimeOffset CreatedAt);

/// <summary>供应商资质证照登记入参。</summary>
/// <param name="CertType">资质类型。</param>
/// <param name="CertNo">资质编号。</param>
/// <param name="IssueOrg">发证机构。</param>
/// <param name="EffectDate">生效日期。</param>
/// <param name="ExpiryDate">失效日期。</param>
/// <param name="FileId">影像文件 Id。</param>
public sealed record SaveSupplierCertPayload(
    string CertType,
    string CertNo,
    string? IssueOrg,
    DateOnly? EffectDate,
    DateOnly? ExpiryDate,
    long? FileId);

/// <summary>供应商银行账户登记入参。</summary>
/// <param name="BankName">开户银行。</param>
/// <param name="AccountName">账户名称。</param>
/// <param name="AccountNo">银行账号。</param>
/// <param name="IsDefault">是否默认账户。</param>
public sealed record SaveSupplierBankPayload(
    string BankName,
    string AccountName,
    string AccountNo,
    bool IsDefault);

/// <summary>供应商银行账户变更审批挂起载荷（审批通过后新增账户并按需设为默认）。</summary>
/// <param name="SupplierId">供应商 Id。</param>
/// <param name="BankName">开户银行。</param>
/// <param name="AccountName">账户名称。</param>
/// <param name="AccountNo">银行账号。</param>
/// <param name="IsDefault">是否设为默认账户。</param>
public sealed record SupplierBankChangePayload(
    long SupplierId,
    string BankName,
    string AccountName,
    string AccountNo,
    bool IsDefault);

/// <summary>供应商开票信息登记入参。</summary>
/// <param name="Title">发票抬头。</param>
/// <param name="TaxNo">纳税人识别号。</param>
/// <param name="BankInfo">开户行及账号。</param>
/// <param name="AddrPhone">地址电话。</param>
/// <param name="InvoiceType">开票类型数值。</param>
public sealed record SaveSupplierInvoicePayload(
    string Title,
    string? TaxNo,
    string? BankInfo,
    string? AddrPhone,
    int InvoiceType);
