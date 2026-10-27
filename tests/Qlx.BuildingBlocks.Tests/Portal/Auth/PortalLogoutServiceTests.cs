/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-25
 * 描述：PortalLogoutService 白盒测试——jti 缺失短路、exp 剩余期 TTL、exp 过期/非法回退默认 TTL、空 principal 防御
 */

using System.Security.Claims;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Qlx.Abstractions.Constants;
using Qlx.BuildingBlocks.Caching;
using Qlx.Modules.Portal.Auth;
using Qlx.Modules.Portal.Services;

namespace Qlx.BuildingBlocks.Tests.Portal.Auth;

public sealed class PortalLogoutServiceTests
{
    private readonly StubCacheService _cache = new();

    private PortalLogoutService CreateService(int accessTokenLifetimeMinutes = 120)
        => new(
            _cache,
            Options.Create(new QlxAuthOptions { AccessTokenLifetimeMinutes = accessTokenLifetimeMinutes }),
            NullLogger<PortalLogoutService>.Instance);

    [Fact]
    public async Task LogoutAsyncNullPrincipalThrows()
    {
        var service = CreateService();

        _ = await Assert.ThrowsAsync<ArgumentNullException>(
            () => service.LogoutAsync(null!, TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task LogoutAsyncMissingJtiSkipsBlacklist(string? jti)
    {
        var service = CreateService();
        var claims = new List<Claim>();
        if (jti is not null)
        {
            claims.Add(new Claim(QlxClaimConstants.JwtId, jti));
        }

        await service.LogoutAsync(
            new ClaimsPrincipal(new ClaimsIdentity(claims)), TestContext.Current.CancellationToken);

        Assert.Empty(_cache.Snapshot);
    }

    [Fact]
    public async Task LogoutAsyncWritesBlacklistWithRemainingLifetimeTtl()
    {
        var service = CreateService();
        var expSeconds = DateTimeOffset.UtcNow.AddMinutes(30).ToUnixTimeSeconds();
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(QlxClaimConstants.JwtId, "jti-001"),
            new Claim(QlxClaimConstants.Expiration, expSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            new Claim(QlxAuthConstants.Claims.LoginAccount, "admin"),
        ]));

        await service.LogoutAsync(principal, TestContext.Current.CancellationToken);

        // 黑名单键 = 前缀 + jti，值为锁定标记
        Assert.True(_cache.Snapshot.ContainsKey(QlxCacheKey.TokenBlacklistPrefix + "jti-001"));
    }

    [Fact]
    public async Task LogoutAsyncExpiredExpFallsBackToConfiguredLifetime()
    {
        // exp 已过期 → remaining ≤ 0 → 使用配置默认生命周期（120 分钟）
        var service = CreateService(accessTokenLifetimeMinutes: 45);
        var expired = DateTimeOffset.UtcNow.AddMinutes(-5).ToUnixTimeSeconds();
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(QlxClaimConstants.JwtId, "jti-002"),
            new Claim(QlxClaimConstants.Expiration, expired.ToString(System.Globalization.CultureInfo.InvariantCulture)),
        ]));

        await service.LogoutAsync(principal, TestContext.Current.CancellationToken);

        Assert.True(_cache.Snapshot.ContainsKey(QlxCacheKey.TokenBlacklistPrefix + "jti-002"));
    }

    [Theory]
    [InlineData("not-a-number")]   // 非法 exp
    [InlineData("  ")]             // 空白 exp
    public async Task LogoutAsyncMalformedExpFallsBackToConfiguredLifetime(string exp)
    {
        var service = CreateService();
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(QlxClaimConstants.JwtId, "jti-003"),
            new Claim(QlxClaimConstants.Expiration, exp),
        ]));

        await service.LogoutAsync(principal, TestContext.Current.CancellationToken);

        Assert.True(_cache.Snapshot.ContainsKey(QlxCacheKey.TokenBlacklistPrefix + "jti-003"));
    }

    [Fact]
    public async Task LogoutAsyncWithoutExpClaimFallsBackToConfiguredLifetime()
    {
        var service = CreateService();
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(QlxClaimConstants.JwtId, "jti-004"),
        ]));

        await service.LogoutAsync(principal, TestContext.Current.CancellationToken);

        Assert.True(_cache.Snapshot.ContainsKey(QlxCacheKey.TokenBlacklistPrefix + "jti-004"));
    }
}
