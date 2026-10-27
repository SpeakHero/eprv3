/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-25 10:00:00 +08:00 中国标准时间 星期日
 * 修改时间：2026-10-25 10:00:00 +08:00 中国标准时间 星期日
 * 描述：AI 对话请求 record
 */

namespace Qlx.Abstractions.Ai;

/// <summary>
/// AI 对话请求。
/// </summary>
public sealed record AiChatRequest
{
    /// <summary>调用用途标识（仅用于日志排障，可为空）。</summary>
    public string? Purpose { get; init; }

    /// <summary>消息列表；为空视为调用方编程错误。</summary>
    public required IReadOnlyList<AiMessage> Messages { get; init; }

    /// <summary>模型名；为空时使用厂商配置的模型。</summary>
    public string? Model { get; init; }

    /// <summary>采样温度；为空时使用厂商默认值。</summary>
    public double? Temperature { get; init; }

    /// <summary>最大生成 token 数；为空时使用厂商默认值。</summary>
    public int? MaxTokens { get; init; }

    /// <summary>响应格式，默认纯文本。</summary>
    public AiResponseFormat ResponseFormat { get; init; } = AiResponseFormat.Text;
}
