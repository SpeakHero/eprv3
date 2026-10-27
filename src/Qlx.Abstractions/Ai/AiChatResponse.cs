/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-25 10:00:00 +08:00 中国标准时间 星期日
 * 修改时间：2026-10-25 10:00:00 +08:00 中国标准时间 星期日
 * 描述：AI 对话响应 record
 */

namespace Qlx.Abstractions.Ai;

/// <summary>
/// AI 对话响应。
/// </summary>
/// <param name="Content">模型输出内容。</param>
/// <param name="ProviderName">实际应答的厂商名。</param>
/// <param name="Model">实际使用的模型名。</param>
/// <param name="DurationMilliseconds">本次调用耗时（毫秒）。</param>
/// <param name="PromptTokens">输入 token 数（厂商未返回时为 <see langword="null"/>）。</param>
/// <param name="CompletionTokens">输出 token 数（厂商未返回时为 <see langword="null"/>）。</param>
/// <param name="FinishReason">结束原因（厂商返回时透传）。</param>
public sealed record AiChatResponse(
    string Content,
    string ProviderName,
    string Model,
    long DurationMilliseconds,
    int? PromptTokens,
    int? CompletionTokens,
    string? FinishReason);
