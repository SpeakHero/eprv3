/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-26
 * 描述：盘点计划 API（/api/wms/stock-take-plans）——Controller 仅参数绑定 + 服务调用 + ApiResponse 包装
 */

using Microsoft.AspNetCore.Mvc;

using Qlx.Abstractions.Constants;
using Qlx.BuildingBlocks.Api;
using Qlx.BuildingBlocks.Audit;
using Qlx.Modules.Wms.Dtos;
using Qlx.Modules.Wms.Services;

namespace Qlx.Modules.Wms.Controllers;

/// <summary>
/// 盘点计划控制器。计划号由后端生成，前端不传码（N-1）；业务 Id 由全局 HashidsModelBinder 自动解码。
/// </summary>
/// <param name="service">库存盘点服务。</param>
[ApiController]
[Route("api/wms/stock-take-plans")]
public sealed class StockTakePlanController(IStockTakeService service) : ControllerBase
{
    /// <summary>盘点计划列表。</summary>
    /// <param name="scopeType">范围类型筛选（数值枚举，可空）。</param>
    /// <param name="warehouseId">仓库筛选（可空）。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>计划扁平行列表。</returns>
    [HttpGet]
    public async Task<ApiResponse<IReadOnlyList<StockTakePlanRow>>> GetList(
        [FromQuery] int? scopeType,
        [FromQuery] long? warehouseId,
        CancellationToken ct)
        => ApiResponse<IReadOnlyList<StockTakePlanRow>>.Ok(
            await service.ListPlansAsync(scopeType, warehouseId, ct));

    /// <summary>新建盘点计划。</summary>
    /// <param name="payload">新建入参（不含计划号）。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>创建后的计划。</returns>
    [HttpPost]
    [AuditOperation(QlxModuleCodes.Wms, "WMS_STOCK_TAKE_PLAN_CREATE", "创建了盘点计划", TargetType = "StockTakePlan")]
    public async Task<ApiResponse<StockTakePlanRow>> Create(
        [FromBody] CreateStockTakePlanPayload payload, CancellationToken ct)
        => ApiResponse<StockTakePlanRow>.Ok(await service.CreatePlanAsync(payload, ct));

    /// <summary>生成盘点单（按范围快照台账并冻结）。</summary>
    /// <param name="id">盘点计划 Id。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>生成后的盘点单详情。</returns>
    [HttpPost("{id}/generate")]
    [AuditOperation(QlxModuleCodes.Wms, "WMS_STOCK_TAKE_GENERATE", "生成了盘点单", TargetType = "StockTakePlan", TargetIdParam = "id")]
    public async Task<ApiResponse<StockTakeDetailRow>> Generate(long id, CancellationToken ct)
        => ApiResponse<StockTakeDetailRow>.Ok(await service.GenerateAsync(id, ct));
}
