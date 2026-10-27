/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-26 10:00:00 +08:00 中国标准时间 星期一
 * 描述：仓库/库区/库位主数据 API（/api/wms/warehouses）——Controller 仅参数绑定 + 服务调用 + ApiResponse 包装
 */

using Microsoft.AspNetCore.Mvc;

using Qlx.Abstractions.Constants;
using Qlx.BuildingBlocks.Api;
using Qlx.BuildingBlocks.Audit;
using Qlx.Modules.Wms.Dtos;
using Qlx.Modules.Wms.Services;

namespace Qlx.Modules.Wms.Controllers;

/// <summary>
/// 仓库主数据控制器。业务 Id 由全局 HashidsModelBinder 自动解码；仓库编码由后端生成，前端不传码（N-1）。
/// </summary>
/// <param name="service">仓库主数据服务。</param>
[ApiController]
[Route("api/wms/warehouses")]
public sealed class WarehouseController(IWarehouseService service) : ControllerBase
{
    /// <summary>仓库扁平列表。</summary>
    /// <param name="ct">取消令牌。</param>
    /// <returns>仓库扁平行列表。</returns>
    [HttpGet]
    public async Task<ApiResponse<IReadOnlyList<WarehouseRow>>> GetList(CancellationToken ct)
        => ApiResponse<IReadOnlyList<WarehouseRow>>.Ok(await service.ListAsync(ct));

    /// <summary>仓库详情。</summary>
    /// <param name="id">仓库 Id。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>仓库扁平行。</returns>
    [HttpGet("{id}")]
    public async Task<ApiResponse<WarehouseRow>> GetDetail(long id, CancellationToken ct)
        => ApiResponse<WarehouseRow>.Ok(await service.GetAsync(id, ct));

    /// <summary>新建仓库。</summary>
    /// <param name="payload">新建入参（不含编码）。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>创建后的仓库扁平行。</returns>
    [HttpPost]
    [AuditOperation(QlxModuleCodes.Wms, "WMS_WAREHOUSE_CREATE", "创建了仓库", TargetType = "Warehouse")]
    public async Task<ApiResponse<WarehouseRow>> Create(
        [FromBody] CreateWarehousePayload payload, CancellationToken ct)
        => ApiResponse<WarehouseRow>.Ok(await service.CreateAsync(payload, ct));

    /// <summary>更新仓库。</summary>
    /// <param name="id">仓库 Id。</param>
    /// <param name="payload">更新入参。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>更新后的仓库扁平行。</returns>
    [HttpPut("{id}")]
    [AuditOperation(QlxModuleCodes.Wms, "WMS_WAREHOUSE_UPDATE", "修改了仓库", TargetType = "Warehouse", TargetIdParam = "id")]
    public async Task<ApiResponse<WarehouseRow>> Update(
        long id, [FromBody] UpdateWarehousePayload payload, CancellationToken ct)
        => ApiResponse<WarehouseRow>.Ok(await service.UpdateAsync(id, payload, ct));

    /// <summary>启用/停用仓库。</summary>
    /// <param name="id">仓库 Id。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>变更后的仓库扁平行。</returns>
    [HttpPost("{id}/toggle")]
    [AuditOperation(QlxModuleCodes.Wms, "WMS_WAREHOUSE_TOGGLE", "变更了仓库启停状态", TargetType = "Warehouse", TargetIdParam = "id")]
    public async Task<ApiResponse<WarehouseRow>> Toggle(long id, CancellationToken ct)
        => ApiResponse<WarehouseRow>.Ok(await service.ToggleAsync(id, ct));

    /// <summary>删除仓库（软删）。</summary>
    /// <param name="id">仓库 Id。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>空响应。</returns>
    [HttpDelete("{id}")]
    [AuditOperation(QlxModuleCodes.Wms, "WMS_WAREHOUSE_DELETE", "删除了仓库", TargetType = "Warehouse", TargetIdParam = "id")]
    public async Task<ApiResponse<object?>> Delete(long id, CancellationToken ct)
    {
        await service.DeleteAsync(id, ct);
        return ApiResponse<object?>.Ok(null);
    }

    /// <summary>按仓库查询库区列表。</summary>
    /// <param name="id">仓库 Id。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>库区扁平行列表。</returns>
    [HttpGet("{id}/zones")]
    public async Task<ApiResponse<IReadOnlyList<ZoneRow>>> GetZones(long id, CancellationToken ct)
        => ApiResponse<IReadOnlyList<ZoneRow>>.Ok(await service.ListZonesAsync(id, ct));

    /// <summary>新建库区。</summary>
    /// <param name="id">仓库 Id。</param>
    /// <param name="payload">新建入参。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>创建后的库区扁平行。</returns>
    [HttpPost("{id}/zones")]
    [AuditOperation(QlxModuleCodes.Wms, "WMS_ZONE_CREATE", "创建了库区", TargetType = "WarehouseZone")]
    public async Task<ApiResponse<ZoneRow>> CreateZone(
        long id, [FromBody] CreateZonePayload payload, CancellationToken ct)
        => ApiResponse<ZoneRow>.Ok(await service.CreateZoneAsync(id, payload, ct));

    /// <summary>更新库区。</summary>
    /// <param name="zoneId">库区 Id。</param>
    /// <param name="payload">更新入参。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>更新后的库区扁平行。</returns>
    [HttpPut("zones/{zoneId}")]
    [AuditOperation(QlxModuleCodes.Wms, "WMS_ZONE_UPDATE", "修改了库区", TargetType = "WarehouseZone", TargetIdParam = "zoneId")]
    public async Task<ApiResponse<ZoneRow>> UpdateZone(
        long zoneId, [FromBody] UpdateZonePayload payload, CancellationToken ct)
        => ApiResponse<ZoneRow>.Ok(await service.UpdateZoneAsync(zoneId, payload, ct));

    /// <summary>启用/停用库区。</summary>
    /// <param name="zoneId">库区 Id。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>变更后的库区扁平行。</returns>
    [HttpPost("zones/{zoneId}/toggle")]
    [AuditOperation(QlxModuleCodes.Wms, "WMS_ZONE_TOGGLE", "变更了库区启停状态", TargetType = "WarehouseZone", TargetIdParam = "zoneId")]
    public async Task<ApiResponse<ZoneRow>> ToggleZone(long zoneId, CancellationToken ct)
        => ApiResponse<ZoneRow>.Ok(await service.ToggleZoneAsync(zoneId, ct));

    /// <summary>软删库区（存在库位时拒绝）。</summary>
    /// <param name="zoneId">库区 Id。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>空响应。</returns>
    [HttpDelete("zones/{zoneId}")]
    [AuditOperation(QlxModuleCodes.Wms, "WMS_ZONE_DELETE", "删除了库区", TargetType = "WarehouseZone", TargetIdParam = "zoneId")]
    public async Task<ApiResponse<object?>> DeleteZone(long zoneId, CancellationToken ct)
    {
        await service.DeleteZoneAsync(zoneId, ct);
        return ApiResponse<object?>.Ok(null);
    }

    /// <summary>按仓库查询库位列表。</summary>
    /// <param name="id">仓库 Id。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>库位扁平行列表。</returns>
    [HttpGet("{id}/locations")]
    public async Task<ApiResponse<IReadOnlyList<LocationRow>>> GetLocations(long id, CancellationToken ct)
        => ApiResponse<IReadOnlyList<LocationRow>>.Ok(await service.ListLocationsAsync(id, ct));

    /// <summary>新建库位。</summary>
    /// <param name="id">仓库 Id。</param>
    /// <param name="zoneId">库区 Id（查询参数）。</param>
    /// <param name="payload">新建入参。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>创建后的库位扁平行。</returns>
    [HttpPost("{id}/locations")]
    [AuditOperation(QlxModuleCodes.Wms, "WMS_LOCATION_CREATE", "创建了库位", TargetType = "WarehouseLocation")]
    public async Task<ApiResponse<LocationRow>> CreateLocation(
        long id, [FromQuery] long zoneId, [FromBody] CreateLocationPayload payload, CancellationToken ct)
        => ApiResponse<LocationRow>.Ok(await service.CreateLocationAsync(id, zoneId, payload, ct));

    /// <summary>更新库位。</summary>
    /// <param name="locationId">库位 Id。</param>
    /// <param name="payload">更新入参。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>更新后的库位扁平行。</returns>
    [HttpPut("locations/{locationId}")]
    [AuditOperation(QlxModuleCodes.Wms, "WMS_LOCATION_UPDATE", "修改了库位", TargetType = "WarehouseLocation", TargetIdParam = "locationId")]
    public async Task<ApiResponse<LocationRow>> UpdateLocation(
        long locationId, [FromBody] UpdateLocationPayload payload, CancellationToken ct)
        => ApiResponse<LocationRow>.Ok(await service.UpdateLocationAsync(locationId, payload, ct));

    /// <summary>启用/停用库位。</summary>
    /// <param name="locationId">库位 Id。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>变更后的库位扁平行。</returns>
    [HttpPost("locations/{locationId}/toggle")]
    [AuditOperation(QlxModuleCodes.Wms, "WMS_LOCATION_TOGGLE", "变更了库位启停状态", TargetType = "WarehouseLocation", TargetIdParam = "locationId")]
    public async Task<ApiResponse<LocationRow>> ToggleLocation(long locationId, CancellationToken ct)
        => ApiResponse<LocationRow>.Ok(await service.ToggleLocationAsync(locationId, ct));

    /// <summary>软删库位（存在未完成作业单引用时拒绝）。</summary>
    /// <param name="locationId">库位 Id。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>空响应。</returns>
    [HttpDelete("locations/{locationId}")]
    [AuditOperation(QlxModuleCodes.Wms, "WMS_LOCATION_DELETE", "删除了库位", TargetType = "WarehouseLocation", TargetIdParam = "locationId")]
    public async Task<ApiResponse<object?>> DeleteLocation(long locationId, CancellationToken ct)
    {
        await service.DeleteLocationAsync(locationId, ct);
        return ApiResponse<object?>.Ok(null);
    }
}
