/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-26
 * 描述：盘点计划实体（wms_stock_take_plans）——范围（全仓/品类/库位）+ 明盘/盲盘 + 只允许生成一次盘点单
 */

using System.ComponentModel.DataAnnotations;

using Microsoft.EntityFrameworkCore;

using Qlx.Abstractions.Domain;

namespace Qlx.Modules.Wms.Domain.StockTake;

/// <summary>盘点范围类型数值枚举。</summary>
public enum StockTakeScopeType
{
    /// <summary>全仓。</summary>
    Warehouse = 1,

    /// <summary>品类。</summary>
    Category = 2,

    /// <summary>库位。</summary>
    Location = 3,
}

/// <summary>
/// 盘点计划（表 wms_stock_take_plans）。计划号由后端发号器生成（N-1），前端不传码；
/// 范围参数只存原始 Id（跨模块不做 Join，B-2 / A-4）。
/// </summary>
[Index(nameof(TenantId), nameof(PlanNo), IsUnique = true)]
[Index(nameof(TenantId), nameof(WarehouseId))]
public sealed class StockTakePlan : AuditableEntity, ITenantEntity
{
    /// <summary>所属租户 Id。</summary>
    public long TenantId { get; internal set; }

    /// <summary>盘点计划号（租户内唯一，不可修改）。</summary>
    [MaxLength(32)]
    public string PlanNo { get; internal set; } = string.Empty;

    /// <summary>计划盘点日期。</summary>
    public DateOnly PlanDate { get; internal set; }

    /// <summary>盘点范围类型数值枚举（见 <see cref="StockTakeScopeType"/>）。</summary>
    public int ScopeType { get; internal set; }

    /// <summary>盘点仓库 Id。</summary>
    public long WarehouseId { get; internal set; }

    /// <summary>品类 Id（范围 = 品类时必填）。</summary>
    public long? CategoryId { get; internal set; }

    /// <summary>库位 Id（范围 = 库位时必填）。</summary>
    public long? LocationId { get; internal set; }

    /// <summary>是否盲盘（盲盘详情不下发账面量，字段级安全过滤 B-3）。</summary>
    public bool IsBlind { get; internal set; }

    /// <summary>备注。</summary>
    [MaxLength(256)]
    public string? Remark { get; internal set; }

    /// <summary>已生成的盘点单 Id（生成后不可重复生成）。</summary>
    public long? TakeId { get; private set; }

    /// <summary>EF 物化用无参构造。</summary>
    public StockTakePlan() { }

    /// <summary>创建盘点计划。</summary>
    /// <param name="tenantId">租户 Id。</param>
    /// <param name="planNo">计划号（后端生成）。</param>
    /// <param name="planDate">盘点日期。</param>
    /// <param name="scopeType">范围类型数值枚举。</param>
    /// <param name="warehouseId">仓库 Id。</param>
    /// <param name="categoryId">品类 Id。</param>
    /// <param name="locationId">库位 Id。</param>
    /// <param name="isBlind">是否盲盘。</param>
    /// <param name="remark">备注。</param>
    public StockTakePlan(
        long tenantId,
        string planNo,
        DateOnly planDate,
        int scopeType,
        long warehouseId,
        long? categoryId,
        long? locationId,
        bool isBlind,
        string? remark)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(planNo);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(warehouseId);
        TenantId = tenantId;
        PlanNo = planNo;
        PlanDate = planDate;
        ScopeType = scopeType;
        WarehouseId = warehouseId;
        CategoryId = categoryId;
        LocationId = locationId;
        IsBlind = isBlind;
        Remark = remark;
    }

    /// <summary>是否已生成盘点单。</summary>
    public bool IsGenerated => TakeId is not null;

    /// <summary>回写已生成的盘点单 Id（每计划仅允许生成一次）。</summary>
    /// <param name="takeId">盘点单 Id。</param>
    public void MarkGenerated(long takeId)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(takeId);
        if (TakeId is not null)
        {
            throw new InvalidOperationException("盘点计划已生成盘点单，不能重复生成");
        }

        TakeId = takeId;
    }
}
