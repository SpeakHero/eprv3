/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-24 10:00:00 +08:00 中国标准时间 星期六
 * 描述：租户预配进度推送器测试（事件信封字段 / SSE 未注册静默跳过 / 推送异常吞掉不影响预配）
 */

using Microsoft.Extensions.Logging.Abstractions;
using Qlx.Abstractions.Sse;
using Qlx.BuildingBlocks.MultiTenancy;
using Qlx.BuildingBlocks.Sse;

namespace Qlx.BuildingBlocks.Tests.MultiTenancy;

/// <summary>
/// <see cref="TenantProvisioningProgressPublisher"/> 单元测试：尽力而为语义与信封字段完整性。
/// </summary>
public sealed class TenantProvisioningProgressPublisherTests
{
    private const long TenantId = 42L;

    /// <summary>收集推送事件的 SSE 管理器桩（可模拟推送异常）。</summary>
    private sealed class FakeSseManager : ISseConnectionManager
    {
        private readonly bool _throwOnPush;

        public FakeSseManager(bool throwOnPush = false) => _throwOnPush = throwOnPush;

        public List<SseEvent> Events { get; } = [];

        public Task RegisterConnectionAsync(
            long userId, string connectionId, long tenantId, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task UnregisterConnectionAsync(
            long userId, string connectionId, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task<int> GetConnectionCountAsync(long tenantId, CancellationToken cancellationToken)
            => Task.FromResult(0);

        public Task PushAsync(SseEvent sseEvent, CancellationToken cancellationToken)
        {
            if (_throwOnPush)
            {
                throw new InvalidOperationException("SSE 推送失败（测试桩）");
            }

            Events.Add(sseEvent);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<SseEvent>> GetEventsSinceAsync(
            long tenantId, long lastEventId, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<SseEvent>>([]);
    }

    private static TenantProvisioningProgressPublisher CreatePublisher(params ISseConnectionManager[] managers)
        => new(managers, NullLogger<TenantProvisioningProgressPublisher>.Instance);

    [Fact]
    public async Task PublishSendsSingleProvisioningProgressEvent()
    {
        var manager = new FakeSseManager();
        var publisher = CreatePublisher(manager);

        await publisher.PublishAsync(
            TenantId,
            TenantProvisioningSteps.Migrating,
            10,
            TenantProvisioningStatuses.Provisioning,
            TestContext.Current.CancellationToken);

        var sseEvent = Assert.Single(manager.Events);
        Assert.Equal(SseEventType.TenantProvisioningProgress, sseEvent.Type);

        var envelope = SseEventEnvelope.TryParse(sseEvent.DataJson);
        Assert.NotNull(envelope);
        Assert.Equal(TenantId, envelope.TenantId);
        Assert.Equal(TenantProvisioningSteps.Migrating, envelope.Step);
        Assert.Equal(10, envelope.Progress);
        Assert.Equal(TenantProvisioningStatuses.Provisioning, envelope.Status);
        Assert.Equal("tenant.provisioning", envelope.BizType);
        Assert.Equal("42", envelope.BizId);
    }

    [Fact]
    public async Task PublishWithoutRegisteredSseManagerIsNoOp()
    {
        var publisher = CreatePublisher();

        await publisher.PublishAsync(
            TenantId,
            TenantProvisioningSteps.Completed,
            100,
            TenantProvisioningStatuses.Active,
            TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task PublishSwallowsPushFailure()
    {
        var publisher = CreatePublisher(new FakeSseManager(throwOnPush: true));

        await publisher.PublishAsync(
            TenantId,
            TenantProvisioningSteps.Failed,
            0,
            TenantProvisioningStatuses.Failed,
            TestContext.Current.CancellationToken);
    }
}
