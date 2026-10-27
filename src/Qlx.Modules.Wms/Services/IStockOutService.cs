/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-26
 * 描述：出库作业服务契约（Controller 仅依赖本接口）
 */

using Qlx.Modules.Wms.Dtos;

namespace Qlx.Modules.Wms.Services;

/// <summary>出库作业服务。</summary>
public interface IStockOutService
{
    /// <summary>出库作业列表。</summary>
    /// <param name="status">状态筛选（数值枚举，可空）。</param>
    /// <param name="warehouseId">仓库筛选（可空）。</param>
    /// <param name="sourceType">来源类型筛选（数值枚举，可空）。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>作业头扁平行列表。</returns>
    Task<IReadOnlyList<StockOutTaskRow>> ListAsync(
        int? status, long? warehouseId, int? sourceType, CancellationToken ct);

    /// <summary>出库作业详情（含明细与 FEFO 建议）。</summary>
    /// <param name="id">作业 Id。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>作业详情。</returns>
    Task<StockOutTaskDetailRow> GetAsync(long id, CancellationToken ct);

    /// <summary>新建出库作业（单号后端生成）。</summary>
    /// <param name="payload">新建入参。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>创建后的作业详情。</returns>
    Task<StockOutTaskDetailRow> CreateAsync(CreateStockOutTaskPayload payload, CancellationToken ct);

    /// <summary>拣货（待作业 → 拣货中）。</summary>
    /// <param name="id">作业 Id。</param>
    /// <param name="payload">拣货结果。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>作业详情。</returns>
    Task<StockOutTaskDetailRow> PickAsync(
        long id, PickStockOutTaskPayload payload, CancellationToken ct);

    /// <summary>复核（拣货中 → 待复核；复核数量须与实拣一致）。</summary>
    /// <param name="id">作业 Id。</param>
    /// <param name="payload">复核结果。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>作业详情。</returns>
    Task<StockOutTaskDetailRow> CheckAsync(
        long id, CheckStockOutTaskPayload payload, CancellationToken ct);

    /// <summary>完成作业（待复核 → 已完成；超阈值转大额审批挂起）。</summary>
    /// <param name="id">作业 Id。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>完成结果。</returns>
    Task<StockOutCompleteResultRow> CompleteAsync(long id, CancellationToken ct);

    /// <summary>取消作业（待作业/拣货中/待复核 → 已取消）。</summary>
    /// <param name="id">作业 Id。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>作业头。</returns>
    Task<StockOutTaskRow> CancelAsync(long id, CancellationToken ct);

    /// <summary>应用大额出库审批终态（供 WmsApprovalApplier 调用，幂等）。</summary>
    /// <param name="id">作业 Id。</param>
    /// <param name="approved">true 审批通过（完成并入队）；false 驳回（保持待复核）。</param>
    /// <param name="ct">取消令牌。</param>
    Task ApplyApprovalAsync(long id, bool approved, CancellationToken ct);
}
