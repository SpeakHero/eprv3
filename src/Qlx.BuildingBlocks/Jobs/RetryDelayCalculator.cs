/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-22 21:36:00 +08:00 中国标准时间 星期四
 * 描述：任务重试延迟计算器（固定 / 线性 / 指数 2^n 退避，带最大延迟封顶）
 */

using Qlx.Abstractions.Jobs;

namespace Qlx.BuildingBlocks.Jobs;

/// <summary>
/// 重试延迟计算器。方案 §4.12 D19：默认 RetryPolicy（MaxRetries=3、Interval=1min、
/// Backoff=Exponential(2^n)、MaxDelay=1h），按尝试序号计算下次重试等待时长。
/// </summary>
public static class RetryDelayCalculator
{
    /// <summary>
    /// 计算第 <paramref name="attempt"/> 次（从 1 起，首次重试为 1）重试前的等待时长。
    /// </summary>
    /// <param name="policy">重试策略。</param>
    /// <param name="attempt">重试序号（1 起）。</param>
    /// <returns>等待时长（不超过 <see cref="RetryPolicy.MaxDelaySeconds"/>）。</returns>
    /// <exception cref="ArgumentOutOfRangeException">attempt 小于 1。</exception>
    public static TimeSpan ComputeDelay(RetryPolicy policy, int attempt)
    {
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(attempt);

        var baseSeconds = policy.Backoff switch
        {
            RetryBackoffStrategy.Fixed => policy.InitialIntervalSeconds,
            RetryBackoffStrategy.Linear => (long)policy.InitialIntervalSeconds * attempt,
            RetryBackoffStrategy.Exponential => (long)policy.InitialIntervalSeconds << Math.Min(attempt - 1, 30),
            _ => policy.InitialIntervalSeconds,
        };

        var capped = Math.Min(baseSeconds, policy.MaxDelaySeconds);
        return TimeSpan.FromSeconds(capped);
    }
}
