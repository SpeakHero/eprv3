/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-22 19:00:00 +08:00 中国标准时间 星期四
 * 描述：审批实例与节点任务状态机（跟随 2C JobStateMachine / 2D MessageStateMachine 模式），非法转换拒绝
 */

using Qlx.Abstractions.Approvals;

namespace Qlx.Modules.Support.Services.Approvals;

/// <summary>
/// 审批实例状态机。转移表：Draft→{Pending}；Pending→{Approved,Rejected,Withdrawn,Terminated,Canceled}；
/// Approved/Rejected/Withdrawn/Terminated/Canceled 为终态（无出边）。
/// </summary>
public static class ApprovalInstanceStateMachine
{
    private static readonly Dictionary<ApprovalStatus, IReadOnlySet<ApprovalStatus>> s_transitions =
        new Dictionary<ApprovalStatus, IReadOnlySet<ApprovalStatus>>
        {
            // 草稿仅可提交进入流转
            [ApprovalStatus.Draft] = new HashSet<ApprovalStatus> { ApprovalStatus.Pending },

            // 审批中：全量终态出口（通过 / 驳回 / 撤回 / 系统终止 / 取消）
            [ApprovalStatus.Pending] = new HashSet<ApprovalStatus>
                {
                    ApprovalStatus.Approved,
                    ApprovalStatus.Rejected,
                    ApprovalStatus.Withdrawn,
                    ApprovalStatus.Terminated,
                    ApprovalStatus.Canceled,
                },
        };

    /// <summary>判定实例状态转移是否合法。</summary>
    /// <param name="from">当前状态。</param>
    /// <param name="to">目标状态。</param>
    /// <returns>合法返回 true。</returns>
    public static bool CanTransition(ApprovalStatus from, ApprovalStatus to)
        => from != to && s_transitions.TryGetValue(from, out var targets) && targets.Contains(to);

    /// <summary>校验实例状态转移，非法转移抛出异常。</summary>
    /// <param name="from">当前状态。</param>
    /// <param name="to">目标状态。</param>
    /// <exception cref="InvalidOperationException">非法转移时抛出。</exception>
    public static void EnsureTransition(ApprovalStatus from, ApprovalStatus to)
    {
        if (!CanTransition(from, to))
        {
            throw new InvalidOperationException($"非法的审批实例状态转移：{from} → {to}");
        }
    }

    /// <summary>获取实例某状态允许转移到的目标集合（终态为空集）。</summary>
    /// <param name="from">当前状态。</param>
    /// <returns>允许的目标状态集合。</returns>
    public static IReadOnlySet<ApprovalStatus> AllowedTargets(ApprovalStatus from)
        => s_transitions.TryGetValue(from, out var targets) ? targets : new HashSet<ApprovalStatus>();

    /// <summary>判定实例是否终态。</summary>
    /// <param name="status">待判定状态。</param>
    /// <returns>终态返回 true。</returns>
    public static bool IsTerminal(ApprovalStatus status) => status != ApprovalStatus.Draft && status != ApprovalStatus.Pending;
}

/// <summary>
/// 审批任务状态机。转移表：Pending→{Approved,Rejected,Transferred,AddSigned,Canceled}；
/// Approved/Rejected/Transferred/AddSigned/Canceled 为终态（无出边）。
/// </summary>
public static class ApprovalTaskStateMachine
{
    private static readonly Dictionary<ApprovalTaskStatus, IReadOnlySet<ApprovalTaskStatus>> s_transitions =
        new Dictionary<ApprovalTaskStatus, IReadOnlySet<ApprovalTaskStatus>>
        {
            // 待处理：通过 / 驳回 / 转办（原任务转出）/ 加签（原任务标记）/ 取消（或签他决、加签重构、上报重构）
            [ApprovalTaskStatus.Pending] = new HashSet<ApprovalTaskStatus>
                {
                    ApprovalTaskStatus.Approved,
                    ApprovalTaskStatus.Rejected,
                    ApprovalTaskStatus.Transferred,
                    ApprovalTaskStatus.AddSigned,
                    ApprovalTaskStatus.Canceled,
                },
        };

    /// <summary>判定任务状态转移是否合法。</summary>
    /// <param name="from">当前状态。</param>
    /// <param name="to">目标状态。</param>
    /// <returns>合法返回 true。</returns>
    public static bool CanTransition(ApprovalTaskStatus from, ApprovalTaskStatus to)
        => from != to && s_transitions.TryGetValue(from, out var targets) && targets.Contains(to);

    /// <summary>校验任务状态转移，非法转移抛出异常。</summary>
    /// <param name="from">当前状态。</param>
    /// <param name="to">目标状态。</param>
    /// <exception cref="InvalidOperationException">非法转移时抛出。</exception>
    public static void EnsureTransition(ApprovalTaskStatus from, ApprovalTaskStatus to)
    {
        if (!CanTransition(from, to))
        {
            throw new InvalidOperationException($"非法的审批任务状态转移：{from} → {to}");
        }
    }

    /// <summary>判定任务是否终态。</summary>
    /// <param name="status">待判定状态。</param>
    /// <returns>终态返回 true。</returns>
    public static bool IsTerminal(ApprovalTaskStatus status) => status != ApprovalTaskStatus.Pending;
}
