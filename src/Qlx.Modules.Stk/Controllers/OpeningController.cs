/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-26
 * 描述：期初建账 API（/api/stk/openings）——Controller 仅参数绑定 + 服务调用 + ApiResponse 包装；
 *       期初单号由后端生成（前端不传码，N-1），过账只能经服务层审批应用写台账（A-5）
 */

using Microsoft.AspNetCore.Mvc;

using Qlx.Abstractions.Constants;
using Qlx.BuildingBlocks.Api;
using Qlx.BuildingBlocks.Audit;
using Qlx.Modules.Stk.Dtos;
using Qlx.Modules.Stk.Services;

namespace Qlx.Modules.Stk.Controllers;

/// <summary>
/// 期初建账控制器。业务 Id 由全局 HashidsModelBinder 自动解码；期初单号由后端生成（N-1）。
/// </summary>
/// <param name="service">期初建账服务。</param>
[ApiController]
[Route("api/stk/openings")]
public sealed class OpeningController(IOpeningService service) : ControllerBase
{
    /// <summary>期初批次扁平列表。</summary>
    /// <param name="status">状态数值（1 草稿 / 2 待审批 / 3 已过账，可空）。</param>
    /// <param name="warehouseId">仓库 Id（可空）。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>期初批次扁平行列表。</returns>
    [HttpGet]
    public async Task<ApiResponse<IReadOnlyList<OpeningBatchRow>>> GetList(
        [FromQuery] int? status, [FromQuery] long? warehouseId, CancellationToken ct)
        => ApiResponse<IReadOnlyList<OpeningBatchRow>>.Ok(
            await service.ListAsync(status, warehouseId, ct));

    /// <summary>期初仓库汇总（仅已过账明细）。</summary>
    /// <param name="ct">取消令牌。</param>
    /// <returns>仓库汇总扁平行列表。</returns>
    [HttpGet("summary")]
    public async Task<ApiResponse<IReadOnlyList<OpeningWarehouseSummaryRow>>> GetSummary(CancellationToken ct)
        => ApiResponse<IReadOnlyList<OpeningWarehouseSummaryRow>>.Ok(
            await service.GetWarehouseSummaryAsync(ct));

    /// <summary>期初批次详情（表头 + 明细）。</summary>
    /// <param name="id">期初批次 Id。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>期初明细。</returns>
    [HttpGet("{id}")]
    public async Task<ApiResponse<OpeningBatchDetailRow>> GetDetail(long id, CancellationToken ct)
        => ApiResponse<OpeningBatchDetailRow>.Ok(await service.GetAsync(id, ct));

    /// <summary>新建期初草稿（不含期初单号与金额）。</summary>
    /// <param name="payload">新建入参。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>创建后的期初明细。</returns>
    [HttpPost]
    [AuditOperation(QlxModuleCodes.Stk, "STK_OPENING_CREATE", "创建了期初建账草稿", TargetType = "OpeningBatch")]
    public async Task<ApiResponse<OpeningBatchDetailRow>> Create(
        [FromBody] CreateOpeningPayload payload, CancellationToken ct)
        => ApiResponse<OpeningBatchDetailRow>.Ok(await service.CreateDraftAsync(payload, ct));

    /// <summary>修改期初草稿（仅草稿可改）。</summary>
    /// <param name="id">期初批次 Id。</param>
    /// <param name="payload">更新入参。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>更新后的期初明细。</returns>
    [HttpPut("{id}")]
    [AuditOperation(QlxModuleCodes.Stk, "STK_OPENING_UPDATE", "修改了期初建账草稿", TargetType = "OpeningBatch", TargetIdParam = "id")]
    public async Task<ApiResponse<OpeningBatchDetailRow>> Update(
        long id, [FromBody] UpdateOpeningPayload payload, CancellationToken ct)
        => ApiResponse<OpeningBatchDetailRow>.Ok(await service.UpdateDraftAsync(id, payload, ct));

    /// <summary>删除期初草稿（仅草稿可删）。</summary>
    /// <param name="id">期初批次 Id。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>空响应。</returns>
    [HttpDelete("{id}")]
    [AuditOperation(QlxModuleCodes.Stk, "STK_OPENING_DELETE", "删除了期初建账草稿", TargetType = "OpeningBatch", TargetIdParam = "id")]
    public async Task<ApiResponse<object?>> Delete(long id, CancellationToken ct)
    {
        await service.DeleteDraftAsync(id, ct);
        return ApiResponse<object?>.Ok(null);
    }

    /// <summary>提交过账审批。</summary>
    /// <param name="id">期初批次 Id。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>审批挂起请求。</returns>
    [HttpPost("{id}/submit")]
    [AuditOperation(QlxModuleCodes.Stk, "STK_OPENING_SUBMIT", "提交了期初过账申请", TargetType = "OpeningBatch", TargetIdParam = "id")]
    public async Task<ApiResponse<StkApprovalRequestRow>> Submit(long id, CancellationToken ct)
        => ApiResponse<StkApprovalRequestRow>.Ok(await service.SubmitAsync(id, ct));
}
