/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-21 03:45:00 +08:00 中国标准时间 星期三
 * 修改时间：2026-10-21 03:45:00 +08:00 中国标准时间 星期三
 * 描述：租户预配仓储契约，租户迁移服务依赖此接口解耦门户模块
 */

namespace Qlx.Abstractions.Tenancy;

/// <summary>
/// 租户预配仓储契约。抽象门户库中"租户生命周期状态 + 数据库连接信息"的读取，
/// 让 BuildingBlocks 层的租户迁移后台服务无需直接依赖 Portal 模块即可查询/更新租户预配进度。
/// </summary>
public interface ITenantProvisioningRepository
{
    /// <summary>
    /// 获取所有处于 Provisioning 状态的租户 ID 列表。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    Task<IReadOnlyList<long>> GetProvisioningTenantIdsAsync(CancellationToken cancellationToken);

    /// <summary>
    /// 将租户状态从 Provisioning 改为 Active（迁移全部成功后调用）。
    /// </summary>
    /// <param name="tenantId">租户 ID。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task MarkTenantActiveAsync(long tenantId, CancellationToken cancellationToken);

    /// <summary>
    /// 标记租户预配失败（迁移过程中出错，待人工介入）。
    /// </summary>
    /// <param name="tenantId">租户 ID。</param>
    /// <param name="errorMessage">失败原因。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task MarkTenantProvisioningFailedAsync(long tenantId, string errorMessage, CancellationToken cancellationToken);

    /// <summary>
    /// 获取租户的预配尝试次数与最近一次失败原因（迁移服务用于判定是否超过最大自动重试次数）。
    /// </summary>
    /// <param name="tenantId">租户 ID。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>租户不存在时返回 <c>null</c>；存在时返回（尝试次数，最近失败原因）。</returns>
    Task<(int AttemptCount, string? LastError)?> GetTenantAttemptCountAsync(long tenantId, CancellationToken cancellationToken);
}
