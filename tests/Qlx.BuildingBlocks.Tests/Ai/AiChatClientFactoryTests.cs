/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-25 10:00:00 +08:00 中国标准时间 星期日
 * 修改时间：2026-10-25 10:00:00 +08:00 中国标准时间 星期日
 * 描述：AI 厂商适配器工厂单元测试（厂商名解析、未配置/未注册异常、大小写不敏感、空白名校验）
 */

using Qlx.Abstractions.Ai;
using Qlx.Abstractions.Exceptions;
using Qlx.BuildingBlocks.Ai;

namespace Qlx.BuildingBlocks.Tests.Ai;

/// <summary>
/// <see cref="AiChatClientFactory"/> 单元测试。
/// </summary>
public sealed class AiChatClientFactoryTests
{
    private static AiChatClientFactory CreateFactory(
        QlxAiOptions options,
        params IAiChatClientProvider[] providers)
        => new(AiTestData.Wrap(options), providers);

    [Fact]
    public void CreateReturnsRegisteredClientForConfiguredProvider()
    {
        Dictionary<string, AiProviderOptions> providers = new(StringComparer.OrdinalIgnoreCase)
        {
            ["zhipu"] = new() { Type = AiProviderTypes.Zhipu, Model = "glm-4" },
        };
        FakeAiChatClientProvider provider = new FakeAiChatClientProvider(AiProviderTypes.Zhipu)
            .Register("zhipu", RecordingAiChatClient.Succeed("zhipu"));
        AiChatClientFactory factory = CreateFactory(AiTestData.Options(providers: providers), provider);

        IAiChatClient client = factory.Create("zhipu");

        Assert.Equal("zhipu", client.ProviderName);
        Assert.Single(provider.CreatedProviderNames);
        Assert.Equal("zhipu", provider.CreatedProviderNames[0]);
    }

    [Fact]
    public void CreateWithUnknownProviderNameThrowsProviderNotConfigured()
    {
        AiChatClientFactory factory = CreateFactory(
            AiTestData.Options(),
            new FakeAiChatClientProvider(AiProviderTypes.Zhipu));

        AiInvocationException exception = Assert.Throws<AiInvocationException>(() => factory.Create("missing"));

        Assert.Equal(AiErrorCodes.ProviderNotConfigured, exception.ErrorCode);
    }

    [Fact]
    public void CreateWithUnregisteredProviderTypeThrowsProviderTypeUnsupported()
    {
        Dictionary<string, AiProviderOptions> providers = new(StringComparer.OrdinalIgnoreCase)
        {
            ["custom"] = new() { Type = "unregistered-type", Model = "m" },
        };
        AiChatClientFactory factory = CreateFactory(
            AiTestData.Options(providers: providers),
            new FakeAiChatClientProvider(AiProviderTypes.Zhipu));

        AiInvocationException exception = Assert.Throws<AiInvocationException>(() => factory.Create("custom"));

        Assert.Equal(AiErrorCodes.ProviderTypeUnsupported, exception.ErrorCode);
    }

    [Fact]
    public void CreateResolvesProviderNameAndTypeCaseInsensitively()
    {
        Dictionary<string, AiProviderOptions> providers = new(StringComparer.OrdinalIgnoreCase)
        {
            ["ZhiPu"] = new() { Type = "ZHIPU", Model = "glm-4" },
        };
        FakeAiChatClientProvider provider = new FakeAiChatClientProvider(AiProviderTypes.Zhipu)
            .Register("zhipu", RecordingAiChatClient.Succeed("zhipu"));
        AiChatClientFactory factory = CreateFactory(AiTestData.Options(providers: providers), provider);

        IAiChatClient lowerCase = factory.Create("zhipu");
        IAiChatClient upperCase = factory.Create("ZHIPU");

        Assert.Equal("zhipu", lowerCase.ProviderName);
        Assert.Equal("zhipu", upperCase.ProviderName);
        Assert.Equal(2, provider.CreatedProviderNames.Count);
    }

    [Fact]
    public void CreateWithWhitespaceProviderNameThrowsArgumentException()
    {
        AiChatClientFactory factory = CreateFactory(
            AiTestData.Options(),
            new FakeAiChatClientProvider(AiProviderTypes.Zhipu));

        ArgumentException exception = Assert.Throws<ArgumentException>(() => factory.Create("  "));

        Assert.Equal("providerName", exception.ParamName);
    }
}
