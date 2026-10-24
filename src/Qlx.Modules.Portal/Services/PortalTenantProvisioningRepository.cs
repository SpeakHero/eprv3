/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-21 04:15:00 +08:00 中国标准时间 星期三
 * 修改时间：2026-10-21 04:15:00 +08:00 中国标准时间 星期三
 * 描述：门户模块租户预配仓储实现，查询/更新租户生命周期状态
 */

using Microsoft.EntityFrameworkCore;
using Qlx.Abstractions.Tenancy;
using Qlx.Modules.Portal.Domain.Tenancy;

namespace Qlx.Modules.Portal.Services;

/// <summary>
/// <see cref="ITenantProvisioningRepository"/> 的门户库实现。
/// 仅依赖 <see cref="PortalDbContext"/> 固定连接串门户库。
/// </summary>
public sealed class PortalTenantProvisioningRepository(PortalDbContext db) : ITenantProvisioningRepository
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<long>> GetProvisioningTenantIdsAsync(CancellationToken cancellationToken)
    {
        // 仅独立库租户进入预配：共享租户不建库，路由行在开通事务内即完成
        return await db.Tenants
            .AsNoTracking()
            .Where(t => t.Status == TenantStatus.Provisioning
                && t.Database != null
                && t.Database.DatabaseMode == TenantDatabaseMode.Isolated)
            .Select(t => t.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task MarkTenantActiveAsync(long tenantId, CancellationToken cancellationToken)
    {
        var tenant = await db.Tenants.FindAsync([tenantId], cancellationToken).ConfigureAwait(false);
        if (tenant is not null)
        {
            tenant.Activate();
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async Task MarkTenantProvisioningFailedAsync(long tenantId, string errorMessage, CancellationToken cancellationToken)
    {
        var tenant = await db.Tenants.FindAsync([tenantId], cancellationToken).ConfigureAwait(false);
        if (tenant is not null)
        {
            tenant.MarkProvisioningFailed(errorMessage);
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async Task<(int AttemptCount, string? LastError)?> GetTenantAttemptCountAsync(long tenantId, CancellationToken cancellationToken)
    {
        var row = await db.Tenants
            .AsNoTracking()
            .Where(t => t.Id == tenantId)
            .Select(t => new { t.ProvisioningAttemptCount, t.LastErrorMessage })
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return row is null ? null : (row.ProvisioningAttemptCount, row.LastErrorMessage);
    }
}
