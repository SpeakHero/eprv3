/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-25
 * 修改时间：2026-10-26
 * 描述：AuthorizationController 白盒测试——薄分发层全分支覆盖：
 *       请求上下文缺失防御、四种 grant_type 分发（portal_identity / tenant_context / tenant_silent / refresh_token）、
 *       参数提取与回退（login_account 兼容 username、Bearer 头解析）、认证失败短路、
 *       成功/失败结果封装（SignIn / Forbid + 扩展参数透传）、来源 IP 提取与空回退。
 */

using System.Net;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using OpenIddict.Abstractions;
using OpenIddict.Server;
using OpenIddict.Server.AspNetCore;
using OpenIddict.Validation.AspNetCore;
using Qlx.BuildingBlocks.Auth;
using Qlx.Modules.Portal;
using Qlx.Modules.Portal.Auth;
using Qlx.Modules.Portal.Controllers;
using Qlx.Modules.Portal.Services;

namespace Qlx.BuildingBlocks.Tests.Portal.Auth;

public sealed class AuthorizationControllerTests
{
    private static readonly IPAddress ClientIp = IPAddress.Parse("10.9.8.7");

    private readonly StubPortalAuthenticationService _service = new();
    private readonly StubAuthenticationService _authentication = new();

    private AuthorizationController CreateController(
        OpenIddictRequest? request, bool nullRemoteIp = false, string? authorizationHeader = null)
    {
        var services = new ServiceCollection()
            .AddSingleton<IAuthenticationService>(_authentication)
            .BuildServiceProvider();

        var httpContext = new DefaultHttpContext
        {
            RequestServices = services,
        };
        httpContext.Connection.RemoteIpAddress = nullRemoteIp ? null : ClientIp;

        var accessor = new HttpContextAccessor { HttpContext = httpContext };

        // OpenIddict Server 中间件正常管道会把 Transaction 写入 Feature；此处手工构造等价上下文
        httpContext.Features.Set(new OpenIddictServerAspNetCoreFeature
        {
            Transaction = new OpenIddictServerTransaction
            {
                Request = request,
            },
        });

        if (authorizationHeader is not null)
        {
            httpContext.Request.Headers.Authorization = authorizationHeader;
        }

        return new AuthorizationController(_service, new AspNetCoreUserIpAddressProvider(accessor))
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext },
        };
    }

    private static OpenIddictRequest Request(string grantType) => new() { GrantType = grantType, ClientId = "portal-web" };

    private static ClaimsPrincipal Principal(string subject = "pub-1") =>
        new(new ClaimsIdentity([new Claim(OpenIddictConstants.Claims.Subject, subject)]));

    // ============ 请求上下文防御 ============

    [Fact]
    public async Task ExchangeRequestContextMissingThrows()
    {
        var controller = CreateController(request: null);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => controller.Exchange(TestContext.Current.CancellationToken));
    }

    // ============ portal_identity 分发 ============

    [Fact]
    public async Task ExchangePortalIdentitySuccessReturnsSignInWithParameters()
    {
        var request = Request(QlxAuthConstants.GrantTypes.PortalIdentity);
        request[PortalConstants.Parameters.LoginAccount] = "alice";
        request[PortalConstants.Parameters.Password] = "pw";
        var principal = Principal();
        _service.IdentityResult = PortalAuthResult.Success(
            principal, new Dictionary<string, string?> { ["tenants"] = "[]" });
        var controller = CreateController(request);

        var result = await controller.Exchange(TestContext.Current.CancellationToken);

        var signIn = Assert.IsType<SignInResult>(result);
        Assert.Equal(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme, signIn.AuthenticationScheme);
        Assert.Same(principal, signIn.Principal);
        Assert.Equal("[]", signIn.Properties!.Parameters["tenants"]);
        Assert.Equal("portal-web", _service.IdentityClientId);
        Assert.Equal("alice", _service.IdentityLoginAccount);
        Assert.Equal("pw", _service.IdentityPassword);
        Assert.Equal(ClientIp.ToString(), _service.IdentityClientIp);
    }

    [Fact]
    public async Task ExchangePortalIdentityLoginAccountFallsBackToUsername()
    {
        var request = Request(QlxAuthConstants.GrantTypes.PortalIdentity);
        request.Username = "bob";
        _service.IdentityResult = PortalAuthResult.Failure("invalid_grant", "x");
        var controller = CreateController(request);

        await controller.Exchange(TestContext.Current.CancellationToken);

        Assert.Equal("bob", _service.IdentityLoginAccount);
    }

    [Fact]
    public async Task ExchangePortalIdentityFailureReturnsForbidWithError()
    {
        var request = Request(QlxAuthConstants.GrantTypes.PortalIdentity);
        _service.IdentityResult = PortalAuthResult.Failure(
            OpenIddictConstants.Errors.InvalidGrant, "账号或密码错误，或账号已被锁定");
        var controller = CreateController(request);

        var result = await controller.Exchange(TestContext.Current.CancellationToken);

        var forbid = Assert.IsType<ForbidResult>(result);
        Assert.Contains(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme, forbid.AuthenticationSchemes);
        Assert.Equal(OpenIddictConstants.Errors.InvalidGrant,
            forbid.Properties!.Items[OpenIddictServerAspNetCoreConstants.Properties.Error]);
        Assert.Equal("账号或密码错误，或账号已被锁定",
            forbid.Properties.Items[OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription]);
    }

    [Fact]
    public async Task ExchangePortalIdentityNullRemoteIpPassesEmptyString()
    {
        var request = Request(QlxAuthConstants.GrantTypes.PortalIdentity);
        _service.IdentityResult = PortalAuthResult.Success(Principal());
        var controller = CreateController(request, nullRemoteIp: true);

        await controller.Exchange(TestContext.Current.CancellationToken);

        Assert.Equal(string.Empty, _service.IdentityClientIp);
    }

    // ============ tenant_context 分发 ============

    [Fact]
    public async Task ExchangeTenantContextIdentityAuthFailedReturnsForbid()
    {
        var request = Request(QlxAuthConstants.GrantTypes.TenantContext);
        _authentication.ValidationResult = AuthenticateResult.Fail("expired");
        var controller = CreateController(request);

        var result = await controller.Exchange(TestContext.Current.CancellationToken);

        var forbid = Assert.IsType<ForbidResult>(result);
        Assert.Equal(OpenIddictConstants.Errors.InvalidGrant,
            forbid.Properties!.Items[OpenIddictServerAspNetCoreConstants.Properties.Error]);
        Assert.Null(_service.TenantPrincipal);
    }

    [Fact]
    public async Task ExchangeTenantContextSuccessCallsServiceWithClaims()
    {
        var request = Request(QlxAuthConstants.GrantTypes.TenantContext);
        request[PortalConstants.Parameters.TenantId] = "pub-9";
        request[PortalConstants.Parameters.MemberRoleId] = "pub-5";
        var identity = Principal();
        _authentication.ValidationResult = AuthenticateResult.Success(
            new AuthenticationTicket(identity, OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme));
        _service.TenantResult = PortalAuthResult.Success(Principal("pub-2"));
        var controller = CreateController(request);

        var result = await controller.Exchange(TestContext.Current.CancellationToken);

        Assert.IsType<SignInResult>(result);
        Assert.Same(identity, _service.TenantPrincipal);
        Assert.Equal("pub-9", _service.TenantIdRaw);
        Assert.Equal("pub-5", _service.MemberRoleIdRaw);
    }

    // ============ tenant_silent 分发 ============

    [Fact]
    public async Task ExchangeTenantSilentMissingAuthorizationHeaderReturnsForbid()
    {
        var request = Request(QlxAuthConstants.GrantTypes.TenantSilent);
        var controller = CreateController(request);

        var result = await controller.Exchange(TestContext.Current.CancellationToken);

        var forbid = Assert.IsType<ForbidResult>(result);
        Assert.Equal(OpenIddictConstants.Errors.InvalidRequest,
            forbid.Properties!.Items[OpenIddictServerAspNetCoreConstants.Properties.Error]);
        Assert.Null(_service.SilentBearerToken);
    }

    [Fact]
    public async Task ExchangeTenantSilentNonBearerHeaderReturnsForbid()
    {
        var request = Request(QlxAuthConstants.GrantTypes.TenantSilent);
        var controller = CreateController(request, authorizationHeader: "Basic dXNlcjpwYXNz");

        var result = await controller.Exchange(TestContext.Current.CancellationToken);

        Assert.IsType<ForbidResult>(result);
        Assert.Null(_service.SilentBearerToken);
    }

    [Fact]
    public async Task ExchangeTenantSilentSuccessExtractsBearerToken()
    {
        var request = Request(QlxAuthConstants.GrantTypes.TenantSilent);
        _service.SilentResult = PortalAuthResult.Success(Principal("pub-3"));
        var controller = CreateController(request, authorizationHeader: "Bearer  token-abc ");

        var result = await controller.Exchange(TestContext.Current.CancellationToken);

        Assert.IsType<SignInResult>(result);
        Assert.Equal("token-abc", _service.SilentBearerToken);
    }

    // ============ refresh_token 分发 ============

    [Fact]
    public async Task ExchangeRefreshTokenAuthFailedReturnsForbid()
    {
        var request = Request(OpenIddictConstants.GrantTypes.RefreshToken);
        _authentication.ServerResult = AuthenticateResult.Fail("invalid");
        var controller = CreateController(request);

        var result = await controller.Exchange(TestContext.Current.CancellationToken);

        var forbid = Assert.IsType<ForbidResult>(result);
        Assert.Equal(OpenIddictConstants.Errors.InvalidGrant,
            forbid.Properties!.Items[OpenIddictServerAspNetCoreConstants.Properties.Error]);
        Assert.Null(_service.RefreshPrincipal);
    }

    [Fact]
    public async Task ExchangeRefreshTokenSuccessCallsService()
    {
        var request = Request(OpenIddictConstants.GrantTypes.RefreshToken);
        var refreshPrincipal = Principal("pub-7");
        _authentication.ServerResult = AuthenticateResult.Success(
            new AuthenticationTicket(refreshPrincipal, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme));
        _service.RefreshResult = PortalAuthResult.Success(Principal("pub-8"));
        var controller = CreateController(request);

        var result = await controller.Exchange(TestContext.Current.CancellationToken);

        Assert.IsType<SignInResult>(result);
        Assert.Same(refreshPrincipal, _service.RefreshPrincipal);
    }

    // ============ 未知授权类型 ============

    [Fact]
    public async Task ExchangeUnsupportedGrantTypeReturnsForbid()
    {
        var request = Request(OpenIddictConstants.GrantTypes.Password);
        var controller = CreateController(request);

        var result = await controller.Exchange(TestContext.Current.CancellationToken);

        var forbid = Assert.IsType<ForbidResult>(result);
        Assert.Equal(OpenIddictConstants.Errors.UnsupportedGrantType,
            forbid.Properties!.Items[OpenIddictServerAspNetCoreConstants.Properties.Error]);
    }

    [Fact]
    public async Task ExchangeTenantContextNullAuthResultReturnsForbid()
    {
        // 防御分支：认证服务返回 null（违反契约）→ 视为认证失败
        var request = Request(QlxAuthConstants.GrantTypes.TenantContext);
        _authentication.ValidationResult = null!;
        _authentication.ReturnNull = true;
        var controller = CreateController(request);

        var result = await controller.Exchange(TestContext.Current.CancellationToken);

        var forbid = Assert.IsType<ForbidResult>(result);
        Assert.Equal(OpenIddictConstants.Errors.InvalidGrant,
            forbid.Properties!.Items[OpenIddictServerAspNetCoreConstants.Properties.Error]);
    }

    [Fact]
    public async Task ExchangeRefreshTokenNullAuthResultReturnsForbid()
    {
        var request = Request(OpenIddictConstants.GrantTypes.RefreshToken);
        _authentication.ReturnNull = true;
        var controller = CreateController(request);

        var result = await controller.Exchange(TestContext.Current.CancellationToken);

        var forbid = Assert.IsType<ForbidResult>(result);
        Assert.Equal(OpenIddictConstants.Errors.InvalidGrant,
            forbid.Properties!.Items[OpenIddictServerAspNetCoreConstants.Properties.Error]);
    }

    // ============ 桩件 ============

    /// <summary>认证服务桩：记录入参并返回预设结果。</summary>
    private sealed class StubPortalAuthenticationService : IPortalAuthenticationService
    {
        public PortalAuthResult IdentityResult { get; set; } = PortalAuthResult.Failure("x", "x");
        public PortalAuthResult TenantResult { get; set; } = PortalAuthResult.Failure("x", "x");
        public PortalAuthResult SilentResult { get; set; } = PortalAuthResult.Failure("x", "x");
        public PortalAuthResult RefreshResult { get; set; } = PortalAuthResult.Failure("x", "x");

        public string? IdentityClientId { get; private set; }
        public string? IdentityLoginAccount { get; private set; }
        public string? IdentityPassword { get; private set; }
        public string? IdentityClientIp { get; private set; }
        public string? TenantIdRaw { get; private set; }
        public string? MemberRoleIdRaw { get; private set; }
        public ClaimsPrincipal? TenantPrincipal { get; private set; }
        public string? SilentBearerToken { get; private set; }
        public ClaimsPrincipal? RefreshPrincipal { get; private set; }

        public Task<PortalAuthResult> AuthenticateIdentityAsync(
            string? clientId, string? loginAccount, string? password, string clientIpAddress, CancellationToken cancellationToken)
        {
            IdentityClientId = clientId;
            IdentityLoginAccount = loginAccount;
            IdentityPassword = password;
            IdentityClientIp = clientIpAddress;
            return Task.FromResult(IdentityResult);
        }

        public Task<PortalAuthResult> EnterTenantContextAsync(
            string? clientId, string? tenantIdRaw, string? memberRoleIdRaw,
            ClaimsPrincipal identityPrincipal, string clientIpAddress, CancellationToken cancellationToken)
        {
            TenantIdRaw = tenantIdRaw;
            MemberRoleIdRaw = memberRoleIdRaw;
            TenantPrincipal = identityPrincipal;
            return Task.FromResult(TenantResult);
        }

        public Task<PortalAuthResult> RenewTenantTokenAsync(
            string? clientId, string bearerToken, string clientIpAddress, CancellationToken cancellationToken)
        {
            SilentBearerToken = bearerToken;
            return Task.FromResult(SilentResult);
        }

        public Task<PortalAuthResult> RefreshIdentityAsync(ClaimsPrincipal principal, CancellationToken cancellationToken)
        {
            RefreshPrincipal = principal;
            return Task.FromResult(RefreshResult);
        }
    }

    /// <summary>认证中间件桩：按方案返回预设 AuthenticateResult（Validation / Server 独立）。</summary>
    private sealed class StubAuthenticationService : IAuthenticationService
    {
        public AuthenticateResult? ValidationResult { get; set; }
        public AuthenticateResult? ServerResult { get; set; }

        /// <summary>为 true 时强制返回 null（模拟违反契约的认证服务，覆盖防御分支）。</summary>
        public bool ReturnNull { get; set; }

        public Task<AuthenticateResult> AuthenticateAsync(HttpContext context, string? scheme)
        {
            if (ReturnNull)
            {
                return Task.FromResult<AuthenticateResult>(null!);
            }

            return Task.FromResult(scheme == OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme
                ? ValidationResult ?? AuthenticateResult.NoResult()
                : ServerResult ?? AuthenticateResult.NoResult());
        }

        public Task ChallengeAsync(HttpContext context, string? scheme, AuthenticationProperties? properties)
            => Task.CompletedTask;

        public Task ForbidAsync(HttpContext context, string? scheme, AuthenticationProperties? properties)
            => Task.CompletedTask;

        public Task SignInAsync(HttpContext context, string? scheme, ClaimsPrincipal principal, AuthenticationProperties? properties)
            => Task.CompletedTask;

        public Task SignOutAsync(HttpContext context, string? scheme, AuthenticationProperties? properties)
            => Task.CompletedTask;
    }
}
