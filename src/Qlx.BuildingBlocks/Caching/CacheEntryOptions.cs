/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-20 22:00:00 +08:00 中国标准时间 星期二
 * 修改时间：2026-10-20 22:00:00 +08:00 中国标准时间 星期二
 * 描述：缓存写入项过期选项，与具体提供方解耦
 */

namespace Qlx.BuildingBlocks.Caching;

/// <summary>
/// 缓存写入项选项。
/// </summary>
public sealed record CacheEntryOptions
{
    /// <summary>
    /// 绝对过期时长（自写入起）；为 <see langword="null"/> 时使用提供方默认。
    /// </summary>
    public TimeSpan? AbsoluteExpirationRelativeToNow { get; init; }

    /// <summary>
    /// 滑动过期时长；仅内存提供方支持，Redis 提供方忽略本项。
    /// </summary>
    public TimeSpan? SlidingExpiration { get; init; }

    /// <summary>
    /// 空值缓存（NullMarker）过期时长；为 <see langword="null"/> 时默认 30 秒。
    /// 空值缓存 TTL 必须显著短于正常值 TTL（C-3-3）。
    /// </summary>
    public TimeSpan? NullValueExpiration { get; init; }

    /// <summary>
    /// 是否禁用 TTL 抖动（默认 <see langword="false"/>，所有绝对过期统一 ±10% 抖动防雪崩）。
    /// 仅黑名单 / 租约等精确过期场景显式置 <see langword="true"/> 并注释说明理由。
    /// </summary>
    public bool DisableJitter { get; init; }
}
