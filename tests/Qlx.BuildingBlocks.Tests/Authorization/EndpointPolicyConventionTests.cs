/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-24
 * 描述：EndpointPolicyConvention 单元测试——命中注入单过滤器多 IAuthorizeData（AND 合并语义）、
 *       未命中仅认证兜底、AllowAnonymous 规则不注入、[Authorize]/[AllowAnonymous] 逃生舱跳过。
 */

using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationModels;
using Microsoft.AspNetCore.Mvc.Authorization;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OpenIddict.Validation.AspNetCore;
using Qlx.Abstractions.Authorization;
using Qlx.Abstractions.Constants;
using Qlx.BuildingBlocks.Authorization;

namespace Qlx.BuildingBlocks.Tests.Authorization;

public sealed class EndpointPolicyConventionTests
{
    [Fact]
    public void ApplyRuleHitInjectsSingleFilterWithCombinedAuthorizeData()
    {
        var catalog = BuildCatalog(
        [
            new EndpointPolicyRule { Controller = "Sample", RequireTenantContext = true },
            new EndpointPolicyRule { Controller = "Sample", Action = "Get", PermissionCode = "sample.view" },
        ]);
        var convention = new EndpointPolicyConvention(catalog);
        var application = BuildApplicationModel<SampleController>(nameof(SampleController.Get));

        convention.Apply(application);

        var action = application.Controllers[0].Actions[0];
        var filter = Assert.IsType<AuthorizeFilter>(Assert.Single(action.Filters));
        var data = filter.AuthorizeData!.ToList();
        Assert.Equal(2, data.Count);
        // 租户上下文前置策略在前，权限点策略在后（与原类级→方法级叠加顺序一致）
        Assert.Equal(QlxPolicyNames.TenantContext, data[0].Policy);
        Assert.Equal("Permission:sample.view", data[1].Policy);
        Assert.All(data, d => Assert.Equal(
            OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme, d.AuthenticationSchemes));
    }

    [Fact]
    public void ApplyRuleMissInjectsAuthOnlyFallbackFilter()
    {
        var catalog = BuildCatalog([]);
        var convention = new EndpointPolicyConvention(catalog);
        var application = BuildApplicationModel<SampleController>(nameof(SampleController.Get));

        convention.Apply(application);

        var action = application.Controllers[0].Actions[0];
        var filter = Assert.IsType<AuthorizeFilter>(Assert.Single(action.Filters));
        var data = Assert.Single(filter.AuthorizeData!);
        Assert.Null(data.Policy);
        Assert.Equal(OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme, data.AuthenticationSchemes);
    }

    [Fact]
    public void ApplyAllowAnonymousRuleInjectsNothing()
    {
        var catalog = BuildCatalog(
        [
            new EndpointPolicyRule { Controller = "Sample", Action = "Get", AllowAnonymous = true },
        ]);
        var convention = new EndpointPolicyConvention(catalog);
        var application = BuildApplicationModel<SampleController>(nameof(SampleController.Get));

        convention.Apply(application);

        Assert.Empty(application.Controllers[0].Actions[0].Filters);
    }

    [Fact]
    public void ApplyControllerHasAuthorizeAttributeEscapeHatchSkips()
    {
        var catalog = BuildCatalog(
        [
            new EndpointPolicyRule { Controller = "Guarded", Action = "Get", PermissionCode = "sample.view" },
        ]);
        var convention = new EndpointPolicyConvention(catalog);
        var application = BuildApplicationModel<GuardedController>(
            nameof(GuardedController.Get),
            controllerAttributes: [new AuthorizeAttribute()]);

        convention.Apply(application);

        Assert.Empty(application.Controllers[0].Actions[0].Filters);
    }

    [Fact]
    public void ApplyActionHasAllowAnonymousAttributeEscapeHatchSkips()
    {
        var catalog = BuildCatalog(
        [
            new EndpointPolicyRule { Controller = "Sample", Action = "Anon", PermissionCode = "sample.view" },
        ]);
        var convention = new EndpointPolicyConvention(catalog);
        var application = BuildApplicationModel<SampleController>(
            nameof(SampleController.Anon),
            actionAttributes: [new AllowAnonymousAttribute()]);

        convention.Apply(application);

        Assert.Empty(application.Controllers[0].Actions[0].Filters);
    }

    [Fact]
    public void ApplyControllerRuleHitOtherActionMissGetsFallback()
    {
        var catalog = BuildCatalog(
        [
            new EndpointPolicyRule { Controller = "Sample", Action = "Get", PermissionCode = "sample.view" },
        ]);
        var convention = new EndpointPolicyConvention(catalog);
        var application = BuildApplicationModel<SampleController>(
            nameof(SampleController.Get), nameof(SampleController.Post));

        convention.Apply(application);

        var actions = application.Controllers[0].Actions;
        Assert.Equal(2, actions.Count);
        var hitFilter = Assert.IsType<AuthorizeFilter>(Assert.Single(actions[0].Filters));
        Assert.Equal("Permission:sample.view", Assert.Single(hitFilter.AuthorizeData!).Policy);
        var missFilter = Assert.IsType<AuthorizeFilter>(Assert.Single(actions[1].Filters));
        Assert.Null(Assert.Single(missFilter.AuthorizeData!).Policy);
    }

    [Fact]
    public void ApplyTenantContextNotRequiredInjectsOnlyPolicyData()
    {
        var catalog = BuildCatalog(
        [
            new EndpointPolicyRule { Controller = "Sample", Action = "Get", Policy = "PortalAdmin" },
        ]);
        var convention = new EndpointPolicyConvention(catalog);
        var application = BuildApplicationModel<SampleController>(nameof(SampleController.Get));

        convention.Apply(application);

        var filter = Assert.IsType<AuthorizeFilter>(Assert.Single(application.Controllers[0].Actions[0].Filters));
        Assert.Equal("PortalAdmin", Assert.Single(filter.AuthorizeData!).Policy);
    }

    // ---------- 测试基建 ----------

    private static EndpointPolicyCatalog BuildCatalog(IReadOnlyList<EndpointPolicyRule> rules)
    {
        return new EndpointPolicyCatalog(
            [new StubProvider(rules)],
            Options.Create(new EndpointPolicyOptions()),
            new StubHostEnvironment
            {
                ContentRootPath = Path.Combine(Path.GetTempPath(), "qlx-no-config-root"),
            },
            NullLogger<EndpointPolicyCatalog>.Instance);
    }

    private static ApplicationModel BuildApplicationModel<TController>(
        string actionMethod,
        string? secondActionMethod = null,
        IReadOnlyList<object>? controllerAttributes = null,
        IReadOnlyList<object>? actionAttributes = null)
    {
        var controllerType = typeof(TController).GetTypeInfo();
        var controller = new ControllerModel(controllerType, controllerAttributes ?? []);
        controller.ControllerName = controllerType.Name.EndsWith("Controller", StringComparison.Ordinal)
            ? controllerType.Name[..^"Controller".Length]
            : controllerType.Name;

        controller.Actions.Add(BuildAction(controllerType, actionMethod, actionAttributes));
        if (secondActionMethod is not null)
        {
            controller.Actions.Add(BuildAction(controllerType, secondActionMethod, null));
        }

        var application = new ApplicationModel();
        application.Controllers.Add(controller);
        return application;
    }

    private static ActionModel BuildAction(
        TypeInfo controllerType,
        string methodName,
        IReadOnlyList<object>? attributes)
    {
        var method = controllerType.GetMethod(methodName)
            ?? throw new InvalidOperationException($"方法 {controllerType.Name}.{methodName} 不存在");
        var action = new ActionModel(method, attributes ?? []);
        action.ActionName = method.Name;
        return action;
    }

    private sealed class StubProvider(IReadOnlyList<EndpointPolicyRule> rules) : IEndpointPolicyMapProvider
    {
        public IReadOnlyList<EndpointPolicyRule> GetRules() => rules;
    }

    private sealed class StubHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;

        public string ApplicationName { get; set; } = "Qlx.Tests";

        public string ContentRootPath { get; set; } = string.Empty;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    // ReSharper disable once ClassNeverInstantiated.Local — 仅供 ApplicationModel 反射使用
    private sealed class SampleController : ControllerBase
    {
        public OkResult Get() => Ok();

        public OkResult Post() => Ok();

        public OkResult Anon() => Ok();
    }

    private sealed class GuardedController : ControllerBase
    {
        public OkResult Get() => Ok();
    }
}
