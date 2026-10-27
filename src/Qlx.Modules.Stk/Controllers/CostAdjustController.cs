/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-26
 * 描述：成本调整 API（/api/stk/cost-adjusts）——Controller 仅参数绑定 + 服务调用 + ApiResponse 包装；
 *       单号、旧均价与差额均由后端生成/取值（N-1/V-1），过账只能经服务层审批应用写台账（A-5）
 */

using Microsoft.AspNetCore.Mvc;

using Qlx.Abstractions.Constants;
using Qlx.BuildingBlocks.Api;
using Qlx.BuildingBlocks.Audit;
using Qlx.Modules.Stk.Dtos;
using Qlx.Modules.Stk.Services;

namespace Qlx.Modules.Stk.Controllers;

/// <summary>
/// 成本调整控制器。业务 Id 由全局 HashidsModelBinder 自动解码；单号由后端生成（N-1）。
/// </summary>
/// <param name="service">成本调整服务。</param>
[ApiController]
[Route("api/stk/cost-adjusts")]
public sealed class CostAdjustController(ICostAdjustService service) : ControllerBase
{
    /// <summary>成本调整单扁平列表。</summary>
    /// <param name="status">状态数值（1 草稿 / 2 待审批 / 3 已通过 / 4 已驳回，可空）。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>成本调整单扁平行列表。</returns>
    [HttpGet]
    public async Task<ApiResponse<IReadOnlyList<CostAdjustRow>>> GetList(
        [FromQuery] int? status, CancellationToken ct)
        => ApiResponse<IReadOnlyList<CostAdjustRow>>.Ok(await service.ListAsync(status, ct));

    /// <summary>成本调整单详情（表头 + 明细）。</summary>
    /// <param name="id">成本调整单 Id。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>成本调整明细。</returns>
    [HttpGet("{id}")]
    public async Task<ApiResponse<CostAdjustDetailRow>> GetDetail(long id, CancellationToken ct)
        => ApiResponse<CostAdjustDetailRow>.Ok(await service.GetAsync(id, ct));

    /// <summary>新建成本调整草稿（不含单号、旧均价与差额）。</summary>
    /// <param name="payload">新建入参。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>创建后的成本调整明细。</returns>
    [HttpPost]
    [AuditOperation(QlxModuleCodes.Stk, "STK_COST_ADJUST_CREATE", "创建了成本调整草稿", TargetType = "CostAdjust")]
    public async Task<ApiResponse<CostAdjustDetailRow>> Create(
        [FromBody] CreateCostAdjustPayload payload, CancellationToken ct)
        => ApiResponse<CostAdjustDetailRow>.Ok(await service.CreateDraftAsync(payload, ct));

    /// <summary>修改成本调整草稿（仅草稿可改）。</summary>
    /// <param name="id">成本调整单 Id。</param>
    /// <param name="payload">更新入参。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>更新后的成本调整明细。</returns>
    [HttpPut("{id}")]
    [AuditOperation(QlxModuleCodes.Stk, "STK_COST_ADJUST_UPDATE", "修改了成本调整草稿", TargetType = "CostAdjust", TargetIdParam = "id")]
    public async Task<ApiResponse<CostAdjustDetailRow>> Update(
        long id, [FromBody] UpdateCostAdjustPayload payload, CancellationToken ct)
        => ApiResponse<CostAdjustDetailRow>.Ok(await service.UpdateDraftAsync(id, payload, ct));

    /// <summary>删除成本调整草稿（仅草稿可删）。</summary>
    /// <param name="id">成本调整单 Id。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>空响应。</returns>
    [HttpDelete("{id}")]
    [AuditOperation(QlxModuleCodes.Stk, "STK_COST_ADJUST_DELETE", "删除了成本调整草稿", TargetType = "CostAdjust", TargetIdParam = "id")]
    public async Task<ApiResponse<object?>> Delete(long id, CancellationToken ct)
    {
        await service.DeleteDraftAsync(id, ct);
        return ApiResponse<object?>.Ok(null);
    }

    /// <summary>提交成本调整审批。</summary>
    /// <param name="id">成本调整单 Id。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>审批挂起请求。</returns>
    [HttpPost("{id}/submit")]
    [AuditOperation(QlxModuleCodes.Stk, "STK_COST_ADJUST_SUBMIT", "提交了成本调整申请", TargetType = "CostAdjust", TargetIdParam = "id")]
    public async Task<ApiResponse<StkApprovalRequestRow>> Submit(long id, CancellationToken ct)
        => ApiResponse<StkApprovalRequestRow>.Ok(await service.SubmitAsync(id, ct));
}
