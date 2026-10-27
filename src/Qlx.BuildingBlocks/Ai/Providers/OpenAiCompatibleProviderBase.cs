/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-25 10:00:00 +08:00 中国标准时间 星期日
 * 修改时间：2026-10-25 10:00:00 +08:00 中国标准时间 星期日
 * 描述：OpenAI 兼容协议适配器的厂商工厂抽象基类（持有命名 HttpClient 工厂）
 */

using Qlx.Abstractions.Ai;

namespace Qlx.BuildingBlocks.Ai.Providers;

/// <summary>
/// OpenAI 兼容协议适配器的厂商工厂抽象基类。子类只需声明 <see cref="Type"/> 与
/// <see cref="DefaultBaseUrl"/>，即复用协议内核 <see cref="OpenAiCompatibleChatClient"/>。
/// </summary>
/// <remarks>
/// 构造函数。
/// </remarks>
/// <param name="httpClientFactory">命名 HttpClient 工厂。</param>
public abstract class OpenAiCompatibleProviderBase(IHttpClientFactory httpClientFactory) : IAiChatClientProvider
{
    /// <summary>命名 HttpClient 工厂，供子类创建具体适配器实例。</summary>
    protected IHttpClientFactory HttpClientFactory { get; } =
        httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));

    /// <inheritdoc />
    public abstract string Type { get; }

    /// <inheritdoc />
    public abstract string? DefaultBaseUrl { get; }

    /// <inheritdoc />
    public virtual IAiChatClient Create(string providerName, AiProviderOptions provider)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerName);
        ArgumentNullException.ThrowIfNull(provider);

        return new OpenAiCompatibleChatClient(providerName, provider, HttpClientFactory, DefaultBaseUrl);
    }
}
