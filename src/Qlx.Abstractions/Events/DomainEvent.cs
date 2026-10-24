/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-20 23:30:00 +08:00 中国标准时间 星期二
 * 修改时间：2026-10-20 23:30:00 +08:00 中国标准时间 星期二
 * 描述：领域事件基类，与 IntegrationEvent 区分：事务内发布、不跨模块
 */

namespace Qlx.Abstractions.Events;

/// <summary>
/// 领域事件标记接口。领域事件与 <see cref="IntegrationEvent"/>（跨模块集成事件）的区别：
/// 领域事件在**同一事务内**发布、只在**当前业务模块内**消费，用于聚合内部状态变更的响应；
/// 集成事件跨模块、经 Outbox 最终一致投递。
/// </summary>
public interface IDomainEvent
{
    /// <summary>事件唯一标识。</summary>
    long EventId { get; }

    /// <summary>事件发生时间（UTC）。</summary>
    DateTimeOffset OccurredAt { get; }
}

/// <summary>
/// 领域事件基类。业务模块在事务内 Raise 领域事件，
/// 供模块内其他 Service 订阅响应（如领域规则级的联动校验）。
/// </summary>
public abstract record DomainEvent : IDomainEvent
{
    /// <inheritdoc />
    public long EventId { get; init; }

    /// <inheritdoc />
    public DateTimeOffset OccurredAt { get; init; } = DateTimeOffset.UtcNow;
}
