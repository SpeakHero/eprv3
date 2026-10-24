/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-20 22:00:00 +08:00 中国标准时间 星期二
 * 修改时间：2026-10-20 22:00:00 +08:00 中国标准时间 星期二
 * 描述：Outbox 后台调度器，轮询领取消息、反序列化并分发给订阅者，at-least-once
 */

using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Qlx.Abstractions.Events;
using Qlx.Abstractions.Serialization;

namespace Qlx.BuildingBlocks.EventBus.Outbox;

/// <summary>
/// Outbox 后台调度器。以固定间隔轮询存储，行锁租约领取批量消息，
/// 按事件类型反序列化后解析全部订阅者顺序派发；成功置完成、失败按线性退避重试，
/// 超过最大尝试次数的消息停留待人工介入。at-least-once：消费者必须幂等。
/// </summary>
public sealed partial class OutboxDispatcher(
    IServiceScopeFactory scopeFactory,
    IOutboxStore store,
    IOptions<OutboxOptions> options,
    ILogger<OutboxDispatcher> logger) : BackgroundService
{
    private static readonly JsonSerializerOptions s_serializerOptions = QlxJsonDefaults.Default;

    private readonly OutboxOptions _options = options.Value;
    private readonly string _owner = $"{Environment.MachineName}-{Environment.ProcessId}";

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(_options.PollIntervalMs));

        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            try
            {
                await DispatchBatchAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                LogDispatchBatchFailed(exception);
            }
        }
    }

    private async Task DispatchBatchAsync(CancellationToken cancellationToken)
    {
        var messages = await store.LeaseBatchAsync(
            _owner,
            _options.BatchSize,
            TimeSpan.FromSeconds(_options.LeaseSeconds),
            cancellationToken).ConfigureAwait(false);

        foreach (var message in messages)
        {
            await DispatchMessageAsync(message, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task DispatchMessageAsync(OutboxMessage message, CancellationToken cancellationToken)
    {
        try
        {
            if (message.AttemptCount > _options.MaxAttempts)
            {
                LogMaxAttemptsExceeded(message.Id, message.EventType, message.AttemptCount);
                await store.CompleteAsync(message.Id, _owner, cancellationToken).ConfigureAwait(false);
                return;
            }

            var eventType = Type.GetType(message.EventType);
            if (eventType is null || !typeof(IntegrationEvent).IsAssignableFrom(eventType))
            {
                throw new InvalidOperationException($"无法解析事件类型 {message.EventType}");
            }

            if (JsonSerializer.Deserialize(message.Payload, eventType, s_serializerOptions) is not IntegrationEvent eventData)
            {
                throw new InvalidOperationException($"事件负载反序列化为空：{message.EventType}");
            }

            await using var scope = scopeFactory.CreateAsyncScope();
            var consumerType = typeof(IIntegrationEventConsumer<>).MakeGenericType(eventType);
            var failures = 0;

            foreach (var consumer in scope.ServiceProvider.GetServices(consumerType))
            {
                if (consumer is null)
                {
                    continue;
                }

                try
                {
                    await ((dynamic)consumer).ConsumeAsync((dynamic)eventData, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    failures++;
                    LogConsumerFailed(exception, message.Id, eventType.Name);
                }
            }

            if (failures > 0)
            {
                throw new InvalidOperationException($"{failures} 个消费者处理失败");
            }

            await store.CompleteAsync(message.Id, _owner, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            var retryDelay = TimeSpan.FromSeconds(_options.RetryDelaySeconds * (double)message.AttemptCount);
            await store.FailAsync(message.Id, _owner, exception.Message, retryDelay, cancellationToken).ConfigureAwait(false);
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Outbox 批量投递发生未处理异常")]
    private partial void LogDispatchBatchFailed(Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Outbox 消息 {MessageId} 消费者 {EventType} 处理失败")]
    private partial void LogConsumerFailed(Exception exception, long messageId, string eventType);

    [LoggerMessage(Level = LogLevel.Error, Message = "Outbox 消息 {MessageId}（{EventType}）已达最大尝试次数 {AttemptCount}，停留待人工介入")]
    private partial void LogMaxAttemptsExceeded(long messageId, string eventType, int attemptCount);
}
