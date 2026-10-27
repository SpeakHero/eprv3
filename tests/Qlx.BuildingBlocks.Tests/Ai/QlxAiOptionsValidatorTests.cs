/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-25 10:00:00 +08:00 中国标准时间 星期日
 * 修改时间：2026-10-25 10:00:00 +08:00 中国标准时间 星期日
 * 描述：AI 调用层配置启动校验器单元测试（启用开关、类型注册、地址兜底、策略与固定厂商）
 */

using Microsoft.Extensions.Options;
using Qlx.Abstractions.Ai;
using Qlx.BuildingBlocks.Ai;

namespace Qlx.BuildingBlocks.Tests.Ai;

/// <summary>
/// <see cref="QlxAiOptionsValidator"/> 单元测试：覆盖启用开关、厂商类型注册、地址兜底、
/// 模型与优先级校验、策略取值与固定厂商目标校验（含大小写不敏感）。
/// </summary>
public sealed class QlxAiOptionsValidatorTests
{
    [Fact]
    public void EnabledWithEmptyProvidersFails()
    {
        ValidateOptionsResult result = Validate(AiTestData.Options());

        AssertFailedWith(result, "没有任何 Enabled");
    }

    [Fact]
    public void UnknownProviderTypeFails()
    {
        ValidateOptionsResult result = Validate(AiTestData.Options(
            providers: Providers(("mystery", Provider(type: "mystery")))));

        AssertFailedWith(result, "未注册");
    }

    [Fact]
    public void OpenAiCompatibleWithoutBaseUrlFails()
    {
        ValidateOptionsResult result = Validate(AiTestData.Options(
            providers: Providers(("gateway", Provider(type: AiProviderTypes.OpenAiCompatible, baseUrl: null)))));

        AssertFailedWith(result, "BaseUrl");
    }

    [Fact]
    public void ZhipuWithoutBaseUrlUsesDefaultBaseUrl()
    {
        ValidateOptionsResult result = Validate(AiTestData.Options(
            providers: Providers(("zhipu", Provider(type: AiProviderTypes.Zhipu, baseUrl: null)))));

        AssertPassed(result);
    }

    [Fact]
    public void BlankModelFails()
    {
        ValidateOptionsResult result = Validate(AiTestData.Options(
            providers: Providers(("zhipu", Provider(model: "   ")))));

        AssertFailedWith(result, "Model");
    }

    [Fact]
    public void NegativePriorityFails()
    {
        ValidateOptionsResult result = Validate(AiTestData.Options(
            providers: Providers(("zhipu", Provider(priority: -1)))));

        AssertFailedWith(result, "Priority");
    }

    [Fact]
    public void UnknownStrategyFails()
    {
        ValidateOptionsResult result = Validate(AiTestData.Options(
            strategy: "Unknown",
            providers: Providers(("zhipu", Provider()))));

        AssertFailedWith(result, "Strategy");
    }

    [Fact]
    public void FixedProviderNotConfiguredFails()
    {
        ValidateOptionsResult result = Validate(AiTestData.Options(
            strategy: QlxAiOptions.Strategies.Fixed,
            fixedProvider: "ghost",
            providers: Providers(("zhipu", Provider()))));

        AssertFailedWith(result, "FixedProvider");
    }

    [Fact]
    public void FixedProviderDisabledFails()
    {
        ValidateOptionsResult result = Validate(AiTestData.Options(
            strategy: QlxAiOptions.Strategies.Fixed,
            fixedProvider: "spare",
            providers: Providers(
                ("spare", Provider(enabled: false)),
                ("zhipu", Provider()))));

        AssertFailedWith(result, "未启用");
    }

    [Fact]
    public void DisabledLayerIgnoresProviderLevelErrors()
    {
        ValidateOptionsResult result = Validate(AiTestData.Options(
            enabled: false,
            providers: Providers(
                ("mystery", Provider(type: "mystery", baseUrl: "https://mystery.example.test/v1", model: "  ")),
                ("gateway", Provider(type: AiProviderTypes.OpenAiCompatible, baseUrl: null)))));

        AssertPassed(result);
    }

    [Fact]
    public void DisabledLayerStillRequiresPositiveTimeout()
    {
        QlxAiOptions options = AiTestData.Options(enabled: false) with { TimeoutSeconds = 0 };

        AssertFailedWith(Validate(options), "TimeoutSeconds");
    }

    [Fact]
    public void DisabledLayerStillRequiresPositiveMaxLogContentLength()
    {
        QlxAiOptions options = AiTestData.Options(enabled: false) with { MaxLogContentLength = 0 };

        AssertFailedWith(Validate(options), "MaxLogContentLength");
    }

    [Fact]
    public void MissingProvidersSectionFails()
    {
        // 刻意传 null 厂商字典，模拟配置节缺失的场景
        QlxAiOptions options = new() { Providers = null! };

        AssertFailedWith(Validate(options), "Providers");
    }

    [Fact]
    public void RoundRobinStrategyIsCaseInsensitive()
    {
        ValidateOptionsResult result = Validate(AiTestData.Options(
            strategy: "roundrobin",
            providers: Providers(
                ("zhipu", Provider()),
                ("gateway", Provider(type: AiProviderTypes.OpenAiCompatible, baseUrl: "https://gateway.example.test/v1")))));

        AssertPassed(result);
    }

    [Fact]
    public void FixedProviderNameIsCaseInsensitive()
    {
        ValidateOptionsResult result = Validate(AiTestData.Options(
            strategy: QlxAiOptions.Strategies.Fixed,
            fixedProvider: "ZHIPU",
            providers: Providers(("zhipu", Provider()))));

        AssertPassed(result);
    }

    private static QlxAiOptionsValidator CreateValidator()
        => new(
            [
                new FakeAiChatClientProvider(AiProviderTypes.OpenAiCompatible, defaultBaseUrl: null),
                new FakeAiChatClientProvider(AiProviderTypes.Zhipu, "https://open.bigmodel.cn/api/paas/v4/"),
            ]);

    private static ValidateOptionsResult Validate(QlxAiOptions options)
        => CreateValidator().Validate(null, options);

    private static AiProviderOptions Provider(
        string type = AiProviderTypes.Zhipu,
        string? baseUrl = "https://api.example.test/v1",
        string? model = "glm-4.7-flash",
        bool enabled = true,
        int priority = 100)
        => new()
        {
            Type = type,
            BaseUrl = baseUrl,
            Model = model,
            Enabled = enabled,
            Priority = priority,
        };

    private static Dictionary<string, AiProviderOptions> Providers(
        params (string Name, AiProviderOptions Provider)[] entries)
    {
        Dictionary<string, AiProviderOptions> providers = new();
        foreach ((string name, AiProviderOptions provider) in entries)
        {
            providers[name] = provider;
        }

        return providers;
    }

    private static void AssertFailedWith(ValidateOptionsResult result, string expectedFragment)
    {
        Assert.True(result.Failed, "配置校验应失败。");

        string[] failures = result.Failures is null ? [] : [.. result.Failures];
        Assert.Contains(failures, message => message.Contains(expectedFragment, StringComparison.Ordinal));
    }

    private static void AssertPassed(ValidateOptionsResult result)
        => Assert.True(
            result.Succeeded,
            $"配置校验应通过，实际失败：{string.Join(" | ", result.Failures ?? Array.Empty<string>())}");
}
