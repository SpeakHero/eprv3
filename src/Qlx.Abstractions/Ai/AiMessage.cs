/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-25 10:00:00 +08:00 中国标准时间 星期日
 * 修改时间：2026-10-25 10:00:00 +08:00 中国标准时间 星期日
 * 描述：AI 对话消息 record
 */

namespace Qlx.Abstractions.Ai;

/// <summary>
/// AI 对话消息。
/// </summary>
/// <param name="Role">消息角色。</param>
/// <param name="Content">消息正文。</param>
public sealed record AiMessage(AiMessageRole Role, string Content);
