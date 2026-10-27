/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-25 19:00:00 +08:00 中国标准时间 星期日
 * 描述：供应商主数据服务契约——准入/评级/冻结/终止状态机、子表维护、采购可用性校验与审批挂起
 */

using Qlx.BuildingBlocks.Api;
using Qlx.Modules.Bas.Dtos;

namespace Qlx.Modules.Bas.Services;

/// <summary>
/// 供应商主数据服务。列表只读不缓存（分页筛选），详情主档经统一缓存读穿；
/// 写路径事务提交后落审计并失效 Bas 租户缓存前缀（C-5 失效优先）。
/// </summary>
public interface ISupplierService
{
    /// <summary>供应商分页列表（评级 / 状态 / 关键字筛选，不缓存）。</summary>
    /// <param name="grade">评级数值（可选）。</param>
    /// <param name="status">合作状态数值（可选）。</param>
    /// <param name="keyword">关键字（名称 / 编码 / 统一社会信用代码）。</param>
    /// <param name="page">页码（从 1 开始）。</param>
    /// <param name="pageSize">每页大小（1~200）。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>分页扁平行结果。</returns>
    Task<PagedResult<SupplierRow>> ListAsync(
        int? grade, int? status, string? keyword, int page, int pageSize, CancellationToken ct);

    /// <summary>供应商详情（主档 + 子表扁平集合，不做层级组装）。</summary>
    /// <param name="id">供应商 Id。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>供应商详情。</returns>
    Task<SupplierDetailRow> GetAsync(long id, CancellationToken ct);

    /// <summary>新建供应商（编码经统一发号器生成，初始状态待准入）。</summary>
    /// <param name="payload">新建入参（无编码字段）。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>创建后的供应商扁平行。</returns>
    Task<SupplierRow> CreateAsync(SaveSupplierPayload payload, CancellationToken ct);

    /// <summary>更新供应商基础信息（编码不可变更）。</summary>
    /// <param name="id">供应商 Id。</param>
    /// <param name="payload">更新入参。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>更新后的供应商扁平行。</returns>
    Task<SupplierRow> UpdateAsync(long id, SaveSupplierPayload payload, CancellationToken ct);

    /// <summary>变更评级并写评级历史（黑名单置位后采购侧禁采）。</summary>
    /// <param name="id">供应商 Id。</param>
    /// <param name="payload">评级变更入参。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>变更后的供应商扁平行。</returns>
    Task<SupplierRow> ChangeGradeAsync(long id, ChangeSupplierGradePayload payload, CancellationToken ct);

    /// <summary>采购可用性校验：未准入 / 冻结 / 终止 / 黑名单一律拒绝（下游下单前调用）。</summary>
    /// <param name="id">供应商 Id。</param>
    /// <param name="ct">取消令牌。</param>
    Task EnsurePurchasableAsync(long id, CancellationToken ct);

    /// <summary>提交供应商准入审批申请（挂起，审批通过后置为合作中）。</summary>
    /// <param name="id">供应商 Id。</param>
    /// <param name="reason">申请理由。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>审批挂起请求行。</returns>
    Task<ApprovalRequestRow> SubmitAdmissionRequestAsync(long id, string? reason, CancellationToken ct);

    /// <summary>审批通过后置为合作中并写审计（由审批应用器在事务内调用）。</summary>
    /// <param name="payload">准入载荷。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>事件构造结果（编码 / 评级），由应用器在事务提交后持久发布。</returns>
    Task<SupplierAdmissionApplied> ApplyAdmissionAsync(SupplierAdmissionPayload payload, CancellationToken ct);

    /// <summary>供应商资质证照扁平集合。</summary>
    /// <param name="id">供应商 Id。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>证照扁平行列表。</returns>
    Task<IReadOnlyList<SupplierCertRow>> ListCertsAsync(long id, CancellationToken ct);

    /// <summary>登记资质证照。</summary>
    /// <param name="id">供应商 Id。</param>
    /// <param name="payload">证照入参。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>创建后的证照扁平行。</returns>
    Task<SupplierCertRow> SaveCertAsync(long id, SaveSupplierCertPayload payload, CancellationToken ct);

    /// <summary>供应商银行账户扁平集合。</summary>
    /// <param name="id">供应商 Id。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>账户扁平行列表。</returns>
    Task<IReadOnlyList<SupplierBankRow>> ListBanksAsync(long id, CancellationToken ct);

    /// <summary>登记银行账户（默认账户唯一）。</summary>
    /// <param name="id">供应商 Id。</param>
    /// <param name="payload">账户入参。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>创建后的账户扁平行。</returns>
    Task<SupplierBankRow> SaveBankAsync(long id, SaveSupplierBankPayload payload, CancellationToken ct);

    /// <summary>提交银行账户变更审批申请（挂起，审批通过后新增账户）。</summary>
    /// <param name="id">供应商 Id。</param>
    /// <param name="payload">账户入参。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>审批挂起请求行。</returns>
    Task<ApprovalRequestRow> SubmitBankChangeRequestAsync(
        long id, SaveSupplierBankPayload payload, CancellationToken ct);

    /// <summary>审批通过后应用银行账户变更（由审批应用器在事务内调用）。</summary>
    /// <param name="payload">银行账户变更载荷。</param>
    /// <param name="ct">取消令牌。</param>
    Task ApplyBankChangeAsync(SupplierBankChangePayload payload, CancellationToken ct);

    /// <summary>供应商开票信息扁平集合。</summary>
    /// <param name="id">供应商 Id。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>开票信息扁平行列表。</returns>
    Task<IReadOnlyList<SupplierInvoiceRow>> ListInvoicesAsync(long id, CancellationToken ct);

    /// <summary>登记开票信息。</summary>
    /// <param name="id">供应商 Id。</param>
    /// <param name="payload">开票信息入参。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>创建后的开票信息扁平行。</returns>
    Task<SupplierInvoiceRow> SaveInvoiceAsync(
        long id, SaveSupplierInvoicePayload payload, CancellationToken ct);
}
