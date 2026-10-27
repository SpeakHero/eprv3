/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-26
 * 描述：库存流水实体（stk_stock_txns）——只增不改不删（A-7）：
 *       实体仅提供构造 + 只读属性，DbContext 与数据库触发器双重守护；
 *       幂等唯一索引 8 列（来源类型+来源单号+行号+商品+仓库+批次+方向）
 */

using System.ComponentModel.DataAnnotations;

using Microsoft.EntityFrameworkCore;

using Qlx.Abstractions.Domain;

namespace Qlx.Modules.Stk.Domain.Inventory;

/// <summary>
/// 库存流水（表 stk_stock_txns）。台账唯一驱动源，**只允许 INSERT**：
/// 本类不提供任何更新/删除方法，<c>StkTenantDbContext</c> 在 SaveChanges 前拒绝
/// <see cref="Microsoft.EntityFrameworkCore.EntityState.Modified"/>/<see cref="Microsoft.EntityFrameworkCore.EntityState.Deleted"/>，
/// SQL Server 侧另有 <c>trg_stk_stock_txns_no_update_delete</c> 触发器兜底；
/// 红字冲正一律通过新增反向流水实现（spec §6-8）。
/// </summary>
public sealed class StockTxn : Entity, ITenantEntity
{
    /// <summary>所属租户 Id。</summary>
    public long TenantId { get; private set; }

    /// <summary>流水号（<c>SP-LS</c> 前缀 + 组织码 + 期间 + 流水）。</summary>
    [MaxLength(32)]
    public string TxnNo { get; private set; } = string.Empty;

    /// <summary>来源类型数值（对应 <c>StockSourceType</c>，只下发数值，中文映射在前端）。</summary>
    public int SourceType { get; private set; }

    /// <summary>来源单据号（可反查）。</summary>
    [MaxLength(32)]
    public string SourceDocNo { get; private set; } = string.Empty;

    /// <summary>来源单据行号（同一单据内唯一）。</summary>
    public int SourceLineNo { get; private set; }

    /// <summary>商品 Id。</summary>
    public long ItemId { get; private set; }

    /// <summary>仓库 Id。</summary>
    public long WarehouseId { get; private set; }

    /// <summary>批次号（非批次商品为 null）。</summary>
    [MaxLength(32)]
    public string? BatchNo { get; private set; }

    /// <summary>变动方向数值（1 入库 / 2 出库）。</summary>
    public int Direction { get; private set; }

    /// <summary>变动数量（正数，4 位小数）。</summary>
    [Precision(18, 4)]
    public decimal Qty { get; private set; }

    /// <summary>单价：入库取实际入库单价，出库取当时移动加权平均价（4 位小数）。</summary>
    [Precision(18, 4)]
    public decimal UnitCost { get; private set; }

    /// <summary>变动金额（2 位小数）。</summary>
    [Precision(18, 2)]
    public decimal Amount { get; private set; }

    /// <summary>变动后台账结存数量。</summary>
    [Precision(18, 4)]
    public decimal BalanceQtyAfter { get; private set; }

    /// <summary>变动后台账结存金额。</summary>
    [Precision(18, 2)]
    public decimal BalanceAmountAfter { get; private set; }

    /// <summary>经办人用户 Id。</summary>
    [MaxLength(64)]
    public string? OperatorId { get; private set; }

    /// <summary>业务发生时间。</summary>
    public DateTimeOffset OccurredAt { get; private set; }

    /// <summary>EF 物化用无参构造。</summary>
    public StockTxn() { }

    /// <summary>新增一条库存流水（本类唯一的写入口）。</summary>
    /// <param name="tenantId">租户 Id。</param>
    /// <param name="txnNo">流水号。</param>
    /// <param name="sourceType">来源类型数值。</param>
    /// <param name="sourceDocNo">来源单据号。</param>
    /// <param name="sourceLineNo">来源单据行号。</param>
    /// <param name="itemId">商品 Id。</param>
    /// <param name="warehouseId">仓库 Id。</param>
    /// <param name="batchNo">批次号。</param>
    /// <param name="direction">变动方向数值。</param>
    /// <param name="qty">变动数量。</param>
    /// <param name="unitCost">单价。</param>
    /// <param name="amount">变动金额。</param>
    /// <param name="balanceQtyAfter">变动后结存数量。</param>
    /// <param name="balanceAmountAfter">变动后结存金额。</param>
    /// <param name="operatorId">经办人用户 Id。</param>
    /// <param name="occurredAt">业务发生时间。</param>
    public StockTxn(
        long tenantId,
        string txnNo,
        int sourceType,
        string sourceDocNo,
        int sourceLineNo,
        long itemId,
        long warehouseId,
        string? batchNo,
        int direction,
        decimal qty,
        decimal unitCost,
        decimal amount,
        decimal balanceQtyAfter,
        decimal balanceAmountAfter,
        string? operatorId,
        DateTimeOffset occurredAt)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(txnNo);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceDocNo);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(itemId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(warehouseId);
        TenantId = tenantId;
        TxnNo = txnNo;
        SourceType = sourceType;
        SourceDocNo = sourceDocNo;
        SourceLineNo = sourceLineNo;
        ItemId = itemId;
        WarehouseId = warehouseId;
        BatchNo = batchNo;
        Direction = direction;
        Qty = qty;
        UnitCost = unitCost;
        Amount = amount;
        BalanceQtyAfter = balanceQtyAfter;
        BalanceAmountAfter = balanceAmountAfter;
        OperatorId = operatorId;
        OccurredAt = occurredAt;
    }
}
