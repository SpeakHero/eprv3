/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-26
 * 描述：盘点明细实体（wms_stock_take_lines）——账面量快照 + 实盘量 + 差异 + 冻结标记（幂等解冻）
 */

using System.ComponentModel.DataAnnotations;

using Microsoft.EntityFrameworkCore;

using Qlx.Abstractions.Domain;

namespace Qlx.Modules.Wms.Domain.StockTake;

/// <summary>
/// 盘点明细行（表 wms_stock_take_lines）。<see cref="BookQty"/> 为生成盘点单时的台账快照；
/// <see cref="Frozen"/> 记录本行是否已对台账执行过冻结，解冻时仅对 <see langword="true"/> 行反向调整（幂等）。
/// </summary>
[Index(nameof(TenantId), nameof(TakeId), nameof(LineNo), IsUnique = true)]
[Index(nameof(TenantId), nameof(ItemId))]
public sealed class StockTakeLine : AuditableEntity, ITenantEntity
{
    /// <summary>所属租户 Id。</summary>
    public long TenantId { get; internal set; }

    /// <summary>盘点单 Id。</summary>
    public long TakeId { get; internal set; }

    /// <summary>行号（单内唯一）。</summary>
    public int LineNo { get; internal set; }

    /// <summary>商品 Id。</summary>
    public long ItemId { get; internal set; }

    /// <summary>批次号（批次维度盘点预留，可空）。</summary>
    [MaxLength(64)]
    public string? BatchNo { get; internal set; }

    /// <summary>账面数量快照。</summary>
    public decimal BookQty { get; internal set; }

    /// <summary>实盘数量（未录入为 null）。</summary>
    public decimal? ActualQty { get; private set; }

    /// <summary>差异数量（实盘 − 账面；未录入为 null）。</summary>
    public decimal? DiffQty { get; private set; }

    /// <summary>差异原因。</summary>
    [MaxLength(256)]
    public string? DiffReason { get; private set; }

    /// <summary>是否已冻结（冻结/解冻幂等标记）。</summary>
    public bool Frozen { get; private set; }

    /// <summary>EF 物化用无参构造。</summary>
    public StockTakeLine() { }

    /// <summary>创建盘点明细行。</summary>
    /// <param name="tenantId">租户 Id。</param>
    /// <param name="lineNo">行号。</param>
    /// <param name="itemId">商品 Id。</param>
    /// <param name="batchNo">批次号。</param>
    /// <param name="bookQty">账面数量快照。</param>
    public StockTakeLine(long tenantId, int lineNo, long itemId, string? batchNo, decimal bookQty)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(tenantId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(lineNo);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(itemId);
        ArgumentOutOfRangeException.ThrowIfNegative(bookQty);
        TenantId = tenantId;
        LineNo = lineNo;
        ItemId = itemId;
        BatchNo = batchNo;
        BookQty = bookQty;
    }

    /// <summary>是否存在差异（已录入实盘且差异非零）。</summary>
    public bool HasDiff => DiffQty is { } diff && diff != 0m;

    /// <summary>录入实盘数量并计算差异。</summary>
    /// <param name="actualQty">实盘数量（&gt;=0）。</param>
    /// <param name="diffReason">差异原因（差异非零时必填）。</param>
    public void RecordCount(decimal actualQty, string? diffReason)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(actualQty);
        ActualQty = actualQty;
        DiffQty = actualQty - BookQty;
        DiffReason = diffReason;
    }

    /// <summary>标记已冻结。</summary>
    public void MarkFrozen() => Frozen = true;

    /// <summary>标记已解冻（幂等标记，重复调用无副作用）。</summary>
    public void MarkUnfrozen() => Frozen = false;
}
