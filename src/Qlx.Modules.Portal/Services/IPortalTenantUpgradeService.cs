/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-22 16:30:00 +08:00 中国标准时间 星期四
 * 修改时间：2026-10-22 16:30:00 +08:00 中国标准时间 星期四
 * 描述：租户升级管理服务契约（共享库 → 独立库的发起 / 查询 / 重试）
 */

using Qlx.Modules.Portal.Dtos;

namespace Qlx.Modules.Portal.Services;

/// <summary>
/// 租户升级管理服务。门户管理端发起共享库 → 独立库升级（登记任务由后台流水线执行）、
/// 查询最新任务进度、重试失败任务。
/// </summary>
public interface IPortalTenantUpgradeService
{
    /// <summary>
    /// 发起升级：登记 Pending 任务，由升级调度后台服务消费执行。
    /// </summary>
    /// <param name="tenantId">租户 ID（混淆字符串）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>新建任务条目。</returns>
    Task<TenantUpgradeJobItem> PromoteToIsolatedAsync(string tenantId, CancellationToken cancellationToken);

    /// <summary>
    /// 查询租户最近一次升级任务；无任务返回 <c>null</c>。
    /// </summary>
    /// <param name="tenantId">租户 ID（混淆字符串）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task<TenantUpgradeJobItem?> GetUpgradeJobAsync(string tenantId, CancellationToken cancellationToken);

    /// <summary>
    /// 重试失败的升级任务（Failed → Pending，由调度器重新消费断点续跑）。
    /// </summary>
    /// <param name="tenantId">租户 ID（混淆字符串）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>重试后的任务条目。</returns>
    Task<TenantUpgradeJobItem> RetryUpgradeJobAsync(string tenantId, CancellationToken cancellationToken);
}
