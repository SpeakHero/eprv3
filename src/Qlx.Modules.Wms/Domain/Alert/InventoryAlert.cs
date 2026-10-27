/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-26
 * 描述：库存预警实体（wms_inventory_alerts）——扫描任务按当日幂等生成，处理/忽略为唯一状态流转
 */

using System.ComponentModel.DataAnnotations;

using Microsoft.EntityFrameworkCore;

using Qlx.Abstractions.Domain;

namespace Qlx.Modules.Wms.Domain.Alert;

/// <summary>
/// 库存预警（表 wms_inventory_alerts）。幂等键 A-16 =（alert_type, item_id, warehouse_id, batch_no, alert_date），
/// <see cref="BatchNo"/> 空值归一为空串参与唯一索引；已处理/已忽略当日不再生成新行。
/// </summary>
[Index(nameof(TenantId), nameof(AlertType), nameof(ItemId), nameof(WarehouseId), nameof(BatchNo), nameof(AlertDate), IsUnique = true)]
[Index(nameof(TenantId), nameof(AlertDate), nameof(Status))]
[Index(nameof(TenantId), nameof(WarehouseId))]
public sealed class InventoryAlert : AuditableEntity, ITenantEntity
{
    /// <summary>所属租户 Id。</summary>
    public long TenantId { get; internal set; }

    /// <summary>预警类型数值枚举。</summary>
    public InventoryAlertType AlertType { get; private set; }

    /// <summary>商品 Id（原始引用，跨模块不 Join）。</summary>
    public long ItemId { get; internal set; }

    /// <summary>仓库 Id（原始引用）。</summary>
    public long WarehouseId { get; internal set; }

    /// <summary>批次号（非批次维度预警为空串，A-16 归一）。</summary>
    [MaxLength(64)]
    public string BatchNo { get; internal set; } = string.Empty;

    /// <summary>判定阈值（安全库存数量 / 天数，业务口径原始值）。</summary>
    [Precision(18, 4)]
    public decimal Threshold { get; internal set; }

    /// <summary>实际值（现存数量 / 无流水天数 / 距失效天数 / 在途超期天数）。</summary>
    [Precision(18, 4)]
    public decimal ActualValue { get; internal set; }

    /// <summary>预警日期（本地日期，当日幂等维度）。</summary>
    public DateOnly AlertDate { get; private set; }

    /// <summary>预警状态。</summary>
    public InventoryAlertStatus Status { get; private set; } = InventoryAlertStatus.Pending;

    /// <summary>处理人标识。</summary>
    [MaxLength(32)]
    public string? HandledBy { get; private set; }

    /// <summary>处理说明（处理必填，忽略可为空）。</summary>
    [MaxLength(256)]
    public string? HandledNote { get; private set; }

    /// <summary>处理时间。</summary>
    public DateTimeOffset? HandledAt { get; private set; }

    /// <summary>EF 物化用无参构造。</summary>
    public InventoryAlert() { }

    /// <summary>创建当日预警行。</summary>
    /// <param name="tenantId">租户 Id。</param>
    /// <param name="alertType">预警类型。</param>
    /// <param name="itemId">商品 Id。</param>
    /// <param name="warehouseId">仓库 Id。</param>
    /// <param name="batchNo">批次号（空值归一为空串）。</param>
    /// <param name="threshold">判定阈值。</param>
    /// <param name="actualValue">实际值。</param>
    /// <param name="alertDate">预警日期（本地日期）。</param>
    public InventoryAlert(
        long tenantId,
        InventoryAlertType alertType,
        long itemId,
        long warehouseId,
        string? batchNo,
        decimal threshold,
        decimal actualValue,
        DateOnly alertDate)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(tenantId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(itemId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(warehouseId);
        ArgumentOutOfRangeException.ThrowIfNegative(threshold);
        TenantId = tenantId;
        AlertType = alertType;
        ItemId = itemId;
        WarehouseId = warehouseId;
        BatchNo = string.IsNullOrWhiteSpace(batchNo) ? string.Empty : batchNo.Trim();
        Threshold = threshold;
        ActualValue = actualValue;
        AlertDate = alertDate;
    }

    /// <summary>处理预警：待处理 → 已处理（说明必填）。</summary>
    /// <param name="handledNote">处理说明。</param>
    /// <param name="handledBy">处理人标识。</param>
    /// <param name="handledAt">处理时间。</param>
    public void Handle(string handledNote, string? handledBy, DateTimeOffset handledAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(handledNote);
        EnsurePending("处理");
        HandledNote = handledNote.Trim();
        HandledBy = handledBy;
        HandledAt = handledAt;
        Status = InventoryAlertStatus.Handled;
    }

    /// <summary>忽略预警：待处理 → 已忽略。</summary>
    /// <param name="handledBy">操作人标识。</param>
    /// <param name="handledAt">操作时间。</param>
    public void Ignore(string? handledBy, DateTimeOffset handledAt)
    {
        EnsurePending("忽略");
        HandledBy = handledBy;
        HandledNote = null;
        HandledAt = handledAt;
        Status = InventoryAlertStatus.Ignored;
    }

    private void EnsurePending(string action)
    {
        if (Status != InventoryAlertStatus.Pending)
        {
            throw new InvalidOperationException($"仅待处理的库存预警可{action}");
        }
    }
}
