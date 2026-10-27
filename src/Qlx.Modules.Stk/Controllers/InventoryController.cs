/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-26
 * 描述：库存台账只读 API（/api/stk/ledger、/api/stk/txns、/api/stk/batches）——
 *       只读无写台账动作（spec §6-1）；成本字段按 stk.cost.field 权限裁剪（B-3-1）；
 *       batches 端点走跨模块契约 IStockQueryService 以复用 FEFO/裁剪统一逻辑
 */

using Microsoft.AspNetCore.Mvc;

using Qlx.Abstractions.Exceptions;
using Qlx.Abstractions.Queries;
using Qlx.BuildingBlocks.Api;
using Qlx.Modules.Stk.Dtos;
using Qlx.Modules.Stk.Services;

namespace Qlx.Modules.Stk.Controllers;

/// <summary>
/// 库存台账只读控制器。仅提供台账余额 / 出入库流水 / 批次库存查询，
/// 任何写台账动作只能经 <c>IStockTxnCommandService</c>（服务层），本控制器不得暴露。
/// </summary>
/// <param name="ledgerService">模块内台账/流水只读服务。</param>
/// <param name="stockQueryService">跨模块库存查询服务（FEFO 批次）。</param>
[ApiController]
[Route("api/stk")]
public sealed class InventoryController(
    IInventoryQueryService ledgerService,
    IStockQueryService stockQueryService) : ControllerBase
{
    /// <summary>流水列表默认返回条数。</summary>
    private const int DefaultTxnTake = 200;

    /// <summary>流水列表最大返回条数。</summary>
    private const int MaxTxnTake = 500;

    /// <summary>库存台账余额列表。</summary>
    /// <param name="itemId">商品 Id（可选）。</param>
    /// <param name="warehouseId">仓库 Id（可选）。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>台账扁平行列表。</returns>
    [HttpGet("ledger")]
    public async Task<ApiResponse<IReadOnlyList<StockBalanceView>>> GetLedger(
        [FromQuery] long? itemId, [FromQuery] long? warehouseId, CancellationToken ct)
        => ApiResponse<IReadOnlyList<StockBalanceView>>.Ok(
            await ledgerService.ListBalancesAsync(itemId, warehouseId, ct));

    /// <summary>出入库流水列表（按 Id 倒序）。</summary>
    /// <param name="itemId">商品 Id（可选）。</param>
    /// <param name="warehouseId">仓库 Id（可选）。</param>
    /// <param name="sourceType">来源类型数值（可选）。</param>
    /// <param name="from">业务发生时间起（可选）。</param>
    /// <param name="to">业务发生时间止（可选）。</param>
    /// <param name="take">返回条数（默认 200，上限 500）。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>流水扁平行列表。</returns>
    [HttpGet("txns")]
    public async Task<ApiResponse<IReadOnlyList<StockTxnView>>> GetTxns(
        [FromQuery] long? itemId,
        [FromQuery] long? warehouseId,
        [FromQuery] int? sourceType,
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to,
        [FromQuery] int? take,
        CancellationToken ct)
    {
        int limit = take is null or <= 0 ? DefaultTxnTake : Math.Min(take.Value, MaxTxnTake);
        return ApiResponse<IReadOnlyList<StockTxnView>>.Ok(
            await ledgerService.ListTxnsAsync(itemId, warehouseId, sourceType, from, to, limit, ct));
    }

    /// <summary>批次库存列表（按失效日期升序，FEFO）。</summary>
    /// <param name="itemId">商品 Id（必填）。</param>
    /// <param name="warehouseId">仓库 Id（必填）。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>批次扁平行列表。</returns>
    [HttpGet("batches")]
    public async Task<ApiResponse<IReadOnlyList<StockBatchView>>> GetBatches(
        [FromQuery] long itemId, [FromQuery] long warehouseId, CancellationToken ct)
    {
        if (itemId <= 0)
        {
            throw new ValidationException("itemId", "商品不能为空");
        }

        if (warehouseId <= 0)
        {
            throw new ValidationException("warehouseId", "仓库不能为空");
        }

        IReadOnlyList<StockBatchRow> rows = await stockQueryService.ListBatchesAsync(itemId, warehouseId, ct);
        return ApiResponse<IReadOnlyList<StockBatchView>>.Ok(
            [.. rows.Select(r => new StockBatchView(
                r.ItemId,
                r.WarehouseId,
                r.BatchNo,
                r.ProduceDate,
                r.ExpiryDate,
                r.Qty,
                r.AvgCost,
                r.Amount))]);
    }
}
