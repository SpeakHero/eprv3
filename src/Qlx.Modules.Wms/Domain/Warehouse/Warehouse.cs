/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-26 10:00:00 +08:00 中国标准时间 星期一
 * 描述：仓库实体（wms_warehouses）——仓库编码由单据号生成器产出，组织归属仅存 Id，启停与软删由领域方法维护
 */

using System.ComponentModel.DataAnnotations;

using Microsoft.EntityFrameworkCore;

using Qlx.Abstractions.Domain;

namespace Qlx.Modules.Wms.Domain.Warehouse;

/// <summary>
/// 仓库实体（表 wms_warehouses）。编码由后端生成（前端不传码，N-1）；
/// 组织归属只存 Id，不建跨模块外键（A-4）。
/// </summary>
[Index(nameof(TenantId), nameof(Code), IsUnique = true)]
[Index(nameof(TenantId), nameof(OrgId))]
[Index(nameof(TenantId), nameof(Status))]
public sealed class Warehouse : AuditableEntity, ITenantEntity
{
    /// <summary>所属租户 Id。</summary>
    public long TenantId { get; internal set; }

    /// <summary>仓库编码（租户内唯一，不可修改）。</summary>
    [MaxLength(32)]
    public string Code { get; internal set; } = string.Empty;

    /// <summary>仓库名称。</summary>
    [MaxLength(64)]
    public string Name { get; internal set; } = string.Empty;

    /// <summary>仓库类型数值枚举（1 自有 / 2 租赁 / 3 临时）。</summary>
    public int WhType { get; internal set; }

    /// <summary>归属组织 Id（可为空）。</summary>
    public long? OrgId { get; internal set; }

    /// <summary>仓库地址。</summary>
    [MaxLength(256)]
    public string? Address { get; internal set; }

    /// <summary>仓管员人员 Id（可为空）。</summary>
    public long? KeeperId { get; internal set; }

    /// <summary>备注。</summary>
    [MaxLength(256)]
    public string? Remark { get; internal set; }

    /// <summary>启停状态。</summary>
    public WarehouseStatus Status { get; private set; } = WarehouseStatus.Enabled;

    /// <summary>软删标记。</summary>
    public bool IsDeleted { get; private set; }

    /// <summary>EF 物化用无参构造。</summary>
    public Warehouse() { }

    /// <summary>创建仓库。</summary>
    /// <param name="tenantId">租户 Id。</param>
    /// <param name="code">仓库编码（后端生成）。</param>
    /// <param name="name">仓库名称。</param>
    /// <param name="whType">仓库类型数值枚举。</param>
    /// <param name="orgId">归属组织 Id。</param>
    /// <param name="address">地址。</param>
    /// <param name="keeperId">仓管员人员 Id。</param>
    /// <param name="remark">备注。</param>
    public Warehouse(
        long tenantId,
        string code,
        string name,
        int whType,
        long? orgId,
        string? address,
        long? keeperId,
        string? remark)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        TenantId = tenantId;
        Code = code;
        Name = name;
        WhType = whType;
        OrgId = orgId;
        Address = address;
        KeeperId = keeperId;
        Remark = remark;
    }

    /// <summary>更新仓库资料（编码不可修改）。</summary>
    /// <param name="name">名称。</param>
    /// <param name="whType">仓库类型。</param>
    /// <param name="orgId">归属组织 Id。</param>
    /// <param name="address">地址。</param>
    /// <param name="keeperId">仓管员人员 Id。</param>
    /// <param name="remark">备注。</param>
    public void UpdateProfile(string name, int whType, long? orgId, string? address, long? keeperId, string? remark)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name;
        WhType = whType;
        OrgId = orgId;
        Address = address;
        KeeperId = keeperId;
        Remark = remark;
    }

    /// <summary>停用仓库。</summary>
    public void Disable() => Status = WarehouseStatus.Disabled;

    /// <summary>启用仓库。</summary>
    public void Enable() => Status = WarehouseStatus.Enabled;

    /// <summary>软删仓库。</summary>
    public void SoftDelete() => IsDeleted = true;
}
