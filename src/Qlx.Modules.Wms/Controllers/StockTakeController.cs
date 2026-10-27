/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-26
 * 描述：盘点单 API（/api/wms/stock-takes）——列表/详情/开始/保存实盘/完成/取消
 */

using Microsoft.AspNetCore.Mvc;

using Qlx.Abstractions.Constants;
using Qlx.BuildingBlocks.Api;
using Qlx.BuildingBlocks.Audit;
using Qlx.Modules.Wms.Dtos;
using Qlx.Modules.Wms.Services;

namespace Qlx.Modules.Wms.Controllers;

/// <summary>
/// 盘点单控制器。盲盘详情由服务端裁剪账面量与差异（字段级安全过滤 B-3）；
/// 业务 Id 由全局 HashidsModelBinder 自动解码。
/// </summary>
/// <param name="service">库存盘点服务。</param>
[ApiController]
[Route("api/wms/stock-takes")]
public sealed class StockTakeController(IStockTakeService service) : ControllerBase
{
    /// <summary>盘点单列表。</summary>
    /// <param name="status">状态筛选（数值枚举，可空）。</param>
    /// <param name="warehouseId">仓库筛选（可空）。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>盘点单头扁平行列表。</returns>
    [HttpGet]
    public async Task<ApiResponse<IReadOnlyList<StockTakeRow>>> GetList(
        [FromQuery] int? status,
        [FromQuery] long? warehouseId,
        CancellationToken ct)
        => ApiResponse<IReadOnlyList<StockTakeRow>>.Ok(
            await service.ListAsync(status, warehouseId, ct));

    /// <summary>盘点单详情（含明细）。</summary>
    /// <param name="id">盘点单 Id。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>盘点单详情。</returns>
    [HttpGet("{id}")]
    public async Task<ApiResponse<StockTakeDetailRow>> GetDetail(long id, CancellationToken ct)
        => ApiResponse<StockTakeDetailRow>.Ok(await service.GetAsync(id, ct));

    /// <summary>开始盘点。</summary>
    /// <param name="id">盘点单 Id。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>盘点单头。</returns>
    [HttpPost("{id}/start")]
    [AuditOperation(QlxModuleCodes.Wms, "WMS_STOCK_TAKE_START", "开始了盘点", TargetType = "StockTake", TargetIdParam = "id")]
    public async Task<ApiResponse<StockTakeRow>> Start(long id, CancellationToken ct)
        => ApiResponse<StockTakeRow>.Ok(await service.StartAsync(id, ct));

    /// <summary>保存实盘数量。</summary>
    /// <param name="id">盘点单 Id。</param>
    /// <param name="payload">实盘结果。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>盘点单详情。</returns>
    [HttpPost("{id}/save-counts")]
    [AuditOperation(QlxModuleCodes.Wms, "WMS_STOCK_TAKE_SAVE_COUNTS", "录入了盘点实盘数量", TargetType = "StockTake", TargetIdParam = "id")]
    public async Task<ApiResponse<StockTakeDetailRow>> SaveCounts(
        long id, [FromBody] SaveStockTakeCountsPayload payload, CancellationToken ct)
        => ApiResponse<StockTakeDetailRow>.Ok(await service.SaveCountsAsync(id, payload, ct));

    /// <summary>完成盘点（初盘有差异自动生成复盘单；复盘完成解冻并生成盘盈亏草稿）。</summary>
    /// <param name="id">盘点单 Id。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>完成结果。</returns>
    [HttpPost("{id}/finish")]
    [AuditOperation(QlxModuleCodes.Wms, "WMS_STOCK_TAKE_FINISH", "完成了盘点", TargetType = "StockTake", TargetIdParam = "id")]
    public async Task<ApiResponse<StockTakeFinishResultRow>> Finish(long id, CancellationToken ct)
        => ApiResponse<StockTakeFinishResultRow>.Ok(await service.FinishAsync(id, ct));

    /// <summary>取消盘点（解冻一次）。</summary>
    /// <param name="id">盘点单 Id。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>盘点单头。</returns>
    [HttpPost("{id}/cancel")]
    [AuditOperation(QlxModuleCodes.Wms, "WMS_STOCK_TAKE_CANCEL", "取消了盘点", TargetType = "StockTake", TargetIdParam = "id")]
    public async Task<ApiResponse<StockTakeRow>> Cancel(long id, CancellationToken ct)
        => ApiResponse<StockTakeRow>.Ok(await service.CancelAsync(id, ct));
}
