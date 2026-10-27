/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-25 10:00:00 +08:00 中国标准时间 星期日
 * 修改时间：2026-10-25 10:00:00 +08:00 中国标准时间 星期日
 * 描述：AddQlxAi 依赖注入装配测试（8 个厂商工厂、默认基地址、门面可解析、适配器可创建）
 */

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Qlx.Abstractions.Ai;
using Qlx.BuildingBlocks.Ai;

namespace Qlx.BuildingBlocks.Tests.Ai;

/// <summary>
/// <see cref="AiServiceCollectionExtensions.AddQlxAi"/> 装配测试。
/// </summary>
public sealed class AiProviderWiringTests
{
    private const string EnabledConfigurationKey = "Qlx:Ai:Enabled";

    private static ServiceProvider BuildProvider()
    {
        IConfigurationRoot configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                [EnabledConfigurationKey] = "false",
            })
            .Build();

        ServiceCollection services = new();
        // 测试容器未经过宿主启动：显式注册配置与日志，保证 BindConfiguration 与 ILogger<T> 可解析
        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging();
        services.AddQlxAi(configuration);
        return services.BuildServiceProvider();
    }

    [Fact]
    public void RegistersExactlyEightProvidersWithDistinctTypes()
    {
        using ServiceProvider provider = BuildProvider();

        IAiChatClientProvider[] registered = [.. provider.GetServices<IAiChatClientProvider>()];

        Assert.Equal(8, registered.Length);
        Assert.Equal(8, registered.Select(static item => item.Type).Distinct(StringComparer.OrdinalIgnoreCase).Count());

        string[] expectedTypes =
        [
            AiProviderTypes.OpenAiCompatible,
            AiProviderTypes.Zhipu,
            AiProviderTypes.DashScope,
            AiProviderTypes.Volcengine,
            AiProviderTypes.SiliconFlow,
            AiProviderTypes.DeepSeek,
            AiProviderTypes.Qianfan,
            AiProviderTypes.Ollama,
        ];
        foreach (string expectedType in expectedTypes)
        {
            Assert.Contains(
                registered,
                candidate => string.Equals(candidate.Type, expectedType, StringComparison.OrdinalIgnoreCase));
        }
    }

    [Fact]
    public void ProviderDefaultBaseUrlsMatchExpectedValues()
    {
        using ServiceProvider provider = BuildProvider();

        IAiChatClientProvider[] registered = [.. provider.GetServices<IAiChatClientProvider>()];
        Dictionary<string, string?> expectedBaseUrls = new(StringComparer.OrdinalIgnoreCase)
        {
            [AiProviderTypes.Zhipu] = "https://open.bigmodel.cn/api/paas/v4/",
            [AiProviderTypes.DashScope] = "https://dashscope.aliyuncs.com/compatible-mode/v1/",
            [AiProviderTypes.Volcengine] = "https://ark.cn-beijing.volces.com/api/v3/",
            [AiProviderTypes.SiliconFlow] = "https://api.siliconflow.cn/v1/",
            [AiProviderTypes.DeepSeek] = "https://api.deepseek.com/v1/",
            [AiProviderTypes.Qianfan] = "https://qianfan.baidubce.com/v2/",
            [AiProviderTypes.OpenAiCompatible] = null,
            [AiProviderTypes.Ollama] = "http://localhost:11434/",
        };

        foreach ((string providerType, string? expectedBaseUrl) in expectedBaseUrls)
        {
            IAiChatClientProvider matched = registered.Single(
                candidate => string.Equals(candidate.Type, providerType, StringComparison.OrdinalIgnoreCase));

            Assert.Equal(expectedBaseUrl, matched.DefaultBaseUrl);
        }
    }

    [Fact]
    public void FactoryAndInvokerAreResolvable()
    {
        using ServiceProvider provider = BuildProvider();

        IAiChatClientFactory factory = provider.GetRequiredService<IAiChatClientFactory>();
        IAiInvoker invoker = provider.GetRequiredService<IAiInvoker>();

        Assert.NotNull(factory);
        Assert.NotNull(invoker);
    }

    [Theory]
    [InlineData(AiProviderTypes.Zhipu)]
    [InlineData(AiProviderTypes.DashScope)]
    [InlineData(AiProviderTypes.Volcengine)]
    [InlineData(AiProviderTypes.SiliconFlow)]
    [InlineData(AiProviderTypes.DeepSeek)]
    [InlineData(AiProviderTypes.Qianfan)]
    [InlineData(AiProviderTypes.OpenAiCompatible)]
    public void CommercialProviderCreatesClientWithConfiguredName(string providerType)
    {
        using ServiceProvider provider = BuildProvider();
        AiProviderOptions options = new() { Type = providerType, Model = "m", ApiKey = "k" };

        IAiChatClientProvider clientProvider = provider.GetServices<IAiChatClientProvider>().Single(
            candidate => string.Equals(candidate.Type, providerType, StringComparison.OrdinalIgnoreCase));

        IAiChatClient client = clientProvider.Create("p", options);

        Assert.Equal("p", client.ProviderName);
    }

    [Fact]
    public void OllamaProviderCreatesClientWithoutApiKey()
    {
        using ServiceProvider provider = BuildProvider();
        AiProviderOptions options = new() { Type = AiProviderTypes.Ollama, Model = "llama3" };

        IAiChatClientProvider clientProvider = provider.GetServices<IAiChatClientProvider>().Single(
            candidate => string.Equals(candidate.Type, AiProviderTypes.Ollama, StringComparison.OrdinalIgnoreCase));

        IAiChatClient client = clientProvider.Create("p", options);

        Assert.Equal("p", client.ProviderName);
    }
}
