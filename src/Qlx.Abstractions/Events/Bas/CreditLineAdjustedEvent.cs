/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-25 10:00:00 +08:00 中国标准时间 星期日
 * 修改时间：2026-10-25 10:00:00 +08:00 中国标准时间 星期日
 * 描述：客户信用额度调整集成事件契约，Bas 额度调整审批通过后经 Outbox 持久发布。
 */

namespace Qlx.Abstractions.Events.Bas;

/// <summary>
/// 客户信用额度调整集成事件。
/// <para>Bas 客户信用额度调整审批通过后经 <c>IEventBus.PublishDurableAsync</c> 投递（Outbox，at-least-once），
/// 消费方必须按 <see cref="IntegrationEvent.EventId"/> 幂等处理。</para>
/// <para>额度为原始 decimal 金额，不做显示格式化（B-2-2）。<see cref="IntegrationEvent.TenantCode"/> 由发布方以对象初始化器赋值。</para>
/// </summary>
public sealed record CreditLineAdjustedEvent : IntegrationEvent
{
    /// <summary>客户 Id（bas_customers.id）。</summary>
    public long CustomerId { get; init; }

    /// <summary>客户编码。</summary>
    public required string CustomerCode { get; init; }

    /// <summary>调整前信用额度（原始金额）。</summary>
    public decimal OldLimit { get; init; }

    /// <summary>调整后信用额度（原始金额）。</summary>
    public decimal NewLimit { get; init; }
}
