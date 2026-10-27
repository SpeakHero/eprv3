/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-26
 * 描述：库存预警扫描专用只读契约——按 Id 升序分页读取台账行与批次行（禁止一次性全量加载），
 *       供 Wms 预警扫描任务（wms.alert.scan）使用
 */

namespace Qlx.Abstractions.Queries;

/// <summary>
/// 预警扫描用台账扁平行（stk_stock_balances 投影）。仅含预警判定所需原始字段，不做成本裁剪与展示映射。
/// </summary>
/// <param name="Id">台账行 Id（分页游标，雪花 Id 单调递增）。</param>
/// <param name="ItemId">商品 Id。</param>
/// <param name="WarehouseId">仓库 Id。</param>
/// <param name="QtyOnhand">现存数量。</param>
/// <param name="LastTxnAt">最近变动时间。</param>
public sealed record StockBalanceScanRow(
    long Id,
    long ItemId,
    long WarehouseId,
    decimal QtyOnhand,
    DateTimeOffset? LastTxnAt);

/// <summary>
/// 预警扫描用批次扁平行（stk_stock_batches 投影）。
/// </summary>
/// <param name="Id">批次行 Id（分页游标）。</param>
/// <param name="ItemId">商品 Id。</param>
/// <param name="WarehouseId">仓库 Id。</param>
/// <param name="BatchNo">批次号。</param>
/// <param name="ExpiryDate">失效日期。</param>
/// <param name="Qty">批次数量。</param>
public sealed record StockBatchScanRow(
    long Id,
    long ItemId,
    long WarehouseId,
    string BatchNo,
    DateTimeOffset? ExpiryDate,
    decimal Qty);

/// <summary>
/// 库存预警扫描只读查询契约。Stk 模块负责实现并强制当前租户过滤；
/// 分页按主键 Id 升序，单批行数由调用方给定（≤500），调用方以末行 Id 作为下一页游标。
/// </summary>
public interface IStockAlertScanQueryService : IModuleQuery
{
    /// <summary>
    /// 分页读取台账行（Id &gt; <paramref name="afterId"/>，按 Id 升序）。
    /// </summary>
    /// <param name="afterId">游标（上一批末行 Id，首批传 0）。</param>
    /// <param name="limit">单批最大行数。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>台账扁平扫描行；无更多数据返回空数组。</returns>
    Task<IReadOnlyList<StockBalanceScanRow>> ListBalancePageAsync(
        long afterId, int limit, CancellationToken cancellationToken);

    /// <summary>
    /// 分页读取批次行（Id &gt; <paramref name="afterId"/>，按 Id 升序）。
    /// </summary>
    /// <param name="afterId">游标（上一批末行 Id，首批传 0）。</param>
    /// <param name="limit">单批最大行数。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>批次扁平扫描行；无更多数据返回空数组。</returns>
    Task<IReadOnlyList<StockBatchScanRow>> ListBatchPageAsync(
        long afterId, int limit, CancellationToken cancellationToken);
}
