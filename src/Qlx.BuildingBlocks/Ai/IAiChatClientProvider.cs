/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-25 10:00:00 +08:00 中国标准时间 星期日
 * 修改时间：2026-10-25 10:00:00 +08:00 中国标准时间 星期日
 * 描述：AI 厂商适配器工厂契约（按厂商类型创建适配器实例）
 */

using Qlx.Abstractions.Ai;

namespace Qlx.BuildingBlocks.Ai;

/// <summary>
/// AI 厂商适配器工厂契约。每个厂商类型注册一个实现，
/// <see cref="AiChatClientFactory"/> 按 <see cref="Type"/> 匹配后委托创建适配器。
/// 新增厂商 = 新增一个实现类 + DI 注册一行 + 配置项，核心类零改动。
/// </summary>
public interface IAiChatClientProvider
{
    /// <summary>厂商类型标识（见 <see cref="AiProviderTypes"/>，大小写不敏感）。</summary>
    string Type { get; }

    /// <summary>该类型的默认接口基地址；无默认地址时为 <see langword="null"/>（必须显式配置 <c>BaseUrl</c>）。</summary>
    string? DefaultBaseUrl { get; }

    /// <summary>
    /// 创建指定厂商的适配器实例。
    /// </summary>
    /// <param name="providerName">厂商名（<c>Qlx:Ai:Providers</c> 的键）。</param>
    /// <param name="provider">该厂商的配置项。</param>
    /// <returns>厂商适配器实例。</returns>
    IAiChatClient Create(string providerName, AiProviderOptions provider);
}
