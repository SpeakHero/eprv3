/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-23 09:20:00 +08:00 中国标准时间 星期五
 * 描述：SSE 连接管理器测试——信封路由红线（无信封拒发 / 不跨租户串流）、
 *       单投 / 广播、事件 Id 单调分配、环形缓冲断线续传（窗口内 / 窗外 / 未来 Id）、连接注册注销
 */

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Qlx.Abstractions.Sse;
using Qlx.BuildingBlocks.Sse;

namespace Qlx.BuildingBlocks.Tests.Sse;

/// <summary>
/// <see cref="SseConnectionManager"/> 单元测试（进程内实现；双接口同一实例）。
/// </summary>
public sealed class SseConnectionManagerTests
{
    private const long TenantId = 7L;
    private const long OtherTenantId = 9L;
    private const long UserA = 1001L;
    private const long UserB = 2002L;

    private static SseConnectionManager CreateManager(int bufferCapacity = 10)
        => new(
            Options.Create(new QlxSseOptions { BufferCapacity = bufferCapacity, ChannelCapacity = 8 }),
            NullLogger<SseConnectionManager>.Instance);

    private static SseEvent BuildEvent(long tenantId, long? recipient, string subject = "标题")
        => new(
            0,
            SseEventType.MessageCreated,
            SseEventEnvelope.Serialize(
                new SseEventEnvelope(tenantId, recipient, subject, "正文", "pur", "1", "CG-202610-0001")),
            DateTimeOffset.UtcNow);

    private static async Task<SseClientLease> OpenAsync(
        SseConnectionManager manager, long userId, string connectionId, long tenantId)
        => await manager.OpenStreamAsync(userId, connectionId, tenantId, TestContext.Current.CancellationToken);

    [Fact]
    public async Task PushRoutesEventToTargetRecipientOnly()
    {
        var manager = CreateManager();
        await using var leaseA = await OpenAsync(manager, UserA, "conn-a", TenantId);
        await using var leaseB = await OpenAsync(manager, UserB, "conn-b", TenantId);

        await manager.PushAsync(BuildEvent(TenantId, UserA), TestContext.Current.CancellationToken);

        Assert.True(leaseA.Reader.TryRead(out var received));
        Assert.Equal(SseEventType.MessageCreated, received.Type);
        Assert.False(leaseB.Reader.TryRead(out _));
    }

    [Fact]
    public async Task PushWithoutRecipientBroadcastsToAllTenantConnections()
    {
        var manager = CreateManager();
        await using var leaseA = await OpenAsync(manager, UserA, "conn-a", TenantId);
        await using var leaseB = await OpenAsync(manager, UserB, "conn-b", TenantId);

        await manager.PushAsync(BuildEvent(TenantId, null), TestContext.Current.CancellationToken);

        Assert.True(leaseA.Reader.TryRead(out _));
        Assert.True(leaseB.Reader.TryRead(out _));
    }

    [Fact]
    public async Task PushDoesNotCrossTenantBoundary()
    {
        var manager = CreateManager();
        await using var otherTenantLease = await OpenAsync(manager, UserA, "conn-t9", OtherTenantId);
        await using var lease = await OpenAsync(manager, UserA, "conn-t7", TenantId);

        await manager.PushAsync(BuildEvent(TenantId, UserA), TestContext.Current.CancellationToken);

        Assert.False(otherTenantLease.Reader.TryRead(out _));
        Assert.True(lease.Reader.TryRead(out _));
    }

    [Fact]
    public async Task PushWithoutEnvelopeIsRejected()
    {
        var manager = CreateManager();
        await using var lease = await OpenAsync(manager, UserA, "conn-a", TenantId);

        // DataJson 无合法路由信封：整条拒发（防跨租户串流红线），且不抛异常
        await manager.PushAsync(
            new SseEvent(0, SseEventType.SystemAlert, "not-a-json", DateTimeOffset.UtcNow),
            TestContext.Current.CancellationToken);

        Assert.False(lease.Reader.TryRead(out _));
    }

    [Fact]
    public async Task EventIdsAreMonotonicWithinTenantAndIndependentAcrossTenants()
    {
        var manager = CreateManager();

        await manager.PushAsync(BuildEvent(TenantId, UserA), TestContext.Current.CancellationToken);
        await manager.PushAsync(BuildEvent(TenantId, UserA), TestContext.Current.CancellationToken);
        await manager.PushAsync(BuildEvent(OtherTenantId, UserA), TestContext.Current.CancellationToken);

        var events = await manager.GetEventsSinceAsync(TenantId, 0, TestContext.Current.CancellationToken);
        Assert.Equal([1L, 2L], events.Select(static e => e.EventId).ToArray());
        var otherEvents = await manager.GetEventsSinceAsync(OtherTenantId, 0, TestContext.Current.CancellationToken);
        var other = Assert.Single(otherEvents);
        Assert.Equal(1, other.EventId);
    }

    [Fact]
    public async Task TryReplayWithinWindowReturnsEventsAfterLastEventId()
    {
        var manager = CreateManager();
        for (var i = 0; i < 4; i++)
        {
            await manager.PushAsync(BuildEvent(TenantId, null), TestContext.Current.CancellationToken);
        }

        var replay = await manager.TryReplayAsync(TenantId, 2, TestContext.Current.CancellationToken);

        Assert.True(replay.IsInRange);
        Assert.Equal([3L, 4L], replay.Events.Select(static e => e.EventId).ToArray());
    }

    [Fact]
    public async Task TryReplayWithFutureIdIsOutOfRange()
    {
        var manager = CreateManager();
        await manager.PushAsync(BuildEvent(TenantId, null), TestContext.Current.CancellationToken);

        var replay = await manager.TryReplayAsync(TenantId, 99, TestContext.Current.CancellationToken);

        Assert.False(replay.IsInRange);
        Assert.Empty(replay.Events);
    }

    [Fact]
    public async Task TryReplayBeforeBufferedWindowIsOutOfRange()
    {
        var manager = CreateManager(bufferCapacity: 10);
        for (var i = 0; i < 12; i++)
        {
            await manager.PushAsync(BuildEvent(TenantId, null), TestContext.Current.CancellationToken);
        }

        // 容量 10 已分配 12：窗口 (2, 12]，Last-Event-ID=1 已被淘汰 → 判不在范围（防伪造）
        var replay = await manager.TryReplayAsync(TenantId, 1, TestContext.Current.CancellationToken);

        Assert.False(replay.IsInRange);
        Assert.Empty(replay.Events);
    }

    [Fact]
    public async Task TryReplayOnTenantWithoutBufferIsOutOfRange()
    {
        var manager = CreateManager();

        var replay = await manager.TryReplayAsync(TenantId, 5, TestContext.Current.CancellationToken);

        Assert.False(replay.IsInRange);
        Assert.Empty(replay.Events);
    }

    [Fact]
    public async Task TryReplayWithNullLastEventIdTreatedAsNewConnection()
    {
        var manager = CreateManager();

        var replay = await manager.TryReplayAsync(TenantId, null, TestContext.Current.CancellationToken);

        Assert.True(replay.IsInRange);
        Assert.Empty(replay.Events);
    }

    [Fact]
    public async Task UnregisterConnectionCompletesChannelAndRemovesRegistration()
    {
        var manager = CreateManager();
        var lease = await OpenAsync(manager, UserA, "conn-a", TenantId);

        await manager.UnregisterConnectionAsync(UserA, "conn-a", TestContext.Current.CancellationToken);

        Assert.Equal(0, await manager.GetConnectionCountAsync(TenantId, TestContext.Current.CancellationToken));
        await lease.DisposeAsync();
    }

    [Fact]
    public async Task ReRegisterWithSameConnectionIdClosesStaleChannel()
    {
        var manager = CreateManager();
        var stale = await OpenAsync(manager, UserA, "conn-a", TenantId);
        var fresh = await OpenAsync(manager, UserA, "conn-a", TenantId);

        await manager.PushAsync(BuildEvent(TenantId, UserA), TestContext.Current.CancellationToken);

        Assert.False(stale.Reader.TryRead(out _));
        Assert.True(fresh.Reader.TryRead(out _));
        await stale.DisposeAsync();
        await fresh.DisposeAsync();
    }
}
