/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-24 10:00:00 +08:00 中国标准时间 星期六
 * 描述：租户安全基线强类型配置校验测试（默认值绑定 / 数值与跨字段校验 / 合法自定义值）
 */

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Qlx.BuildingBlocks.Configuration;

namespace Qlx.BuildingBlocks.Tests.Configuration;

/// <summary>
/// <see cref="TenantSecurityOptions"/> 校验测试：走生产注册链路 <see cref="SecurityServiceCollectionExtensions.AddQlxTenantSecurity"/>，
/// 断言默认值绑定与 <see cref="IValidatableObject"/> 校验行为。
/// </summary>
public sealed class TenantSecurityOptionsTests
{
    private static ServiceProvider BuildProvider(Dictionary<string, string?> values)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddQlxTenantSecurity(configuration);

        return services.BuildServiceProvider();
    }

    [Fact]
    public void EmptyConfigurationBindsSecureDefaults()
    {
        using var provider = BuildProvider([]);

        var options = provider.GetRequiredService<IOptions<TenantSecurityOptions>>().Value;

        Assert.Equal(10, options.Password.MinLength);
        Assert.True(options.Password.RequireUppercase);
        Assert.True(options.Password.RequireSymbol);
        Assert.Equal(5, options.Lockout.MaxFailedAttempts);
        Assert.Equal(15, options.Lockout.FailureWindowMinutes);
        Assert.Equal(30, options.Session.IdleTimeoutMinutes);
        Assert.Equal(720, options.Session.AbsoluteTimeoutMinutes);
        Assert.Equal(104_857_600, options.Upload.MaxFileSizeBytes);
        Assert.Contains(".png", options.Upload.AllowedExtensions);
    }

    [Fact]
    public void PasswordMinLengthBelowLowerBoundFailsValidation()
    {
        using var provider = BuildProvider(new Dictionary<string, string?>
        {
            ["Qlx:Tenant:Security:Password:MinLength"] = "6",
        });

        Assert.Throws<OptionsValidationException>(
            () => _ = provider.GetRequiredService<IOptions<TenantSecurityOptions>>().Value);
    }

    [Fact]
    public void IdleTimeoutNotLessThanAbsoluteTimeoutFailsValidation()
    {
        using var provider = BuildProvider(new Dictionary<string, string?>
        {
            ["Qlx:Tenant:Security:Session:IdleTimeoutMinutes"] = "900",
            ["Qlx:Tenant:Security:Session:AbsoluteTimeoutMinutes"] = "600",
        });

        var exception = Assert.Throws<OptionsValidationException>(
            () => _ = provider.GetRequiredService<IOptions<TenantSecurityOptions>>().Value);
        Assert.Contains("Qlx:Tenant:Security:Session:IdleTimeoutMinutes", exception.Message);
    }

    [Fact]
    public void ExtensionsEqualToDefaultsAreNotDuplicated()
    {
        // 回归保护：配置绑定对集合属性为追加语义，配置文件重复列默认白名单不得被合并成重复项
        var defaults = TenantSecurityOptions.UploadPolicyOptions.DefaultAllowedExtensions;
        var values = new Dictionary<string, string?>();
        for (var i = 0; i < defaults.Count; i++)
        {
            values[$"Qlx:Tenant:Security:Upload:AllowedExtensions:{i}"] = defaults[i];
        }

        using var provider = BuildProvider(values);

        var options = provider.GetRequiredService<IOptions<TenantSecurityOptions>>().Value;

        Assert.Equal(defaults.Count, options.Upload.AllowedExtensions.Count);
    }

    [Fact]
    public void ValidCustomValuesPassValidation()
    {
        using var provider = BuildProvider(new Dictionary<string, string?>
        {
            ["Qlx:Tenant:Security:Password:MinLength"] = "12",
            ["Qlx:Tenant:Security:Password:ExpiryDays"] = "0",
            ["Qlx:Tenant:Security:Session:IdleTimeoutMinutes"] = "20",
            ["Qlx:Tenant:Security:Session:AbsoluteTimeoutMinutes"] = "600",
            ["Qlx:Tenant:Security:Lockout:MaxFailedAttempts"] = "3",
            ["Qlx:Tenant:Security:Lockout:FailureWindowMinutes"] = "10",
            ["Qlx:Tenant:Security:Upload:MaxFileSizeBytes"] = "1048576",
            ["Qlx:Tenant:Security:Upload:AllowedExtensions:0"] = ".png",
            ["Qlx:Tenant:Security:Upload:AllowedExtensions:1"] = ".pdf",
        });

        var options = provider.GetRequiredService<IOptions<TenantSecurityOptions>>().Value;

        Assert.Equal(12, options.Password.MinLength);
        Assert.Equal(0, options.Password.ExpiryDays);
        Assert.Equal(20, options.Session.IdleTimeoutMinutes);
        Assert.Equal(3, options.Lockout.MaxFailedAttempts);
        Assert.Equal(1_048_576, options.Upload.MaxFileSizeBytes);
        Assert.Equal(2, options.Upload.AllowedExtensions.Count);
        Assert.Equal(".png", options.Upload.AllowedExtensions[0]);
        Assert.Equal(".pdf", options.Upload.AllowedExtensions[1]);
    }
}
