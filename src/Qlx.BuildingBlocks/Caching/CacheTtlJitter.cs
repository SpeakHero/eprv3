/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-23 00:00:00 +08:00 中国标准时间 星期五
 * 修改时间：2026-10-23 00:00:00 +08:00 中国标准时间 星期五
 * 描述：缓存 TTL 统一抖动助手（C-4 防雪崩），基线 ±10% 随机偏移
 */

namespace Qlx.BuildingBlocks.Caching;

/// <summary>
/// TTL 抖动助手（C-4-1）：对所有生效的绝对过期时长统一施加 ±10% 随机偏移，
/// 防止大批键同一时刻过期引发雪崩。滑动过期不抖动（语义是访问续期）。
/// </summary>
internal static class CacheTtlJitter
{
    /// <summary>抖动比例（±10%）。</summary>
    private const double JitterRatio = 0.1d;

    /// <summary>
    /// 对基线 TTL 施加 ±10% 随机抖动。
    /// </summary>
    /// <param name="baseline">基线过期时长。</param>
    /// <param name="disableJitter">
    /// 是否禁用抖动（<see cref="CacheEntryOptions.DisableJitter"/>）；
    /// 仅黑名单 / 租约等精确过期场景显式禁用。
    /// </param>
    /// <returns>抖动后的过期时长；禁用或基线非正时原样返回。</returns>
    public static TimeSpan Apply(TimeSpan baseline, bool disableJitter)
    {
        if (disableJitter || baseline <= TimeSpan.Zero)
        {
            return baseline;
        }

        var factor = 1d + (((Random.Shared.NextDouble() * 2d) - 1d) * JitterRatio);
        return TimeSpan.FromTicks((long)(baseline.Ticks * factor));
    }
}
