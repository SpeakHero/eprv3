/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-26 10:00:00 +08:00 中国标准时间 星期一
 * 描述：库位实体（wms_locations）——隶属库区，同库区库位编码唯一，条码可空
 */

using System.ComponentModel.DataAnnotations;

using Microsoft.EntityFrameworkCore;

using Qlx.Abstractions.Domain;

namespace Qlx.Modules.Wms.Domain.Warehouse;

/// <summary>库位实体（表 wms_locations）。</summary>
[Index(nameof(TenantId), nameof(ZoneId), nameof(Code), IsUnique = true)]
[Index(nameof(TenantId), nameof(Status))]
public sealed class WarehouseLocation : AuditableEntity, ITenantEntity
{
    /// <summary>所属租户 Id。</summary>
    public long TenantId { get; internal set; }

    /// <summary>所属仓库 Id（冗余便于按仓库查询，来自库区）。</summary>
    public long WarehouseId { get; internal set; }

    /// <summary>所属库区 Id。</summary>
    public long ZoneId { get; internal set; }

    /// <summary>库位编码（同库区唯一）。</summary>
    [MaxLength(32)]
    public string Code { get; internal set; } = string.Empty;

    /// <summary>库位条码（可空）。</summary>
    [MaxLength(64)]
    public string? Barcode { get; internal set; }

    /// <summary>排序号（原始值）。</summary>
    public int SortOrder { get; internal set; }

    /// <summary>启停状态。</summary>
    public WarehouseStatus Status { get; private set; } = WarehouseStatus.Enabled;

    /// <summary>软删标记。</summary>
    public bool IsDeleted { get; private set; }

    /// <summary>EF 物化用无参构造。</summary>
    public WarehouseLocation() { }

    /// <summary>创建库位。</summary>
    /// <param name="tenantId">租户 Id。</param>
    /// <param name="warehouseId">仓库 Id。</param>
    /// <param name="zoneId">库区 Id。</param>
    /// <param name="code">库位编码。</param>
    /// <param name="barcode">条码。</param>
    /// <param name="sortOrder">排序号。</param>
    public WarehouseLocation(
        long tenantId, long warehouseId, long zoneId, string code, string? barcode, int sortOrder)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(tenantId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(warehouseId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(zoneId);
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        TenantId = tenantId;
        WarehouseId = warehouseId;
        ZoneId = zoneId;
        Code = code;
        Barcode = barcode;
        SortOrder = sortOrder;
    }

    /// <summary>更新库位资料（编码不可修改）。</summary>
    /// <param name="barcode">条码。</param>
    /// <param name="sortOrder">排序号。</param>
    public void UpdateProfile(string? barcode, int sortOrder)
    {
        Barcode = barcode;
        SortOrder = sortOrder;
    }

    /// <summary>停用库位。</summary>
    public void Disable() => Status = WarehouseStatus.Disabled;

    /// <summary>启用库位。</summary>
    public void Enable() => Status = WarehouseStatus.Enabled;

    /// <summary>软删库位。</summary>
    public void SoftDelete() => IsDeleted = true;
}
