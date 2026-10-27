/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-26
 * 描述：盘点单实体（wms_stock_takes）——初盘/复盘两轮，状态机仅经领域方法流转
 */

using System.ComponentModel.DataAnnotations;

using Microsoft.EntityFrameworkCore;

using Qlx.Abstractions.Domain;

namespace Qlx.Modules.Wms.Domain.StockTake;

/// <summary>
/// 盘点单（表 wms_stock_takes）。单号由后端发号器生成（N-1）；
/// 初盘单生成时为待盘点，复盘单生成时即进入复盘中（无需再次开始）。
/// </summary>
[Index(nameof(TenantId), nameof(TakeNo), IsUnique = true)]
[Index(nameof(TenantId), nameof(Status))]
[Index(nameof(TenantId), nameof(WarehouseId))]
[Index(nameof(TenantId), nameof(PlanId))]
public sealed class StockTake : AuditableEntity, ITenantEntity
{
    /// <summary>所属租户 Id。</summary>
    public long TenantId { get; internal set; }

    /// <summary>盘点单号（租户内唯一，不可修改）。</summary>
    [MaxLength(32)]
    public string TakeNo { get; internal set; } = string.Empty;

    /// <summary>来源盘点计划 Id。</summary>
    public long PlanId { get; internal set; }

    /// <summary>盘点仓库 Id。</summary>
    public long WarehouseId { get; internal set; }

    /// <summary>盘点轮次（1 初盘 / 2 复盘）。</summary>
    public int Round { get; internal set; }

    /// <summary>是否盲盘（盲盘详情不下发账面量与差异，字段级安全过滤 B-3）。</summary>
    public bool IsBlind { get; internal set; }

    /// <summary>来源初盘单 Id（仅复盘单有值）。</summary>
    public long? SourceTakeId { get; internal set; }

    /// <summary>盘点状态。</summary>
    public StockTakeStatus Status { get; private set; }

    /// <summary>备注。</summary>
    [MaxLength(256)]
    public string? Remark { get; internal set; }

    /// <summary>盘点明细行。</summary>
    public List<StockTakeLine> Lines { get; private set; } = [];

    /// <summary>EF 物化用无参构造。</summary>
    public StockTake() { }

    /// <summary>创建盘点单（round=1 待盘点；round=2 复盘单直接进入复盘中）。</summary>
    /// <param name="tenantId">租户 Id。</param>
    /// <param name="takeNo">盘点单号（后端生成）。</param>
    /// <param name="planId">盘点计划 Id。</param>
    /// <param name="warehouseId">仓库 Id。</param>
    /// <param name="round">轮次（1 初盘 / 2 复盘）。</param>
    /// <param name="isBlind">是否盲盘。</param>
    /// <param name="sourceTakeId">来源初盘单 Id（round=2 必填）。</param>
    /// <param name="remark">备注。</param>
    public StockTake(
        long tenantId,
        string takeNo,
        long planId,
        long warehouseId,
        int round,
        bool isBlind,
        long? sourceTakeId,
        string? remark)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(takeNo);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(planId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(warehouseId);
        if (round is not (1 or 2))
        {
            throw new ArgumentOutOfRangeException(nameof(round), "盘点轮次必须为 1 或 2");
        }

        if (round == 2 && sourceTakeId is null)
        {
            throw new ArgumentNullException(nameof(sourceTakeId), "复盘单必须指定来源初盘单");
        }

        TenantId = tenantId;
        TakeNo = takeNo;
        PlanId = planId;
        WarehouseId = warehouseId;
        Round = round;
        IsBlind = isBlind;
        SourceTakeId = sourceTakeId;
        Remark = remark;
        Status = round == 2 ? StockTakeStatus.Rechecking : StockTakeStatus.Pending;
    }

    /// <summary>追加明细行（明细从属盘点单，Id 由 EF 在保存时回填）。</summary>
    /// <param name="line">明细行。</param>
    public void AddLine(StockTakeLine line)
    {
        ArgumentNullException.ThrowIfNull(line);
        Lines.Add(line);
    }

    /// <summary>开始盘点：待盘点 → 初盘中。</summary>
    public void Start()
    {
        if (Round != 1 || Status != StockTakeStatus.Pending)
        {
            throw new InvalidOperationException("仅初盘待盘点单可开始盘点");
        }

        Status = StockTakeStatus.InitialCounting;
    }

    /// <summary>完成初盘：初盘中 → 初盘完成。</summary>
    public void FinishInitial()
    {
        if (Round != 1 || Status != StockTakeStatus.InitialCounting)
        {
            throw new InvalidOperationException("仅初盘中的盘点单可完成初盘");
        }

        Status = StockTakeStatus.InitialFinished;
    }

    /// <summary>无差异直接完成：初盘完成 → 已完成（不再生成复盘单）。</summary>
    public void CompleteWithoutRecheck()
    {
        if (Round != 1 || Status != StockTakeStatus.InitialFinished)
        {
            throw new InvalidOperationException("仅初盘完成且无差异的盘点单可直接完成");
        }

        Status = StockTakeStatus.Completed;
    }

    /// <summary>完成复盘：复盘中 → 已完成。</summary>
    public void FinishRecheck()
    {
        if (Round != 2 || Status != StockTakeStatus.Rechecking)
        {
            throw new InvalidOperationException("仅复盘中的复盘单可完成");
        }

        Status = StockTakeStatus.Completed;
    }

    /// <summary>取消盘点：初盘中/复盘中 → 已取消。</summary>
    public void Cancel()
    {
        if (Status is not (StockTakeStatus.InitialCounting or StockTakeStatus.Rechecking))
        {
            throw new InvalidOperationException("仅初盘中或复盘中的盘点单可取消");
        }

        Status = StockTakeStatus.Cancelled;
    }
}
