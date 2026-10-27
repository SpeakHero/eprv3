/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-25 10:00:00 +08:00 中国标准时间 星期日
 * 修改时间：2026-10-25 10:00:00 +08:00 中国标准时间 星期日
 * 描述：供应商准入通过集成事件契约，Bas 供应商状态流转为 ACTIVE 后经 Outbox 持久发布。
 */

namespace Qlx.Abstractions.Events.Bas;

/// <summary>
/// 供应商准入通过集成事件。
/// <para>Bas 供应商准入审批通过并置为 ACTIVE 后经 <c>IEventBus.PublishDurableAsync</c> 投递（Outbox，at-least-once），
/// 消费方必须按 <see cref="IntegrationEvent.EventId"/> 幂等处理。</para>
/// <para><see cref="IntegrationEvent.TenantCode"/> 由发布方以对象初始化器赋值。</para>
/// </summary>
public sealed record SupplierApprovedEvent : IntegrationEvent
{
    /// <summary>供应商 Id（bas_suppliers.id）。</summary>
    public long SupplierId { get; init; }

    /// <summary>供应商编码。</summary>
    public required string SupplierCode { get; init; }

    /// <summary>供应商评级数值枚举（A/B/C/Blacklist）。</summary>
    public int Grade { get; init; }
}
