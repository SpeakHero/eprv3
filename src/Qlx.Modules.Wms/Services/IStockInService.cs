/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-26 16:20:00 +08:00 中国标准时间 星期一
 * 描述：入库作业服务契约（Controller 仅依赖本接口）
 */

using Qlx.Modules.Wms.Dtos;

namespace Qlx.Modules.Wms.Services;

/// <summary>入库作业服务。</summary>
public interface IStockInService
{
    /// <summary>入库作业列表。</summary>
    /// <param name="status">状态筛选（数值枚举，可空）。</param>
    /// <param name="warehouseId">仓库筛选（可空）。</param>
    /// <param name="sourceType">来源类型筛选（数值枚举，可空）。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>作业头扁平行列表。</returns>
    Task<IReadOnlyList<StockInTaskRow>> ListAsync(
        int? status, long? warehouseId, int? sourceType, CancellationToken ct);

    /// <summary>入库作业详情（含明细）。</summary>
    /// <param name="id">作业 Id。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>作业详情。</returns>
    Task<StockInTaskDetailRow> GetAsync(long id, CancellationToken ct);

    /// <summary>新建入库作业（单号后端生成）。</summary>
    /// <param name="payload">新建入参。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>创建后的作业详情。</returns>
    Task<StockInTaskDetailRow> CreateAsync(CreateStockInTaskPayload payload, CancellationToken ct);

    /// <summary>开始作业（待作业 → 作业中）。</summary>
    /// <param name="id">作业 Id。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>作业头。</returns>
    Task<StockInTaskRow> StartAsync(long id, CancellationToken ct);

    /// <summary>完成作业（作业中 → 已完成，同事务写库存命令 Outbox）。</summary>
    /// <param name="id">作业 Id。</param>
    /// <param name="payload">清点/确认结果。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>完成后的作业详情。</returns>
    Task<StockInTaskDetailRow> CompleteAsync(
        long id, CompleteStockInTaskPayload payload, CancellationToken ct);

    /// <summary>取消作业（待作业/作业中 → 已取消）。</summary>
    /// <param name="id">作业 Id。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>作业头。</returns>
    Task<StockInTaskRow> CancelAsync(long id, CancellationToken ct);
}
