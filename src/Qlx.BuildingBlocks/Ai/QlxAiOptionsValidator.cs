/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-25 10:00:00 +08:00 中国标准时间 星期日
 * 修改时间：2026-10-25 10:00:00 +08:00 中国标准时间 星期日
 * 描述：AI 调用层配置启动校验器（类型已注册 / 地址可解析 / 模型与超时必填）
 */

using Microsoft.Extensions.Options;

namespace Qlx.BuildingBlocks.Ai;

/// <summary>
/// AI 调用层配置启动校验器。启用（<see cref="QlxAiOptions.Enabled"/> 为 <see langword="true"/>）时
/// 校验每个启用厂商的类型已注册、地址可解析、模型非空、优先级与超时合法，并校验固定模式目标厂商存在且启用。
/// </summary>
public sealed class QlxAiOptionsValidator(IEnumerable<IAiChatClientProvider> providers) : IValidateOptions<QlxAiOptions>
{
    private readonly IReadOnlyList<IAiChatClientProvider> _providers = [.. providers];

    /// <summary>
    /// 执行配置校验。
    /// </summary>
    /// <param name="name">命名选项名（本层未使用命名选项）。</param>
    /// <param name="options">待校验的配置。</param>
    /// <returns>校验结果。</returns>
    public ValidateOptionsResult Validate(string? name, QlxAiOptions options)
    {
        List<string> errors = [];

        if (options.TimeoutSeconds <= 0)
        {
            errors.Add($"Qlx:Ai:TimeoutSeconds 必须大于 0，当前为 {options.TimeoutSeconds}。");
        }

        if (options.MaxLogContentLength <= 0)
        {
            errors.Add($"Qlx:Ai:MaxLogContentLength 必须大于 0，当前为 {options.MaxLogContentLength}。");
        }

        if (!string.Equals(options.Strategy, QlxAiOptions.Strategies.Fixed, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(options.Strategy, QlxAiOptions.Strategies.RoundRobin, StringComparison.OrdinalIgnoreCase))
        {
            errors.Add($"Qlx:Ai:Strategy 仅支持 Fixed / RoundRobin，当前为「{options.Strategy}」。");
        }

        if (options.Providers is null)
        {
            errors.Add("Qlx:Ai:Providers 配置节缺失。");
            return ValidateOptionsResult.Fail(errors);
        }

        if (!options.Enabled)
        {
            return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
        }

        int enabledProviderCount = 0;
        foreach ((string providerName, AiProviderOptions provider) in options.Providers)
        {
            if (!provider.Enabled)
            {
                continue;
            }

            enabledProviderCount++;

            if (provider.Priority < 0)
            {
                errors.Add($"厂商 {providerName} 的 Priority 不能为负数，当前为 {provider.Priority}。");
            }

            if (string.IsNullOrWhiteSpace(provider.Model))
            {
                errors.Add($"厂商 {providerName} 的 Model 未配置。");
            }

            int effectiveTimeoutSeconds = provider.TimeoutSeconds ?? options.TimeoutSeconds;
            if (effectiveTimeoutSeconds <= 0)
            {
                errors.Add($"厂商 {providerName} 的超时配置必须大于 0，当前为 {effectiveTimeoutSeconds} 秒。");
            }

            IAiChatClientProvider? registered = FindProvider(provider.Type);
            if (registered is null)
            {
                errors.Add($"厂商 {providerName} 的 Type「{provider.Type}」未注册。");
                continue;
            }

            string? baseUrl = provider.BaseUrl;
            if (string.IsNullOrWhiteSpace(baseUrl))
            {
                baseUrl = registered.DefaultBaseUrl;
            }

            if (string.IsNullOrWhiteSpace(baseUrl) || !Uri.TryCreate(baseUrl, UriKind.Absolute, out _))
            {
                errors.Add($"厂商 {providerName} 的 BaseUrl 无法解析（当前值：{baseUrl ?? "(未配置)"}），请配置绝对地址或改用带默认地址的厂商类型。");
            }
        }

        if (enabledProviderCount == 0)
        {
            errors.Add("Qlx:Ai 已启用（Enabled=true），但没有任何 Enabled=true 的厂商。");
        }

        if (string.Equals(options.Strategy, QlxAiOptions.Strategies.Fixed, StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(options.FixedProvider))
        {
            bool fixedProviderExists = false;
            bool fixedProviderEnabled = false;
            foreach ((string providerName, AiProviderOptions provider) in options.Providers)
            {
                if (!string.Equals(providerName, options.FixedProvider, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                fixedProviderExists = true;
                fixedProviderEnabled = provider.Enabled;
                break;
            }

            if (!fixedProviderExists)
            {
                errors.Add($"Qlx:Ai:FixedProvider「{options.FixedProvider}」未在 Providers 中配置。");
            }
            else if (!fixedProviderEnabled)
            {
                errors.Add($"Qlx:Ai:FixedProvider「{options.FixedProvider}」未启用（Enabled=false）。");
            }
        }

        return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
    }

    private IAiChatClientProvider? FindProvider(string type)
    {
        foreach (IAiChatClientProvider candidate in _providers)
        {
            if (string.Equals(candidate.Type, type, StringComparison.OrdinalIgnoreCase))
            {
                return candidate;
            }
        }

        return null;
    }
}
