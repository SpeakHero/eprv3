/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-25 19:20:00 +08:00 中国标准时间 星期日
 * 描述：客户主数据服务契约——准入/信用额度调整审批、信用校验、额度视图与子表维护
 */

using Qlx.BuildingBlocks.Api;
using Qlx.Modules.Bas.Dtos;

namespace Qlx.Modules.Bas.Services;

/// <summary>
/// 客户主数据服务。列表只读不缓存（分页筛选），详情主档经统一缓存读穿；
/// 写路径事务提交后落审计并失效 Bas 租户缓存前缀（C-5 失效优先）。
/// </summary>
public interface ICustomerService
{
    /// <summary>客户分页列表（信用等级 / 状态 / 关键字筛选，不缓存）。</summary>
    /// <param name="creditGrade">信用等级数值（可选）。</param>
    /// <param name="status">合作状态数值（可选）。</param>
    /// <param name="keyword">关键字（名称 / 编码 / 统一社会信用代码）。</param>
    /// <param name="page">页码（从 1 开始）。</param>
    /// <param name="pageSize">每页大小（1~200）。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>分页扁平行结果。</returns>
    Task<PagedResult<CustomerRow>> ListAsync(
        int? creditGrade, int? status, string? keyword, int page, int pageSize, CancellationToken ct);

    /// <summary>客户详情（主档 + 子表扁平集合，不做层级组装）。</summary>
    /// <param name="id">客户 Id。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>客户详情。</returns>
    Task<CustomerDetailRow> GetAsync(long id, CancellationToken ct);

    /// <summary>新建客户（编码经统一发号器生成，初始状态待准入）。</summary>
    /// <param name="payload">新建入参（无编码字段）。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>创建后的客户扁平行。</returns>
    Task<CustomerRow> CreateAsync(SaveCustomerPayload payload, CancellationToken ct);

    /// <summary>更新客户基础信息（编码不可变更）。</summary>
    /// <param name="id">客户 Id。</param>
    /// <param name="payload">更新入参。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>更新后的客户扁平行。</returns>
    Task<CustomerRow> UpdateAsync(long id, SaveCustomerPayload payload, CancellationToken ct);

    /// <summary>信用额度校验：已占用 + 本次下单金额不得超过信用额度（下游下单前调用）。</summary>
    /// <param name="id">客户 Id。</param>
    /// <param name="orderAmount">本次下单金额。</param>
    /// <param name="ct">取消令牌。</param>
    Task EnsureCreditAvailableAsync(long id, decimal orderAmount, CancellationToken ct);

    /// <summary>提交客户准入审批申请（挂起，审批通过后置为合作中）。</summary>
    /// <param name="id">客户 Id。</param>
    /// <param name="reason">申请理由。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>审批挂起请求行。</returns>
    Task<ApprovalRequestRow> SubmitAdmissionRequestAsync(long id, string? reason, CancellationToken ct);

    /// <summary>审批通过后置为合作中并写审计（由审批应用器在事务内调用）。</summary>
    /// <param name="payload">准入载荷。</param>
    /// <param name="ct">取消令牌。</param>
    Task ApplyAdmissionAsync(CustomerAdmissionPayload payload, CancellationToken ct);

    /// <summary>提交信用额度调整审批申请（阈值仅记录于 BizData / 标题，本批统一单流程）。</summary>
    /// <param name="id">客户 Id。</param>
    /// <param name="payload">调整入参。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>审批挂起请求行。</returns>
    Task<ApprovalRequestRow> SubmitCreditAdjustRequestAsync(
        long id, CustomerCreditAdjustPayload payload, CancellationToken ct);

    /// <summary>审批通过后更新额度与等级并写信用历史（由审批应用器在事务内调用）。</summary>
    /// <param name="payload">调整载荷。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>事件构造结果，由应用器在事务提交后持久发布。</returns>
    Task<CustomerCreditApplied> ApplyCreditAdjustAsync(
        CustomerCreditAdjustPayload payload, CancellationToken ct);

    /// <summary>客户信用额度视图（额度 / 占用 / 可用额度原始金额）。</summary>
    /// <param name="id">客户 Id。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>信用额度视图。</returns>
    Task<CustomerCreditViewRow> GetCreditViewAsync(long id, CancellationToken ct);

    /// <summary>客户回款账户扁平集合。</summary>
    /// <param name="id">客户 Id。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>账户扁平行列表。</returns>
    Task<IReadOnlyList<CustomerBankRow>> ListBanksAsync(long id, CancellationToken ct);

    /// <summary>登记回款账户（默认账户唯一）。</summary>
    /// <param name="id">客户 Id。</param>
    /// <param name="payload">账户入参。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>创建后的账户扁平行。</returns>
    Task<CustomerBankRow> SaveBankAsync(long id, SaveCustomerBankPayload payload, CancellationToken ct);

    /// <summary>客户开票信息扁平集合。</summary>
    /// <param name="id">客户 Id。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>开票信息扁平行列表。</returns>
    Task<IReadOnlyList<CustomerInvoiceRow>> ListInvoicesAsync(long id, CancellationToken ct);

    /// <summary>登记开票信息。</summary>
    /// <param name="id">客户 Id。</param>
    /// <param name="payload">开票信息入参。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>创建后的开票信息扁平行。</returns>
    Task<CustomerInvoiceRow> SaveInvoiceAsync(
        long id, SaveCustomerInvoicePayload payload, CancellationToken ct);
}
