/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-22 21:35:00 +08:00 中国标准时间 星期四
 * 描述：任务执行状态机（PENDING→QUEUED→RUNNING→终态；SYNC 捷径 PENDING→RUNNING）转移规则
 */

using Qlx.Abstractions.Jobs;

namespace Qlx.BuildingBlocks.Jobs;

/// <summary>
/// 任务执行状态机。方案 §4.12 D19：状态转换不可跳过
/// （PENDING 不能直接 SUCCESS；SYNC 模式捷径为 PENDING→RUNNING→终态）。
/// </summary>
public static class JobStateMachine
{
    private static readonly Dictionary<JobExecutionStatus, IReadOnlySet<JobExecutionStatus>> s_transitions =
        new Dictionary<JobExecutionStatus, IReadOnlySet<JobExecutionStatus>>
        {
            // 登记 → 入队（ASYNC）/ 直接开始（SYNC 捷径）/ 登记期取消
            [JobExecutionStatus.Pending] = new HashSet<JobExecutionStatus>
                { JobExecutionStatus.Queued, JobExecutionStatus.Running, JobExecutionStatus.Canceled },

            // 已入 Hangfire 队列 → 开始执行 / 用户取消
            [JobExecutionStatus.Queued] = new HashSet<JobExecutionStatus>
                { JobExecutionStatus.Running, JobExecutionStatus.Canceled },

            // 执行中 → 全部终态（超时强制终止 / 用户取消均可能）
            [JobExecutionStatus.Running] = new HashSet<JobExecutionStatus>
                {
                    JobExecutionStatus.Success,
                    JobExecutionStatus.Failed,
                    JobExecutionStatus.Timeout,
                    JobExecutionStatus.Canceled,
                },
        };

    /// <summary>判定状态转移是否合法。</summary>
    /// <param name="from">当前状态。</param>
    /// <param name="to">目标状态。</param>
    /// <returns>合法返回 true。</returns>
    public static bool CanTransition(JobExecutionStatus from, JobExecutionStatus to)
        => from != to && s_transitions.TryGetValue(from, out var targets) && targets.Contains(to);

    /// <summary>校验状态转移，非法转移抛出异常。</summary>
    /// <param name="from">当前状态。</param>
    /// <param name="to">目标状态。</param>
    /// <exception cref="InvalidOperationException">非法转移时抛出。</exception>
    public static void EnsureTransition(JobExecutionStatus from, JobExecutionStatus to)
    {
        if (!CanTransition(from, to))
        {
            throw new InvalidOperationException($"非法的任务状态转移：{from} → {to}");
        }
    }

    /// <summary>获取某状态允许转移到的目标集合（终态为空集）。</summary>
    /// <param name="from">当前状态。</param>
    /// <returns>允许的目标状态集合。</returns>
    public static IReadOnlySet<JobExecutionStatus> AllowedTargets(JobExecutionStatus from)
        => s_transitions.TryGetValue(from, out var targets) ? targets : new HashSet<JobExecutionStatus>();

    /// <summary>判定是否终态（Success / Failed / Timeout / Canceled）。</summary>
    /// <param name="status">待判定状态。</param>
    /// <returns>终态返回 true。</returns>
    public static bool IsTerminal(JobExecutionStatus status)
        => status is JobExecutionStatus.Success
            or JobExecutionStatus.Failed
            or JobExecutionStatus.Timeout
            or JobExecutionStatus.Canceled;
}
