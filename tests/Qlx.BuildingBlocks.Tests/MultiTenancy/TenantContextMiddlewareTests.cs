/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-21 06:30:00 +08:00 中国标准时间 星期三
 * 修改时间：2026-10-21 06:30:00 +08:00 中国标准时间 星期三
 * 描述：租户上下文中间件单元测试（X-Tenant-Id 混淆串解析 / claim 回退 / 无效值忽略）
 */

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Qlx.Abstractions.Tenancy;
using Qlx.BuildingBlocks.Ids;
using Qlx.BuildingBlocks.MultiTenancy;

namespace Qlx.BuildingBlocks.Tests.MultiTenancy;

/// <summary>测试用租户上下文（多租户中间件专用）。</summary>
internal sealed class FakeTenantContext : ITenantContext
{
    public string? TenantCode { get; private set; }

    public long? TenantId { get; private set; }

    public bool IsResolved => TenantId.HasValue && TenantId.Value > 0;

    public void SetTenantId(long tenantId) => TenantId = tenantId > 0 ? tenantId : null;

    public void SetTenantCode(string? tenantCode) => TenantCode = tenantCode;
}

public sealed class TenantContextMiddlewareTests
{
    private static (DefaultHttpContext Context, FakeTenantContext Fake) BuildContext(
        IIdObfuscator obfuscator,
        string? headerValue = null,
        string? claimValue = null)
    {
        var services = new ServiceCollection()
            .AddSingleton(obfuscator)
            .BuildServiceProvider();
        var context = new DefaultHttpContext { RequestServices = services };
        if (headerValue is not null)
        {
            context.Request.Headers["X-Tenant-Id"] = headerValue;
        }

        if (claimValue is not null)
        {
            context.User = new System.Security.Claims.ClaimsPrincipal(
                new System.Security.Claims.ClaimsIdentity(
                    [new System.Security.Claims.Claim("tenant_id", claimValue)], "Bearer"));
        }

        var fake = new FakeTenantContext();
        return (context, fake);
    }

    private static async Task<FakeTenantContext> InvokeAsync(DefaultHttpContext context, FakeTenantContext fake, IIdObfuscator obfuscator)
    {
        var middleware = new TenantContextMiddleware(_ => Task.CompletedTask);
        await middleware.InvokeAsync(context, fake, obfuscator);
        return fake;
    }

    [Fact]
    public async Task ObfuscatedHeaderResolvesTenant()
    {
        var obfuscator = new HashidsObfuscator("test-salt");
        var expected = 123456789L;
        var obfuscated = obfuscator.Encode(expected);
        var (context, fake) = BuildContext(obfuscator, headerValue: obfuscated);

        var resolved = await InvokeAsync(context, fake, obfuscator);

        Assert.Equal(expected, resolved.TenantId);
        Assert.True(resolved.IsResolved);
        // 注：X-Tenant-Id 回显走 OnStarting 回调，由 Host 冒烟验证（DefaultHttpContext 单测不可靠）
    }

    [Fact]
    public async Task NumericHeaderResolvesDirectly()
    {
        var obfuscator = new HashidsObfuscator("test-salt");
        var (context, fake) = BuildContext(obfuscator, headerValue: "42");

        var resolved = await InvokeAsync(context, fake, obfuscator);

        Assert.Equal(42L, resolved.TenantId);
    }

    [Fact]
    public async Task InvalidHeaderFallsBackToClaim()
    {
        var obfuscator = new HashidsObfuscator("test-salt");
        var expected = 987654321L;
        var obfuscated = obfuscator.Encode(expected);
        var (context, fake) = BuildContext(obfuscator, headerValue: "not-a-valid-hash", claimValue: obfuscated);

        var resolved = await InvokeAsync(context, fake, obfuscator);

        Assert.Equal(expected, resolved.TenantId);
    }

    [Fact]
    public async Task MissingBothLeavesUnresolved()
    {
        var obfuscator = new HashidsObfuscator("test-salt");
        var (context, fake) = BuildContext(obfuscator);

        var resolved = await InvokeAsync(context, fake, obfuscator);

        Assert.False(resolved.IsResolved);
    }

    [Fact]
    public async Task GarbageHeaderStaysUnresolved()
    {
        var obfuscator = new HashidsObfuscator("test-salt");
        var (context, fake) = BuildContext(obfuscator, headerValue: "###");

        var resolved = await InvokeAsync(context, fake, obfuscator);

        Assert.False(resolved.IsResolved);
    }
}
