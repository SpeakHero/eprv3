/*
 * 所属公司：四川淇利馨集团有限公司
 * 描述：PermissionPolicyConfigurer 单元测试
 *       覆盖：按模块 Manifest 物化 Permission:{code} 策略、策略含对应 PermissionRequirement、
 *             空模块集合不抛异常、重复权限码不重复注册
 */

using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Qlx.Abstractions.Authorization;
using Qlx.Abstractions.Constants;
using Qlx.Abstractions.Modules;
using Qlx.BuildingBlocks.Authorization;

namespace Qlx.BuildingBlocks.Tests.Authorization;

public sealed class PermissionPolicyConfigurerTests
{
    /// <summary>最小 IModule 测试桩：仅携带 Manifest。</summary>
    private sealed class FakeModule(ModuleManifest manifest) : IModule
    {
        public ModuleManifest Manifest { get; } = manifest;

        public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
        {
        }
    }

    private static FakeModule CreateModule(params PermissionDefinition[] permissions)
        => new(new ModuleManifest
        {
            ModuleCode = "fake",
            DisplayName = "测试模块",
            TablePrefix = "fake_",
            Permissions = permissions,
        });

    [Fact]
    public void ConfigureRegistersPolicyPerManifestPermission()
    {
        var module = CreateModule(
            new PermissionDefinition("fake.res.read", "读取", PermissionKind.Menu),
            new PermissionDefinition("fake.res.write", "写入", PermissionKind.Action));
        var options = new AuthorizationOptions();

        new PermissionPolicyConfigurer([module]).Configure(options);

        Assert.NotNull(options.GetPolicy(QlxPolicyNames.Permission("fake.res.read")));
        Assert.NotNull(options.GetPolicy(QlxPolicyNames.Permission("fake.res.write")));
    }

    [Fact]
    public void ConfigurePolicyContainsMatchingPermissionRequirement()
    {
        var module = CreateModule(new PermissionDefinition("fake.res.read", "读取", PermissionKind.Menu));
        var options = new AuthorizationOptions();

        new PermissionPolicyConfigurer([module]).Configure(options);

        var policy = options.GetPolicy(QlxPolicyNames.Permission("fake.res.read"));
        var requirement = Assert.IsType<PermissionRequirement>(
            Assert.Single(policy!.Requirements.OfType<PermissionRequirement>()));
        Assert.Equal("fake.res.read", requirement.PermissionCode);
    }

    [Fact]
    public void ConfigureWithEmptyModulesDoesNotThrow()
    {
        var options = new AuthorizationOptions();

        new PermissionPolicyConfigurer([]).Configure(options);

        Assert.Null(options.GetPolicy(QlxPolicyNames.Permission("fake.res.read")));
    }

    [Fact]
    public void ConfigureWithDuplicatePermissionCodeKeepsSinglePolicy()
    {
        var moduleA = CreateModule(new PermissionDefinition("fake.res.read", "读取A", PermissionKind.Menu));
        var moduleB = CreateModule(new PermissionDefinition("fake.res.read", "读取B", PermissionKind.Menu));
        var options = new AuthorizationOptions();

        // 重复编码（异常数据）不应导致注册期崩溃
        new PermissionPolicyConfigurer([moduleA, moduleB]).Configure(options);

        var policy = options.GetPolicy(QlxPolicyNames.Permission("fake.res.read"));
        Assert.NotNull(policy);
        Assert.Single(policy!.Requirements.OfType<PermissionRequirement>());
    }
}
