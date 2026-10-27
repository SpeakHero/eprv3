/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-26 10:00:00 +08:00 中国标准时间 星期一
 * 描述：库区实体（wms_zones）——隶属仓库，租户内同一仓库库区编码唯一
 */

using System.ComponentModel.DataAnnotations;

using Microsoft.EntityFrameworkCore;

using Qlx.Abstractions.Domain;

namespace Qlx.Modules.Wms.Domain.Warehouse;

/// <summary>库区实体（表 wms_zones）。</summary>
[Index(nameof(TenantId), nameof(WarehouseId), nameof(Code), IsUnique = true)]
[Index(nameof(TenantId), nameof(Status))]
public sealed class WarehouseZone : AuditableEntity, ITenantEntity
{
    /// <summary>所属租户 Id。</summary>
    public long TenantId { get; internal set; }

    /// <summary>所属仓库 Id。</summary>
    public long WarehouseId { get; internal set; }

    /// <summary>库区编码（同仓库内唯一）。</summary>
    [MaxLength(32)]
    public string Code { get; internal set; } = string.Empty;

    /// <summary>库区名称。</summary>
    [MaxLength(64)]
    public string Name { get; internal set; } = string.Empty;

    /// <summary>排序号（原始值，排序呈现由前端决定）。</summary>
    public int SortOrder { get; internal set; }

    /// <summary>启停状态。</summary>
    public WarehouseStatus Status { get; private set; } = WarehouseStatus.Enabled;

    /// <summary>软删标记。</summary>
    public bool IsDeleted { get; private set; }

    /// <summary>EF 物化用无参构造。</summary>
    public WarehouseZone() { }

    /// <summary>创建库区。</summary>
    /// <param name="tenantId">租户 Id。</param>
    /// <param name="warehouseId">仓库 Id。</param>
    /// <param name="code">库区编码。</param>
    /// <param name="name">库区名称。</param>
    /// <param name="sortOrder">排序号。</param>
    public WarehouseZone(long tenantId, long warehouseId, string code, string name, int sortOrder)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(tenantId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(warehouseId);
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        TenantId = tenantId;
        WarehouseId = warehouseId;
        Code = code;
        Name = name;
        SortOrder = sortOrder;
    }

    /// <summary>更新库区资料（编码不可修改）。</summary>
    /// <param name="name">名称。</param>
    /// <param name="sortOrder">排序号。</param>
    public void UpdateProfile(string name, int sortOrder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name;
        SortOrder = sortOrder;
    }

    /// <summary>停用库区。</summary>
    public void Disable() => Status = WarehouseStatus.Disabled;

    /// <summary>启用库区。</summary>
    public void Enable() => Status = WarehouseStatus.Enabled;

    /// <summary>软删库区。</summary>
    public void SoftDelete() => IsDeleted = true;
}
