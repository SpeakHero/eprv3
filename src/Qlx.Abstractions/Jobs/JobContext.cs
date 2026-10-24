/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-22 15:45:00 +08:00 中国标准时间 星期三
 * 修改时间：2026-10-22 15:45:00 +08:00 中国标准时间 星期三
 * 描述：任务执行上下文（可变进度 + 取消令牌 + 参数），用 class 承载运行期可变状态
 */

namespace Qlx.Abstractions.Jobs;

/// <summary>
/// 任务执行上下文。每次任务执行由调度器组装一次，承载执行标识、参数、取消令牌与进度上报能力。
/// 本类型使用 class（非 record）因为执行期存在可变状态（进度百分比、CancellationToken 可能被下游替换）。
/// </summary>
public sealed class JobContext
{
    /// <summary>执行 ID（全局唯一）。</summary>
    public Guid ExecutionId { get; init; }

    /// <summary>任务编码。</summary>
    public string TaskCode { get; init; } = string.Empty;

    /// <summary>租户 ID。</summary>
    public Guid TenantId { get; init; }

    /// <summary>执行模式。</summary>
    public ExecutionMode Mode { get; init; }

    /// <summary>任务参数（按 TaskSubmitRequest.Parameters 传入）。</summary>
    public IReadOnlyDictionary<string, object?> Parameters { get; init; } = new Dictionary<string, object?>();

    /// <summary>取消令牌（可变：执行期可能被替换为触发后的 token）。</summary>
    public CancellationToken CancellationToken { get; set; }

    /// <summary>当前进度百分比（0~100）。</summary>
    public int ProgressPercent { get; private set; }

    /// <summary>
    /// 上报进度。
    /// </summary>
    /// <param name="percent">进度百分比（自动 clamp 至 0~100）。</param>
    public void ReportProgress(int percent)
    {
        ProgressPercent = Math.Clamp(percent, 0, 100);
    }
}
