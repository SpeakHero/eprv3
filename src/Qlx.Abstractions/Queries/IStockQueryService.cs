/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-26 10:00:00 +08:00 中国标准时间 星期一
 * 修改时间：2026-10-26 10:00:00 +08:00 中国标准时间 星期一
 * 描述：跨模块库存只读查询契约——Wms 作业、下游采购/销售读取 Stk 台账与批次时使用。
 */

namespace Qlx.Abstractions.Queries;

/// <summary>
/// 库存台账扁平只读视图（stk_stock_balances 投影）。
/// <para>成本类字段（<c>AvgCost</c>/<c>Amount</c>）在调用方无 <c>stk.cost.field</c> 权限时由实现置为
/// <see langword="null"/>（字段级安全过滤，B-3-1）；数量与冻结量始终下发。</para>
/// </summary>
/// <param name="ItemId">商品 Id。</param>
/// <param name="WarehouseId">仓库 Id（原始引用，不联表取名称）。</param>
/// <param name="QtyOnhand">现存数量。</param>
/// <param name="AvgCost">移动加权平均单价（无成本字段权限时为 <see langword="null"/>）。</param>
/// <param name="Amount">库存金额（无成本字段权限时为 <see langword="null"/>）。</param>
/// <param name="QtyLocked">冻结量（盘点冻结/占用）。</param>
/// <param name="LastTxnAt">最近变动时间。</param>
public sealed record StockBalanceRow(
    long ItemId,
    long WarehouseId,
    decimal? QtyOnhand,
    decimal? AvgCost,
    decimal? Amount,
    decimal QtyLocked,
    DateTimeOffset? LastTxnAt);

/// <summary>
/// 批次库存扁平只读视图（stk_stock_batches 投影）。
/// </summary>
/// <param name="ItemId">商品 Id。</param>
/// <param name="WarehouseId">仓库 Id。</param>
/// <param name="BatchNo">批次号。</param>
/// <param name="ProduceDate">生产日期。</param>
/// <param name="ExpiryDate">失效日期。</param>
/// <param name="Qty">批次数量。</param>
/// <param name="AvgCost">批次平均单价（无成本字段权限时为 <see langword="null"/>）。</param>
/// <param name="Amount">批次金额（无成本字段权限时为 <see langword="null"/>）。</param>
public sealed record StockBatchRow(
    long ItemId,
    long WarehouseId,
    string BatchNo,
    DateTimeOffset? ProduceDate,
    DateTimeOffset? ExpiryDate,
    decimal Qty,
    decimal? AvgCost,
    decimal? Amount);

/// <summary>
/// 跨模块库存只读查询契约。Stk 模块负责实现并强制当前租户过滤；
/// 消费方禁止自行跨库 Join，一律经本接口。
/// </summary>
public interface IStockQueryService : IModuleQuery
{
    /// <summary>
    /// 查询「商品×仓库」台账余额。
    /// </summary>
    /// <param name="itemId">商品 Id。</param>
    /// <param name="warehouseId">仓库 Id。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>台账行；不存在返回 <see langword="null"/>。</returns>
    Task<StockBalanceRow?> FindBalanceAsync(long itemId, long warehouseId, CancellationToken cancellationToken);

    /// <summary>
    /// 查询指定商品在各仓库的台账分布（扁平列表）。
    /// </summary>
    /// <param name="itemId">商品 Id。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>台账行列表；无数据返回空数组。</returns>
    Task<IReadOnlyList<StockBalanceRow>> ListBalancesByItemAsync(long itemId, CancellationToken cancellationToken);

    /// <summary>
    /// 查询指定仓库下全部商品的台账（扁平列表）。
    /// </summary>
    /// <param name="warehouseId">仓库 Id。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>台账行列表；无数据返回空数组。</returns>
    Task<IReadOnlyList<StockBalanceRow>> ListBalancesByWarehouseAsync(long warehouseId, CancellationToken cancellationToken);

    /// <summary>
    /// 查询指定商品在指定仓库的批次库存，按失效日期升序（FEFO 拣货建议，业务规则在后端求值）。
    /// </summary>
    /// <param name="itemId">商品 Id。</param>
    /// <param name="warehouseId">仓库 Id。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>批次行列表；无数据返回空数组。</returns>
    Task<IReadOnlyList<StockBatchRow>> ListBatchesAsync(long itemId, long warehouseId, CancellationToken cancellationToken);

    /// <summary>
    /// 判断指定仓库是否存在非零库存（仓库停用前置校验）。
    /// </summary>
    /// <param name="warehouseId">仓库 Id。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>存在任意非零数量返回 <see langword="true"/>。</returns>
    Task<bool> HasStockInWarehouseAsync(long warehouseId, CancellationToken cancellationToken);
}
