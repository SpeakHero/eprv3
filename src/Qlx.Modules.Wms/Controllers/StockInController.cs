/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-26 16:40:00 +08:00 中国标准时间 星期一
 * 描述：入库作业 API（/api/wms/stock-in-tasks）——Controller 仅参数绑定 + 服务调用 + ApiResponse 包装
 */

using Microsoft.AspNetCore.Mvc;

using Qlx.Abstractions.Constants;
using Qlx.BuildingBlocks.Api;
using Qlx.BuildingBlocks.Audit;
using Qlx.Modules.Wms.Dtos;
using Qlx.Modules.Wms.Services;

namespace Qlx.Modules.Wms.Controllers;

/// <summary>
/// 入库作业控制器。作业单号由后端生成，前端不传码（N-1）；业务 Id 由全局 HashidsModelBinder 自动解码。
/// </summary>
/// <param name="service">入库作业服务。</param>
[ApiController]
[Route("api/wms/stock-in-tasks")]
public sealed class StockInController(IStockInService service) : ControllerBase
{
    /// <summary>入库作业列表。</summary>
    /// <param name="status">状态筛选（数值枚举，可空）。</param>
    /// <param name="warehouseId">仓库筛选（可空）。</param>
    /// <param name="sourceType">来源类型筛选（数值枚举，可空）。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>作业头扁平行列表。</returns>
    [HttpGet]
    public async Task<ApiResponse<IReadOnlyList<StockInTaskRow>>> GetList(
        [FromQuery] int? status,
        [FromQuery] long? warehouseId,
        [FromQuery] int? sourceType,
        CancellationToken ct)
        => ApiResponse<IReadOnlyList<StockInTaskRow>>.Ok(
            await service.ListAsync(status, warehouseId, sourceType, ct));

    /// <summary>入库作业详情（含明细）。</summary>
    /// <param name="id">作业 Id。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>作业详情。</returns>
    [HttpGet("{id}")]
    public async Task<ApiResponse<StockInTaskDetailRow>> GetDetail(long id, CancellationToken ct)
        => ApiResponse<StockInTaskDetailRow>.Ok(await service.GetAsync(id, ct));

    /// <summary>新建入库作业。</summary>
    /// <param name="payload">新建入参（不含单号）。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>创建后的作业详情。</returns>
    [HttpPost]
    [AuditOperation(QlxModuleCodes.Wms, "WMS_STOCK_IN_CREATE", "创建了入库作业单", TargetType = "StockInTask")]
    public async Task<ApiResponse<StockInTaskDetailRow>> Create(
        [FromBody] CreateStockInTaskPayload payload, CancellationToken ct)
        => ApiResponse<StockInTaskDetailRow>.Ok(await service.CreateAsync(payload, ct));

    /// <summary>开始作业。</summary>
    /// <param name="id">作业 Id。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>作业头。</returns>
    [HttpPost("{id}/start")]
    [AuditOperation(QlxModuleCodes.Wms, "WMS_STOCK_IN_START", "开始了入库作业", TargetType = "StockInTask", TargetIdParam = "id")]
    public async Task<ApiResponse<StockInTaskRow>> Start(long id, CancellationToken ct)
        => ApiResponse<StockInTaskRow>.Ok(await service.StartAsync(id, ct));

    /// <summary>完成作业。</summary>
    /// <param name="id">作业 Id。</param>
    /// <param name="payload">清点/确认结果。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>完成后的作业详情。</returns>
    [HttpPost("{id}/complete")]
    [AuditOperation(QlxModuleCodes.Wms, "WMS_STOCK_IN_COMPLETE", "完成了入库作业", TargetType = "StockInTask", TargetIdParam = "id")]
    public async Task<ApiResponse<StockInTaskDetailRow>> Complete(
        long id, [FromBody] CompleteStockInTaskPayload payload, CancellationToken ct)
        => ApiResponse<StockInTaskDetailRow>.Ok(await service.CompleteAsync(id, payload, ct));

    /// <summary>取消作业。</summary>
    /// <param name="id">作业 Id。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>作业头。</returns>
    [HttpPost("{id}/cancel")]
    [AuditOperation(QlxModuleCodes.Wms, "WMS_STOCK_IN_CANCEL", "取消了入库作业", TargetType = "StockInTask", TargetIdParam = "id")]
    public async Task<ApiResponse<StockInTaskRow>> Cancel(long id, CancellationToken ct)
        => ApiResponse<StockInTaskRow>.Ok(await service.CancelAsync(id, ct));
}
