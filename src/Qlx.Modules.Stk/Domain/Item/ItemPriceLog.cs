/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-26
 * 描述：商品价格/税率/单位变更日志实体（stk_price_logs）——只增不改，历史单据不回溯
 */

using System.ComponentModel.DataAnnotations;

using Microsoft.EntityFrameworkCore;

using Qlx.Abstractions.Domain;

namespace Qlx.Modules.Stk.Domain.Item;

/// <summary>
/// 商品价格类变更日志（表 stk_price_logs）。仅追加、无更新方法；
/// <c>Field</c> 取 1 参考成本 / 2 指导价 / 3 税率 / 4 单位。
/// </summary>
[Index(nameof(TenantId), nameof(ItemId))]
public sealed class ItemPriceLog : Entity, ITenantEntity
{
    /// <summary>所属租户 Id。</summary>
    public long TenantId { get; internal set; }

    /// <summary>商品 Id。</summary>
    public long ItemId { get; internal set; }

    /// <summary>变更字段（1 参考成本 / 2 指导价 / 3 税率 / 4 单位）。</summary>
    public int Field { get; internal set; }

    /// <summary>变更前值（原始值字符串）。</summary>
    [MaxLength(64)]
    public string? OldValue { get; internal set; }

    /// <summary>变更后值（原始值字符串）。</summary>
    [MaxLength(64)]
    public string? NewValue { get; internal set; }

    /// <summary>变更生效人（审批通过时记录审批人，直接变更时记录操作人）。</summary>
    [MaxLength(64)]
    public string? ApprovedBy { get; internal set; }

    /// <summary>记录时间（UTC）。</summary>
    public DateTimeOffset CreatedAt { get; internal set; }

    /// <summary>EF 物化用无参构造。</summary>
    public ItemPriceLog() { }

    /// <summary>追加一条价格类变更日志。</summary>
    /// <param name="tenantId">租户 Id。</param>
    /// <param name="itemId">商品 Id。</param>
    /// <param name="field">变更字段（1 参考成本 / 2 指导价 / 3 税率 / 4 单位）。</param>
    /// <param name="oldValue">变更前值。</param>
    /// <param name="newValue">变更后值。</param>
    /// <param name="approvedBy">变更生效人。</param>
    public ItemPriceLog(
        long tenantId, long itemId, int field, string? oldValue, string? newValue, string? approvedBy)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(tenantId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(itemId);
        TenantId = tenantId;
        ItemId = itemId;
        Field = field;
        OldValue = oldValue;
        NewValue = newValue;
        ApprovedBy = approvedBy;
        CreatedAt = DateTimeOffset.UtcNow;
    }
}
