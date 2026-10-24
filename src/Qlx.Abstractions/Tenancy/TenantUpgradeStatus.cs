/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-22 16:30:00 +08:00 中国标准时间 星期四
 * 修改时间：2026-10-22 16:30:00 +08:00 中国标准时间 星期四
 * 描述：租户共享库升级独立库任务状态枚举与合法迁移白名单
 */

namespace Qlx.Abstractions.Tenancy;

/// <summary>
/// 租户升级任务状态（共享库 → 独立库单向升级流水线）。
/// </summary>
public enum TenantUpgradeStatus
{
    /// <summary>待处理（已登记，等待调度器消费）。</summary>
    Pending = 0,

    /// <summary>准备中（创建 / 迁移目标独立库）。</summary>
    Preparing = 1,

    /// <summary>全量复制中（按模块逐表键集分页复制，断点续跑）。</summary>
    Copying = 2,

    /// <summary>校验中（行数比对 + 抽样哈希比对）。</summary>
    Verifying = 3,

    /// <summary>切换中（增量补差 + 门户路由改写 + 缓存刷新）。</summary>
    Cutover = 4,

    /// <summary>已完成（共享库数据待保留期过后清理）。</summary>
    Done = 5,

    /// <summary>失败（保留独立库现场，可人工修复后重试）。</summary>
    Failed = 6,

    /// <summary>共享库残留数据已清理（终态）。</summary>
    Cleaned = 7,
}

/// <summary>
/// 升级状态机合法迁移白名单。非法迁移一律拒绝（返回 <c>false</c>），
/// 由实体层转为 <see cref="Qlx.Abstractions.Exceptions.ConflictException"/>。
/// </summary>
public static class TenantUpgradeStateTransitions
{
    /// <summary>
    /// 判定 <paramref name="from"/> → <paramref name="to"/> 是否为合法迁移。
    /// 合法路径：Pending→Preparing；Preparing→Copying/Failed；Copying→Verifying/Failed；
    /// Verifying→Cutover/Failed；Cutover→Done/Failed；Failed→Pending（重试）；Done→Cleaned。
    /// </summary>
    /// <param name="from">当前状态。</param>
    /// <param name="to">目标状态。</param>
    /// <returns>合法迁移返回 <c>true</c>，否则 <c>false</c>。</returns>
    public static bool CanTransition(TenantUpgradeStatus from, TenantUpgradeStatus to)
        => (from, to) switch
        {
            (TenantUpgradeStatus.Pending, TenantUpgradeStatus.Preparing) => true,
            (TenantUpgradeStatus.Preparing, TenantUpgradeStatus.Copying) => true,
            (TenantUpgradeStatus.Preparing, TenantUpgradeStatus.Failed) => true,
            (TenantUpgradeStatus.Copying, TenantUpgradeStatus.Verifying) => true,
            (TenantUpgradeStatus.Copying, TenantUpgradeStatus.Failed) => true,
            (TenantUpgradeStatus.Verifying, TenantUpgradeStatus.Cutover) => true,
            (TenantUpgradeStatus.Verifying, TenantUpgradeStatus.Failed) => true,
            (TenantUpgradeStatus.Cutover, TenantUpgradeStatus.Done) => true,
            (TenantUpgradeStatus.Cutover, TenantUpgradeStatus.Failed) => true,
            (TenantUpgradeStatus.Failed, TenantUpgradeStatus.Pending) => true,
            (TenantUpgradeStatus.Done, TenantUpgradeStatus.Cleaned) => true,
            _ => false,
        };
}
