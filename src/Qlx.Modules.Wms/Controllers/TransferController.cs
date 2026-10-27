/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-26
 * 描述：调拨与在途 API（/api/wms/transfers）——Controller 仅参数绑定 + 服务调用 + ApiResponse 包装
 */

using Microsoft.AspNetCore.Mvc;

using Qlx.Abstractions.Constants;
using Qlx.BuildingBlocks.Api;
using Qlx.BuildingBlocks.Audit;
using Qlx.Modules.Wms.Dtos;
using Qlx.Modules.Wms.Services;

namespace Qlx.Modules.Wms.Controllers;

/// <summary>
/// 物资调拨控制器。调拨单号由后端生成，前端不传码（N-1）；业务 Id 由全局 HashidsModelBinder 自动解码。
/// </summary>
/// <param name="service">调拨与在途服务。</param>
[ApiController]
[Route("api/wms/transfers")]
public sealed class TransferController(ITransferService service) : ControllerBase
{
    /// <summary>调拨单列表。</summary>
    /// <param name="status">状态筛选（数值枚举，可空）。</param>
    /// <param name="warehouseId">仓库筛选（调出或调入命中，可空）。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>调拨单头扁平行列表。</returns>
    [HttpGet]
    public async Task<ApiResponse<IReadOnlyList<StockTransferRow>>> GetList(
        [FromQuery] int? status,
        [FromQuery] long? warehouseId,
        CancellationToken ct)
        => ApiResponse<IReadOnlyList<StockTransferRow>>.Ok(
            await service.ListAsync(status, warehouseId, ct));

    /// <summary>在途库存列表（扁平行）。</summary>
    /// <param name="status">在途状态筛选（数值枚举，可空）。</param>
    /// <param name="warehouseId">仓库筛选（调出或调入命中，可空）。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>在途库存扁平行列表。</returns>
    [HttpGet("in-transit")]
    public async Task<ApiResponse<IReadOnlyList<InTransitStockRow>>> GetInTransit(
        [FromQuery] int? status,
        [FromQuery] long? warehouseId,
        CancellationToken ct)
        => ApiResponse<IReadOnlyList<InTransitStockRow>>.Ok(
            await service.ListInTransitAsync(status, warehouseId, ct));

    /// <summary>调拨单详情（含明细）。</summary>
    /// <param name="id">调拨单 Id。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>调拨单详情。</returns>
    [HttpGet("{id}")]
    public async Task<ApiResponse<StockTransferDetailRow>> GetDetail(long id, CancellationToken ct)
        => ApiResponse<StockTransferDetailRow>.Ok(await service.GetAsync(id, ct));

    /// <summary>新建调拨单（新建即提交审批）。</summary>
    /// <param name="payload">新建入参（不含单号）。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>创建后的调拨单详情。</returns>
    [HttpPost]
    [AuditOperation(QlxModuleCodes.Wms, "WMS_TRANSFER_CREATE", "创建了物资调拨单", TargetType = "StockTransfer")]
    public async Task<ApiResponse<StockTransferDetailRow>> Create(
        [FromBody] CreateStockTransferPayload payload, CancellationToken ct)
        => ApiResponse<StockTransferDetailRow>.Ok(await service.CreateAsync(payload, ct));

    /// <summary>发货。</summary>
    /// <param name="id">调拨单 Id。</param>
    /// <param name="payload">发货结果。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>调拨单详情。</returns>
    [HttpPost("{id}/ship")]
    [AuditOperation(QlxModuleCodes.Wms, "WMS_TRANSFER_SHIP", "提交了调拨发货", TargetType = "StockTransfer", TargetIdParam = "id")]
    public async Task<ApiResponse<StockTransferDetailRow>> Ship(
        long id, [FromBody] ShipStockTransferPayload payload, CancellationToken ct)
        => ApiResponse<StockTransferDetailRow>.Ok(await service.ShipAsync(id, payload, ct));

    /// <summary>收货。</summary>
    /// <param name="id">调拨单 Id。</param>
    /// <param name="payload">收货结果。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>调拨单详情。</returns>
    [HttpPost("{id}/receive")]
    [AuditOperation(QlxModuleCodes.Wms, "WMS_TRANSFER_RECEIVE", "完成了调拨收货", TargetType = "StockTransfer", TargetIdParam = "id")]
    public async Task<ApiResponse<StockTransferDetailRow>> Receive(
        long id, [FromBody] ReceiveStockTransferPayload payload, CancellationToken ct)
        => ApiResponse<StockTransferDetailRow>.Ok(await service.ReceiveAsync(id, payload, ct));

    /// <summary>作废。</summary>
    /// <param name="id">调拨单 Id。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>调拨单头。</returns>
    [HttpPost("{id}/void")]
    [AuditOperation(QlxModuleCodes.Wms, "WMS_TRANSFER_VOID", "作废了物资调拨单", TargetType = "StockTransfer", TargetIdParam = "id")]
    public async Task<ApiResponse<StockTransferRow>> Void(long id, CancellationToken ct)
        => ApiResponse<StockTransferRow>.Ok(await service.VoidAsync(id, ct));
}
