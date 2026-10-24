/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-23 06:00:00 +08:00 中国标准时间 星期五
 * 描述：消息发送状态机（Draft→Queued→Sent/Failed→Delivered/Read→Archived；Failed 可重回 Queued 重试）
 */

using Qlx.Abstractions.Messages;

namespace Qlx.Modules.Support.Services.Messages;

/// <summary>
/// 消息发送状态机。转移表：Draft→{Queued}；Queued→{Sent,Failed}；Sent→{Delivered,Read}；
/// Delivered→{Read}；Read→{Archived}；Failed→{Queued}；Archived 终态。
/// </summary>
public static class MessageStateMachine
{
    private static readonly Dictionary<MessageStatus, IReadOnlySet<MessageStatus>> s_transitions =
        new Dictionary<MessageStatus, IReadOnlySet<MessageStatus>>
        {
            [MessageStatus.Draft] = new HashSet<MessageStatus> { MessageStatus.Queued },

            // 入队后：渠道返回成功 / 失败（失败可重试回 Queued）
            [MessageStatus.Queued] = new HashSet<MessageStatus>
                { MessageStatus.Sent, MessageStatus.Failed },

            // 已发送（渠道受理成功，不保证送达）：回执送达或用户已读
            [MessageStatus.Sent] = new HashSet<MessageStatus>
                { MessageStatus.Delivered, MessageStatus.Read },

            // 已确认送达：用户已读
            [MessageStatus.Delivered] = new HashSet<MessageStatus> { MessageStatus.Read },

            // 已读：归档（手动或归档任务）
            [MessageStatus.Read] = new HashSet<MessageStatus> { MessageStatus.Archived },

            // 发送失败：重试重新入队
            [MessageStatus.Failed] = new HashSet<MessageStatus> { MessageStatus.Queued },
        };

    /// <summary>判定状态转移是否合法。</summary>
    /// <param name="from">当前状态。</param>
    /// <param name="to">目标状态。</param>
    /// <returns>合法返回 true。</returns>
    public static bool CanTransition(MessageStatus from, MessageStatus to)
        => from != to && s_transitions.TryGetValue(from, out var targets) && targets.Contains(to);

    /// <summary>校验状态转移，非法转移抛出异常。</summary>
    /// <param name="from">当前状态。</param>
    /// <param name="to">目标状态。</param>
    /// <exception cref="InvalidOperationException">非法转移时抛出。</exception>
    public static void EnsureTransition(MessageStatus from, MessageStatus to)
    {
        if (!CanTransition(from, to))
        {
            throw new InvalidOperationException($"非法的消息状态转移：{from} → {to}");
        }
    }

    /// <summary>获取某状态允许转移到的目标集合（终态为空集）。</summary>
    /// <param name="from">当前状态。</param>
    /// <returns>允许的目标状态集合。</returns>
    public static IReadOnlySet<MessageStatus> AllowedTargets(MessageStatus from)
        => s_transitions.TryGetValue(from, out var targets) ? targets : new HashSet<MessageStatus>();

    /// <summary>判定是否终态（Archived）。</summary>
    /// <param name="status">待判定状态。</param>
    /// <returns>终态返回 true。</returns>
    public static bool IsTerminal(MessageStatus status) => status == MessageStatus.Archived;
}
