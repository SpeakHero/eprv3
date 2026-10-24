/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-22 20:00:00 +08:00 中国标准时间 星期四
 * 修改时间：2026-10-22 20:00:00 +08:00 中国标准时间 星期四
 * 描述：租户升级链路审计操作编码（模块私有常量集中管理，禁止散落各服务）
 */

namespace Qlx.BuildingBlocks.MultiTenancy.Upgrade;

/// <summary>
/// 租户升级链路（共享库 → 独立库）审计操作编码。仅 Upgrade 子域内部使用，不外泄。
/// </summary>
internal static class TenantUpgradeAuditOperations
{
    /// <summary>升级独立库完成。</summary>
    public const string UpgradeDone = "UPGRADE_TO_ISOLATED_DONE";

    /// <summary>升级独立库失败。</summary>
    public const string UpgradeFailed = "UPGRADE_TO_ISOLATED_FAILED";

    /// <summary>共享库残留数据已清理。</summary>
    public const string SharedDataCleaned = "SHARED_DATA_CLEANED";

    /// <summary>隔离巡检发现严重问题告警。</summary>
    public const string IsolationAuditAlert = "ISOLATION_AUDIT_ALERT";
}
