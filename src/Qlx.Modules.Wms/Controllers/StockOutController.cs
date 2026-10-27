/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-26
 * 描述：出库作业 API（/api/wms/stock-out-tasks）——Controller 仅参数绑定 + 服务调用 + ApiResponse 包装
 */

using Microsoft.AspNetCore.Mvc;

using Qlx.Abstractions.Constants;
using Qlx.BuildingBlocks.Api;
using Qlx.BuildingBlocks.Audit;
using Qlx.Modules.Wms.Dtos;
using Qlx.Modules.Wms.Services;

namespace Qlx.Modules.Wms.Controllers;

/// <summary>
/// 出库作业控制器。作业单号由后端生成，前端不传码（N-1）；业务 Id 由全局 HashidsModelBinder 自动解码。
/// </summary>
/// <param name="service">出库作业服务。</param>
[ApiController]
[Route("api/wms/stock-out-tasks")]
public sealed class StockOutController(IStockOutService service) : ControllerBase
{
    /// <summary>出库作业列表。</summary>
    /// <param name="status">状态筛选（数值枚举，可空）。</param>
    /// <param name="warehouseId">仓库筛选（可空）。</param>
    /// <param name="sourceType">来源类型筛选（数值枚举，可空）。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>作业头扁平行列表。</returns>
    [HttpGet]
    public async Task<ApiResponse<IReadOnlyList<StockOutTaskRow>>> GetList(
        [FromQuery] int? status,
        [FromQuery] long? warehouseId,
        [FromQuery] int? sourceType,
        CancellationToken ct)
        => ApiResponse<IReadOnlyList<StockOutTaskRow>>.Ok(
            await service.ListAsync(status, warehouseId, sourceType, ct));

    /// <summary>出库作业详情（含明细与 FEFO 建议）。</summary>
    /// <param name="id">作业 Id。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>作业详情。</returns>
    [HttpGet("{id}")]
    public async Task<ApiResponse<StockOutTaskDetailRow>> GetDetail(long id, CancellationToken ct)
        => ApiResponse<StockOutTaskDetailRow>.Ok(await service.GetAsync(id, ct));

    /// <summary>新建出库作业。</summary>
    /// <param name="payload">新建入参（不含单号）。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>创建后的作业详情。</returns>
    [HttpPost]
    [AuditOperation(QlxModuleCodes.Wms, "WMS_STOCK_OUT_CREATE", "创建了出库作业单", TargetType = "StockOutTask")]
    public async Task<ApiResponse<StockOutTaskDetailRow>> Create(
        [FromBody] CreateStockOutTaskPayload payload, CancellationToken ct)
        => ApiResponse<StockOutTaskDetailRow>.Ok(await service.CreateAsync(payload, ct));

    /// <summary>拣货。</summary>
    /// <param name="id">作业 Id。</param>
    /// <param name="payload">拣货结果。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>作业详情。</returns>
    [HttpPost("{id}/pick")]
    [AuditOperation(QlxModuleCodes.Wms, "WMS_STOCK_OUT_PICK", "提交了出库拣货", TargetType = "StockOutTask", TargetIdParam = "id")]
    public async Task<ApiResponse<StockOutTaskDetailRow>> Pick(
        long id, [FromBody] PickStockOutTaskPayload payload, CancellationToken ct)
        => ApiResponse<StockOutTaskDetailRow>.Ok(await service.PickAsync(id, payload, ct));

    /// <summary>复核。</summary>
    /// <param name="id">作业 Id。</param>
    /// <param name="payload">复核结果。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>作业详情。</returns>
    [HttpPost("{id}/check")]
    [AuditOperation(QlxModuleCodes.Wms, "WMS_STOCK_OUT_CHECK", "完成了出库复核", TargetType = "StockOutTask", TargetIdParam = "id")]
    public async Task<ApiResponse<StockOutTaskDetailRow>> Check(
        long id, [FromBody] CheckStockOutTaskPayload payload, CancellationToken ct)
        => ApiResponse<StockOutTaskDetailRow>.Ok(await service.CheckAsync(id, payload, ct));

    /// <summary>完成作业。</summary>
    /// <param name="id">作业 Id。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>完成结果（含是否转大额审批）。</returns>
    [HttpPost("{id}/complete")]
    [AuditOperation(QlxModuleCodes.Wms, "WMS_STOCK_OUT_COMPLETE", "完成了出库作业", TargetType = "StockOutTask", TargetIdParam = "id")]
    public async Task<ApiResponse<StockOutCompleteResultRow>> Complete(long id, CancellationToken ct)
        => ApiResponse<StockOutCompleteResultRow>.Ok(await service.CompleteAsync(id, ct));

    /// <summary>取消作业。</summary>
    /// <param name="id">作业 Id。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>作业头。</returns>
    [HttpPost("{id}/cancel")]
    [AuditOperation(QlxModuleCodes.Wms, "WMS_STOCK_OUT_CANCEL", "取消了出库作业", TargetType = "StockOutTask", TargetIdParam = "id")]
    public async Task<ApiResponse<StockOutTaskRow>> Cancel(long id, CancellationToken ct)
        => ApiResponse<StockOutTaskRow>.Ok(await service.CancelAsync(id, ct));
}
