/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-23 00:00:00 +08:00 中国标准时间 星期五
 * 修改时间：2026-10-23 00:00:00 +08:00 中国标准时间 星期五
 * 描述：RemoveByPrefixAsync 分批删除（C-6-2：SCAN 分批 ≤500/批 + 批间让出）单元测试
 */

using Microsoft.Extensions.Options;
using Qlx.BuildingBlocks.Caching;

namespace Qlx.BuildingBlocks.Tests.Caching;

public sealed class RemoveByPrefixBatchTests
{
    private static RedisCacheService CreateRedis(FakeRedisStore store)
        => new(store, Options.Create(new CacheOptions { Provider = "Redis", InstanceName = "test" }));

    [Fact]
    public async Task RemoveByPrefixDeletesInBatchesOfAtMost500()
    {
        var store = new FakeRedisStore();
        var cache = CreateRedis(store);

        // 预置 1200 个匹配键 + 2 个不匹配键
        for (var i = 0; i < 1200; i++)
        {
            await store.StringSetAsync($"test:t1:pur:key{i}", "1", TimeSpan.FromMinutes(10));
        }

        await store.StringSetAsync("test:t1:sal:other", "1", TimeSpan.FromMinutes(10));
        await store.StringSetAsync("test:t2:pur:otherTenant", "1", TimeSpan.FromMinutes(10));

        var removed = await cache.RemoveByPrefixAsync("t1:pur:", TestContext.Current.CancellationToken);

        Assert.Equal(1200, removed);
        // 分批：500 + 500 + 200，单批不超 500
        Assert.Equal(3, store.DeleteBatchSizes.Count);
        Assert.All(store.DeleteBatchSizes, size => Assert.InRange(size, 1, 500));
        Assert.Equal(1200, store.DeleteBatchSizes.Sum());

        // 不匹配键保留
        Assert.True(store.HasKey("test:t1:sal:other"));
        Assert.True(store.HasKey("test:t2:pur:otherTenant"));
    }

    [Fact]
    public async Task RemoveByPrefixWithNoMatchDeletesNothing()
    {
        var store = new FakeRedisStore();
        var cache = CreateRedis(store);

        var removed = await cache.RemoveByPrefixAsync("t1:nothing:", TestContext.Current.CancellationToken);

        Assert.Equal(0, removed);
        Assert.Empty(store.DeleteBatchSizes);
    }

    [Fact]
    public async Task RemoveByPrefixSmallSetUsesSingleBatch()
    {
        var store = new FakeRedisStore();
        var cache = CreateRedis(store);

        for (var i = 0; i < 3; i++)
        {
            await store.StringSetAsync($"test:t1:pur:small{i}", "1", TimeSpan.FromMinutes(10));
        }

        var removed = await cache.RemoveByPrefixAsync("t1:pur:", TestContext.Current.CancellationToken);

        Assert.Equal(3, removed);
        Assert.Equal([3], store.DeleteBatchSizes);
    }
}
