/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-22 21:10:00 +08:00 中国标准时间 星期四
 * 描述：登出服务：将当前 access token 的 jti 写入黑名单（TTL = Token 剩余有效期）
 */

using System.Globalization;
using System.Security.Claims;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Qlx.Abstractions.Constants;
using Qlx.BuildingBlocks.Caching;
using Qlx.Modules.Portal.Auth;

namespace Qlx.Modules.Portal.Services;

/// <summary>门户登出契约。</summary>
public interface IPortalLogoutService
{
    /// <summary>将当前 access token 的 jti 写入黑名单（规格 6.6.2 Layer 2 即时拦截）。</summary>
    /// <param name="principal">当前请求的 Claims 主体。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task LogoutAsync(ClaimsPrincipal principal, CancellationToken cancellationToken);
}

/// <summary>
/// 登出服务实现。jti 缺失时无即时拦截手段，直接返回成功（客户端仍应调用 /connect/revoke 吊销 refresh token）。
/// </summary>
public sealed partial class PortalLogoutService(
    ICacheService cache,
    IOptions<QlxAuthOptions> authOptions,
    ILogger<PortalLogoutService> logger) : IPortalLogoutService
{
    /// <summary>LoggerMessage 源生成器要求字段（主构造参数经字段初始化器接入）。</summary>
    private readonly ILogger<PortalLogoutService> _logger = logger;

    /// <inheritdoc />
    public async Task LogoutAsync(ClaimsPrincipal principal, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(principal);

        var jti = principal.FindFirstValue(QlxClaimConstants.JwtId);
        if (string.IsNullOrWhiteSpace(jti))
        {
            // Token 无 jti（理论不发生）：无即时拦截手段，按成功处理
            return;
        }

        var ttl = ResolveRemainingLifetime(principal);
        await cache.SetAsync(
            QlxCacheKey.TokenBlacklistPrefix + jti,
            PortalConstants.LogoutBlacklistValue,
            cancellationToken,
            new CacheEntryOptions { AbsoluteExpirationRelativeToNow = ttl }).ConfigureAwait(false);

        LogLogout(principal.FindFirstValue(QlxAuthConstants.Claims.LoginAccount) ?? PortalConstants.UnknownValue, jti);
    }

    /// <summary>计算当前 access token 剩余有效期（exp claim 缺失时退化为配置的 Token 生命周期）。</summary>
    private TimeSpan ResolveRemainingLifetime(ClaimsPrincipal principal)
    {
        if (long.TryParse(
                principal.FindFirstValue(QlxClaimConstants.Expiration),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var expSeconds))
        {
            var remaining = DateTimeOffset.FromUnixTimeSeconds(expSeconds) - DateTimeOffset.UtcNow;
            if (remaining > TimeSpan.Zero)
            {
                return remaining;
            }
        }

        return TimeSpan.FromMinutes(authOptions.Value.AccessTokenLifetimeMinutes);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "用户登出成功：{LoginAccount}（jti 已入黑名单：{TokenId}）")]
    private partial void LogLogout(string loginAccount, string tokenId);
}
