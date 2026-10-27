/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-25 10:00:00 +08:00 中国标准时间 星期日
 * 修改时间：2026-10-25 10:00:00 +08:00 中国标准时间 星期日
 * 描述：AI 调用业务门面契约（策略、故障转移、日志与异常）
 */

using Qlx.Abstractions.Exceptions;

namespace Qlx.Abstractions.Ai;

/// <summary>
/// AI 调用业务门面。业务代码只依赖本接口，不感知厂商类型、地址、模型与密钥。
/// </summary>
public interface IAiInvoker
{
    /// <summary>
    /// 按配置策略发起对话调用，任一候选厂商成功即返回。
    /// </summary>
    /// <param name="request">对话请求。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>对话响应。</returns>
    /// <exception cref="ArgumentException">消息列表为空时抛出（调用方编程错误）。</exception>
    /// <exception cref="AiInvocationException">AI 未启用、无可用厂商或全部候选厂商均失败时抛出。</exception>
    Task<AiChatResponse> InvokeAsync(AiChatRequest request, CancellationToken cancellationToken);
}
