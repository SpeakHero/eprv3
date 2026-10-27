/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-25 11:00:00 +08:00 中国标准时间 星期日
 * 描述：Bas 模块后台任务编码常量（资质到期扫描 / 缓存清理）
 */

namespace Qlx.Modules.Bas.Jobs;

/// <summary>
/// Bas 模块后台任务编码。约定格式 <c>模块码.任务名</c>，与 <see cref="BasModule.Manifest"/> 声明保持一致。
/// </summary>
public static class BasJobCodes
{
    /// <summary>供应商资质到期扫描（每日，分批扫描到期资质并预警）。</summary>
    public const string SupplierCertExpiry = "bas.supplier.cert.expiry";

    /// <summary>Bas 缓存清理（每日，按当前租户前缀清理模块缓存）。</summary>
    public const string CacheCleanup = "bas.cache.cleanup";
}
