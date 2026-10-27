/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-26 10:00:00 +08:00 中国标准时间 星期一
 * 修改时间：2026-10-26 10:00:00 +08:00 中国标准时间 星期一
 * 描述：库存变动集成事件契约，Stk 台账与流水写入成功后经 Outbox 持久发布。
 */

namespace Qlx.Abstractions.Events.Stk;

/// <summary>
/// 库存变动事件。
/// <para>Stk 每次成功写流水后经 <c>IEventBus.PublishDurableAsync</c> 投递（Outbox，at-least-once），
/// 消费方必须按 <see cref="IntegrationEvent.EventId"/> 幂等处理。</para>
/// </summary>
public sealed record StockChangedEvent : IntegrationEvent
{
    /// <summary>商品 Id。</summary>
    public long ItemId { get; init; }

    /// <summary>仓库 Id。</summary>
    public long WarehouseId { get; init; }

    /// <summary>批次号；非批次商品为 <see langword="null"/>。</summary>
    public string? BatchNo { get; init; }

    /// <summary>变动方向数值枚举（1 入 / 2 出）。</summary>
    public int Direction { get; init; }

    /// <summary>变动数量（正数）。</summary>
    public decimal Qty { get; init; }

    /// <summary>变动后结存数量。</summary>
    public decimal BalanceQtyAfter { get; init; }
}
