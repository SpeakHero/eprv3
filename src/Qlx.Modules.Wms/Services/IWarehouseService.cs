/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-26 10:00:00 +08:00 中国标准时间 星期一
 * 描述：仓库/库区/库位主数据服务契约（模块内）
 */

using Qlx.Modules.Wms.Dtos;

namespace Qlx.Modules.Wms.Services;

/// <summary>仓库主数据服务。</summary>
public interface IWarehouseService
{
    /// <summary>仓库扁平列表（层级/排序呈现由前端决定）。</summary>
    Task<IReadOnlyList<WarehouseRow>> ListAsync(CancellationToken ct);

    /// <summary>仓库详情。</summary>
    Task<WarehouseRow> GetAsync(long id, CancellationToken ct);

    /// <summary>新建仓库（编码由后端生成，N-1）。</summary>
    Task<WarehouseRow> CreateAsync(CreateWarehousePayload payload, CancellationToken ct);

    /// <summary>更新仓库（编码不可修改）。</summary>
    Task<WarehouseRow> UpdateAsync(long id, UpdateWarehousePayload payload, CancellationToken ct);

    /// <summary>启用/停用仓库（停用需前置校验通过后转审批挂起）。</summary>
    Task<WarehouseRow> ToggleAsync(long id, CancellationToken ct);

    /// <summary>应用仓库停用审批终态（供 WmsApprovalApplier 调用，幂等）。</summary>
    /// <param name="id">仓库 Id。</param>
    /// <param name="approved">true 审批通过（停用）；false 驳回（保持启用）。</param>
    /// <param name="ct">取消令牌。</param>
    Task ApplyDisableApprovalAsync(long id, bool approved, CancellationToken ct);

    /// <summary>软删仓库（存在库区或库位时拒绝）。</summary>
    Task DeleteAsync(long id, CancellationToken ct);

    /// <summary>按仓库列出库区（扁平列表）。</summary>
    Task<IReadOnlyList<ZoneRow>> ListZonesAsync(long warehouseId, CancellationToken ct);

    /// <summary>新建库区。</summary>
    Task<ZoneRow> CreateZoneAsync(long warehouseId, CreateZonePayload payload, CancellationToken ct);

    /// <summary>更新库区。</summary>
    Task<ZoneRow> UpdateZoneAsync(long zoneId, UpdateZonePayload payload, CancellationToken ct);

    /// <summary>启用/停用库区。</summary>
    Task<ZoneRow> ToggleZoneAsync(long zoneId, CancellationToken ct);

    /// <summary>软删库区（存在库位时拒绝）。</summary>
    Task DeleteZoneAsync(long zoneId, CancellationToken ct);

    /// <summary>按仓库列出库位（扁平列表）。</summary>
    Task<IReadOnlyList<LocationRow>> ListLocationsAsync(long warehouseId, CancellationToken ct);

    /// <summary>新建库位（库区必须属于该仓库）。</summary>
    Task<LocationRow> CreateLocationAsync(
        long warehouseId, long zoneId, CreateLocationPayload payload, CancellationToken ct);

    /// <summary>更新库位。</summary>
    Task<LocationRow> UpdateLocationAsync(long locationId, UpdateLocationPayload payload, CancellationToken ct);

    /// <summary>启用/停用库位。</summary>
    Task<LocationRow> ToggleLocationAsync(long locationId, CancellationToken ct);

    /// <summary>软删库位（存在未完成作业单引用时拒绝）。</summary>
    Task DeleteLocationAsync(long locationId, CancellationToken ct);
}
