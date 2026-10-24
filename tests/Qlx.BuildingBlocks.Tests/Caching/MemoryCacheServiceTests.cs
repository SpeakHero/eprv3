/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-20 22:30:00 +08:00 中国标准时间 星期二
 * 修改时间：2026-10-20 22:30:00 +08:00 中国标准时间 星期二
 * 描述：内存缓存提供方单元测试（读写删、前缀清除、GetOrCreate 并发合并）
 */

using Microsoft.Extensions.Options;
using Qlx.BuildingBlocks.Caching;

namespace Qlx.BuildingBlocks.Tests.Caching;

public sealed class MemoryCacheServiceTests
{
    private static MemoryCacheService CreateCache()
        => new(Options.Create(new CacheOptions { Provider = "Memory", InstanceName = "test" }));

    [Fact]
    public async Task SetThenGetReturnsValue()
    {
        using var cache = CreateCache();

        await cache.SetAsync("t1:pur:order1", "value-1", cancellationToken: TestContext.Current.CancellationToken);
        var hit = await cache.GetAsync<string>("t1:pur:order1", TestContext.Current.CancellationToken);

        Assert.Equal("value-1", hit);
    }

    [Fact]
    public async Task GetMissingKeyReturnsDefault()
    {
        using var cache = CreateCache();

        var hit = await cache.GetAsync<string>("t1:pur:missing", TestContext.Current.CancellationToken);

        Assert.Null(hit);
    }

    [Fact]
    public async Task RemoveDeletesEntry()
    {
        using var cache = CreateCache();

        await cache.SetAsync("t1:pur:order1", "value-1", cancellationToken: TestContext.Current.CancellationToken);
        var removed = await cache.RemoveAsync("t1:pur:order1", TestContext.Current.CancellationToken);
        var hit = await cache.GetAsync<string>("t1:pur:order1", TestContext.Current.CancellationToken);

        Assert.True(removed);
        Assert.Null(hit);
    }

    [Fact]
    public async Task RemoveByPrefixClearsOnlyMatchingKeys()
    {
        using var cache = CreateCache();
        var cancellationToken = TestContext.Current.CancellationToken;

        await cache.SetAsync("t1:pur:a", 1, cancellationToken: cancellationToken);
        await cache.SetAsync("t1:pur:b", 2, cancellationToken: cancellationToken);
        await cache.SetAsync("t1:sal:c", 3, cancellationToken: cancellationToken);

        var removed = await cache.RemoveByPrefixAsync("t1:pur:", cancellationToken);

        Assert.Equal(2, removed);
        Assert.Null(await cache.GetAsync<int?>("t1:pur:a", cancellationToken));
        Assert.Null(await cache.GetAsync<int?>("t1:pur:b", cancellationToken));
        Assert.Equal(3, await cache.GetAsync<int>("t1:sal:c", cancellationToken));
    }

    [Fact]
    public async Task GetOrCreateCoalescesConcurrentFactoryCalls()
    {
        using var cache = CreateCache();
        var factoryCalls = 0;

        var tasks = Enumerable.Range(0, 20)
            .Select(_ => cache.GetOrCreateAsync(
                "t1:pur:shared",
                async cancellationToken =>
                {
                    Interlocked.Increment(ref factoryCalls);
                    await Task.Delay(50, cancellationToken);
                    return "loaded";
                },
                cancellationToken: TestContext.Current.CancellationToken))
            .ToArray();

        var results = await Task.WhenAll(tasks);

        Assert.All(results, value => Assert.Equal("loaded", value));
        Assert.Equal(1, factoryCalls);
    }
}
