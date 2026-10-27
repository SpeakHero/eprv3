/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-26
 * 描述：盘点服务契约（计划 + 盘点单，Controller 仅依赖本接口）
 */

using Qlx.Modules.Wms.Dtos;

namespace Qlx.Modules.Wms.Services;

/// <summary>库存盘点服务（计划 / 生成 / 初盘 / 复盘 / 取消）。</summary>
public interface IStockTakeService
{
    /// <summary>盘点计划列表。</summary>
    /// <param name="scopeType">范围类型筛选（数值枚举，可空）。</param>
    /// <param name="warehouseId">仓库筛选（可空）。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>计划扁平行列表。</returns>
    Task<IReadOnlyList<StockTakePlanRow>> ListPlansAsync(int? scopeType, long? warehouseId, CancellationToken ct);

    /// <summary>新建盘点计划（计划号后端生成）。</summary>
    /// <param name="payload">新建入参。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>创建后的计划。</returns>
    Task<StockTakePlanRow> CreatePlanAsync(CreateStockTakePlanPayload payload, CancellationToken ct);

    /// <summary>按范围快照台账生成盘点单并冻结（每计划仅允许一次）。</summary>
    /// <param name="planId">盘点计划 Id。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>生成后的盘点单详情。</returns>
    Task<StockTakeDetailRow> GenerateAsync(long planId, CancellationToken ct);

    /// <summary>盘点单列表。</summary>
    /// <param name="status">状态筛选（数值枚举，可空）。</param>
    /// <param name="warehouseId">仓库筛选（可空）。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>盘点单头扁平行列表。</returns>
    Task<IReadOnlyList<StockTakeRow>> ListAsync(int? status, long? warehouseId, CancellationToken ct);

    /// <summary>盘点单详情（盲盘裁剪账面量与差异）。</summary>
    /// <param name="id">盘点单 Id。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>盘点单详情。</returns>
    Task<StockTakeDetailRow> GetAsync(long id, CancellationToken ct);

    /// <summary>开始盘点（待盘点 → 初盘中）。</summary>
    /// <param name="id">盘点单 Id。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>盘点单头。</returns>
    Task<StockTakeRow> StartAsync(long id, CancellationToken ct);

    /// <summary>保存实盘数量（初盘中/复盘中）。</summary>
    /// <param name="id">盘点单 Id。</param>
    /// <param name="payload">实盘结果。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>盘点单详情。</returns>
    Task<StockTakeDetailRow> SaveCountsAsync(
        long id, SaveStockTakeCountsPayload payload, CancellationToken ct);

    /// <summary>完成盘点（初盘有差异自动生成复盘单；复盘完成解冻并生成盘盈亏草稿）。</summary>
    /// <param name="id">盘点单 Id。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>完成结果。</returns>
    Task<StockTakeFinishResultRow> FinishAsync(long id, CancellationToken ct);

    /// <summary>取消盘点（初盘中/复盘中 → 已取消，解冻一次）。</summary>
    /// <param name="id">盘点单 Id。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>盘点单头。</returns>
    Task<StockTakeRow> CancelAsync(long id, CancellationToken ct);
}
