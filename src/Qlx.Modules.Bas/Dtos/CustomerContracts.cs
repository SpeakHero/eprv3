/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-25 18:10:00 +08:00 中国标准时间 星期日
 * 描述：Bas 客户主数据 DTO——仅扁平原始字段与数值枚举（B-2），金额为原始 decimal（B-2-2）；
 *       新建/更新入参不含 Code（编码由后端发号器生成，N-1），业务 Id 由全局 Hashids 元数据自动混淆
 */

namespace Qlx.Modules.Bas.Dtos;

// ================ 客户主档 ================

/// <summary>
/// 客户扁平行（原始字段）。<c>CreditGrade</c>/<c>Status</c> 为数值枚举，金额不做显示格式化。
/// </summary>
/// <param name="Id">客户 Id。</param>
/// <param name="Code">客户编码（后端生成，不可修改）。</param>
/// <param name="Name">客户全称。</param>
/// <param name="Uscc">统一社会信用代码。</param>
/// <param name="CustomerType">客户类型数值（1生产厂家/2经销商/3劳务分包商/4服务商/5其他）。</param>
/// <param name="Contact">联系人。</param>
/// <param name="Phone">联系电话。</param>
/// <param name="CreditGrade">信用等级（1=A / 2=B / 3=C / 4=D）。</param>
/// <param name="CreditLimit">信用额度（原始金额）。</param>
/// <param name="CreditUsed">已占用信用额度（原始金额）。</param>
/// <param name="PaymentTerms">默认回款条款。</param>
/// <param name="Status">合作状态（1=待准入 / 2=合作中 / 3=冻结 / 4=终止）。</param>
/// <param name="UpdatedAt">更新时间。</param>
public sealed record CustomerRow(
    long Id,
    string Code,
    string Name,
    string Uscc,
    int CustomerType,
    string? Contact,
    string? Phone,
    int CreditGrade,
    decimal CreditLimit,
    decimal CreditUsed,
    string? PaymentTerms,
    int Status,
    DateTimeOffset UpdatedAt);

/// <summary>客户详情（主档 + 各子表扁平集合，不做层级组装）。</summary>
/// <param name="Master">主档扁平行。</param>
/// <param name="Banks">回款账户扁平集合。</param>
/// <param name="Invoices">开票信息扁平集合。</param>
/// <param name="CreditLogs">信用变更历史扁平集合。</param>
public sealed record CustomerDetailRow(
    CustomerRow Master,
    IReadOnlyList<CustomerBankRow> Banks,
    IReadOnlyList<CustomerInvoiceRow> Invoices,
    IReadOnlyList<CustomerCreditLogRow> CreditLogs);

/// <summary>客户新建/更新入参（无编码字段）。</summary>
/// <param name="Name">客户全称。</param>
/// <param name="Uscc">统一社会信用代码。</param>
/// <param name="CustomerType">客户类型数值。</param>
/// <param name="Contact">联系人。</param>
/// <param name="Phone">联系电话。</param>
/// <param name="PaymentTerms">默认回款条款。</param>
public sealed record SaveCustomerPayload(
    string Name,
    string Uscc,
    int CustomerType,
    string? Contact,
    string? Phone,
    string? PaymentTerms);

/// <summary>客户准入审批挂起载荷。</summary>
/// <param name="CustomerId">客户 Id。</param>
/// <param name="Reason">申请理由。</param>
public sealed record CustomerAdmissionPayload(long CustomerId, string? Reason);

/// <summary>客户信用额度调整审批挂起载荷（审批通过后按此更新额度与等级）。</summary>
/// <param name="CustomerId">客户 Id。</param>
/// <param name="NewLimit">新信用额度。</param>
/// <param name="NewCreditGrade">新信用等级数值。</param>
/// <param name="Reason">调整理由。</param>
public sealed record CustomerCreditAdjustPayload(
    long CustomerId,
    decimal NewLimit,
    int NewCreditGrade,
    string? Reason);

/// <summary>客户信用调整通过后的事件构造结果（由审批应用器在事务提交后持久发布）。</summary>
/// <param name="CustomerId">客户 Id。</param>
/// <param name="CustomerCode">客户编码。</param>
/// <param name="OldLimit">调整前信用额度。</param>
/// <param name="NewLimit">调整后信用额度。</param>
/// <param name="NewCreditGrade">新的信用等级数值。</param>
public sealed record CustomerCreditApplied(
    long CustomerId,
    string CustomerCode,
    decimal OldLimit,
    decimal NewLimit,
    int NewCreditGrade);

/// <summary>客户信用额度视图（额度 / 占用 / 可用额度原始金额）。</summary>
/// <param name="CustomerId">客户 Id。</param>
/// <param name="CreditLimit">信用额度。</param>
/// <param name="CreditUsed">已占用额度。</param>
/// <param name="Available">可用额度（额度 - 占用，业务规则值）。</param>
/// <param name="CreditGrade">信用等级数值。</param>
/// <param name="Status">合作状态数值。</param>
public sealed record CustomerCreditViewRow(
    long CustomerId,
    decimal CreditLimit,
    decimal CreditUsed,
    decimal Available,
    int CreditGrade,
    int Status);

// ================ 客户子表 ================

/// <summary>客户回款账户扁平行。</summary>
/// <param name="Id">账户 Id。</param>
/// <param name="CustomerId">客户 Id。</param>
/// <param name="BankName">开户银行。</param>
/// <param name="AccountName">账户名称。</param>
/// <param name="AccountNo">银行账号。</param>
/// <param name="IsDefault">是否默认账户。</param>
/// <param name="Status">启停状态（1=启用 / 2=停用）。</param>
public sealed record CustomerBankRow(
    long Id,
    long CustomerId,
    string BankName,
    string AccountName,
    string AccountNo,
    bool IsDefault,
    int Status);

/// <summary>客户开票信息扁平行。</summary>
/// <param name="Id">开票信息 Id。</param>
/// <param name="CustomerId">客户 Id。</param>
/// <param name="Title">发票抬头。</param>
/// <param name="TaxNo">纳税人识别号。</param>
/// <param name="BankInfo">开户行及账号。</param>
/// <param name="AddrPhone">地址电话。</param>
/// <param name="InvoiceType">开票类型（1专票 / 2普票 / 3数电票）。</param>
/// <param name="Status">启停状态（1=启用 / 2=停用）。</param>
public sealed record CustomerInvoiceRow(
    long Id,
    long CustomerId,
    string Title,
    string? TaxNo,
    string? BankInfo,
    string? AddrPhone,
    int InvoiceType,
    int Status);

/// <summary>客户信用变更历史扁平行。</summary>
/// <param name="Id">历史行 Id。</param>
/// <param name="CustomerId">客户 Id。</param>
/// <param name="OldCreditGrade">变更前信用等级数值。</param>
/// <param name="NewCreditGrade">变更后信用等级数值。</param>
/// <param name="OldLimit">变更前信用额度。</param>
/// <param name="NewLimit">变更后信用额度。</param>
/// <param name="Reason">变更原因。</param>
/// <param name="ApprovedBy">审批人。</param>
/// <param name="CreatedAt">记录时间。</param>
public sealed record CustomerCreditLogRow(
    long Id,
    long CustomerId,
    int OldCreditGrade,
    int NewCreditGrade,
    decimal OldLimit,
    decimal NewLimit,
    string? Reason,
    string? ApprovedBy,
    DateTimeOffset CreatedAt);

/// <summary>客户回款账户登记入参。</summary>
/// <param name="BankName">开户银行。</param>
/// <param name="AccountName">账户名称。</param>
/// <param name="AccountNo">银行账号。</param>
/// <param name="IsDefault">是否默认账户。</param>
public sealed record SaveCustomerBankPayload(
    string BankName,
    string AccountName,
    string AccountNo,
    bool IsDefault);

/// <summary>客户开票信息登记入参。</summary>
/// <param name="Title">发票抬头。</param>
/// <param name="TaxNo">纳税人识别号。</param>
/// <param name="BankInfo">开户行及账号。</param>
/// <param name="AddrPhone">地址电话。</param>
/// <param name="InvoiceType">开票类型数值。</param>
public sealed record SaveCustomerInvoicePayload(
    string Title,
    string? TaxNo,
    string? BankInfo,
    string? AddrPhone,
    int InvoiceType);
