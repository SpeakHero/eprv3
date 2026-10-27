/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-25 10:00:00 +08:00 中国标准时间 星期日
 * 修改时间：2026-10-25 10:00:00 +08:00 中国标准时间 星期日
 * 描述：AI 厂商适配器契约（仅协议编解码与 HTTP 调用）
 */

namespace Qlx.Abstractions.Ai;

/// <summary>
/// AI 厂商适配器契约。实现类只负责单厂商协议编解码与 HTTP 调用，
/// 策略、故障转移、日志与异常统一由 <see cref="IAiInvoker"/> 承担。
/// </summary>
public interface IAiChatClient
{
    /// <summary>厂商名称（<c>Qlx:Ai:Providers</c> 中的键）。</summary>
    string ProviderName { get; }

    /// <summary>
    /// 发起一次对话调用。
    /// </summary>
    /// <param name="request">对话请求。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>对话响应。</returns>
    /// <exception cref="AiProviderException">厂商配置缺失、HTTP 调用失败或响应无效时抛出。</exception>
    Task<AiChatResponse> ChatAsync(AiChatRequest request, CancellationToken cancellationToken);
}
