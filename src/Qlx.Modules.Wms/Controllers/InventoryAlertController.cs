/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-26
 * 描述：库存预警 API（/api/wms/inventory-alerts）——列表筛选 + 处理/忽略回填
 */

using Microsoft.AspNetCore.Mvc;

using Qlx.Abstractions.Constants;
using Qlx.BuildingBlocks.Api;
using Qlx.BuildingBlocks.Audit;
using Qlx.Modules.Wms.Dtos;
using Qlx.Modules.Wms.Services;

namespace Qlx.Modules.Wms.Controllers;

/// <summary>
/// 库存预警控制器。预警只由扫描任务生成，接口仅提供查询与处理回填；
/// 业务 Id 由全局 HashidsModelBinder 自动解码。
/// </summary>
/// <param name="service">库存预警服务。</param>
[ApiController]
[Route("api/wms/inventory-alerts")]
public sealed class InventoryAlertController(IInventoryAlertService service) : ControllerBase
{
    /// <summary>预警列表。</summary>
    /// <param name="alertType">类型筛选（数值枚举，可空）。</param>
    /// <param name="status">状态筛选（数值枚举，可空）。</param>
    /// <param name="warehouseId">仓库筛选（可空）。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>预警扁平行列表。</returns>
    [HttpGet]
    public async Task<ApiResponse<IReadOnlyList<InventoryAlertRow>>> GetList(
        [FromQuery] int? alertType,
        [FromQuery] int? status,
        [FromQuery] long? warehouseId,
        CancellationToken ct)
        => ApiResponse<IReadOnlyList<InventoryAlertRow>>.Ok(
            await service.ListAsync(alertType, status, warehouseId, ct));

    /// <summary>处理预警（1 待处理 → 2 已处理，处理说明必填）。</summary>
    /// <param name="id">预警 Id。</param>
    /// <param name="payload">处理入参。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>处理后的预警行。</returns>
    [HttpPost("{id}/handle")]
    [AuditOperation(QlxModuleCodes.Wms, "WMS_INVENTORY_ALERT_HANDLE", "处理了库存预警", TargetType = "InventoryAlert", TargetIdParam = "id")]
    public async Task<ApiResponse<InventoryAlertRow>> Handle(
        long id, [FromBody] HandleInventoryAlertPayload payload, CancellationToken ct)
        => ApiResponse<InventoryAlertRow>.Ok(await service.HandleAsync(id, payload, ct));

    /// <summary>忽略预警（1 待处理 → 3 已忽略）。</summary>
    /// <param name="id">预警 Id。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>忽略后的预警行。</returns>
    [HttpPost("{id}/ignore")]
    [AuditOperation(QlxModuleCodes.Wms, "WMS_INVENTORY_ALERT_IGNORE", "忽略了库存预警", TargetType = "InventoryAlert", TargetIdParam = "id")]
    public async Task<ApiResponse<InventoryAlertRow>> Ignore(long id, CancellationToken ct)
        => ApiResponse<InventoryAlertRow>.Ok(await service.IgnoreAsync(id, ct));
}
