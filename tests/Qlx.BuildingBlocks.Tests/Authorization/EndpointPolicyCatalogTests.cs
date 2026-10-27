/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-24
 * 描述：EndpointPolicyCatalog 单元测试——基线并集合并 / JSON 同键覆盖 / 禁用键删除 /
 *       AllowAnonymous 传播 / 空 Controller 跳过 / ConfigurationBinder 对 required 成员兼容性 /
 *       authorization.json 防删自动生成与既有文件保护。
 */

using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Qlx.Abstractions.Authorization;
using Qlx.BuildingBlocks.Authorization;

namespace Qlx.BuildingBlocks.Tests.Authorization;

public sealed class EndpointPolicyCatalogTests
{
    // ---------- 合并语义 ----------

    [Fact]
    public void TryFindControllerAndActionRulesMergesWithAndSemantics()
    {
        var catalog = BuildCatalog(
        [
            new EndpointPolicyRule { Controller = "TenantAdmin", RequireTenantContext = true },
            new EndpointPolicyRule { Controller = "TenantAdmin", Action = "GetOrgTree", PermissionCode = "bas.admin.org.menu" },
        ]);

        var hit = catalog.TryFind("TenantAdmin", "GetOrgTree", out var policy);

        Assert.True(hit);
        Assert.NotNull(policy);
        Assert.True(policy.RequireTenantContext);
        Assert.False(policy.AllowAnonymous);
        Assert.Equal(["Permission:bas.admin.org.menu"], policy.PolicyNames);
    }

    [Fact]
    public void TryFindControllerLevelOnlyAppliesToAllActions()
    {
        var catalog = BuildCatalog(
        [
            new EndpointPolicyRule { Controller = "TenantAdmin", RequireTenantContext = true },
        ]);

        var hit = catalog.TryFind("TenantAdmin", "AnyAction", out var policy);

        Assert.True(hit);
        Assert.NotNull(policy);
        Assert.True(policy.RequireTenantContext);
        Assert.Empty(policy.PolicyNames);
    }

    [Fact]
    public void TryFindPolicyAndPermissionCodeBothAppendedControllerFirst()
    {
        var catalog = BuildCatalog(
        [
            new EndpointPolicyRule { Controller = "PortalAdmin", Policy = "PortalAdmin" },
            new EndpointPolicyRule
            {
                Controller = "PortalAdmin",
                Action = "GetTenants",
                PermissionCode = "portal.tenant.view",
            },
        ]);

        var hit = catalog.TryFind("PortalAdmin", "GetTenants", out var policy);

        Assert.True(hit);
        Assert.NotNull(policy);
        Assert.Equal(["PortalAdmin", "Permission:portal.tenant.view"], policy.PolicyNames);
    }

    [Fact]
    public void TryFindDuplicateBaselineKeysMergeAsUnionAcrossProviders()
    {
        var catalog = BuildCatalog(
        [
            new EndpointPolicyRule { Controller = "Job", Action = "GetHistory", Policy = "JobAdmin" },
            new EndpointPolicyRule { Controller = "Job", Action = "GetHistory", PermissionCode = "support.job.view" },
        ]);

        var hit = catalog.TryFind("Job", "GetHistory", out var policy);

        Assert.True(hit);
        Assert.NotNull(policy);
        Assert.Equal(["JobAdmin", "Permission:support.job.view"], policy.PolicyNames);
    }

    [Fact]
    public void TryFindMissReturnsFalse()
    {
        var catalog = BuildCatalog(
        [
            new EndpointPolicyRule { Controller = "TenantAdmin", RequireTenantContext = true },
        ]);

        var hit = catalog.TryFind("Unknown", "Get", out var policy);

        Assert.False(hit);
        Assert.Null(policy);
    }

    [Fact]
    public void TryFindMatchingIsCaseInsensitive()
    {
        var catalog = BuildCatalog(
        [
            new EndpointPolicyRule { Controller = "TenantAdmin", Action = "GetOrgTree", PermissionCode = "bas.admin.org.menu" },
        ]);

        var hit = catalog.TryFind("tenantadmin", "getorgtree", out var policy);

        Assert.True(hit);
        Assert.NotNull(policy);
    }

    [Fact]
    public void TryFindAllowAnonymousEitherLevelPropagates()
    {
        var catalog = BuildCatalog(
        [
            new EndpointPolicyRule { Controller = "Authorization", RequireTenantContext = false },
            new EndpointPolicyRule { Controller = "Authorization", Action = "Exchange", AllowAnonymous = true },
        ]);

        var hit = catalog.TryFind("Authorization", "Exchange", out var policy);

        Assert.True(hit);
        Assert.NotNull(policy);
        Assert.True(policy.AllowAnonymous);
    }

    // ---------- JSON 覆盖 / 禁用 ----------

    [Fact]
    public void TryFindJsonOverrideSameKeyReplacesBaseline()
    {
        var catalog = BuildCatalog(
            [
                new EndpointPolicyRule { Controller = "File", Action = "Upload", PermissionCode = "support.file.upload" },
            ],
            options: new EndpointPolicyOptions
            {
                Rules =
                [
                    new EndpointPolicyRule { Controller = "File", Action = "Upload", PermissionCode = "support.file.manage" },
                ],
            });

        var hit = catalog.TryFind("File", "Upload", out var policy);

        Assert.True(hit);
        Assert.NotNull(policy);
        Assert.Equal(["Permission:support.file.manage"], policy.PolicyNames);
    }

    [Fact]
    public void TryFindJsonOverrideNewKeyAddedWithoutBaseline()
    {
        var catalog = BuildCatalog(
            [],
            options: new EndpointPolicyOptions
            {
                Rules =
                [
                    new EndpointPolicyRule { Controller = "Extra", Action = "Get", PermissionCode = "extra.view" },
                ],
            });

        var hit = catalog.TryFind("Extra", "Get", out var policy);

        Assert.True(hit);
        Assert.NotNull(policy);
        Assert.Equal(["Permission:extra.view"], policy.PolicyNames);
    }

    [Fact]
    public void TryFindJsonRuleWithEmptyControllerSkippedBaselineIntact()
    {
        var catalog = BuildCatalog(
            [
                new EndpointPolicyRule { Controller = "Job", RequireTenantContext = true },
            ],
            options: new EndpointPolicyOptions
            {
                Rules =
                [
                    new EndpointPolicyRule { Controller = "", PermissionCode = "bad.rule" },
                    new EndpointPolicyRule { Controller = "   ", Action = "Get", PermissionCode = "bad.rule2" },
                ],
            });

        // 空 Controller 条目被跳过（构造期不抛异常），基线规则不受影响
        Assert.True(catalog.TryFind("Job", "Get", out var policy));
        Assert.NotNull(policy);
        Assert.True(policy.RequireTenantContext);
    }

    [Fact]
    public void TryFindDisabledActionKeyRemovesOnlyActionRule()
    {
        var catalog = BuildCatalog(
            [
                new EndpointPolicyRule { Controller = "TenantAdmin", RequireTenantContext = true },
                new EndpointPolicyRule { Controller = "TenantAdmin", Action = "GetOrgTree", PermissionCode = "bas.admin.org.menu" },
            ],
            options: new EndpointPolicyOptions
            {
                DisabledKeys = ["TenantAdmin.GetOrgTree"],
            });

        var hit = catalog.TryFind("TenantAdmin", "GetOrgTree", out var policy);

        // 控制器级规则仍在：仅认证 + 租户上下文，权限点被摘除
        Assert.True(hit);
        Assert.NotNull(policy);
        Assert.True(policy.RequireTenantContext);
        Assert.Empty(policy.PolicyNames);
    }

    [Fact]
    public void TryFindDisabledControllerKeyRemovesControllerRuleOnly()
    {
        var catalog = BuildCatalog(
            [
                new EndpointPolicyRule { Controller = "TenantAdmin", RequireTenantContext = true },
                new EndpointPolicyRule { Controller = "TenantAdmin", Action = "GetOrgTree", PermissionCode = "bas.admin.org.menu" },
            ],
            options: new EndpointPolicyOptions
            {
                DisabledKeys = ["TenantAdmin"],
            });

        var hit = catalog.TryFind("TenantAdmin", "GetOrgTree", out var policy);

        Assert.True(hit);
        Assert.NotNull(policy);
        Assert.False(policy.RequireTenantContext);
        Assert.Equal(["Permission:bas.admin.org.menu"], policy.PolicyNames);
    }

    [Fact]
    public void TryFindDisabledAllLevelsReturnsFalseFallsBackToAuthOnly()
    {
        var catalog = BuildCatalog(
            [
                new EndpointPolicyRule { Controller = "Job", RequireTenantContext = true },
                new EndpointPolicyRule { Controller = "Job", Action = "Run", PermissionCode = "support.job.run" },
            ],
            options: new EndpointPolicyOptions
            {
                DisabledKeys = ["Job", "Job.Run"],
            });

        var hit = catalog.TryFind("Job", "Run", out var policy);

        Assert.False(hit);
        Assert.Null(policy);
    }

    // ---------- ConfigurationBinder 对 required 成员的兼容性 ----------

    [Fact]
    public void ConfigurationBinderBindsRulesIntoOptionsRequiredMemberCompatible()
    {
        const string json = """
            {
              "rules": [
                { "controller": "TenantAdmin", "requireTenantContext": true },
                { "controller": "TenantAdmin", "action": "GetOrgTree", "permissionCode": "bas.admin.org.menu" },
                { "controller": "Authorization", "action": "Exchange", "allowAnonymous": true }
              ],
              "disabledKeys": [ "Job" ]
            }
            """;

        var configuration = new ConfigurationBuilder()
            .AddJsonStream(new MemoryStream(Encoding.UTF8.GetBytes(json)))
            .Build();

        var options = new EndpointPolicyOptions();
        configuration.Bind(options);

        Assert.Equal(3, options.Rules.Count);
        Assert.Equal("TenantAdmin", options.Rules[0].Controller);
        Assert.True(options.Rules[0].RequireTenantContext);
        Assert.Equal("bas.admin.org.menu", options.Rules[1].PermissionCode);
        Assert.True(options.Rules[2].AllowAnonymous);
        Assert.Equal(["Job"], options.DisabledKeys);
    }

    // ---------- JSON 防删 ----------

    [Fact]
    public void ConstructorConfigFileMissingGeneratesBaselineFile()
    {
        var contentRoot = CreateTempContentRootWithConfigDir();
        try
        {
            _ = BuildCatalog(
                [
                    new EndpointPolicyRule { Controller = "Job", Action = "GetHistory", PermissionCode = "support.job.view" },
                ],
                contentRootPath: contentRoot);

            var path = Path.Combine(contentRoot, "config", "common", "authorization.json");
            Assert.True(File.Exists(path));
            var content = File.ReadAllText(path);
            Assert.Contains("endpointPolicies", content, StringComparison.Ordinal);
            Assert.Contains("support.job.view", content, StringComparison.Ordinal);
            Assert.Contains("disabledKeys", content, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(contentRoot, recursive: true);
        }
    }

    [Fact]
    public void ConstructorConfigFileExistsPreservesExistingContent()
    {
        var contentRoot = CreateTempContentRootWithConfigDir();
        try
        {
            var path = Path.Combine(contentRoot, "config", "common", "authorization.json");
            const string sentinel = "{ \"endpointPolicies\": { \"rules\": [] } }";
            File.WriteAllText(path, sentinel);

            _ = BuildCatalog(
                [
                    new EndpointPolicyRule { Controller = "Job", PermissionCode = "support.job.view" },
                ],
                contentRootPath: contentRoot);

            Assert.Equal(sentinel, File.ReadAllText(path));
        }
        finally
        {
            Directory.Delete(contentRoot, recursive: true);
        }
    }

    // ---------- 测试基建 ----------

    private static EndpointPolicyCatalog BuildCatalog(
        IReadOnlyList<EndpointPolicyRule> baseline,
        EndpointPolicyOptions? options = null,
        string? contentRootPath = null)
    {
        var provider = new StubProvider(baseline);
        var environment = new StubHostEnvironment
        {
            ContentRootPath = contentRootPath ?? Path.Combine(Path.GetTempPath(), "qlx-no-config-root"),
        };
        return new EndpointPolicyCatalog(
            [provider],
            Options.Create(options ?? new EndpointPolicyOptions()),
            environment,
            NullLogger<EndpointPolicyCatalog>.Instance);
    }

    private static string CreateTempContentRootWithConfigDir()
    {
        var root = Path.Combine(Path.GetTempPath(), $"qlx-cat-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(root, "config", "common"));
        return root;
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
}
