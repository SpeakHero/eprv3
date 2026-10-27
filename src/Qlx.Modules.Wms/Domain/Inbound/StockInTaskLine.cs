/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-26 16:10:00 +08:00 中国标准时间 星期一
 * 描述：入库作业明细实体（wms_stock_in_task_lines）——预期/清点/确认数量与批次库位原始字段
 */

using System.ComponentModel.DataAnnotations;

using Microsoft.EntityFrameworkCore;

using Qlx.Abstractions.Domain;

namespace Qlx.Modules.Wms.Domain.Inbound;

/// <summary>入库作业明细（表 wms_stock_in_task_lines）。</summary>
[Index(nameof(TenantId), nameof(TaskId), nameof(LineNo), IsUnique = true)]
[Index(nameof(TenantId), nameof(ItemId))]
public sealed class StockInTaskLine : AuditableEntity, ITenantEntity
{
    /// <summary>所属租户 Id。</summary>
    public long TenantId { get; internal set; }

    /// <summary>所属入库作业 Id（由 EF 关系回填）。</summary>
    public long TaskId { get; internal set; }

    /// <summary>行号（同单内唯一）。</summary>
    public int LineNo { get; internal set; }

    /// <summary>商品 Id。</summary>
    public long ItemId { get; internal set; }

    /// <summary>预期数量。</summary>
    public decimal ExpectQty { get; internal set; }

    /// <summary>清点数量（作业时录入）。</summary>
    public decimal CountQty { get; internal set; }

    /// <summary>确认入库数量。</summary>
    public decimal ConfirmQty { get; internal set; }

    /// <summary>库位 Id（可选，须属同仓库且启用）。</summary>
    public long? LocationId { get; internal set; }

    /// <summary>批次号（启用批次商品必填）。</summary>
    [MaxLength(64)]
    public string? BatchNo { get; internal set; }

    /// <summary>入库单价（成本来源：行值 → 商品参考成本 → 0，A-14）。</summary>
    public decimal? UnitCost { get; internal set; }

    /// <summary>EF 物化用无参构造。</summary>
    public StockInTaskLine() { }

    /// <summary>创建明细行。</summary>
    /// <param name="tenantId">租户 Id。</param>
    /// <param name="lineNo">行号。</param>
    /// <param name="itemId">商品 Id。</param>
    /// <param name="expectQty">预期数量。</param>
    /// <param name="locationId">库位 Id。</param>
    /// <param name="batchNo">批次号。</param>
    /// <param name="unitCost">入库单价。</param>
    public StockInTaskLine(
        long tenantId,
        int lineNo,
        long itemId,
        decimal expectQty,
        long? locationId,
        string? batchNo,
        decimal? unitCost)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(tenantId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(lineNo);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(itemId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(expectQty);
        TenantId = tenantId;
        LineNo = lineNo;
        ItemId = itemId;
        ExpectQty = expectQty;
        LocationId = locationId;
        BatchNo = batchNo;
        UnitCost = unitCost;
    }

    /// <summary>录入清点与确认结果（完成作业时调用）。</summary>
    /// <param name="countQty">清点数量。</param>
    /// <param name="confirmQty">确认数量。</param>
    /// <param name="locationId">库位 Id。</param>
    /// <param name="batchNo">批次号。</param>
    /// <param name="unitCost">入库单价。</param>
    public void RecordCount(
        decimal countQty,
        decimal confirmQty,
        long? locationId,
        string? batchNo,
        decimal? unitCost)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(countQty);
        ArgumentOutOfRangeException.ThrowIfNegative(confirmQty);
        if (confirmQty > countQty)
        {
            throw new ArgumentOutOfRangeException(nameof(confirmQty), "确认数量不能超过清点数量");
        }

        CountQty = countQty;
        ConfirmQty = confirmQty;
        LocationId = locationId;
        BatchNo = batchNo;
        UnitCost = unitCost;
    }
}
