/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-26
 * 描述：调拨与在途服务契约（Controller 仅依赖本接口；审批应用供 WmsApprovalApplier 调用）
 */

using Qlx.Modules.Wms.Dtos;

namespace Qlx.Modules.Wms.Services;

/// <summary>调拨与在途服务。</summary>
public interface ITransferService
{
    /// <summary>调拨单列表。</summary>
    /// <param name="status">状态筛选（数值枚举，可空）。</param>
    /// <param name="warehouseId">仓库筛选（调出或调入命中，可空）。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>调拨单头扁平行列表。</returns>
    Task<IReadOnlyList<StockTransferRow>> ListAsync(
        int? status, long? warehouseId, CancellationToken ct);

    /// <summary>调拨单详情（含明细）。</summary>
    /// <param name="id">调拨单 Id。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>调拨单详情。</returns>
    Task<StockTransferDetailRow> GetAsync(long id, CancellationToken ct);

    /// <summary>新建调拨单（单号后端生成；同事务发起调拨审批）。</summary>
    /// <param name="payload">新建入参。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>创建后的调拨单详情。</returns>
    Task<StockTransferDetailRow> CreateAsync(CreateStockTransferPayload payload, CancellationToken ct);

    /// <summary>发货（已生效 → 调出已发货；同事务写 Outbox 与在途行）。</summary>
    /// <param name="id">调拨单 Id。</param>
    /// <param name="payload">发货结果。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>调拨单详情。</returns>
    Task<StockTransferDetailRow> ShipAsync(
        long id, ShipStockTransferPayload payload, CancellationToken ct);

    /// <summary>收货（调出已发货 → 调入已收货；同事务写 Outbox 并关闭在途行）。</summary>
    /// <param name="id">调拨单 Id。</param>
    /// <param name="payload">收货结果。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>调拨单详情。</returns>
    Task<StockTransferDetailRow> ReceiveAsync(
        long id, ReceiveStockTransferPayload payload, CancellationToken ct);

    /// <summary>作废（待审批/已生效 → 已作废）。</summary>
    /// <param name="id">调拨单 Id。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>调拨单头。</returns>
    Task<StockTransferRow> VoidAsync(long id, CancellationToken ct);

    /// <summary>在途库存列表（扁平行；超期由前端计算）。</summary>
    /// <param name="status">在途状态筛选（可空）。</param>
    /// <param name="warehouseId">仓库筛选（调出或调入命中，可空）。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>在途库存扁平行列表。</returns>
    Task<IReadOnlyList<InTransitStockRow>> ListInTransitAsync(
        int? status, long? warehouseId, CancellationToken ct);

    /// <summary>应用审批终态（供 WmsApprovalApplier 调用，幂等）。</summary>
    /// <param name="id">调拨单 Id。</param>
    /// <param name="approved">true 审批通过（→ 已生效）；false 驳回（→ 已作废）。</param>
    /// <param name="ct">取消令牌。</param>
    Task ApplyApprovalAsync(long id, bool approved, CancellationToken ct);
}
