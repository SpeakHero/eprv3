/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-20 22:30:00 +08:00 中国标准时间 星期二
 * 修改时间：2026-10-20 22:30:00 +08:00 中国标准时间 星期二
 * 描述：内存 Outbox 存储单元测试（租约互斥、完成置位、失败退避重排）
 */

using Qlx.BuildingBlocks.EventBus.Outbox;

namespace Qlx.BuildingBlocks.Tests.EventBus;

public sealed class InMemoryOutboxStoreTests
{
    private static OutboxMessage CreateMessage(long id) => new()
    {
        Id = id,
        EventType = "Test.Event",
        Payload = "{}",
        OccurredAt = DateTimeOffset.UtcNow,
        AvailableAt = DateTimeOffset.UtcNow,
    };

    [Fact]
    public async Task LeaseIsMutuallyExclusiveBetweenOwners()
    {
        var store = new InMemoryOutboxStore();
        var cancellationToken = TestContext.Current.CancellationToken;
        await store.EnqueueAsync(CreateMessage(1L), cancellationToken);

        var firstLease = await store.LeaseBatchAsync("owner-1", 10, TimeSpan.FromMinutes(5), cancellationToken);
        var secondLease = await store.LeaseBatchAsync("owner-2", 10, TimeSpan.FromMinutes(5), cancellationToken);

        Assert.Single(firstLease);
        Assert.Empty(secondLease);
    }

    [Fact]
    public async Task CompleteMarksMessageProcessed()
    {
        var store = new InMemoryOutboxStore();
        var cancellationToken = TestContext.Current.CancellationToken;
        await store.EnqueueAsync(CreateMessage(2L), cancellationToken);

        var leased = await store.LeaseBatchAsync("owner-1", 10, TimeSpan.FromMinutes(5), cancellationToken);
        await store.CompleteAsync(leased[0].Id, "owner-1", cancellationToken);

        var reLeased = await store.LeaseBatchAsync("owner-2", 10, TimeSpan.FromMinutes(5), cancellationToken);
        Assert.Empty(reLeased);
        Assert.NotNull(leased[0].ProcessedAt);
    }

    [Fact]
    public async Task CompleteWithWrongOwnerIsIgnored()
    {
        var store = new InMemoryOutboxStore();
        var cancellationToken = TestContext.Current.CancellationToken;
        await store.EnqueueAsync(CreateMessage(3L), cancellationToken);

        var leased = await store.LeaseBatchAsync("owner-1", 10, TimeSpan.FromMinutes(5), cancellationToken);
        await store.CompleteAsync(leased[0].Id, "owner-2", cancellationToken);

        Assert.Null(leased[0].ProcessedAt);
    }

    [Fact]
    public async Task FailReleasesLeaseAndDefersAvailability()
    {
        var store = new InMemoryOutboxStore();
        var cancellationToken = TestContext.Current.CancellationToken;
        await store.EnqueueAsync(CreateMessage(4L), cancellationToken);

        var leased = await store.LeaseBatchAsync("owner-1", 10, TimeSpan.FromMinutes(5), cancellationToken);
        await store.FailAsync(leased[0].Id, "owner-1", "模拟失败", TimeSpan.FromHours(1), cancellationToken);

        var deferred = await store.LeaseBatchAsync("owner-2", 10, TimeSpan.FromMinutes(5), cancellationToken);
        Assert.Empty(deferred);
        Assert.Equal("模拟失败", leased[0].LastError);
    }

    [Fact]
    public async Task FailWithZeroDelayMakesMessageImmediatelyReleasable()
    {
        var store = new InMemoryOutboxStore();
        var cancellationToken = TestContext.Current.CancellationToken;
        await store.EnqueueAsync(CreateMessage(5L), cancellationToken);

        var first = await store.LeaseBatchAsync("owner-1", 10, TimeSpan.FromMinutes(5), cancellationToken);
        await store.FailAsync(first[0].Id, "owner-1", "重试", TimeSpan.Zero, cancellationToken);

        var second = await store.LeaseBatchAsync("owner-2", 10, TimeSpan.FromMinutes(5), cancellationToken);
        Assert.Single(second);
        Assert.Equal(2, second[0].AttemptCount);
    }
}
