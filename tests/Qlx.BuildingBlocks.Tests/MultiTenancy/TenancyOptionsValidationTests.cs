/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-21 23:45:00 +08:00 中国标准时间 星期三
 * 修改时间：2026-10-21 23:45:00 +08:00 中国标准时间 星期三
 * 描述：多租户强类型配置校验测试（共享库连接、数据库模式默认值、平台共享库）
 */

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Qlx.Abstractions.Tenancy;
using Qlx.BuildingBlocks.MultiTenancy;
using Qlx.Modules.Portal.Services;

namespace Qlx.BuildingBlocks.Tests.MultiTenancy;

/// <summary>
/// 多租户强类型配置校验测试：直接走生产注册链路 <see cref="MultiTenancyServiceCollectionExtensions.AddQlxMultiTenancy"/>，
/// 断言 Options 绑定结果与数据注解校验行为。
/// </summary>
public sealed class TenancyOptionsValidationTests
{
    /// <summary>测试用宿主环境桩。</summary>
    private sealed class StubHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;

        public string ApplicationName { get; set; } = "Qlx.Tests";

        public string ContentRootPath { get; set; } = Directory.GetCurrentDirectory();

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private static ServiceProvider BuildProvider(Dictionary<string, string?> values)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddQlxMultiTenancy(configuration, new StubHostEnvironment());

        return services.BuildServiceProvider();
    }

    [Fact]
    public void SharedDatabaseOptionsBindsConfiguredValues()
    {
        using var provider = BuildProvider(new Dictionary<string, string?>
        {
            ["Qlx:Tenancy:SharedDatabase:DbHost"] = "(localdb)",
            ["Qlx:Tenancy:SharedDatabase:DbName"] = "qlx_tenant_shared",
            ["Qlx:Tenancy:SharedDatabase:MaxPoolSize"] = "120",
        });

        var options = provider.GetRequiredService<IOptions<SharedDatabaseOptions>>().Value;
        var connection = options.ToConnectionInfo(42);

        Assert.Equal("qlx_tenant_shared", options.DbName);
        Assert.Equal(120, options.MaxPoolSize);
        Assert.Equal(42, connection.TenantId);
        Assert.True(connection.IsShared);
    }

    [Fact]
    public void SharedDatabaseOptionsFailsWhenDbHostMissing()
    {
        using var provider = BuildProvider(new Dictionary<string, string?>
        {
            ["Qlx:Tenancy:SharedDatabase:DbName"] = "qlx_tenant_shared",
        });

        Assert.Throws<OptionsValidationException>(
            () => _ = provider.GetRequiredService<IOptions<SharedDatabaseOptions>>().Value);
    }

    [Fact]
    public void TenantDatabaseModeOptionsDefaultsToShared()
    {
        using var provider = BuildProvider([]);

        var options = provider.GetRequiredService<IOptions<TenantDatabaseModeOptions>>().Value;

        Assert.Equal(TenantDatabaseMode.Shared, options.DefaultMode);
    }

    [Fact]
    public void TenantDatabaseModeOptionsRejectsUndefinedMode()
    {
        using var provider = BuildProvider(new Dictionary<string, string?>
        {
            ["Qlx:Tenancy:DatabaseMode:DefaultMode"] = "9",
        });

        Assert.Throws<OptionsValidationException>(
            () => _ = provider.GetRequiredService<IOptions<TenantDatabaseModeOptions>>().Value);
    }

    [Fact]
    public void PlatformSharedOptionsFailsWhenConnectionStringMissing()
    {
        using var provider = BuildProvider([]);

        Assert.Throws<OptionsValidationException>(
            () => _ = provider.GetRequiredService<IOptions<PlatformSharedOptions>>().Value);
    }

    [Fact]
    public void ProvisioningOptionsDefaultsToSharedMode()
    {
        var options = new PortalProvisioningOptions();

        Assert.Equal(TenantDatabaseMode.Shared, options.DefaultDatabaseMode);
    }

    [Fact]
    public void DevelopmentFallbackDisabledByDefault()
    {
        using var provider = BuildProvider([]);

        var options = provider.GetRequiredService<IOptions<TenantConnectionFallbackOptions>>().Value;

        Assert.False(options.Enabled);
        Assert.Null(options.ConnectionString);
    }

    [Fact]
    public void DevelopmentFallbackRequiresConnectionStringWhenEnabled()
    {
        using var provider = BuildProvider(new Dictionary<string, string?>
        {
            ["Qlx:Tenancy:DevelopmentFallback:Enabled"] = "true",
        });

        Assert.Throws<OptionsValidationException>(
            () => _ = provider.GetRequiredService<IOptions<TenantConnectionFallbackOptions>>().Value);
    }

    [Fact]
    public void MigrationOptionsDisableDropAndSetLockTimeoutByDefault()
    {
        using var provider = BuildProvider([]);

        var options = provider.GetRequiredService<IOptions<TenantMigrationOptions>>().Value;

        // 默认禁止删库（任何模式），应用锁等待 60 秒
        Assert.False(options.AllowDropOnFirstModuleFailure);
        Assert.Equal(60, options.MigrationLockTimeoutSeconds);
    }

    [Fact]
    public void MigrationOptionsBindsDropSwitch()
    {
        using var provider = BuildProvider(new Dictionary<string, string?>
        {
            ["Qlx:MultiTenancy:AllowDropOnFirstModuleFailure"] = "true",
            ["Qlx:MultiTenancy:MigrationLockTimeoutSeconds"] = "15",
        });

        var options = provider.GetRequiredService<IOptions<TenantMigrationOptions>>().Value;

        Assert.True(options.AllowDropOnFirstModuleFailure);
        Assert.Equal(15, options.MigrationLockTimeoutSeconds);
    }

    [Fact]
    public void SharedMigrationHistoryTableIsPerModule()
    {
        Assert.Equal("__EFMigrationsHistory_bas", TenantMigrationRunner.BuildHistoryTableName("bas"));
        Assert.Equal("__EFMigrationsHistory_some_module", TenantMigrationRunner.BuildHistoryTableName("some-module"));
    }
}
