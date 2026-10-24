/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-20 22:00:00 +08:00 中国标准时间 星期二
 * 修改时间：2026-10-20 22:00:00 +08:00 中国标准时间 星期二
 * 描述：进程内事件分发器，经 DI 解析全部订阅者并顺序派发
 */

using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Qlx.Abstractions.Events;
using Qlx.Abstractions.Serialization;
using Qlx.BuildingBlocks.EventBus.Outbox;
using Qlx.BuildingBlocks.Ids;

namespace Qlx.BuildingBlocks.EventBus;

/// <summary>
/// 进程内事件总线默认实现。
/// 即时发布经 DI 解析全部 <see cref="IIntegrationEventConsumer{TEvent}"/> 顺序派发，
/// 单个消费者异常不阻断其余消费者，全部完成后聚合抛出。
/// 持久发布序列化后写入 <see cref="IOutboxStore"/>，由 Outbox 调度器异步投递。
/// </summary>
public sealed class InProcessEventBus(IServiceScopeFactory scopeFactory, IOutboxStore outboxStore, ISnowflakeIdGenerator idGenerator) : IEventBus
{
    private static readonly JsonSerializerOptions s_serializerOptions = QlxJsonDefaults.Default;

    /// <inheritdoc />
    public async Task PublishAsync<TEvent>(TEvent eventData, CancellationToken cancellationToken)
        where TEvent : IntegrationEvent
    {
        ArgumentNullException.ThrowIfNull(eventData);

        await using var scope = scopeFactory.CreateAsyncScope();
        var consumers = scope.ServiceProvider.GetServices<IIntegrationEventConsumer<TEvent>>();

        List<Exception> failures = [];
        foreach (var consumer in consumers)
        {
            try
            {
                await consumer.ConsumeAsync(eventData, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                failures.Add(exception);
            }
        }

        if (failures.Count > 0)
        {
            throw new AggregateException($"事件 {typeof(TEvent).Name} 的 {failures.Count} 个消费者处理失败", failures);
        }
    }

    /// <inheritdoc />
    public async Task PublishDurableAsync<TEvent>(TEvent eventData, CancellationToken cancellationToken)
        where TEvent : IntegrationEvent
    {
        ArgumentNullException.ThrowIfNull(eventData);

        var message = new OutboxMessage
        {
            Id = idGenerator.NextId(),
            EventType = typeof(TEvent).AssemblyQualifiedName ?? typeof(TEvent).FullName ?? typeof(TEvent).Name,
            Payload = JsonSerializer.Serialize(eventData, s_serializerOptions),
            TenantCode = eventData.TenantCode,
            OccurredAt = eventData.OccurredAt,
            AvailableAt = DateTimeOffset.UtcNow,
        };

        await outboxStore.EnqueueAsync(message, cancellationToken).ConfigureAwait(false);
    }
}
