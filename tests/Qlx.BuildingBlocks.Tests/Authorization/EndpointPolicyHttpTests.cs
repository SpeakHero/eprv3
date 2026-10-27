/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-24
 * 描述：集中式授权映射 HTTP 集成测试——TestServer 真实管线验证：
 *       未认证 401 / 缺租户上下文 403 / 缺权限码 403 / 权限码命中 200 /
 *       自助端点仅继承控制器级 200 / 门户策略 / 仅认证空规则 / AllowAnonymous 豁免 /
 *       未映射兜底 / [Authorize]/[AllowAnonymous] 逃生舱 / JSON 覆盖与 disabledKeys 重启生效。
 *       测试认证 handler 挂于 OpenIddict Validation scheme 名下，经 X-Test-Identity 头模拟身份。
 */

using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenIddict.Validation.AspNetCore;
using Qlx.Abstractions.Authorization;
using Qlx.Abstractions.Constants;
using Qlx.BuildingBlocks.Authorization;

namespace Qlx.BuildingBlocks.Tests.Authorization;

public sealed class EndpointPolicyHttpTests
{
    private const string IdentityHeader = "X-Test-Identity";
    private const string PortalAdminPolicy = "TestPortalAdmin";
    private const string TenantIdClaim = "tenant_id";
    private const string PortalAdminClaim = "is_portal_admin";
    private const string PermissionsClaim = "permissions";

    // ---------- 认证边界 ----------

    [Fact]
    public async Task AnonymousOnProtectedEndpointReturns401()
    {
        using var host = await StartHostAsync();
        var response = await host.GetTestClient().GetAsync("/api/ep-test/widget/get", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task AuthenticatedWithoutTenantContextOnTenantEndpointReturns403()
    {
        using var host = await StartHostAsync();
        var response = await SendAsync(host, "/api/ep-test/widget/get", "plain");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task TenantWithoutPermissionCodeReturns403()
    {
        using var host = await StartHostAsync();
        var response = await SendAsync(host, "/api/ep-test/widget/get", "tenant");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task TenantWithPermissionCodeReturns200()
    {
        using var host = await StartHostAsync();
        var response = await SendAsync(host, "/api/ep-test/widget/get", "tenant;perm:test.widget.view");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task TenantWithWrongPermissionCodeReturns403()
    {
        using var host = await StartHostAsync();
        var response = await SendAsync(host, "/api/ep-test/widget/create", "tenant;perm:test.widget.view");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task SelfServiceEndpointInheritsControllerLevelOnly()
    {
        using var host = await StartHostAsync();
        // Self 无动作级权限码：租户身份无任何权限码即可访问
        var response = await SendAsync(host, "/api/ep-test/widget/self", "tenant");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ---------- 门户策略 / 仅认证 / 匿名豁免 ----------

    [Fact]
    public async Task PortalAdminPolicyRejectsPlainIdentity()
    {
        using var host = await StartHostAsync();
        var response = await SendAsync(host, "/api/ep-test/admin-box/stats", "tenant");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task PortalAdminPolicyAcceptsPortalAdminIdentity()
    {
        using var host = await StartHostAsync();
        var response = await SendAsync(host, "/api/ep-test/admin-box/stats", "portaladmin");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task AuthOnlyRuleRejectsAnonymousAcceptsAnyIdentity()
    {
        using var host = await StartHostAsync();
        var anonymous = await host.GetTestClient().GetAsync("/api/ep-test/plain/data", TestContext.Current.CancellationToken);
        var authenticated = await SendAsync(host, "/api/ep-test/plain/data", "plain");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        Assert.Equal(HttpStatusCode.OK, authenticated.StatusCode);
    }

    [Fact]
    public async Task AllowAnonymousRuleAcceptsAnonymous()
    {
        using var host = await StartHostAsync();
        var response = await host.GetTestClient().GetAsync("/api/ep-test/open/ping", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ---------- 未映射兜底 / 逃生舱 ----------

    [Fact]
    public async Task UnmappedEndpointFallsBackToAuthOnly()
    {
        using var host = await StartHostAsync();
        var anonymous = await host.GetTestClient().GetAsync("/api/ep-test/unmapped/data", TestContext.Current.CancellationToken);
        var authenticated = await SendAsync(host, "/api/ep-test/unmapped/data", "plain");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        Assert.Equal(HttpStatusCode.OK, authenticated.StatusCode);
    }

    [Fact]
    public async Task EscapeHatchAllowAnonymousAttributeStillHonored()
    {
        using var host = await StartHostAsync();
        var response = await host.GetTestClient().GetAsync("/api/ep-test/escape/anon", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task EscapeHatchManualAuthorizeAttributeStillEvaluated()
    {
        using var host = await StartHostAsync();
        var anonymous = await host.GetTestClient().GetAsync("/api/ep-test/escape/manual", TestContext.Current.CancellationToken);
        var tenantIdentity = await SendAsync(host, "/api/ep-test/escape/manual", "tenant");
        var portalAdmin = await SendAsync(host, "/api/ep-test/escape/manual", "portaladmin");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, tenantIdentity.StatusCode);
        Assert.Equal(HttpStatusCode.OK, portalAdmin.StatusCode);
    }

    // ---------- JSON 覆盖 / disabledKeys（配置变更重启生效） ----------

    [Fact]
    public async Task JsonOverrideChangesPermissionCodeAfterRestart()
    {
        var config = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["endpointPolicies:rules:0:controller"] = "Widget",
            ["endpointPolicies:rules:0:action"] = "Get",
            ["endpointPolicies:rules:0:permissionCode"] = "test.widget.v2",
        };
        using var host = await StartHostAsync(config);

        var oldCode = await SendAsync(host, "/api/ep-test/widget/get", "tenant;perm:test.widget.view");
        var newCode = await SendAsync(host, "/api/ep-test/widget/get", "tenant;perm:test.widget.v2");
        Assert.Equal(HttpStatusCode.Forbidden, oldCode.StatusCode);
        Assert.Equal(HttpStatusCode.OK, newCode.StatusCode);
    }

    [Fact]
    public async Task DisabledKeyDegradesToControllerLevelOnly()
    {
        var config = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["endpointPolicies:disabledKeys:0"] = "Widget.Create",
        };
        using var host = await StartHostAsync(config);

        // 动作级权限码被摘除：仅剩控制器级租户上下文，租户身份无权限码也可访问
        var response = await SendAsync(host, "/api/ep-test/widget/create", "tenant");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ---------- 测试基建 ----------

    private static async Task<HttpResponseMessage> SendAsync(IHost host, string url, string identity)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add(IdentityHeader, identity);
        var response = await host.GetTestClient().SendAsync(request, TestContext.Current.CancellationToken);
        request.Dispose();
        return response;
    }

    private static async Task<IHost> StartHostAsync(IReadOnlyDictionary<string, string?>? extraConfig = null)
    {
        var builder = Host.CreateDefaultBuilder();
        builder.ConfigureWebHostDefaults(webBuilder =>
        {
            webBuilder.UseTestServer();
            if (extraConfig is not null)
            {
                webBuilder.ConfigureAppConfiguration((_, cfg) => cfg.AddInMemoryCollection(extraConfig));
            }

            webBuilder.ConfigureServices((context, services) =>
            {
                services.AddAuthentication(options =>
                    {
                        options.DefaultScheme = OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme;
                    })
                    .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(
                        OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme, _ => { });

                services.AddAuthorization(options =>
                {
                    options.AddPolicy(QlxPolicyNames.TenantContext, policy => policy
                        .RequireAuthenticatedUser()
                        .RequireClaim(TenantIdClaim));
                    options.AddPolicy(PortalAdminPolicy, policy => policy
                        .RequireAuthenticatedUser()
                        .RequireClaim(PortalAdminClaim, "1"));
                    options.AddPolicy(QlxPolicyNames.Permission("test.widget.view"), policy => policy
                        .RequireAuthenticatedUser()
                        .RequireClaim(PermissionsClaim, "test.widget.view"));
                    options.AddPolicy(QlxPolicyNames.Permission("test.widget.create"), policy => policy
                        .RequireAuthenticatedUser()
                        .RequireClaim(PermissionsClaim, "test.widget.create"));
                    options.AddPolicy(QlxPolicyNames.Permission("test.widget.v2"), policy => policy
                        .RequireAuthenticatedUser()
                        .RequireClaim(PermissionsClaim, "test.widget.v2"));
                });

                services.AddSingleton<IEndpointPolicyMapProvider>(new StubProvider(
                [
                    new EndpointPolicyRule { Controller = "Widget", RequireTenantContext = true },
                    new EndpointPolicyRule { Controller = "Widget", Action = "Get", PermissionCode = "test.widget.view" },
                    new EndpointPolicyRule { Controller = "Widget", Action = "Create", PermissionCode = "test.widget.create" },
                    new EndpointPolicyRule { Controller = "AdminBox", Policy = PortalAdminPolicy },
                    new EndpointPolicyRule { Controller = "Plain" },
                    new EndpointPolicyRule { Controller = "Open", Action = "Ping", AllowAnonymous = true },
                ]));
                services.AddQlxEndpointPolicies(context.Configuration);

                services.AddControllers()
                    .AddApplicationPart(typeof(EndpointPolicyHttpTests).Assembly);
            });
            webBuilder.Configure(app =>
            {
                app.UseRouting();
                app.UseAuthentication();
                app.UseAuthorization();
                app.UseEndpoints(endpoints => endpoints.MapControllers());
            });
        });

        var host = builder.Build();
        await host.StartAsync(TestContext.Current.CancellationToken);
        return host;
    }

    private sealed class StubProvider(IReadOnlyList<EndpointPolicyRule> rules) : IEndpointPolicyMapProvider
    {
        public IReadOnlyList<EndpointPolicyRule> GetRules() => rules;
    }

    /// <summary>
    /// 测试认证 handler：无 <c>X-Test-Identity</c> 头 → NoResult（401）；
    /// 分号分隔身份标记：<c>tenant</c>（租户上下文）、<c>portaladmin</c>（门户管理员）、<c>perm:{code}</c>（权限码）。
    /// </summary>
    private sealed class TestAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var header = Request.Headers[IdentityHeader].ToString();
            if (string.IsNullOrWhiteSpace(header))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var claims = new List<Claim>();
            foreach (var token in header.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (token.Equals("tenant", StringComparison.Ordinal))
                {
                    claims.Add(new Claim(TenantIdClaim, "t-1"));
                }
                else if (token.Equals("portaladmin", StringComparison.Ordinal))
                {
                    claims.Add(new Claim(PortalAdminClaim, "1"));
                }
                else if (token.StartsWith("perm:", StringComparison.Ordinal))
                {
                    claims.Add(new Claim(PermissionsClaim, token["perm:".Length..]));
                }
            }

            var identity = new ClaimsIdentity(claims, Scheme.Name, "name", "role");
            return Task.FromResult(AuthenticateResult.Success(
                new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name)));
        }
    }
}

// ---------- 测试专用控制器（顶级 public 方可被 ApplicationPart 发现） ----------

[ApiController]
[Route("api/ep-test/widget")]
public sealed class WidgetController : ControllerBase
{
    [HttpGet("get")]
    public OkResult Get() => Ok();

    [HttpGet("create")]
    public OkResult Create() => Ok();

    [HttpGet("self")]
    public OkResult Self() => Ok();
}

[ApiController]
[Route("api/ep-test/admin-box")]
public sealed class AdminBoxController : ControllerBase
{
    [HttpGet("stats")]
    public OkResult Stats() => Ok();
}

[ApiController]
[Route("api/ep-test/plain")]
public sealed class PlainController : ControllerBase
{
    [HttpGet("data")]
    public OkResult Data() => Ok();
}

[ApiController]
[Route("api/ep-test/open")]
public sealed class OpenController : ControllerBase
{
    [HttpGet("ping")]
    public OkResult Ping() => Ok();
}

[ApiController]
[Route("api/ep-test/unmapped")]
public sealed class UnmappedController : ControllerBase
{
    [HttpGet("data")]
    public OkResult Data() => Ok();
}

[ApiController]
[Route("api/ep-test/escape")]
public sealed class EscapeHatchController : ControllerBase
{
    [AllowAnonymous]
    [HttpGet("anon")]
    public OkResult Anon() => Ok();

    [Authorize(Policy = "TestPortalAdmin")]
    [HttpGet("manual")]
    public OkResult Manual() => Ok();
}
