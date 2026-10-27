/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-26
 * 描述：盘盈盘亏服务契约——读写端点 + 审批终态应用接缝（供 Task 21 WmsApprovalApplier 调用）
 */

using Qlx.Modules.Wms.Dtos;

namespace Qlx.Modules.Wms.Services;

/// <summary>盘盈盘亏服务。</summary>
public interface IInventoryAdjustService
{
    /// <summary>盘盈盘亏单列表。</summary>
    /// <param name="type">类型筛选（数值枚举，可空）。</param>
    /// <param name="status">状态筛选（数值枚举，可空）。</param>
    /// <param name="warehouseId">仓库筛选（可空）。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>单头扁平行列表。</returns>
    Task<IReadOnlyList<InventoryAdjustRow>> ListAsync(
        int? type, int? status, long? warehouseId, CancellationToken ct);

    /// <summary>盘盈盘亏单详情。</summary>
    /// <param name="id">单据 Id。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>单头 + 扁平明细。</returns>
    Task<InventoryAdjustDetailRow> GetAsync(long id, CancellationToken ct);

    /// <summary>手工建单（草稿态，金额按当前均价估算）。</summary>
    /// <param name="payload">建单入参。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>新建单详情。</returns>
    Task<InventoryAdjustDetailRow> CreateAsync(CreateInventoryAdjustPayload payload, CancellationToken ct);

    /// <summary>提交审批：1 草稿 → 2 待审批（流程 flow.wms.inventory.adjust）。</summary>
    /// <param name="id">单据 Id。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>提交后的单头。</returns>
    Task<InventoryAdjustRow> SubmitAsync(long id, CancellationToken ct);
}

/// <summary>
/// 盘盈盘亏审批终态应用接缝。由审批完成事件消费者在 Task 21 调用；
/// 仅「2 待审批」状态生效，重复调用返回 <see langword="false"/> 保证幂等。
/// </summary>
internal interface IInventoryAdjustApprovalApplier
{
    /// <summary>应用审批结果。</summary>
    /// <param name="docId">盘盈盘亏单 Id。</param>
    /// <param name="approved">是否审批通过。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>是否发生了状态流转（重复应用返回 false）。</returns>
    Task<bool> ApplyAsync(long docId, bool approved, CancellationToken ct);
}
