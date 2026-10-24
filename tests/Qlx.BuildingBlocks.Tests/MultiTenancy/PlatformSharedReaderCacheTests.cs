/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-23 00:00:00 +08:00 中国标准时间 星期五
 * 修改时间：2026-10-23 00:00:00 +08:00 中国标准时间 星期五
 * 描述：PlatformSharedReader 迁移 ICacheService 后的短缓存命中 / 失效 / 故障降级语义回归测试
 */

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Qlx.BuildingBlocks.Caching;
using Qlx.BuildingBlocks.MultiTenancy;
using Qlx.BuildingBlocks.MultiTenancy.PlatformShared;
using Qlx.BuildingBlocks.Tests.Portal;
using PlatformSharedOptions = Qlx.BuildingBlocks.MultiTenancy.PlatformShared.PlatformSharedOptions;

namespace Qlx.BuildingBlocks.Tests.MultiTenancy;

public sealed class PlatformSharedReaderCacheTests
{
    private static readonly string s_cacheKey = QlxCacheKey.BuildPortalKey("msg", "glb-messages:active");

    private static MemoryCacheService CreateCache()
        => new(Options.Create(new CacheOptions { Provider = "Memory", InstanceName = "test" }));

    private static PlatformSharedDbContext CreateInMemoryContext(string databaseName)
    {
        var options = new DbContextOptionsBuilder<PlatformSharedDbContext>()
            .UseInMemoryDatabase(databaseName)
            .Options;
        return new PlatformSharedDbContext(new NoopResolver(), tenantId: 0, options);
    }

    private static GlobalMessageEntity NewMessage(long id) => new()
    {
        Id = id,
        MsgType = "ANN",
        Subject = "系统维护通知",
        Content = "本周日凌晨例行维护",
        PublishedBy = "portal-admin",
        PublishedAt = DateTimeOffset.UtcNow,
    };

    [Fact]
    public async Task SecondCallHitsCacheWithoutRefetch()
    {
        using var cache = CreateCache();
        var databaseName = Guid.NewGuid().ToString("N");
        var factoryCalls = 0;

        await using (var seed = CreateInMemoryContext(databaseName))
        {
            seed.Messages.Add(NewMessage(9001));
            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var reader = new PlatformSharedReader(
            new FakeFactory(() =>
            {
                Interlocked.Increment(ref factoryCalls);
                return CreateInMemoryContext(databaseName);
            }),
            cache,
            Options.Create(new PlatformSharedOptions()),
            new StubIdObfuscator(),
            NullLogger<PlatformSharedReader>.Instance);

        var first = await reader.GetActiveMessagesAsync(TestContext.Current.CancellationToken);
        var second = await reader.GetActiveMessagesAsync(TestContext.Current.CancellationToken);

        Assert.Single(first);
        Assert.Equal("pub-9001", first[0].GlobalId);
        Assert.Single(second);
        Assert.Equal(1, factoryCalls);
    }

    [Fact]
    public async Task CacheRemoveTriggersRefetch()
    {
        using var cache = CreateCache();
        var databaseName = Guid.NewGuid().ToString("N");
        var factoryCalls = 0;

        await using (var seed = CreateInMemoryContext(databaseName))
        {
            seed.Messages.Add(NewMessage(9002));
            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var reader = new PlatformSharedReader(
            new FakeFactory(() =>
            {
                Interlocked.Increment(ref factoryCalls);
                return CreateInMemoryContext(databaseName);
            }),
            cache,
            Options.Create(new PlatformSharedOptions()),
            new StubIdObfuscator(),
            NullLogger<PlatformSharedReader>.Instance);

        _ = await reader.GetActiveMessagesAsync(TestContext.Current.CancellationToken);
        await cache.RemoveAsync(s_cacheKey, TestContext.Current.CancellationToken);
        var after = await reader.GetActiveMessagesAsync(TestContext.Current.CancellationToken);

        Assert.Single(after);
        Assert.Equal(2, factoryCalls);
    }

    [Fact]
    public async Task SharedDatabaseFailureDegradesToEmptyList()
    {
        using var cache = CreateCache();
        var reader = new PlatformSharedReader(
            new FakeFactory(() => throw new InvalidOperationException("共享库不可达")),
            cache,
            Options.Create(new PlatformSharedOptions()),
            new StubIdObfuscator(),
            NullLogger<PlatformSharedReader>.Instance);

        var result = await reader.GetActiveMessagesAsync(TestContext.Current.CancellationToken);

        Assert.Empty(result);
    }

    [Fact]
    public async Task CachedResultSurvivesLaterFailure()
    {
        using var cache = CreateCache();
        var databaseName = Guid.NewGuid().ToString("N");
        var fail = false;

        await using (var seed = CreateInMemoryContext(databaseName))
        {
            seed.Messages.Add(NewMessage(9003));
            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var reader = new PlatformSharedReader(
            new FakeFactory(() => fail
                ? throw new InvalidOperationException("共享库不可达")
                : CreateInMemoryContext(databaseName)),
            cache,
            Options.Create(new PlatformSharedOptions()),
            new StubIdObfuscator(),
            NullLogger<PlatformSharedReader>.Instance);

        var first = await reader.GetActiveMessagesAsync(TestContext.Current.CancellationToken);
        fail = true;
        var second = await reader.GetActiveMessagesAsync(TestContext.Current.CancellationToken);

        Assert.Single(first);
        Assert.Single(second);
        Assert.Equal(first[0].GlobalId, second[0].GlobalId);
    }

    private sealed class FakeFactory(Func<PlatformSharedDbContext> create) : IDbContextFactory<PlatformSharedDbContext>
    {
        public PlatformSharedDbContext CreateDbContext() => create();

        public Task<PlatformSharedDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(create());
    }

    private sealed class NoopResolver : ITenantDatabaseResolver
    {
        public TenantConnectionInfo Resolve(long tenantId) => throw new NotSupportedException();

        public Task PreloadAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public void Invalidate(long tenantId)
        {
        }
    }
}
