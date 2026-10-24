/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-21 06:30:00 +08:00 中国标准时间 星期三
 * 修改时间：2026-10-21 06:30:00 +08:00 中国标准时间 星期三
 * 描述：模块许可过滤中间件单元测试（无许可 404、有许可放行、未解析租户直通）
 */

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Qlx.Abstractions.Modules;
using Qlx.Abstractions.Tenancy;
using Qlx.BuildingBlocks.Licensing;

namespace Qlx.BuildingBlocks.Tests.Licensing;

/// <summary>测试用租户上下文（可注入解析状态）。</summary>
internal sealed class FakeTenantContext(long? tenantId = null, string? tenantCode = null) : ITenantContext
{
    public string? TenantCode { get; private set; } = tenantCode;

    public long? TenantId { get; private set; } = tenantId;

    public bool IsResolved => TenantId is > 0;

    public void SetTenantId(long tenantId) => TenantId = tenantId > 0 ? tenantId : null;

    public void SetTenantCode(string? tenantCode) => TenantCode = tenantCode;
}

/// <summary>测试用许可服务（按 (tenantId, moduleCode) 白名单）。</summary>
internal sealed class FakeLicenseService(params (long TenantId, string ModuleCode)[] licensed) : IModuleLicenseService
{
    public Task<bool> HasLicenseAsync(long tenantId, string moduleCode, CancellationToken cancellationToken)
        => Task.FromResult(licensed.Contains((tenantId, moduleCode)));
}

public sealed class ModuleLicenseMiddlewareTests
{
    private static DefaultHttpContext BuildContext(ITenantContext tenantContext, IModuleLicenseService? license, string path)
    {
        var services = new ServiceCollection();
        if (license is not null)
        {
            services.AddSingleton(license);
        }

        var context = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
        context.Request.Path = path;
        return context;
    }

    private static async Task<(int StatusCode, bool NextCalled)> InvokeAsync(HttpContext context, ITenantContext tenantContext, IModuleLicenseService? license)
    {
        var nextCalled = false;
        var middleware = new ModuleLicenseMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(context, tenantContext, license);
        return (context.Response.StatusCode, nextCalled);
    }

    [Fact]
    public async Task UnlicensedModuleReturns404WithoutCallingNext()
    {
        var tenant = new FakeTenantContext(1);
        var license = new FakeLicenseService((1, "portal"));
        var context = BuildContext(tenant, license, "/api/pur/orders");

        var (status, nextCalled) = await InvokeAsync(context, tenant, license);

        Assert.Equal(StatusCodes.Status404NotFound, status);
        Assert.False(nextCalled);
    }

    [Fact]
    public async Task LicensedModuleCallsNext()
    {
        var tenant = new FakeTenantContext(1);
        var license = new FakeLicenseService((1, "portal"));
        var context = BuildContext(tenant, license, "/api/portal/me");

        var (status, nextCalled) = await InvokeAsync(context, tenant, license);

        Assert.Equal(StatusCodes.Status200OK, status);
        Assert.True(nextCalled);
    }

    [Fact]
    public async Task SameModuleDifferentTenantReturns404()
    {
        var tenant = new FakeTenantContext(2);
        var license = new FakeLicenseService((1, "pur"));
        var context = BuildContext(tenant, license, "/api/pur/orders");

        var (status, nextCalled) = await InvokeAsync(context, tenant, license);

        Assert.Equal(StatusCodes.Status404NotFound, status);
        Assert.False(nextCalled);
    }

    [Fact]
    public async Task UnresolvedTenantBypassesLicenseCheck()
    {
        var tenant = new FakeTenantContext();
        var license = new FakeLicenseService();
        var context = BuildContext(tenant, license, "/api/pur/orders");

        var (status, nextCalled) = await InvokeAsync(context, tenant, license);

        Assert.Equal(StatusCodes.Status200OK, status);
        Assert.True(nextCalled);
    }

    [Fact]
    public async Task NonApiPathBypassesLicenseCheck()
    {
        var tenant = new FakeTenantContext(1);
        var license = new FakeLicenseService();
        var context = BuildContext(tenant, license, "/health");

        var (status, nextCalled) = await InvokeAsync(context, tenant, license);

        Assert.Equal(StatusCodes.Status200OK, status);
        Assert.True(nextCalled);
    }

    [Fact]
    public async Task SupportModuleBypassesLicenseCheck()
    {
        // 支撑服务中心为平台公共能力：无许可记录也应放行（B-0 豁免清单）
        var tenant = new FakeTenantContext(1);
        var license = new FakeLicenseService();
        var context = BuildContext(tenant, license, "/api/support/files/list");

        var (status, nextCalled) = await InvokeAsync(context, tenant, license);

        Assert.Equal(StatusCodes.Status200OK, status);
        Assert.True(nextCalled);
    }

    [Fact]
    public async Task TenantSelfServiceModuleBypassesLicenseCheck()
    {
        // 租户自管理域（Bas 所在 /api/tenant 前缀）为租户基础能力：豁免许可检查
        var tenant = new FakeTenantContext(1);
        var license = new FakeLicenseService();
        var context = BuildContext(tenant, license, "/api/tenant/organizations/tree");

        var (status, nextCalled) = await InvokeAsync(context, tenant, license);

        Assert.Equal(StatusCodes.Status200OK, status);
        Assert.True(nextCalled);
    }
}
