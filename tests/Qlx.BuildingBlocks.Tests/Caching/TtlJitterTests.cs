/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-23 00:00:00 +08:00 中国标准时间 星期五
 * 修改时间：2026-10-23 00:00:00 +08:00 中国标准时间 星期五
 * 描述：TTL 抖动（C-4 防雪崩）单元测试，覆盖抖动助手与 Redis 提供方写入路径
 */

using Microsoft.Extensions.Options;
using Qlx.BuildingBlocks.Caching;

namespace Qlx.BuildingBlocks.Tests.Caching;

public sealed class TtlJitterTests
{
    private static RedisCacheService CreateRedis(FakeRedisStore store)
        => new(store, Options.Create(new CacheOptions { Provider = "Redis", InstanceName = "test" }));

    [Fact]
    public void HelperDisabledJitterReturnsBaseline()
    {
        var baseline = TimeSpan.FromMinutes(5);
        Assert.Equal(baseline, CacheTtlJitter.Apply(baseline, disableJitter: true));
    }

    [Fact]
    public void HelperJitterStaysWithinTenPercentAndDisperses()
    {
        var baseline = TimeSpan.FromSeconds(100);
        var samples = Enumerable.Range(0, 1000).Select(_ => CacheTtlJitter.Apply(baseline, disableJitter: false)).ToArray();

        Assert.All(samples, ttl => Assert.InRange(ttl, TimeSpan.FromSeconds(90), TimeSpan.FromSeconds(110)));
        Assert.True(samples.Distinct().Count() > 500, "抖动样本应高度离散");
    }

    [Fact]
    public async Task RedisSetDispersesTtlWithinTenPercent()
    {
        var store = new FakeRedisStore();
        var cache = CreateRedis(store);
        var baseline = TimeSpan.FromSeconds(1000);

        for (var i = 0; i < 200; i++)
        {
            await cache.SetAsync($"t1:pur:jitter{i}", i, cancellationToken: TestContext.Current.CancellationToken,
                options: new CacheEntryOptions { AbsoluteExpirationRelativeToNow = baseline });
        }

        var ttls = Enumerable.Range(0, 200)
            .Select(i => store.GetRemainingTtl($"test:t1:pur:jitter{i}")!.Value)
            .ToArray();

        // 全部落在 ±10% 区间（留 5s 执行偏移余量）
        Assert.All(ttls, ttl => Assert.InRange(ttl, TimeSpan.FromSeconds(895), TimeSpan.FromSeconds(1100)));
        // 离散无聚集
        Assert.True(ttls.Distinct().Count() > 100, "TTL 应离散分布而非聚集");
        // 统计上必然同时出现低于与高于基线的样本
        Assert.Contains(ttls, ttl => ttl < TimeSpan.FromSeconds(980));
        Assert.Contains(ttls, ttl => ttl > TimeSpan.FromSeconds(1020));
    }

    [Fact]
    public async Task RedisSetWithDisableJitterUsesExactBaseline()
    {
        var store = new FakeRedisStore();
        var cache = CreateRedis(store);
        var baseline = TimeSpan.FromSeconds(500);

        await cache.SetAsync("t1:pur:exact", 1, cancellationToken: TestContext.Current.CancellationToken,
            options: new CacheEntryOptions { AbsoluteExpirationRelativeToNow = baseline, DisableJitter = true });

        var ttl = store.GetRemainingTtl("test:t1:pur:exact");
        Assert.NotNull(ttl);
        Assert.InRange(ttl.Value, TimeSpan.FromSeconds(495), TimeSpan.FromSeconds(500));
    }

    [Fact]
    public async Task RedisDefaultExpirationAlsoJittered()
    {
        var store = new FakeRedisStore();
        var cache = CreateRedis(store);

        // 默认基线 1800s（CacheOptions.DefaultExpirationSeconds）
        await cache.SetAsync("t1:pur:defaultTtl", 1, cancellationToken: TestContext.Current.CancellationToken);

        var ttl = store.GetRemainingTtl("test:t1:pur:defaultTtl");
        Assert.NotNull(ttl);
        Assert.InRange(ttl.Value, TimeSpan.FromSeconds(1615), TimeSpan.FromSeconds(1980));
    }
}
