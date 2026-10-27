/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-25 10:00:00 +08:00 中国标准时间 星期日
 * 修改时间：2026-10-25 10:00:00 +08:00 中国标准时间 星期日
 * 描述：AI 对话消息角色枚举
 */

namespace Qlx.Abstractions.Ai;

/// <summary>
/// AI 对话消息角色。
/// </summary>
public enum AiMessageRole
{
    /// <summary>系统提示词。</summary>
    System = 1,

    /// <summary>用户消息。</summary>
    User = 2,

    /// <summary>模型回复。</summary>
    Assistant = 3
}
