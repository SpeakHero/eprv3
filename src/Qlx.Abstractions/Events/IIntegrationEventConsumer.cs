/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-20 22:00:00 +08:00 中国标准时间 星期二
 * 修改时间：2026-10-20 22:00:00 +08:00 中国标准时间 星期二
 * 描述：集成事件消费者契约，模块通过实现本接口订阅事件总线消息
 */

namespace Qlx.Abstractions.Events;

/// <summary>
/// 集成事件消费者契约。实现类经 DI 注册后由事件总线自动分发调用。
/// 消费者必须幂等：Outbox 为 at-least-once 投递，同一事件可能到达多次。
/// </summary>
/// <typeparam name="TEvent">订阅的事件契约类型。</typeparam>
public interface IIntegrationEventConsumer<in TEvent>
    where TEvent : IntegrationEvent
{
    /// <summary>
    /// 消费一条集成事件。
    /// </summary>
    /// <param name="eventData">事件契约实例。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task ConsumeAsync(TEvent eventData, CancellationToken cancellationToken);
}
