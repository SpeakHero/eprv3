/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-26 14:00:00 +08:00 中国标准时间 星期一
 * 描述：Stk 模块任务编码常量
 */

namespace Qlx.Modules.Stk.Jobs;

/// <summary>Stk 模块内置任务编码。</summary>
public static class StkJobCodes
{
    /// <summary>Stk 缓存清理（每日 03:30）。</summary>
    public const string CacheCleanup = "stk.cache.cleanup";
}
