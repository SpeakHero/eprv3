/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-23 00:00:00 +08:00 中国标准时间 星期五
 * 修改时间：2026-10-23 00:00:00 +08:00 中国标准时间 星期五
 * 描述：缓存坏值自愈（C-5-3：反序列化失败按未命中处理并清除坏键）单元测试
 */

using Microsoft.Extensions.Options;
using Qlx.BuildingBlocks.Caching;

namespace Qlx.BuildingBlocks.Tests.Caching;

public sealed class BadValueSelfHealTests
{
    private static RedisCacheService CreateRedis(FakeRedisStore store)
        => new(store, Options.Create(new CacheOptions { Provider = "Redis", InstanceName = "test" }));

    [Fact]
    public async Task GetWithCorruptedValueReturnsDefaultAndPurgesKey()
    {
        var store = new FakeRedisStore();
        var cache = CreateRedis(store);

        // 手工写入非法 JSON 载荷
        await store.StringSetAsync("test:t1:pur:bad", "{not-json", TimeSpan.FromMinutes(10));

        var hit = await cache.GetAsync<OrderDto>("t1:pur:bad", TestContext.Current.CancellationToken);

        Assert.Null(hit);
        Assert.False(store.HasKey("test:t1:pur:bad"), "坏键应被清除");
    }

    [Fact]
    public async Task GetOrCreateWithCorruptedValueRefetchesAndCaches()
    {
        var store = new FakeRedisStore();
        var cache = CreateRedis(store);
        var factoryCalls = 0;

        await store.StringSetAsync("test:t1:pur:bad2", "[[[", TimeSpan.FromMinutes(10));

        var value = await cache.GetOrCreateAsync(
            "t1:pur:bad2",
            cancellationToken =>
            {
                Interlocked.Increment(ref factoryCalls);
                return Task.FromResult(new OrderDto(42, "ok"));
            },
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(42, value.Id);
        Assert.Equal(1, factoryCalls);

        // 回源后已写入正常值，后续读取命中缓存
        var hit = await cache.GetAsync<OrderDto>("t1:pur:bad2", TestContext.Current.CancellationToken);
        Assert.NotNull(hit);
        Assert.Equal("ok", hit.Name);
        Assert.Equal(1, factoryCalls);
    }

    [Fact]
    public async Task TypeMismatchValueAlsoSelfHeals()
    {
        var store = new FakeRedisStore();
        var cache = CreateRedis(store);

        // 载荷是合法 JSON 但与目标类型不匹配（JSON 字符串反序列化为记录类型）
        await store.StringSetAsync("test:t1:pur:mismatch", "\"plain-string\"", TimeSpan.FromMinutes(10));

        var hit = await cache.GetAsync<OrderDto>("t1:pur:mismatch", TestContext.Current.CancellationToken);

        Assert.Null(hit);
        Assert.False(store.HasKey("test:t1:pur:mismatch"));
    }

    private sealed record OrderDto(long Id, string Name);
}
