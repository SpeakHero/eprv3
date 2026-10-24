/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-21 06:30:00 +08:00 中国标准时间 星期三
 * 修改时间：2026-10-21 06:30:00 +08:00 中国标准时间 星期三
 * 描述：Token 黑名单中间件单元测试（命中 401、未命中放行、匿名放行）
 */

using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Qlx.BuildingBlocks.Auth;
using Qlx.BuildingBlocks.Caching;

namespace Qlx.BuildingBlocks.Tests.Auth;

public sealed class TokenBlacklistMiddlewareTests
{
    private static MemoryCacheService CreateCache()
        => new(Options.Create(new CacheOptions { Provider = "Memory", InstanceName = "test" }));

    private static (HttpContext Context, bool NextCalled) BuildContext(
        ICacheService cache,
        bool authenticated = true,
        string? jti = null)
    {
        var services = new ServiceCollection().AddSingleton(cache).BuildServiceProvider();
        var context = new DefaultHttpContext { RequestServices = services };
        if (authenticated)
        {
            var identity = new ClaimsIdentity("Bearer");
            if (jti is not null)
            {
                identity.AddClaim(new Claim("jti", jti));
            }

            context.User = new ClaimsPrincipal(identity);
        }

        var nextCalled = false;
        return (context, nextCalled);
    }

    private static async Task<(int StatusCode, bool NextCalled)> InvokeAsync(HttpContext context, ICacheService cache)
    {
        var nextCalled = false;
        var middleware = new TokenBlacklistMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(context, cache);
        return (context.Response.StatusCode, nextCalled);
    }

    [Fact]
    public async Task BlacklistedJtiReturns401WithoutCallingNext()
    {
        var cache = CreateCache();
        await cache.SetAsync(QlxCacheKey.TokenBlacklistPrefix + "jti-1", "revoked",
            cancellationToken: TestContext.Current.CancellationToken);
        var (context, _) = BuildContext(cache, jti: "jti-1");

        var (status, nextCalled) = await InvokeAsync(context, cache);

        Assert.Equal(StatusCodes.Status401Unauthorized, status);
        Assert.False(nextCalled);
    }

    [Fact]
    public async Task NonBlacklistedJtiCallsNext()
    {
        var cache = CreateCache();
        var (context, _) = BuildContext(cache, jti: "jti-live");

        var (status, nextCalled) = await InvokeAsync(context, cache);

        Assert.Equal(StatusCodes.Status200OK, status);
        Assert.True(nextCalled);
    }

    [Fact]
    public async Task AuthenticatedWithoutJtiCallsNext()
    {
        var cache = CreateCache();
        var (context, _) = BuildContext(cache, jti: null);

        var (status, nextCalled) = await InvokeAsync(context, cache);

        Assert.Equal(StatusCodes.Status200OK, status);
        Assert.True(nextCalled);
    }

    [Fact]
    public async Task AnonymousRequestCallsNext()
    {
        var cache = CreateCache();
        var (context, _) = BuildContext(cache, authenticated: false, jti: "jti-ignored");

        var (status, nextCalled) = await InvokeAsync(context, cache);

        Assert.Equal(StatusCodes.Status200OK, status);
        Assert.True(nextCalled);
    }
}
