/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-25 10:00:00 +08:00 中国标准时间 星期日
 * 修改时间：2026-10-25 10:00:00 +08:00 中国标准时间 星期日
 * 描述：AI 调用层 DI 装配扩展（配置绑定与启动校验、命名 HttpClient、厂商工厂、调用门面）
 */

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

using Qlx.Abstractions.Ai;
using Qlx.BuildingBlocks.Ai.Providers;

namespace Qlx.BuildingBlocks.Ai;

/// <summary>
/// AI 调用层装配扩展。配置节 <c>Qlx:Ai</c>，策略 / 厂商 / 超时变更需重启生效（<see cref="IOptions{QlxAiOptions}"/>
/// 与命名 HttpClient 均在启动期定稿，本层不做热切换）；<c>ApiKey</c> 只经环境变量注入
/// （如 <c>Qlx__Ai__Providers__zhipu__ApiKey</c>），配置文件中不得保存明文。
/// </summary>
public static class AiServiceCollectionExtensions
{
    /// <summary>
    /// 注册 AI 调用层：配置绑定 + 启动校验 + 命名 HttpClient + 8 个厂商工厂 + 适配器工厂 + 调用门面。
    /// </summary>
    /// <param name="services">服务集合。</param>
    /// <param name="configuration">配置根。</param>
    /// <returns>服务集合。</returns>
    public static IServiceCollection AddQlxAi(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        _ = services.AddOptions<QlxAiOptions>()
            .BindConfiguration(QlxAiOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        _ = services.AddSingleton<IValidateOptions<QlxAiOptions>, QlxAiOptionsValidator>();

        // 无条件注册基础 HttpClient 工厂：保证 IHttpClientFactory 恒有注册，
        // 启动校验解析厂商工厂时不会因「未注册任何 HttpClient」而失败。
        _ = services.AddHttpClient();

        // 强类型绑定后读取，禁止裸读配置字符串键
        QlxAiOptions options = configuration.GetSection(QlxAiOptions.SectionName).Get<QlxAiOptions>() ?? new QlxAiOptions();

        foreach ((string providerName, AiProviderOptions provider) in options.Providers)
        {
            if (!provider.Enabled)
            {
                continue;
            }

            TimeSpan timeout = TimeSpan.FromSeconds(provider.TimeoutSeconds ?? options.TimeoutSeconds);
            _ = services.AddHttpClient(AiHttpClientNames.For(providerName), client => client.Timeout = timeout);
        }

        _ = services.AddSingleton<IAiChatClientProvider, OpenAiCompatibleChatClientProvider>();
        _ = services.AddSingleton<IAiChatClientProvider, ZhipuChatClientProvider>();
        _ = services.AddSingleton<IAiChatClientProvider, DashScopeChatClientProvider>();
        _ = services.AddSingleton<IAiChatClientProvider, VolcengineArkChatClientProvider>();
        _ = services.AddSingleton<IAiChatClientProvider, SiliconFlowChatClientProvider>();
        _ = services.AddSingleton<IAiChatClientProvider, DeepSeekChatClientProvider>();
        _ = services.AddSingleton<IAiChatClientProvider, QianfanChatClientProvider>();
        _ = services.AddSingleton<IAiChatClientProvider, OllamaChatClientProvider>();

        services.TryAddSingleton<IAiChatClientFactory, AiChatClientFactory>();
        services.TryAddSingleton<IAiInvoker, AiInvoker>();

        return services;
    }
}
