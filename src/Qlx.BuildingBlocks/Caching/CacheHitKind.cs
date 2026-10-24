/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-23 00:00:00 +08:00 中国标准时间 星期五
 * 修改时间：2026-10-23 00:00:00 +08:00 中国标准时间 星期五
 * 描述：缓存读取三态结果（命中值 / 命中空值缓存 / 未命中），供提供方内部区分空值缓存语义
 */

namespace Qlx.BuildingBlocks.Caching;

/// <summary>
/// 缓存读取三态（C-3 空值缓存）：区分「命中空值缓存」与「未命中」，
/// 使命中 NullMarker 的读穿路径不再回源。
/// </summary>
internal enum CacheHitKind
{
    /// <summary>未命中（键不存在或已过期）。</summary>
    Miss,

    /// <summary>命中空值缓存（查询结论为「不存在」），不再回源。</summary>
    HitNull,

    /// <summary>命中有效缓存值。</summary>
    HitValue,
}
