/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-21 06:30:00 +08:00 中国标准时间 星期三
 * 修改时间：2026-10-26
 * 描述：OpenIddict Token 端点控制器：仅做 grant_type 分发、HTTP 参数提取与响应封装，业务逻辑由 IPortalAuthenticationService 承载
 */

using System.Net.Mime;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using OpenIddict.Abstractions;
using OpenIddict.Server;
using OpenIddict.Server.AspNetCore;
using OpenIddict.Validation.AspNetCore;
using Qlx.Abstractions.Domain;
using Qlx.Modules.Portal.Auth;
using Qlx.Modules.Portal.Services;

namespace Qlx.Modules.Portal.Controllers;

/// <summary>
/// 认证端点控制器。实现架构规格 6.3 三阶段登录：
/// <list type="number">
/// <item>Phase 1 身份认证：<c>grant_type=urn:qlx:grant:portal_identity</c> → 身份级 Token + 租户列表</item>
/// <item>身份 Token 刷新：标准 <c>refresh_token</c> grant（身份级 refresh token 自动轮换）</item>
/// <item>Phase 3 租户上下文：<c>grant_type=urn:qlx:grant:tenant_context</c>，
/// 凭 identity access token + 混淆 tenant_id 换取租户级 Token（仅 access token，无 refresh token）</item>
/// <item>租户级 Token 静默续期：<c>grant_type=urn:qlx:grant:tenant_silent</c>，
/// 凭宽限期内旧租户 access token 换取新租户级 access token，无需重新登录</item>
/// </list>
/// 吊销走标准 <c>/connect/revoke</c>（OpenIddict 内部处理，无需 Passthrough）。
/// <para>分层职责（global-csharp-standard §7.1）：本控制器只负责 grant_type 分发、HTTP 请求/响应转换与
/// OpenIddict 结果封装；客户端校验、账号锁定、密码校验、成员关系与 Token 版本校验、Claims 构造
/// 全部由 <see cref="IPortalAuthenticationService"/> 承载。</para>
/// </summary>
[ApiController]
public sealed class AuthorizationController(
    IPortalAuthenticationService authenticationService,
    IUserIpAddressProvider userIpAddressProvider) : ControllerBase
{
    /// <summary>客户端来源 IP（审计扩展信息）。统一经 <see cref="IUserIpAddressProvider"/> 解析，不直接读连接远端地址。</summary>
    private string ClientIpAddress => userIpAddressProvider.GetCurrentUserIp() ?? string.Empty;

    /// <summary>
    /// /connect/token：按 grant_type 分发到对应阶段处理器。
    /// </summary>
    /// <param name="cancellationToken">请求中止令牌（框架绑定 RequestAborted）。</param>
    [HttpPost("~/connect/token")]
    [Produces(MediaTypeNames.Application.Json)]
    public async Task<IActionResult> Exchange(CancellationToken cancellationToken)
    {
        var request = HttpContext.GetOpenIddictServerRequest()
            ?? throw new InvalidOperationException("OpenIddict Token 请求上下文缺失");

        if (request.GrantType == QlxAuthConstants.GrantTypes.PortalIdentity)
        {
            // 规格参数名 login_account；兼容标准 username 参数
            var loginAccount = (string?)request[PortalConstants.Parameters.LoginAccount] ?? request.Username;
            var result = await authenticationService.AuthenticateIdentityAsync(
                    request.ClientId,
                    loginAccount,
                    (string?)request[PortalConstants.Parameters.Password],
                    ClientIpAddress,
                    cancellationToken)
                .ConfigureAwait(false);
            return ToActionResult(result);
        }

        if (request.GrantType == QlxAuthConstants.GrantTypes.TenantContext)
        {
            // 身份 access token 经 Bearer 头由 OpenIddict Validation 中间件完成签名/受众/有效期/吊销校验
            //（自定义 grant 不自动验证 refresh_token 参数）
            var identityAuth = await HttpContext
                .AuthenticateAsync(OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme)
                .ConfigureAwait(false);

            if (identityAuth is not { Succeeded: true })
            {
                return Reject(OpenIddictConstants.Errors.InvalidGrant, "身份令牌无效或已过期");
            }

            var result = await authenticationService.EnterTenantContextAsync(
                    request.ClientId,
                    (string?)request[PortalConstants.Parameters.TenantId],
                    (string?)request[PortalConstants.Parameters.MemberRoleId],
                    identityAuth.Principal!,
                    ClientIpAddress,
                    cancellationToken)
                .ConfigureAwait(false);
            return ToActionResult(result);
        }

        if (request.GrantType == QlxAuthConstants.GrantTypes.TenantSilent)
        {
            var authHeader = HttpContext.Request.Headers.Authorization.ToString();
            if (string.IsNullOrWhiteSpace(authHeader)
                || !authHeader.StartsWith(PortalConstants.Endpoints.BearerPrefix, StringComparison.OrdinalIgnoreCase))
            {
                return Reject(OpenIddictConstants.Errors.InvalidRequest, "缺少有效的租户令牌（Authorization: Bearer 头）");
            }

            var bearerToken = authHeader[PortalConstants.Endpoints.BearerPrefix.Length..].Trim();
            var result = await authenticationService.RenewTenantTokenAsync(
                    request.ClientId, bearerToken, ClientIpAddress, cancellationToken)
                .ConfigureAwait(false);
            return ToActionResult(result);
        }

        if (request.IsRefreshTokenGrantType())
        {
            var refreshAuth = await HttpContext
                .AuthenticateAsync(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme)
                .ConfigureAwait(false);

            if (refreshAuth is not { Succeeded: true })
            {
                return Reject(OpenIddictConstants.Errors.InvalidGrant, "刷新令牌无效或已过期");
            }

            var result = await authenticationService
                .RefreshIdentityAsync(refreshAuth.Principal!, cancellationToken)
                .ConfigureAwait(false);
            return ToActionResult(result);
        }

        return Reject(OpenIddictConstants.Errors.UnsupportedGrantType, "不支持的授权类型");
    }

    /// <summary>业务认证结果 → OpenIddict 响应（成功签发 Token，失败返回 Forbid 错误）。</summary>
    private IActionResult ToActionResult(PortalAuthResult result)
    {
        if (!result.Succeeded)
        {
            return Reject(result.Error!, result.ErrorDescription!);
        }

        var properties = new AuthenticationProperties();
        foreach (var (key, value) in result.ResponseParameters)
        {
            properties.Parameters[key] = value;
        }

        return SignIn(result.Principal!, properties, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    /// <summary>构造 OpenIddict 错误响应（透传至 Token 端点 JSON）。</summary>
    private ForbidResult Reject(string error, string description) => Forbid(
        new AuthenticationProperties(new Dictionary<string, string?>
        {
            [OpenIddictServerAspNetCoreConstants.Properties.Error] = error,
            [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = description,
        }),
        OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
}
