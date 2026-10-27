/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-25 10:00:00 +08:00 中国标准时间 星期日
 * 修改时间：2026-10-25 10:00:00 +08:00 中国标准时间 星期日
 * 描述：AI 厂商适配器获取门面契约（按厂商名解析配置与类型）
 */

using Qlx.Abstractions.Ai;

namespace Qlx.BuildingBlocks.Ai;

/// <summary>
/// AI 厂商适配器获取门面契约。按厂商名解析配置并按厂商类型创建适配器，
/// 业务层不出现任何具体实现类的 <c>new</c> 或类型判断。
/// </summary>
public interface IAiChatClientFactory
{
    /// <summary>
    /// 按厂商名创建适配器实例。
    /// </summary>
    /// <param name="providerName">厂商名（<c>Qlx:Ai:Providers</c> 的键，大小写不敏感）。</param>
    /// <returns>厂商适配器实例。</returns>
    /// <exception cref="ArgumentException">厂商名为空白时抛出。</exception>
    /// <exception cref="Qlx.Abstractions.Exceptions.AiInvocationException">厂商未配置或厂商类型未注册时抛出。</exception>
    IAiChatClient Create(string providerName);
}
