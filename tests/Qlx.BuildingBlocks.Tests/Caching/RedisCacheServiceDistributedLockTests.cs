/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-23 00:00:00 +08:00 中国标准时间 星期五
 * 修改时间：2026-10-23 00:00:00 +08:00 中国标准时间 星期五
 * 描述：Redis 缓存提供方跨实例分布式锁（C-2-2）单元测试，基于假存储确定性验证
 */

using Microsoft.Extensions.Options;
using Qlx.BuildingBlocks.Caching;

namespace Qlx.BuildingBlocks.Tests.Caching;

public sealed class RedisCacheServiceDistributedLockTests
{
    private static RedisCacheService CreateService(FakeRedisStore store)
        => new(store, Options.Create(new CacheOptions { Provider = "Redis", InstanceName = "test" }));

    [Fact]
    public async Task ConcurrentCallsAcrossInstancesCoalesceToSingleFactoryCall()
    {
        var store = new FakeRedisStore();
        var serviceA = CreateService(store);
        var serviceB = CreateService(store);
        var factoryCalls = 0;

        Task<string> Call(ICacheService service) => service.GetOrCreateAsync(
            "t1:pur:shared",
            async cancellationToken =>
            {
                Interlocked.Increment(ref factoryCalls);
                await Task.Delay(100, cancellationToken);
                return "loaded";
            },
            cancellationToken: TestContext.Current.CancellationToken);

        var tasks = Enumerable.Range(0, 25).SelectMany(_ => new[] { Call(serviceA), Call(serviceB) }).ToArray();
        var results = await Task.WhenAll(tasks);

        Assert.All(results, value => Assert.Equal("loaded", value));
        Assert.Equal(1, factoryCalls);
    }

    [Fact]
    public async Task SecondCallHitsCacheWithoutFactory()
    {
        var store = new FakeRedisStore();
        var service = CreateService(store);
        var factoryCalls = 0;

        Task<string> Call() => service.GetOrCreateAsync(
            "t1:pur:order1",
            cancellationToken =>
            {
                Interlocked.Increment(ref factoryCalls);
                return Task.FromResult("v1");
            },
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("v1", await Call());
        Assert.Equal("v1", await Call());
        Assert.Equal(1, factoryCalls);
    }

    [Fact]
    public async Task FactoryExceptionReleasesLockAndDoesNotCache()
    {
        var store = new FakeRedisStore();
        var service = CreateService(store);
        var factoryCalls = 0;

        Task<string> Call(bool throwOnCall) => service.GetOrCreateAsync(
            "t1:pur:fragile",
            cancellationToken =>
            {
                var call = Interlocked.Increment(ref factoryCalls);
                if (throwOnCall && call == 1)
                {
                    throw new InvalidOperationException("回源失败");
                }

                return Task.FromResult("recovered");
            },
            cancellationToken: TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<InvalidOperationException>(() => Call(throwOnCall: true));

        // 回源失败不写缓存
        Assert.False(store.HasKey("test:t1:pur:fragile"));

        // 锁已释放：后续调用可重新获锁回源成功并写缓存
        Assert.Equal("recovered", await Call(throwOnCall: true));
        Assert.Equal(2, factoryCalls);
        Assert.True(store.HasKey("test:t1:pur:fragile"));
    }

    [Fact]
    public async Task LockHeldByOtherInstanceFallsBackToFactoryWithoutCaching()
    {
        var store = new FakeRedisStore();
        var service = CreateService(store);
        var factoryCalls = 0;

        // 他实例持锁不放（模拟长事务），本实例等待预算耗尽后回源但不写缓存
        store.HoldLock("test:t1:pur:busy:lock", "other-token", TimeSpan.FromMinutes(1));

        var value = await service.GetOrCreateAsync(
            "t1:pur:busy",
            cancellationToken =>
            {
                Interlocked.Increment(ref factoryCalls);
                return Task.FromResult("direct-load");
            },
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("direct-load", value);
        Assert.Equal(1, factoryCalls);
        Assert.False(store.HasKey("test:t1:pur:busy"));

        // 锁释放后再次调用：正常获锁回源并写缓存
        store.ReleaseLockByOwner("test:t1:pur:busy:lock", "other-token");
        var second = await service.GetOrCreateAsync(
            "t1:pur:busy",
            cancellationToken =>
            {
                Interlocked.Increment(ref factoryCalls);
                return Task.FromResult("cached-load");
            },
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("cached-load", second);
        Assert.Equal(2, factoryCalls);
        Assert.True(store.HasKey("test:t1:pur:busy"));
    }
}
