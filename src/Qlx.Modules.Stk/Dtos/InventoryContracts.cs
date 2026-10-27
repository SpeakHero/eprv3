/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-26
 * 描述：库存台账/流水/批次只读 DTO——全部扁平原始字段，Id 保持 long 由 Hashids 出参混淆，
 *       枚举下发数值 int（中文映射在前端，B-2）；成本字段无 stk.cost.field 权限时置 null（B-3-1）
 */

namespace Qlx.Modules.Stk.Dtos;

/// <summary>库存台账余额扁平行。</summary>
/// <param name="ItemId">商品 Id。</param>
/// <param name="WarehouseId">仓库 Id（原始引用，不联表取名称）。</param>
/// <param name="QtyOnhand">现存数量。</param>
/// <param name="AvgCost">移动加权平均单价（无成本字段权限时为 <see langword="null"/>）。</param>
/// <param name="Amount">库存金额（无成本字段权限时为 <see langword="null"/>）。</param>
/// <param name="QtyLocked">冻结量。</param>
/// <param name="LastTxnAt">最近变动时间。</param>
public sealed record StockBalanceView(
    long ItemId,
    long WarehouseId,
    decimal QtyOnhand,
    decimal? AvgCost,
    decimal? Amount,
    decimal QtyLocked,
    DateTimeOffset? LastTxnAt);

/// <summary>库存流水扁平行（只读，流水不可变）。</summary>
/// <param name="Id">流水 Id。</param>
/// <param name="TxnNo">流水号。</param>
/// <param name="SourceType">来源类型数值（对应 StockSourceType）。</param>
/// <param name="SourceDocNo">来源单据号。</param>
/// <param name="SourceLineNo">来源单据行号。</param>
/// <param name="ItemId">商品 Id。</param>
/// <param name="WarehouseId">仓库 Id。</param>
/// <param name="BatchNo">批次号（非批次商品为 null）。</param>
/// <param name="Direction">变动方向数值（1 入库 / 2 出库）。</param>
/// <param name="Qty">变动数量。</param>
/// <param name="UnitCost">单价（无成本字段权限时为 <see langword="null"/>）。</param>
/// <param name="Amount">变动金额（无成本字段权限时为 <see langword="null"/>）。</param>
/// <param name="BalanceQtyAfter">变动后台账结存数量。</param>
/// <param name="BalanceAmountAfter">变动后台账结存金额（无成本字段权限时为 <see langword="null"/>）。</param>
/// <param name="OperatorId">经办人用户 Id。</param>
/// <param name="OccurredAt">业务发生时间。</param>
public sealed record StockTxnView(
    long Id,
    string TxnNo,
    int SourceType,
    string SourceDocNo,
    int SourceLineNo,
    long ItemId,
    long WarehouseId,
    string? BatchNo,
    int Direction,
    decimal Qty,
    decimal? UnitCost,
    decimal? Amount,
    decimal BalanceQtyAfter,
    decimal? BalanceAmountAfter,
    string? OperatorId,
    DateTimeOffset OccurredAt);

/// <summary>批次库存扁平行（按失效日期升序，FEFO）。</summary>
/// <param name="ItemId">商品 Id。</param>
/// <param name="WarehouseId">仓库 Id。</param>
/// <param name="BatchNo">批次号。</param>
/// <param name="ProduceDate">生产日期。</param>
/// <param name="ExpiryDate">失效日期。</param>
/// <param name="Qty">批次数量。</param>
/// <param name="AvgCost">批次平均单价（无成本字段权限时为 <see langword="null"/>）。</param>
/// <param name="Amount">批次金额（无成本字段权限时为 <see langword="null"/>）。</param>
public sealed record StockBatchView(
    long ItemId,
    long WarehouseId,
    string BatchNo,
    DateTimeOffset? ProduceDate,
    DateTimeOffset? ExpiryDate,
    decimal Qty,
    decimal? AvgCost,
    decimal? Amount);
