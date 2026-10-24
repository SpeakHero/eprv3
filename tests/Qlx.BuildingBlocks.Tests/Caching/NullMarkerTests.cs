/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-23 00:00:00 +08:00 中国标准时间 星期五
 * 修改时间：2026-10-23 00:00:00 +08:00 中国标准时间 星期五
 * 描述：NullMarker 空值缓存（C-3 防穿透）单元测试，覆盖 Redis 与内存两提供方
 */

using Microsoft.Extensions.Options;
using Qlx.BuildingBlocks.Caching;

namespace Qlx.BuildingBlocks.Tests.Caching;

public sealed class NullMarkerTests
{
    private static RedisCacheService CreateRedis(FakeRedisStore store)
        => new(store, Options.Create(new CacheOptions { Provider = "Redis", InstanceName = "test" }));

    private static MemoryCacheService CreateMemory()
        => new(Options.Create(new CacheOptions { Provider = "Memory", InstanceName = "test" }));

    [Fact]
    public async Task RedisFactoryReturningNullCachesNullMarkerAndSkipsRefetch()
    {
        var store = new FakeRedisStore();
        var cache = CreateRedis(store);
        var factoryCalls = 0;

        Task<string?> Load() => cache.GetOrCreateAsync<string?>(
            "t1:pur:missing",
            cancellationToken =>
            {
                Interlocked.Increment(ref factoryCalls);
                return Task.FromResult<string?>(null);
            },
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Null(await Load());
        Assert.Null(await Load());
        Assert.Equal(1, factoryCalls);

        // 空值缓存以原始哨兵字符串存储（绕过 JSON）
        Assert.Equal("qlx:null", store.GetRawValue("test:t1:pur:missing"));

        // 对 GetAsync 调用方透明：命中空值缓存等同未命中语义但不回源
        Assert.Null(await cache.GetAsync<string>("t1:pur:missing", TestContext.Current.CancellationToken));
        Assert.Equal(1, factoryCalls);
    }

    [Fact]
    public async Task RedisNullMarkerTtlIsShortAndConfigurable()
    {
        var store = new FakeRedisStore();
        var cache = CreateRedis(store);

        // 默认空值 TTL：30s 基线 ±10% 抖动（含执行偏移余量）
        await cache.GetOrCreateAsync<string?>(
            "t1:pur:nullDefault",
            _ => Task.FromResult<string?>(null),
            cancellationToken: TestContext.Current.CancellationToken);
        var defaultTtl = store.GetRemainingTtl("test:t1:pur:nullDefault");
        Assert.NotNull(defaultTtl);
        Assert.InRange(defaultTtl.Value, TimeSpan.FromSeconds(25), TimeSpan.FromSeconds(33));

        // 显式配置空值 TTL 60s：±10% 抖动（含执行偏移余量）
        await cache.GetOrCreateAsync<string?>(
            "t1:pur:nullCustom",
            _ => Task.FromResult<string?>(null),
            cancellationToken: TestContext.Current.CancellationToken,
            options: new CacheEntryOptions { NullValueExpiration = TimeSpan.FromSeconds(60) });
        var customTtl = store.GetRemainingTtl("test:t1:pur:nullCustom");
        Assert.NotNull(customTtl);
        Assert.InRange(customTtl.Value, TimeSpan.FromSeconds(52), TimeSpan.FromSeconds(66));
    }

    [Fact]
    public async Task RedisExplicitSetNullCachesNullMarker()
    {
        var store = new FakeRedisStore();
        var cache = CreateRedis(store);
        var factoryCalls = 0;

        await cache.SetAsync<string?>("t1:pur:explicitNull", null, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Null(await cache.GetAsync<string>("t1:pur:explicitNull", TestContext.Current.CancellationToken));
        Assert.Equal("qlx:null", store.GetRawValue("test:t1:pur:explicitNull"));

        // 读穿路径命中空值缓存，不触发回源
        var value = await cache.GetOrCreateAsync<string?>(
            "t1:pur:explicitNull",
            cancellationToken =>
            {
                Interlocked.Increment(ref factoryCalls);
                return Task.FromResult<string?>("should-not-load");
            },
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.Null(value);
        Assert.Equal(0, factoryCalls);
    }

    [Fact]
    public async Task MemoryFactoryReturningNullCachesNullMarkerAndSkipsRefetch()
    {
        using var cache = CreateMemory();
        var factoryCalls = 0;

        Task<string?> Load() => cache.GetOrCreateAsync<string?>(
            "t1:pur:missing",
            cancellationToken =>
            {
                Interlocked.Increment(ref factoryCalls);
                return Task.FromResult<string?>(null);
            },
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Null(await Load());
        Assert.Null(await Load());
        Assert.Equal(1, factoryCalls);
        Assert.Null(await cache.GetAsync<string>("t1:pur:missing", TestContext.Current.CancellationToken));
        Assert.Equal(1, factoryCalls);
    }

    [Fact]
    public async Task MemoryExplicitSetNullCachesNullMarker()
    {
        using var cache = CreateMemory();
        var factoryCalls = 0;

        await cache.SetAsync<string?>("t1:pur:explicitNull", null, cancellationToken: TestContext.Current.CancellationToken);

        var value = await cache.GetOrCreateAsync<string?>(
            "t1:pur:explicitNull",
            cancellationToken =>
            {
                Interlocked.Increment(ref factoryCalls);
                return Task.FromResult<string?>("should-not-load");
            },
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.Null(value);
        Assert.Equal(0, factoryCalls);
    }

    [Fact]
    public async Task MemoryNullMarkerExpiresAndRefetches()
    {
        using var cache = CreateMemory();
        var factoryCalls = 0;
        var options = new CacheEntryOptions { NullValueExpiration = TimeSpan.FromMilliseconds(150) };

        Task<string?> Load() => cache.GetOrCreateAsync<string?>(
            "t1:pur:shortNull",
            cancellationToken =>
            {
                Interlocked.Increment(ref factoryCalls);
                return Task.FromResult<string?>(null);
            },
            cancellationToken: TestContext.Current.CancellationToken,
            options: options);

        Assert.Null(await Load());
        Assert.Null(await Load());
        Assert.Equal(1, factoryCalls);

        // 空值缓存过期后重新回源
        await Task.Delay(400, TestContext.Current.CancellationToken);
        Assert.Null(await Load());
        Assert.Equal(2, factoryCalls);
    }

    [Fact]
    public async Task NormalValueIsNotConfusedWithNullMarker()
    {
        var store = new FakeRedisStore();
        var cache = CreateRedis(store);

        await cache.SetAsync("t1:pur:real", "qlx:null-ish", cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal("qlx:null-ish", await cache.GetAsync<string>("t1:pur:real", TestContext.Current.CancellationToken));

        // 业务字符串 "null" 正常 JSON 序列化存储，不会被判为空值缓存
        await cache.SetAsync("t1:pur:literal", "null", cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal("null", await cache.GetAsync<string>("t1:pur:literal", TestContext.Current.CancellationToken));
    }
}
