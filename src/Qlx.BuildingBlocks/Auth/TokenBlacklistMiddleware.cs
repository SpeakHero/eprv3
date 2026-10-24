/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-21 06:30:00 +08:00 中国标准时间 星期三
 * 修改时间：2026-10-21 06:30:00 +08:00 中国标准时间 星期三
 * 描述：Token 黑名单中间件：认证后检查 jti 是否被拉黑，命中立即 401（架构规格 6.6 Layer 2）
 */

using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Qlx.Abstractions.Constants;
using Qlx.BuildingBlocks.Caching;

namespace Qlx.BuildingBlocks.Auth;

/// <summary>
/// Token 黑名单校验中间件（架构规格 6.6.2 双重保障之 Layer 2：即时短窗口拦截）。
/// 对已认证请求提取 <c>jti</c>，命中黑名单键 <c>token:blacklist:{jti}</c>（TTL = Token 剩余有效期）
/// 立即返回 401；未认证请求与匿名端点直通。写入侧由 Portal 认证流程（登出 / 吊销 / 停用）负责。
/// </summary>
public sealed class TokenBlacklistMiddleware(RequestDelegate next)
{
    private const string JwtIdClaim = QlxClaimConstants.JwtId;

    /// <inheritdoc />
    public async Task InvokeAsync(HttpContext context, ICacheService cache)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.User.Identity?.IsAuthenticated == true)
        {
            var jti = context.User.FindFirstValue(JwtIdClaim);
            if (!string.IsNullOrWhiteSpace(jti))
            {
                var reason = await cache
                    .GetAsync<string>(QlxCacheKey.TokenBlacklistPrefix + jti, context.RequestAborted)
                    .ConfigureAwait(false);

                if (reason is not null)
                {
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    context.Response.Headers.WWWAuthenticate =
                        $"Bearer error=\"invalid_token\", error_description=\"{reason}\"";
                    return;
                }
            }
        }

        await next(context).ConfigureAwait(false);
    }
}

/// <summary>
/// 中间件注册扩展。
/// </summary>
public static class TokenBlacklistMiddlewareExtensions
{
    /// <summary>
    /// 注册 Token 黑名单校验中间件。必须置于 <c>UseAuthentication</c> 之后。
    /// </summary>
    public static IApplicationBuilder UseTokenBlacklist(this IApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.UseMiddleware<TokenBlacklistMiddleware>();
    }
}
