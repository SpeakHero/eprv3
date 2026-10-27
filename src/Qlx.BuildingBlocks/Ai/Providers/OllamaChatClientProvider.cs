/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-25 10:00:00 +08:00 中国标准时间 星期日
 * 修改时间：2026-10-25 10:00:00 +08:00 中国标准时间 星期日
 * 描述：本地 Ollama 厂商工厂（默认地址 http://localhost:11434/，免密钥）
 */

using Qlx.Abstractions.Ai;

namespace Qlx.BuildingBlocks.Ai.Providers;

/// <summary>
/// 本地 Ollama 厂商工厂。使用 <see cref="OllamaChatClient"/>（<c>/api/chat</c>），不要求 <c>ApiKey</c>。
/// </summary>
/// <remarks>
/// 构造函数。
/// </remarks>
/// <param name="httpClientFactory">命名 HttpClient 工厂。</param>
public sealed class OllamaChatClientProvider(IHttpClientFactory httpClientFactory)
    : OpenAiCompatibleProviderBase(httpClientFactory)
{
    /// <inheritdoc />
    public override string Type => AiProviderTypes.Ollama;

    /// <inheritdoc />
    public override string? DefaultBaseUrl => "http://localhost:11434/";

    /// <inheritdoc />
    public override IAiChatClient Create(string providerName, AiProviderOptions provider)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerName);
        ArgumentNullException.ThrowIfNull(provider);

        return new OllamaChatClient(providerName, provider, HttpClientFactory, DefaultBaseUrl);
    }
}
