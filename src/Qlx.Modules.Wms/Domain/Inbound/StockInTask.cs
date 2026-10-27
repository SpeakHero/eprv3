/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-26 16:10:00 +08:00 中国标准时间 星期一
 * 描述：入库作业单实体（wms_stock_in_tasks）——单号由 WL-RK 后端生成，状态机仅经领域方法流转
 */

using System.ComponentModel.DataAnnotations;

using Microsoft.EntityFrameworkCore;

using Qlx.Abstractions.Domain;

namespace Qlx.Modules.Wms.Domain.Inbound;

/// <summary>
/// 入库作业单（表 wms_stock_in_tasks）。单号由后端发号器生成（N-1），前端不传码；
/// 仓库与明细只存 Id/原始字段，不做展示拼装（B-2）。
/// </summary>
[Index(nameof(TenantId), nameof(TaskNo), IsUnique = true)]
[Index(nameof(TenantId), nameof(Status))]
[Index(nameof(TenantId), nameof(WarehouseId))]
public sealed class StockInTask : AuditableEntity, ITenantEntity
{
    /// <summary>所属租户 Id。</summary>
    public long TenantId { get; internal set; }

    /// <summary>入库作业单号（租户内唯一，不可修改）。</summary>
    [MaxLength(32)]
    public string TaskNo { get; internal set; } = string.Empty;

    /// <summary>来源类型数值枚举（见 <see cref="StockInSourceType"/>）。</summary>
    public int SourceType { get; internal set; }

    /// <summary>来源单据号（无单禁入，必填）。</summary>
    [MaxLength(32)]
    public string SourceDocNo { get; internal set; } = string.Empty;

    /// <summary>入库仓库 Id。</summary>
    public long WarehouseId { get; internal set; }

    /// <summary>备注。</summary>
    [MaxLength(256)]
    public string? Remark { get; internal set; }

    /// <summary>作业状态。</summary>
    public StockInTaskStatus Status { get; private set; } = StockInTaskStatus.Pending;

    /// <summary>作业明细行。</summary>
    public List<StockInTaskLine> Lines { get; private set; } = [];

    /// <summary>EF 物化用无参构造。</summary>
    public StockInTask() { }

    /// <summary>创建入库作业单。</summary>
    /// <param name="tenantId">租户 Id。</param>
    /// <param name="taskNo">单号（后端生成）。</param>
    /// <param name="sourceType">来源类型数值枚举。</param>
    /// <param name="sourceDocNo">来源单据号。</param>
    /// <param name="warehouseId">入库仓库 Id。</param>
    /// <param name="remark">备注。</param>
    public StockInTask(
        long tenantId,
        string taskNo,
        int sourceType,
        string sourceDocNo,
        long warehouseId,
        string? remark)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(taskNo);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceDocNo);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(warehouseId);
        TenantId = tenantId;
        TaskNo = taskNo;
        SourceType = sourceType;
        SourceDocNo = sourceDocNo;
        WarehouseId = warehouseId;
        Remark = remark;
    }

    /// <summary>追加明细行（明细从属作业单，Id 由 EF 在保存时回填）。</summary>
    /// <param name="line">明细行。</param>
    public void AddLine(StockInTaskLine line)
    {
        ArgumentNullException.ThrowIfNull(line);
        Lines.Add(line);
    }

    /// <summary>开始作业：待作业 → 作业中。</summary>
    public void Start()
    {
        if (Status != StockInTaskStatus.Pending)
        {
            throw new InvalidOperationException("仅待作业的入库单可开始作业");
        }

        Status = StockInTaskStatus.InProgress;
    }

    /// <summary>完成作业：作业中 → 已完成。</summary>
    public void Complete()
    {
        if (Status != StockInTaskStatus.InProgress)
        {
            throw new InvalidOperationException("仅作业中的入库单可完成");
        }

        Status = StockInTaskStatus.Completed;
    }

    /// <summary>取消作业：待作业/作业中 → 已取消。</summary>
    public void Cancel()
    {
        if (Status is not (StockInTaskStatus.Pending or StockInTaskStatus.InProgress))
        {
            throw new InvalidOperationException("仅待作业或作业中的入库单可取消");
        }

        Status = StockInTaskStatus.Cancelled;
    }
}
