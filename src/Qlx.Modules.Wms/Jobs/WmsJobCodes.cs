/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-26 10:00:00 +08:00 中国标准时间 星期一
 * 描述：Wms 模块任务编码常量（全局唯一入口）
 */

namespace Qlx.Modules.Wms.Jobs;

/// <summary>Wms 模块后台任务编码。</summary>
public static class WmsJobCodes
{
    /// <summary>Wms 缓存清理（C-6）。</summary>
    public const string CacheCleanup = "wms.cache.cleanup";

    /// <summary>库存命令 Outbox 分发（spec §14.3）。</summary>
    public const string StockCommandDispatch = "wms.stock.command.dispatch";

    /// <summary>库存预警扫描（spec §14.4/§14.5）。</summary>
    public const string AlertScan = "wms.alert.scan";
}
