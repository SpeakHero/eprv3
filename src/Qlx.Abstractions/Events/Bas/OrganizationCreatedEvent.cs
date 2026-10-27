/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-25 10:00:00 +08:00 中国标准时间 星期日
 * 修改时间：2026-10-25 10:00:00 +08:00 中国标准时间 星期日
 * 描述：组织创建集成事件契约，Bas 组织创建事务提交后经 Outbox 持久发布，下游模块订阅消费。
 */

namespace Qlx.Abstractions.Events.Bas;

/// <summary>
/// 组织创建集成事件。
/// <para>Bas 组织创建事务提交后经 <c>IEventBus.PublishDurableAsync</c> 投递（Outbox，at-least-once），
/// 消费方必须按 <see cref="IntegrationEvent.EventId"/> 幂等处理。</para>
/// <para>仅承载最小事实字段；<see cref="IntegrationEvent.TenantCode"/> 由发布方以对象初始化器赋值。</para>
/// </summary>
public sealed record OrganizationCreatedEvent : IntegrationEvent
{
    /// <summary>组织 Id（bas_organizations.id）。</summary>
    public long OrgId { get; init; }

    /// <summary>组织编码。</summary>
    public required string OrgCode { get; init; }

    /// <summary>父级组织 Id；顶级组织为 <see langword="null"/>。</summary>
    public long? ParentId { get; init; }

    /// <summary>组织类型数值枚举。</summary>
    public int Type { get; init; }
}
