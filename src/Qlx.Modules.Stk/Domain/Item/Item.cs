/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-26
 * 描述：商品档案实体（stk_items）——编码由后端按「分类编码 + 序号」生成（N-1），
 *       成本字段受 stk.cost.field 权限裁剪（B-3-1），启停与软删由领域方法维护
 */

using System.ComponentModel.DataAnnotations;

using Microsoft.EntityFrameworkCore;

using Qlx.Abstractions.Domain;

namespace Qlx.Modules.Stk.Domain.Item;

/// <summary>
/// 商品档案实体（表 stk_items）。商品编码由后端生成（前端不传码，N-1），分类/单位仅存原始 Id（不建跨模块外键）；
/// 条码租户内唯一（过滤软删行），删除一律软删。
/// </summary>
[Index(nameof(TenantId), nameof(ItemCode), IsUnique = true)]
[Index(nameof(TenantId), nameof(CategoryId))]
[Index(nameof(TenantId), nameof(Status))]
public sealed class Item : AuditableEntity, ITenantEntity
{
    /// <summary>所属租户 Id。</summary>
    public long TenantId { get; internal set; }

    /// <summary>商品编码（租户内唯一，后端生成后不可修改）。</summary>
    [MaxLength(24)]
    public string ItemCode { get; internal set; } = string.Empty;

    /// <summary>商品名称。</summary>
    [MaxLength(128)]
    public string Name { get; private set; } = string.Empty;

    /// <summary>规格型号。</summary>
    [MaxLength(128)]
    public string? Spec { get; private set; }

    /// <summary>条码（租户内唯一，可空）。</summary>
    [MaxLength(32)]
    public string? Barcode { get; private set; }

    /// <summary>商品分类 Id（原始引用，不联表取名称）。</summary>
    public long CategoryId { get; private set; }

    /// <summary>计量单位 Id（原始引用）。</summary>
    public long UnitId { get; private set; }

    /// <summary>是否启用批次管理。</summary>
    public bool BatchEnabled { get; private set; }

    /// <summary>是否启用有效期管理。</summary>
    public bool ExpiryEnabled { get; private set; }

    /// <summary>税率（0~1 的小数，如 13% = 0.13）。</summary>
    [Precision(5, 4)]
    public decimal TaxRate { get; private set; }

    /// <summary>参考成本价（受成本字段权限裁剪）。</summary>
    [Precision(18, 4)]
    public decimal? RefCostPrice { get; private set; }

    /// <summary>指导价（受成本字段权限裁剪）。</summary>
    [Precision(18, 4)]
    public decimal? GuidePrice { get; private set; }

    /// <summary>启停状态。</summary>
    public ItemStatus Status { get; private set; } = ItemStatus.Enabled;

    /// <summary>软删标记。</summary>
    public bool IsDeleted { get; private set; }

    /// <summary>EF 物化用无参构造。</summary>
    public Item() { }

    /// <summary>创建商品档案。</summary>
    /// <param name="tenantId">租户 Id。</param>
    /// <param name="itemCode">商品编码（后端生成）。</param>
    /// <param name="name">商品名称。</param>
    /// <param name="spec">规格型号。</param>
    /// <param name="barcode">条码。</param>
    /// <param name="categoryId">商品分类 Id。</param>
    /// <param name="unitId">计量单位 Id。</param>
    /// <param name="batchEnabled">是否启用批次管理。</param>
    /// <param name="expiryEnabled">是否启用有效期管理。</param>
    /// <param name="taxRate">税率。</param>
    /// <param name="refCostPrice">参考成本价。</param>
    /// <param name="guidePrice">指导价。</param>
    public Item(
        long tenantId,
        string itemCode,
        string name,
        string? spec,
        string? barcode,
        long categoryId,
        long unitId,
        bool batchEnabled,
        bool expiryEnabled,
        decimal taxRate,
        decimal? refCostPrice,
        decimal? guidePrice)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(itemCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(categoryId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(unitId);
        TenantId = tenantId;
        ItemCode = itemCode;
        Name = name;
        Spec = spec;
        Barcode = barcode;
        CategoryId = categoryId;
        UnitId = unitId;
        BatchEnabled = batchEnabled;
        ExpiryEnabled = expiryEnabled;
        TaxRate = taxRate;
        RefCostPrice = refCostPrice;
        GuidePrice = guidePrice;
    }

    /// <summary>更新非关键资料（名称/规格/条码/分类/价格；编码不可修改）。</summary>
    /// <param name="name">商品名称。</param>
    /// <param name="spec">规格型号。</param>
    /// <param name="barcode">条码。</param>
    /// <param name="categoryId">商品分类 Id。</param>
    /// <param name="refCostPrice">参考成本价。</param>
    /// <param name="guidePrice">指导价。</param>
    public void UpdateProfile(
        string name, string? spec, string? barcode, long categoryId, decimal? refCostPrice, decimal? guidePrice)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(categoryId);
        Name = name;
        Spec = spec;
        Barcode = barcode;
        CategoryId = categoryId;
        RefCostPrice = refCostPrice;
        GuidePrice = guidePrice;
    }

    /// <summary>重命名商品。</summary>
    /// <param name="name">新名称。</param>
    public void Rename(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name;
    }

    /// <summary>设置需审批生效的关键属性（税率/单位/批次/有效期）。</summary>
    /// <param name="unitId">计量单位 Id。</param>
    /// <param name="batchEnabled">是否启用批次管理。</param>
    /// <param name="expiryEnabled">是否启用有效期管理。</param>
    /// <param name="taxRate">税率。</param>
    public void SetKeyAttributes(long unitId, bool batchEnabled, bool expiryEnabled, decimal taxRate)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(unitId);
        UnitId = unitId;
        BatchEnabled = batchEnabled;
        ExpiryEnabled = expiryEnabled;
        TaxRate = taxRate;
    }

    /// <summary>停用商品。</summary>
    public void Disable() => Status = ItemStatus.Disabled;

    /// <summary>启用商品。</summary>
    public void Enable() => Status = ItemStatus.Enabled;

    /// <summary>软删商品。</summary>
    public void SoftDelete() => IsDeleted = true;
}
