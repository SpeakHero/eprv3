/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-25 20:15:00 +08:00 中国标准时间 星期日
 * 描述：客户主数据 API（/api/bas/customers）——Controller 仅参数校验 + 服务调用 + ApiResponse 包装
 */

using Microsoft.AspNetCore.Mvc;

using Qlx.Abstractions.Constants;
using Qlx.BuildingBlocks.Api;
using Qlx.BuildingBlocks.Audit;
using Qlx.Modules.Bas.Dtos;
using Qlx.Modules.Bas.Services;

namespace Qlx.Modules.Bas.Controllers;

/// <summary>
/// 客户控制器。业务 Id 由全局 HashidsModelBinder 自动解码；客户编码由后端统一发号器生成，
/// 前端不传码（N-1）。
/// </summary>
/// <param name="service">客户主数据服务。</param>
[ApiController]
[Route("api/bas/customers")]
public sealed class CustomerController(ICustomerService service) : ControllerBase
{
    /// <summary>客户分页列表（信用等级 / 状态 / 关键字筛选）。</summary>
    /// <param name="creditGrade">信用等级数值（可选）。</param>
    /// <param name="status">合作状态数值（可选）。</param>
    /// <param name="keyword">关键字（可选）。</param>
    /// <param name="page">页码。</param>
    /// <param name="pageSize">每页大小。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>分页扁平行结果。</returns>
    [HttpGet]
    public async Task<ApiResponse<PagedResult<CustomerRow>>> GetList(
        int? creditGrade, int? status, string? keyword,
        int page = 1, int pageSize = 20, CancellationToken ct = default)
        => ApiResponse<PagedResult<CustomerRow>>.Ok(
            await service.ListAsync(creditGrade, status, keyword, page, pageSize, ct));

    /// <summary>客户详情（主档 + 子表扁平集合）。</summary>
    /// <param name="id">客户 Id。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>客户详情。</returns>
    [HttpGet("{id}")]
    public async Task<ApiResponse<CustomerDetailRow>> GetDetail(long id, CancellationToken ct)
        => ApiResponse<CustomerDetailRow>.Ok(await service.GetAsync(id, ct));

    /// <summary>新建客户。</summary>
    /// <param name="payload">新建入参。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>创建后的客户扁平行。</returns>
    [HttpPost]
    [AuditOperation(QlxModuleCodes.Bas, "BAS_CUSTOMER_CREATE", "创建了客户", TargetType = "Customer")]
    public async Task<ApiResponse<CustomerRow>> Create(
        [FromBody] SaveCustomerPayload payload, CancellationToken ct)
        => ApiResponse<CustomerRow>.Ok(await service.CreateAsync(payload, ct));

    /// <summary>更新客户基础信息。</summary>
    /// <param name="id">客户 Id。</param>
    /// <param name="payload">更新入参。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>更新后的客户扁平行。</returns>
    [HttpPut("{id}")]
    [AuditOperation(QlxModuleCodes.Bas, "BAS_CUSTOMER_UPDATE", "修改了客户", TargetType = "Customer", TargetIdParam = "id")]
    public async Task<ApiResponse<CustomerRow>> Update(
        long id, [FromBody] SaveCustomerPayload payload, CancellationToken ct)
        => ApiResponse<CustomerRow>.Ok(await service.UpdateAsync(id, payload, ct));

    /// <summary>提交客户准入审批申请。</summary>
    /// <param name="id">客户 Id。</param>
    /// <param name="payload">申请入参。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>审批挂起请求行。</returns>
    [HttpPost("{id}/admission-requests")]
    [AuditOperation(QlxModuleCodes.Bas, "BAS_CUSTOMER_ADMISSION_REQUEST", "提交了客户准入审批申请", TargetType = "Customer", TargetIdParam = "id")]
    public async Task<ApiResponse<ApprovalRequestRow>> SubmitAdmission(
        long id, [FromBody] CustomerAdmissionPayload payload, CancellationToken ct)
        => ApiResponse<ApprovalRequestRow>.Ok(
            await service.SubmitAdmissionRequestAsync(id, payload?.Reason, ct));

    /// <summary>提交客户信用额度调整审批申请。</summary>
    /// <param name="id">客户 Id。</param>
    /// <param name="payload">调整入参。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>审批挂起请求行。</returns>
    [HttpPost("{id}/credit-adjust-requests")]
    [AuditOperation(QlxModuleCodes.Bas, "BAS_CUSTOMER_CREDIT_REQUEST", "提交了客户信用额度调整审批申请", TargetType = "Customer", TargetIdParam = "id")]
    public async Task<ApiResponse<ApprovalRequestRow>> SubmitCreditAdjust(
        long id, [FromBody] CustomerCreditAdjustPayload payload, CancellationToken ct)
        => ApiResponse<ApprovalRequestRow>.Ok(
            await service.SubmitCreditAdjustRequestAsync(id, payload, ct));

    /// <summary>客户信用额度视图（额度 / 占用 / 可用额度原始金额）。</summary>
    /// <param name="id">客户 Id。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>信用额度视图。</returns>
    [HttpGet("{id}/credit")]
    public async Task<ApiResponse<CustomerCreditViewRow>> GetCredit(long id, CancellationToken ct)
        => ApiResponse<CustomerCreditViewRow>.Ok(await service.GetCreditViewAsync(id, ct));

    /// <summary>客户回款账户扁平集合。</summary>
    /// <param name="id">客户 Id。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>账户扁平行列表。</returns>
    [HttpGet("{id}/banks")]
    public async Task<ApiResponse<IReadOnlyList<CustomerBankRow>>> GetBanks(long id, CancellationToken ct)
        => ApiResponse<IReadOnlyList<CustomerBankRow>>.Ok(await service.ListBanksAsync(id, ct));

    /// <summary>登记客户回款账户（默认账户唯一）。</summary>
    /// <param name="id">客户 Id。</param>
    /// <param name="payload">账户入参。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>创建后的账户扁平行。</returns>
    [HttpPost("{id}/banks")]
    [AuditOperation(QlxModuleCodes.Bas, "BAS_CUSTOMER_BANK_SAVE", "登记了客户回款账户", TargetType = "Customer", TargetIdParam = "id")]
    public async Task<ApiResponse<CustomerBankRow>> SaveBank(
        long id, [FromBody] SaveCustomerBankPayload payload, CancellationToken ct)
        => ApiResponse<CustomerBankRow>.Ok(await service.SaveBankAsync(id, payload, ct));

    /// <summary>客户开票信息扁平集合。</summary>
    /// <param name="id">客户 Id。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>开票信息扁平行列表。</returns>
    [HttpGet("{id}/invoices")]
    public async Task<ApiResponse<IReadOnlyList<CustomerInvoiceRow>>> GetInvoices(
        long id, CancellationToken ct)
        => ApiResponse<IReadOnlyList<CustomerInvoiceRow>>.Ok(await service.ListInvoicesAsync(id, ct));

    /// <summary>登记客户开票信息。</summary>
    /// <param name="id">客户 Id。</param>
    /// <param name="payload">开票信息入参。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>创建后的开票信息扁平行。</returns>
    [HttpPost("{id}/invoices")]
    [AuditOperation(QlxModuleCodes.Bas, "BAS_CUSTOMER_INVOICE_SAVE", "登记了客户开票信息", TargetType = "Customer", TargetIdParam = "id")]
    public async Task<ApiResponse<CustomerInvoiceRow>> SaveInvoice(
        long id, [FromBody] SaveCustomerInvoicePayload payload, CancellationToken ct)
        => ApiResponse<CustomerInvoiceRow>.Ok(await service.SaveInvoiceAsync(id, payload, ct));
}
