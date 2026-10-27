/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-25 10:00:00 +08:00 中国标准时间 星期日
 * 修改时间：2026-10-25 10:00:00 +08:00 中国标准时间 星期日
 * 描述：AI 厂商适配器工厂实现（厂商名 → 配置 → 类型 → 适配器）
 */

using Microsoft.Extensions.Options;

using Qlx.Abstractions.Ai;
using Qlx.Abstractions.Exceptions;

namespace Qlx.BuildingBlocks.Ai;

/// <summary>
/// AI 厂商适配器工厂。按厂商名取配置、按配置 <c>Type</c> 选实现，无状态、线程安全。
/// </summary>
public sealed class AiChatClientFactory : IAiChatClientFactory
{
    private readonly QlxAiOptions _options;
    private readonly IReadOnlyList<IAiChatClientProvider> _providers;

    /// <summary>
    /// 构造函数。
    /// </summary>
    /// <param name="options">AI 调用层全局配置。</param>
    /// <param name="providers">已注册的厂商类型工厂集合。</param>
    public AiChatClientFactory(IOptions<QlxAiOptions> options, IEnumerable<IAiChatClientProvider> providers)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(providers);

        _options = options.Value;
        _providers = [.. providers];
    }

    /// <inheritdoc />
    /// <exception cref="ArgumentException">厂商名为空白时抛出。</exception>
    /// <exception cref="AiInvocationException">厂商未配置或厂商类型未注册时抛出。</exception>
    public IAiChatClient Create(string providerName)
    {
        if (string.IsNullOrWhiteSpace(providerName))
        {
            throw new ArgumentException("AI 厂商名不能为空。", nameof(providerName));
        }

        AiProviderOptions? provider = null;
        foreach ((string name, AiProviderOptions providerOptions) in _options.Providers)
        {
            if (string.Equals(name, providerName, StringComparison.OrdinalIgnoreCase))
            {
                provider = providerOptions;
                break;
            }
        }

        if (provider is null)
        {
            throw new AiInvocationException(
                AiErrorCodes.ProviderNotConfigured,
                $"AI 厂商未配置：{providerName}",
                []);
        }

        IAiChatClientProvider? clientProvider = null;
        foreach (IAiChatClientProvider candidate in _providers)
        {
            if (string.Equals(candidate.Type, provider.Type, StringComparison.OrdinalIgnoreCase))
            {
                clientProvider = candidate;
                break;
            }
        }

        if (clientProvider is null)
        {
            throw new AiInvocationException(
                AiErrorCodes.ProviderTypeUnsupported,
                $"AI 厂商类型未注册：{provider.Type}（厂商 {providerName}）",
                []);
        }

        return clientProvider.Create(providerName, provider);
    }
}
