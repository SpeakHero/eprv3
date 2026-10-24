/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-21 06:30:00 +08:00 中国标准时间 星期三
 * 修改时间：2026-10-22 21:10:00 +08:00 中国标准时间 星期四
 * 描述：当前用户信息端点：/api/portal/me + 登出 /api/portal/logout（仅做 Claims 读取与响应封装）
 */

using System.Net.Mime;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OpenIddict.Validation.AspNetCore;
using Qlx.BuildingBlocks.Api;
using Qlx.Modules.Portal.Services;

namespace Qlx.Modules.Portal.Controllers;

/// <summary>
/// 门户身份信息控制器。返回当前 Token 主体对应的用户信息：
/// 身份级 Token 返回基础身份数据；租户级 Token 额外返回租户上下文与模块/权限清单。
/// 登出将当前 access token 的 jti 写入黑名单（规格 6.6.2 Layer 2 即时拦截），
/// 客户端应同时调用 /connect/revoke 吊销 refresh token（Layer 1）。
/// <para>分层职责（global-csharp-standard §7.1）：Claims 解析、用户/成员查询、DTO 组装、
/// 黑名单 TTL 计算均由 Service 层承载，控制器仅做响应封装。</para>
/// </summary>
[ApiController]
[Authorize(AuthenticationSchemes = OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme)]
[Route("api/portal")]
public sealed class MeController(
    ICurrentUserService currentUserService,
    IPortalLogoutService logoutService) : ControllerBase
{
    /// <summary>
    /// 获取当前登录用户信息。
    /// </summary>
    /// <param name="cancellationToken">请求中止令牌（框架绑定 RequestAborted）。</param>
    [HttpGet("me")]
    [Produces(MediaTypeNames.Application.Json)]
    public async Task<IActionResult> Me(CancellationToken cancellationToken)
    {
        var response = await currentUserService
            .GetCurrentUserAsync(User, cancellationToken)
            .ConfigureAwait(false);

        return response is null ? Unauthorized() : Ok(response);
    }

    /// <summary>
    /// 登出：将当前 access token 的 jti 写入黑名单（TTL = Token 剩余有效期），
    /// 后续持该 Token 的请求被 <see cref="Qlx.BuildingBlocks.Auth.TokenBlacklistMiddleware"/> 立即 401。
    /// </summary>
    /// <param name="cancellationToken">请求中止令牌（框架绑定 RequestAborted）。</param>
    [HttpPost("logout")]
    [Produces(MediaTypeNames.Application.Json)]
    public async Task<IActionResult> Logout(CancellationToken cancellationToken)
    {
        await logoutService.LogoutAsync(User, cancellationToken).ConfigureAwait(false);
        return Ok(ApiResponse.Ok("登出成功"));
    }
}
