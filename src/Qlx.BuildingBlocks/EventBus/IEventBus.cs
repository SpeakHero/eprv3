/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-20 22:00:00 +08:00 中国标准时间 星期二
 * 修改时间：2026-10-20 22:00:00 +08:00 中国标准时间 星期二
 * 描述：事件总线契约，模块间解耦通信的唯一通道，分即时与持久（Outbox）两档
 */

using Qlx.Abstractions.Events;

namespace Qlx.BuildingBlocks.EventBus;

/// <summary>
/// 事件总线。模块之间禁止直接调用，一律经事件通信。
/// </summary>
public interface IEventBus
{
    /// <summary>
    /// 进程内即时发布：同步分发给当前进程内全部订阅者，不持久化。
    /// 用于模块内事件或对可靠性无要求的即时联动。
    /// </summary>
    /// <typeparam name="TEvent">事件契约类型。</typeparam>
    /// <param name="eventData">事件实例。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task PublishAsync<TEvent>(TEvent eventData, CancellationToken cancellationToken)
        where TEvent : IntegrationEvent;

    /// <summary>
    /// 持久发布：先写入 Outbox，由后台调度器异步投递（at-least-once，消费者须幂等）。
    /// 跨模块联动的默认通道，保证最终一致。
    /// </summary>
    /// <typeparam name="TEvent">事件契约类型。</typeparam>
    /// <param name="eventData">事件实例。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task PublishDurableAsync<TEvent>(TEvent eventData, CancellationToken cancellationToken)
        where TEvent : IntegrationEvent;
}
