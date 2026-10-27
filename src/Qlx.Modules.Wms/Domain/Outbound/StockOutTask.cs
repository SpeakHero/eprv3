/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-26
 * 描述：出库作业单实体（wms_stock_out_tasks）——单号由 WL-CK 后端生成，状态机仅经领域方法流转
 */

using System.ComponentModel.DataAnnotations;

using Microsoft.EntityFrameworkCore;

using Qlx.Abstractions.Domain;

namespace Qlx.Modules.Wms.Domain.Outbound;

/// <summary>
/// 出库作业单（表 wms_stock_out_tasks）。单号后端生成（N-1）；拣货/复核人员与交接人以原始 Id/文本字段存储，
/// 不做展示拼装（B-2）。
/// </summary>
[Index(nameof(TenantId), nameof(TaskNo), IsUnique = true)]
[Index(nameof(TenantId), nameof(Status))]
[Index(nameof(TenantId), nameof(WarehouseId))]
public sealed class StockOutTask : AuditableEntity, ITenantEntity
{
    /// <summary>所属租户 Id。</summary>
    public long TenantId { get; internal set; }

    /// <summary>出库作业单号（租户内唯一，不可修改）。</summary>
    [MaxLength(32)]
    public string TaskNo { get; internal set; } = string.Empty;

    /// <summary>来源类型数值枚举（见 <see cref="StockOutSourceType"/>）。</summary>
    public int SourceType { get; internal set; }

    /// <summary>来源单据号（无单禁入，必填）。</summary>
    [MaxLength(32)]
    public string SourceDocNo { get; internal set; } = string.Empty;

    /// <summary>出库仓库 Id。</summary>
    public long WarehouseId { get; internal set; }

    /// <summary>拣货人用户 Id。</summary>
    [MaxLength(64)]
    public string? PickerId { get; internal set; }

    /// <summary>复核人用户 Id。</summary>
    [MaxLength(64)]
    public string? CheckerId { get; internal set; }

    /// <summary>交接人（复核必填）。</summary>
    [MaxLength(64)]
    public string? HandoverTo { get; internal set; }

    /// <summary>出库总金额（完成时按拣货数量 × 当前均价估算，用于大额审批判定）。</summary>
    public decimal TotalAmount { get; private set; }

    /// <summary>备注。</summary>
    [MaxLength(256)]
    public string? Remark { get; internal set; }

    /// <summary>作业状态。</summary>
    public StockOutTaskStatus Status { get; private set; } = StockOutTaskStatus.Pending;

    /// <summary>作业明细行。</summary>
    public List<StockOutTaskLine> Lines { get; private set; } = [];

    /// <summary>EF 物化用无参构造。</summary>
    public StockOutTask() { }

    /// <summary>创建出库作业单。</summary>
    /// <param name="tenantId">租户 Id。</param>
    /// <param name="taskNo">单号（后端生成）。</param>
    /// <param name="sourceType">来源类型数值枚举。</param>
    /// <param name="sourceDocNo">来源单据号。</param>
    /// <param name="warehouseId">出库仓库 Id。</param>
    /// <param name="remark">备注。</param>
    public StockOutTask(
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
    public void AddLine(StockOutTaskLine line)
    {
        ArgumentNullException.ThrowIfNull(line);
        Lines.Add(line);
    }

    /// <summary>开始拣货：待作业 → 拣货中。</summary>
    /// <param name="pickerId">拣货人用户 Id。</param>
    public void StartPicking(string? pickerId)
    {
        if (Status != StockOutTaskStatus.Pending)
        {
            throw new InvalidOperationException("仅待作业的出库单可拣货");
        }

        PickerId = pickerId;
        Status = StockOutTaskStatus.Picking;
    }

    /// <summary>复核：拣货中 → 待复核。</summary>
    /// <param name="checkerId">复核人用户 Id。</param>
    /// <param name="handoverTo">交接人。</param>
    public void Check(string? checkerId, string handoverTo)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(handoverTo);
        if (Status != StockOutTaskStatus.Picking)
        {
            throw new InvalidOperationException("仅拣货中的出库单可复核");
        }

        CheckerId = checkerId;
        HandoverTo = handoverTo;
        Status = StockOutTaskStatus.PendingCheck;
    }

    /// <summary>登记出库总金额（完成前估算，用于大额审批判定）。</summary>
    /// <param name="totalAmount">总金额（&gt;=0）。</param>
    public void RecordTotalAmount(decimal totalAmount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(totalAmount);
        TotalAmount = totalAmount;
    }

    /// <summary>完成作业：待复核 → 已完成。</summary>
    public void Complete()
    {
        if (Status != StockOutTaskStatus.PendingCheck)
        {
            throw new InvalidOperationException("仅待复核的出库单可完成");
        }

        Status = StockOutTaskStatus.Completed;
    }

    /// <summary>取消作业：待作业/拣货中/待复核 → 已取消。</summary>
    public void Cancel()
    {
        if (Status is not (StockOutTaskStatus.Pending
            or StockOutTaskStatus.Picking
            or StockOutTaskStatus.PendingCheck))
        {
            throw new InvalidOperationException("仅待作业、拣货中或待复核的出库单可取消");
        }

        Status = StockOutTaskStatus.Cancelled;
    }
}
