/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-20 22:30:00 +08:00 中国标准时间 星期二
 * 修改时间：2026-10-20 22:30:00 +08:00 中国标准时间 星期二
 * 描述：进程内事件总线单元测试（多消费者分发、异常聚合、持久发布写 Outbox）
 */

using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Qlx.Abstractions.Events;
using Qlx.BuildingBlocks.EventBus;
using Qlx.BuildingBlocks.EventBus.Outbox;
using Qlx.BuildingBlocks.Ids;

namespace Qlx.BuildingBlocks.Tests.EventBus;

public sealed class InProcessEventBusTests
{
    public sealed record TestIntegrationEvent : IntegrationEvent
    {
        public required string PayloadText { get; init; }
    }

    public sealed class ReceivedRecorder
    {
        public ConcurrentQueue<string> Entries { get; } = new();
    }

    public sealed class FirstConsumer(ReceivedRecorder recorder) : IIntegrationEventConsumer<TestIntegrationEvent>
    {
        public Task ConsumeAsync(TestIntegrationEvent eventData, CancellationToken cancellationToken)
        {
            recorder.Entries.Enqueue($"first:{eventData.PayloadText}");
            return Task.CompletedTask;
        }
    }

    public sealed class SecondConsumer(ReceivedRecorder recorder) : IIntegrationEventConsumer<TestIntegrationEvent>
    {
        public Task ConsumeAsync(TestIntegrationEvent eventData, CancellationToken cancellationToken)
        {
            recorder.Entries.Enqueue($"second:{eventData.PayloadText}");
            return Task.CompletedTask;
        }
    }

    public sealed class FailingConsumer : IIntegrationEventConsumer<TestIntegrationEvent>
    {
        public Task ConsumeAsync(TestIntegrationEvent eventData, CancellationToken cancellationToken)
            => throw new InvalidOperationException("模拟消费失败");
    }

    private static (InProcessEventBus Bus, ReceivedRecorder Recorder, InMemoryOutboxStore Store) CreateBus(
        Action<IServiceCollection>? configureConsumers = null)
    {
        var services = new ServiceCollection();
        var recorder = new ReceivedRecorder();
        var store = new InMemoryOutboxStore();
        services.AddSingleton(recorder);
        configureConsumers?.Invoke(services);
        var provider = services.BuildServiceProvider();

        return (new InProcessEventBus(provider.GetRequiredService<IServiceScopeFactory>(), store, new SnowflakeIdGenerator(1)), recorder, store);
    }

    [Fact]
    public async Task PublishAsyncDispatchesToAllConsumers()
    {
        var (bus, recorder, _) = CreateBus(services =>
        {
            services.AddScoped<IIntegrationEventConsumer<TestIntegrationEvent>, FirstConsumer>();
            services.AddScoped<IIntegrationEventConsumer<TestIntegrationEvent>, SecondConsumer>();
        });

        await bus.PublishAsync(new TestIntegrationEvent { PayloadText = "hello" }, TestContext.Current.CancellationToken);

        Assert.Equal(2, recorder.Entries.Count);
        Assert.Contains("first:hello", recorder.Entries);
        Assert.Contains("second:hello", recorder.Entries);
    }

    [Fact]
    public async Task PublishAsyncAggregatesFailuresWithoutBlockingOthers()
    {
        var (bus, recorder, _) = CreateBus(services =>
        {
            services.AddScoped<IIntegrationEventConsumer<TestIntegrationEvent>, FailingConsumer>();
            services.AddScoped<IIntegrationEventConsumer<TestIntegrationEvent>, FirstConsumer>();
        });

        await Assert.ThrowsAsync<AggregateException>(
            () => bus.PublishAsync(new TestIntegrationEvent { PayloadText = "x" }, TestContext.Current.CancellationToken));

        Assert.Contains("first:x", recorder.Entries);
    }

    [Fact]
    public async Task PublishDurableAsyncEnqueuesOutboxMessage()
    {
        var (bus, _, store) = CreateBus();

        await bus.PublishDurableAsync(
            new TestIntegrationEvent { PayloadText = "durable", TenantCode = "t001" },
            TestContext.Current.CancellationToken);

        var leased = await store.LeaseBatchAsync("owner-test", 10, TimeSpan.FromMinutes(1), TestContext.Current.CancellationToken);

        var message = Assert.Single(leased);
        Assert.Equal(typeof(TestIntegrationEvent).AssemblyQualifiedName, message.EventType);
        Assert.Equal("t001", message.TenantCode);
        Assert.Contains("durable", message.Payload, StringComparison.Ordinal);
    }
}
