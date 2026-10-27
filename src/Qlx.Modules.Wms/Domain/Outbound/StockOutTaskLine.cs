/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-26
 * 描述：出库作业明细实体（wms_stock_out_task_lines）——申请/拣货/复核数量与批次库位原始字段
 */

using System.ComponentModel.DataAnnotations;

using Microsoft.EntityFrameworkCore;

using Qlx.Abstractions.Domain;

namespace Qlx.Modules.Wms.Domain.Outbound;

/// <summary>出库作业明细（表 wms_stock_out_task_lines）。</summary>
[Index(nameof(TenantId), nameof(TaskId), nameof(LineNo), IsUnique = true)]
[Index(nameof(TenantId), nameof(ItemId))]
public sealed class StockOutTaskLine : AuditableEntity, ITenantEntity
{
    /// <summary>所属租户 Id。</summary>
    public long TenantId { get; internal set; }

    /// <summary>所属出库作业 Id（由 EF 关系回填）。</summary>
    public long TaskId { get; internal set; }

    /// <summary>行号（同单内唯一）。</summary>
    public int LineNo { get; internal set; }

    /// <summary>商品 Id。</summary>
    public long ItemId { get; internal set; }

    /// <summary>申请出库数量。</summary>
    public decimal ApplyQty { get; internal set; }

    /// <summary>实拣数量（拣货时录入，≤ 申请数量）。</summary>
    public decimal PickQty { get; internal set; }

    /// <summary>复核数量（须与实拣数量一致）。</summary>
    public decimal CheckQty { get; internal set; }

    /// <summary>库位 Id（可选，须属同仓库且启用）。</summary>
    public long? LocationId { get; internal set; }

    /// <summary>批次号（启用批次商品实拣 &gt; 0 时必填）。</summary>
    [MaxLength(64)]
    public string? BatchNo { get; internal set; }

    /// <summary>EF 物化用无参构造。</summary>
    public StockOutTaskLine() { }

    /// <summary>创建明细行。</summary>
    /// <param name="tenantId">租户 Id。</param>
    /// <param name="lineNo">行号。</param>
    /// <param name="itemId">商品 Id。</param>
    /// <param name="applyQty">申请数量（&gt;0）。</param>
    /// <param name="locationId">库位 Id。</param>
    /// <param name="batchNo">批次号。</param>
    public StockOutTaskLine(
        long tenantId,
        int lineNo,
        long itemId,
        decimal applyQty,
        long? locationId,
        string? batchNo)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(tenantId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(lineNo);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(itemId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(applyQty);
        TenantId = tenantId;
        LineNo = lineNo;
        ItemId = itemId;
        ApplyQty = applyQty;
        LocationId = locationId;
        BatchNo = batchNo;
    }

    /// <summary>录入拣货结果（拣货时调用）。</summary>
    /// <param name="pickQty">实拣数量（&gt;=0 且 ≤ 申请数量）。</param>
    /// <param name="locationId">库位 Id。</param>
    /// <param name="batchNo">批次号。</param>
    public void RecordPick(decimal pickQty, long? locationId, string? batchNo)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(pickQty);
        if (pickQty > ApplyQty)
        {
            throw new ArgumentOutOfRangeException(nameof(pickQty), "实拣数量不能超过申请数量");
        }

        PickQty = pickQty;
        LocationId = locationId;
        BatchNo = batchNo;
    }

    /// <summary>录入复核数量（复核时调用）。</summary>
    /// <param name="checkQty">复核数量（&gt;=0）。</param>
    public void RecordCheck(decimal checkQty)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(checkQty);
        CheckQty = checkQty;
    }
}
