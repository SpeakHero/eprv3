/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-25 20:10:00 +08:00 中国标准时间 星期日
 * 描述：供应商主数据 API（/api/bas/suppliers）——Controller 仅参数校验 + 服务调用 + ApiResponse 包装
 */

using Microsoft.AspNetCore.Mvc;

using Qlx.Abstractions.Constants;
using Qlx.BuildingBlocks.Api;
using Qlx.BuildingBlocks.Audit;
using Qlx.Modules.Bas.Dtos;
using Qlx.Modules.Bas.Services;

namespace Qlx.Modules.Bas.Controllers;

/// <summary>
/// 供应商控制器。业务 Id 由全局 HashidsModelBinder 自动解码；供应商编码由后端统一发号器生成，
/// 前端不传码（N-1）。
/// </summary>
/// <param name="service">供应商主数据服务。</param>
[ApiController]
[Route("api/bas/suppliers")]
public sealed class SupplierController(ISupplierService service) : ControllerBase
{
    /// <summary>供应商分页列表（评级 / 状态 / 关键字筛选）。</summary>
    /// <param name="grade">评级数值（可选）。</param>
    /// <param name="status">合作状态数值（可选）。</param>
    /// <param name="keyword">关键字（可选）。</param>
    /// <param name="page">页码。</param>
    /// <param name="pageSize">每页大小。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>分页扁平行结果。</returns>
    [HttpGet]
    public async Task<ApiResponse<PagedResult<SupplierRow>>> GetList(
        int? grade, int? status, string? keyword, int page = 1, int pageSize = 20, CancellationToken ct = default)
        => ApiResponse<PagedResult<SupplierRow>>.Ok(
            await service.ListAsync(grade, status, keyword, page, pageSize, ct));

    /// <summary>供应商详情（主档 + 子表扁平集合）。</summary>
    /// <param name="id">供应商 Id。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>供应商详情。</returns>
    [HttpGet("{id}")]
    public async Task<ApiResponse<SupplierDetailRow>> GetDetail(long id, CancellationToken ct)
        => ApiResponse<SupplierDetailRow>.Ok(await service.GetAsync(id, ct));

    /// <summary>新建供应商。</summary>
    /// <param name="payload">新建入参。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>创建后的供应商扁平行。</returns>
    [HttpPost]
    [AuditOperation(QlxModuleCodes.Bas, "BAS_SUPPLIER_CREATE", "创建了供应商", TargetType = "Supplier")]
    public async Task<ApiResponse<SupplierRow>> Create(
        [FromBody] SaveSupplierPayload payload, CancellationToken ct)
        => ApiResponse<SupplierRow>.Ok(await service.CreateAsync(payload, ct));

    /// <summary>更新供应商基础信息。</summary>
    /// <param name="id">供应商 Id。</param>
    /// <param name="payload">更新入参。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>更新后的供应商扁平行。</returns>
    [HttpPut("{id}")]
    [AuditOperation(QlxModuleCodes.Bas, "BAS_SUPPLIER_UPDATE", "修改了供应商", TargetType = "Supplier", TargetIdParam = "id")]
    public async Task<ApiResponse<SupplierRow>> Update(
        long id, [FromBody] SaveSupplierPayload payload, CancellationToken ct)
        => ApiResponse<SupplierRow>.Ok(await service.UpdateAsync(id, payload, ct));

    /// <summary>变更供应商评级（黑名单置位后采购侧禁采）。</summary>
    /// <param name="id">供应商 Id。</param>
    /// <param name="payload">评级变更入参。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>变更后的供应商扁平行。</returns>
    [HttpPost("{id}/grade")]
    [AuditOperation(QlxModuleCodes.Bas, "BAS_SUPPLIER_GRADE", "变更了供应商评级", TargetType = "Supplier", TargetIdParam = "id")]
    public async Task<ApiResponse<SupplierRow>> ChangeGrade(
        long id, [FromBody] ChangeSupplierGradePayload payload, CancellationToken ct)
        => ApiResponse<SupplierRow>.Ok(await service.ChangeGradeAsync(id, payload, ct));

    /// <summary>提交供应商准入审批申请（挂起，审批通过后置为合作中）。</summary>
    /// <param name="id">供应商 Id。</param>
    /// <param name="payload">申请入参。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>审批挂起请求行。</returns>
    [HttpPost("{id}/admission-requests")]
    [AuditOperation(QlxModuleCodes.Bas, "BAS_SUPPLIER_ADMISSION_REQUEST", "提交了供应商准入审批申请", TargetType = "Supplier", TargetIdParam = "id")]
    public async Task<ApiResponse<ApprovalRequestRow>> SubmitAdmission(
        long id, [FromBody] SupplierAdmissionPayload payload, CancellationToken ct)
        => ApiResponse<ApprovalRequestRow>.Ok(
            await service.SubmitAdmissionRequestAsync(id, payload?.Reason, ct));

    /// <summary>供应商资质证照扁平集合。</summary>
    /// <param name="id">供应商 Id。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>证照扁平行列表。</returns>
    [HttpGet("{id}/certs")]
    public async Task<ApiResponse<IReadOnlyList<SupplierCertRow>>> GetCerts(long id, CancellationToken ct)
        => ApiResponse<IReadOnlyList<SupplierCertRow>>.Ok(await service.ListCertsAsync(id, ct));

    /// <summary>登记供应商资质证照。</summary>
    /// <param name="id">供应商 Id。</param>
    /// <param name="payload">证照入参。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>创建后的证照扁平行。</returns>
    [HttpPost("{id}/certs")]
    [AuditOperation(QlxModuleCodes.Bas, "BAS_SUPPLIER_CERT_SAVE", "登记了供应商资质证照", TargetType = "Supplier", TargetIdParam = "id")]
    public async Task<ApiResponse<SupplierCertRow>> SaveCert(
        long id, [FromBody] SaveSupplierCertPayload payload, CancellationToken ct)
        => ApiResponse<SupplierCertRow>.Ok(await service.SaveCertAsync(id, payload, ct));

    /// <summary>供应商银行账户扁平集合。</summary>
    /// <param name="id">供应商 Id。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>账户扁平行列表。</returns>
    [HttpGet("{id}/banks")]
    public async Task<ApiResponse<IReadOnlyList<SupplierBankRow>>> GetBanks(long id, CancellationToken ct)
        => ApiResponse<IReadOnlyList<SupplierBankRow>>.Ok(await service.ListBanksAsync(id, ct));

    /// <summary>登记供应商银行账户（默认账户唯一）。</summary>
    /// <param name="id">供应商 Id。</param>
    /// <param name="payload">账户入参。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>创建后的账户扁平行。</returns>
    [HttpPost("{id}/banks")]
    [AuditOperation(QlxModuleCodes.Bas, "BAS_SUPPLIER_BANK_SAVE", "登记了供应商银行账户", TargetType = "Supplier", TargetIdParam = "id")]
    public async Task<ApiResponse<SupplierBankRow>> SaveBank(
        long id, [FromBody] SaveSupplierBankPayload payload, CancellationToken ct)
        => ApiResponse<SupplierBankRow>.Ok(await service.SaveBankAsync(id, payload, ct));

    /// <summary>提交供应商银行账户变更审批申请。</summary>
    /// <param name="id">供应商 Id。</param>
    /// <param name="payload">账户入参。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>审批挂起请求行。</returns>
    [HttpPost("{id}/bank-change-requests")]
    [AuditOperation(QlxModuleCodes.Bas, "BAS_SUPPLIER_BANK_REQUEST", "提交了供应商银行账户变更审批申请", TargetType = "Supplier", TargetIdParam = "id")]
    public async Task<ApiResponse<ApprovalRequestRow>> SubmitBankChange(
        long id, [FromBody] SaveSupplierBankPayload payload, CancellationToken ct)
        => ApiResponse<ApprovalRequestRow>.Ok(
            await service.SubmitBankChangeRequestAsync(id, payload, ct));

    /// <summary>供应商开票信息扁平集合。</summary>
    /// <param name="id">供应商 Id。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>开票信息扁平行列表。</returns>
    [HttpGet("{id}/invoices")]
    public async Task<ApiResponse<IReadOnlyList<SupplierInvoiceRow>>> GetInvoices(
        long id, CancellationToken ct)
        => ApiResponse<IReadOnlyList<SupplierInvoiceRow>>.Ok(await service.ListInvoicesAsync(id, ct));

    /// <summary>登记供应商开票信息。</summary>
    /// <param name="id">供应商 Id。</param>
    /// <param name="payload">开票信息入参。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>创建后的开票信息扁平行。</returns>
    [HttpPost("{id}/invoices")]
    [AuditOperation(QlxModuleCodes.Bas, "BAS_SUPPLIER_INVOICE_SAVE", "登记了供应商开票信息", TargetType = "Supplier", TargetIdParam = "id")]
    public async Task<ApiResponse<SupplierInvoiceRow>> SaveInvoice(
        long id, [FromBody] SaveSupplierInvoicePayload payload, CancellationToken ct)
        => ApiResponse<SupplierInvoiceRow>.Ok(await service.SaveInvoiceAsync(id, payload, ct));
}
