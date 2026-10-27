/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-26
 * 描述：库存预警服务契约（列表筛选 + 处理/忽略回填）
 */

using Qlx.Modules.Wms.Dtos;

namespace Qlx.Modules.Wms.Services;

/// <summary>库存预警服务契约。</summary>
public interface IInventoryAlertService
{
    /// <summary>预警列表（类型/状态/仓库筛选）。</summary>
    /// <param name="alertType">类型数值枚举筛选（可空）。</param>
    /// <param name="status">状态数值枚举筛选（可空）。</param>
    /// <param name="warehouseId">仓库筛选（可空）。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>预警扁平行列表。</returns>
    Task<IReadOnlyList<InventoryAlertRow>> ListAsync(
        int? alertType, int? status, long? warehouseId, CancellationToken ct);

    /// <summary>处理预警（1 待处理 → 2 已处理，处理说明必填）。</summary>
    /// <param name="id">预警 Id。</param>
    /// <param name="payload">处理入参。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>处理后的预警行。</returns>
    Task<InventoryAlertRow> HandleAsync(long id, HandleInventoryAlertPayload payload, CancellationToken ct);

    /// <summary>忽略预警（1 待处理 → 3 已忽略）。</summary>
    /// <param name="id">预警 Id。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>忽略后的预警行。</returns>
    Task<InventoryAlertRow> IgnoreAsync(long id, CancellationToken ct);
}
