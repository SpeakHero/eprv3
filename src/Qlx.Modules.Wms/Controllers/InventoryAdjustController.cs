/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-26
 * 描述：盘盈盘亏 API（/api/wms/inventory-adjusts）——列表/详情/新建/提交审批
 */

using Microsoft.AspNetCore.Mvc;

using Qlx.Abstractions.Constants;
using Qlx.BuildingBlocks.Api;
using Qlx.BuildingBlocks.Audit;
using Qlx.Modules.Wms.Dtos;
using Qlx.Modules.Wms.Services;

namespace Qlx.Modules.Wms.Controllers;

/// <summary>
/// 盘盈盘亏控制器。审批前不产生任何库存命令；通过/驳回由审批完成事件驱动（Task 21 Applier）。
/// 业务 Id 由全局 HashidsModelBinder 自动解码。
/// </summary>
/// <param name="service">盘盈盘亏服务。</param>
[ApiController]
[Route("api/wms/inventory-adjusts")]
public sealed class InventoryAdjustController(IInventoryAdjustService service) : ControllerBase
{
    /// <summary>盘盈盘亏单列表。</summary>
    /// <param name="type">类型筛选（数值枚举，可空）。</param>
    /// <param name="status">状态筛选（数值枚举，可空）。</param>
    /// <param name="warehouseId">仓库筛选（可空）。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>单头扁平行列表。</returns>
    [HttpGet]
    public async Task<ApiResponse<IReadOnlyList<InventoryAdjustRow>>> GetList(
        [FromQuery] int? type,
        [FromQuery] int? status,
        [FromQuery] long? warehouseId,
        CancellationToken ct)
        => ApiResponse<IReadOnlyList<InventoryAdjustRow>>.Ok(
            await service.ListAsync(type, status, warehouseId, ct));

    /// <summary>盘盈盘亏单详情（含明细）。</summary>
    /// <param name="id">单据 Id。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>单头 + 扁平明细。</returns>
    [HttpGet("{id}")]
    public async Task<ApiResponse<InventoryAdjustDetailRow>> GetDetail(long id, CancellationToken ct)
        => ApiResponse<InventoryAdjustDetailRow>.Ok(await service.GetAsync(id, ct));

    /// <summary>新建盘盈盘亏单（草稿）。</summary>
    /// <param name="payload">建单入参。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>新建单详情。</returns>
    [HttpPost]
    [AuditOperation(QlxModuleCodes.Wms, "WMS_INVENTORY_ADJUST_CREATE", "新建了盘盈盘亏单", TargetType = "InventoryAdjust")]
    public async Task<ApiResponse<InventoryAdjustDetailRow>> Create(
        [FromBody] CreateInventoryAdjustPayload payload, CancellationToken ct)
        => ApiResponse<InventoryAdjustDetailRow>.Ok(await service.CreateAsync(payload, ct));

    /// <summary>提交审批（1 草稿 → 2 待审批）。</summary>
    /// <param name="id">单据 Id。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>提交后的单头。</returns>
    [HttpPost("{id}/submit")]
    [AuditOperation(QlxModuleCodes.Wms, "WMS_INVENTORY_ADJUST_SUBMIT", "提交了盘盈盘亏审批", TargetType = "InventoryAdjust", TargetIdParam = "id")]
    public async Task<ApiResponse<InventoryAdjustRow>> Submit(long id, CancellationToken ct)
        => ApiResponse<InventoryAdjustRow>.Ok(await service.SubmitAsync(id, ct));
}
