/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-26 10:00:00 +08:00 中国标准时间 星期一
 * 修改时间：2026-10-26 10:00:00 +08:00 中国标准时间 星期一
 * 描述：跨模块库存写入命令契约——Wms 作业/盘点/调拨完成时经本契约驱动 Stk 写流水与重算台账。
 */

namespace Qlx.Abstractions.Stock;

/// <summary>
/// 库存变动方向。
/// </summary>
public enum StockDirection
{
    /// <summary>入库。</summary>
    In = 1,

    /// <summary>出库。</summary>
    Out = 2,
}

/// <summary>
/// 库存流水来源类型。数值一经发布不可变更，新增类型只能追加。
/// </summary>
public enum StockSourceType
{
    /// <summary>期初建账过账。</summary>
    Opening = 1,

    /// <summary>采购入库。</summary>
    PurchaseIn = 2,

    /// <summary>销售退货入库。</summary>
    SaleReturn = 3,

    /// <summary>调拨入库。</summary>
    TransferIn = 4,

    /// <summary>盘盈入库。</summary>
    Gain = 5,

    /// <summary>采购退货出库。</summary>
    PurchaseReturn = 6,

    /// <summary>销售出库。</summary>
    SaleOut = 7,

    /// <summary>调拨出库。</summary>
    TransferOut = 8,

    /// <summary>盘亏出库。</summary>
    Loss = 9,

    /// <summary>领用出库。</summary>
    Use = 10,

    /// <summary>成本调整（红字/蓝字成对生成）。</summary>
    CostAdjust = 11,
}

/// <summary>
/// 单笔库存变动请求。由命令消费方（Wms 作业、期初过账、成本调整）构造。
/// <para><see cref="IdempotencyKey"/> 为跨进程幂等键（Outbox 重投、用户重试共用），
/// Stk 侧以「来源类型 + 来源单号 + 行号 + 商品 + 仓库 + 批次 + 方向」唯一索引去重。</para>
/// </summary>
/// <param name="SourceType">来源类型。</param>
/// <param name="SourceDocNo">来源单据号（可反查）。</param>
/// <param name="SourceLineNo">来源单据行号（同一单据内唯一）。</param>
/// <param name="ItemId">商品 Id。</param>
/// <param name="WarehouseId">仓库 Id。</param>
/// <param name="BatchNo">批次号；启用批次商品必填。</param>
/// <param name="Direction">变动方向。</param>
/// <param name="Qty">数量（正数）。</param>
/// <param name="UnitCost">单价；入库为实际单价，出库由 Stk 取当时移动加权平均价（传 0 即可）。</param>
/// <param name="OperatorId">经办人用户 Id。</param>
/// <param name="IdempotencyKey">幂等键。</param>
/// <param name="OccurredAt">业务发生时间。</param>
public sealed record PostStockTxnRequest(
    StockSourceType SourceType,
    string SourceDocNo,
    int SourceLineNo,
    long ItemId,
    long WarehouseId,
    string? BatchNo,
    StockDirection Direction,
    decimal Qty,
    decimal UnitCost,
    string? OperatorId,
    string IdempotencyKey,
    DateTimeOffset OccurredAt);

/// <summary>
/// 单笔库存变动结果。
/// </summary>
/// <param name="Success">是否成功。</param>
/// <param name="TxnId">流水 Id（成功时非空）。</param>
/// <param name="TxnNo">流水号（成功时非空）。</param>
/// <param name="BalanceQtyAfter">变动后结存数量。</param>
/// <param name="BalanceAmountAfter">变动后结存金额。</param>
/// <param name="ErrorCode">失败错误码；成功为 <see langword="null"/>。</param>
/// <param name="ShortageQty">负库存缺口数量（<c>STK_INSUFFICIENT</c> 时有效）。</param>
public sealed record StockPostResult(
    bool Success,
    long? TxnId,
    string? TxnNo,
    decimal BalanceQtyAfter,
    decimal BalanceAmountAfter,
    string? ErrorCode,
    decimal ShortageQty);

/// <summary>
/// 库存写入命令契约。Stk 模块负责实现；任何模块不得直接访问 <c>stk_</c> 表。
/// </summary>
public interface IStockTxnCommandService
{
    /// <summary>
    /// 提交单笔库存变动。
    /// </summary>
    /// <param name="request">变动请求。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>变动结果；幂等键命中时返回既有结果且 <see cref="StockPostResult.Success"/> 为 <see langword="true"/>。</returns>
    Task<StockPostResult> PostAsync(PostStockTxnRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// 批量提交库存变动（单事务；任一行失败整单回滚）。
    /// </summary>
    /// <param name="requests">变动请求列表。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>与请求顺序一致的结果列表。</returns>
    Task<IReadOnlyList<StockPostResult>> PostBatchAsync(
        IReadOnlyList<PostStockTxnRequest> requests,
        CancellationToken cancellationToken);

    /// <summary>
    /// 调整冻结量（盘点冻结/解冻）。正数冻结、负数释放。
    /// </summary>
    /// <param name="itemId">商品 Id。</param>
    /// <param name="warehouseId">仓库 Id。</param>
    /// <param name="deltaQty">冻结量增量。</param>
    /// <param name="reason">调整原因（审计用）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task AdjustLockedQtyAsync(
        long itemId,
        long warehouseId,
        decimal deltaQty,
        string reason,
        CancellationToken cancellationToken);

    /// <summary>
    /// 查询台账余额（供命令消费方在提交前估算金额/展示缺口）。
    /// </summary>
    /// <param name="itemId">商品 Id。</param>
    /// <param name="warehouseId">仓库 Id。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>台账行；不存在返回 <see langword="null"/>。</returns>
    Task<Qlx.Abstractions.Queries.StockBalanceRow?> FindBalanceAsync(
        long itemId,
        long warehouseId,
        CancellationToken cancellationToken);
}
