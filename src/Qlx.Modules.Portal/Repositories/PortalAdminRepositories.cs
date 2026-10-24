/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-21 18:20:00 +08:00 中国标准时间 星期三
 * 修改时间：2026-10-21 18:20:00 +08:00 中国标准时间 星期三
 * 描述：门户管理端仓储实现：租户 / 用户 / 模块（读路径 AsNoTracking，写路径跟踪态）
 */

using Microsoft.EntityFrameworkCore;
using Qlx.Abstractions.Modules;
using Qlx.Abstractions.Tenancy;
using Qlx.BuildingBlocks.Api;
using Qlx.Modules.Portal.Domain.Identity;
using Qlx.Modules.Portal.Domain.Tenancy;

namespace Qlx.Modules.Portal.Repositories;

/// <summary>租户管理仓储实现。</summary>
public sealed class PortalTenantAdminRepository(PortalDbContext db) : IPortalTenantAdminRepository
{
    /// <inheritdoc />
    public Task<PagedResult<Tenant>> QueryAsync(
        string? keyword, string? statusFilter, string? databaseModeFilter, PagedRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var query = db.Tenants.AsNoTracking().Include(t => t.Database).Include(t => t.Modules).AsQueryable();

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            var kw = keyword.Trim();
            query = query.Where(t => t.DisplayName.Contains(kw)
                || t.TenantCode.Contains(kw)
                || (t.TaxNo != null && t.TaxNo.Contains(kw)));
        }

        if (TryMapTenantStatus(statusFilter, out var status))
        {
            query = query.Where(t => t.Status == status);
        }

        if (TryMapDatabaseMode(databaseModeFilter, out var mode))
        {
            query = query.Where(t => t.Database != null && t.Database.DatabaseMode == mode);
        }

        return query.OrderByDescending(t => t.Id).ToPagedAsync(request, cancellationToken);
    }

    private static bool TryMapDatabaseMode(string? filter, out TenantDatabaseMode mode)
    {
        if (string.IsNullOrWhiteSpace(filter))
        {
            mode = default;
            return false;
        }
        return Enum.TryParse(filter, ignoreCase: true, out mode);
    }

    /// <inheritdoc />
    public Task<Tenant?> FindTrackedAsync(long tenantId, CancellationToken cancellationToken)
        => db.Tenants.FirstOrDefaultAsync(t => t.Id == tenantId, cancellationToken);

    /// <inheritdoc />
    public Task<Tenant?> FindWithDetailsAsync(long tenantId, CancellationToken cancellationToken)
        => db.Tenants.AsNoTracking()
            .Include(t => t.Database)
            .Include(t => t.Modules)
            .FirstOrDefaultAsync(t => t.Id == tenantId, cancellationToken);

    /// <inheritdoc />
    public Task<Tenant?> FindTrackedWithModulesAsync(long tenantId, CancellationToken cancellationToken)
        => db.Tenants.Include(t => t.Modules)
            .FirstOrDefaultAsync(t => t.Id == tenantId, cancellationToken);

    /// <inheritdoc />
    public Task<bool> CodeExistsAsync(string tenantCode, CancellationToken cancellationToken)
        => db.Tenants.AnyAsync(t => t.TenantCode == tenantCode, cancellationToken);

    /// <inheritdoc />
    public Task<bool> TaxNoExistsAsync(string taxNo, CancellationToken cancellationToken)
        => db.Tenants.AnyAsync(t => t.TaxNo == taxNo, cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<long, DateTimeOffset?>> GetLastLoginTimesAsync(
        IReadOnlyCollection<long> tenantIds, CancellationToken cancellationToken)
    {
        if (tenantIds.Count == 0)
        {
            return new Dictionary<long, DateTimeOffset?>();
        }

        var rows = await db.TenantMembers
            .AsNoTracking()
            .Where(m => tenantIds.Contains(m.TenantId))
            .GroupBy(m => m.TenantId)
            .Select(g => new { TenantId = g.Key, LastLoginAt = g.Max(m => m.LastLoginAt) })
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        return rows.ToDictionary(r => r.TenantId, r => r.LastLoginAt);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<LicenseRecord>> QueryLicenseRecordsAsync(
        long? tenantId, int take, CancellationToken cancellationToken)
    {
        var query = db.LicenseRecords.AsNoTracking();
        if (tenantId.HasValue)
        {
            query = query.Where(r => r.TenantId == tenantId.Value);
        }

        return await query.OrderByDescending(r => r.At)
            .ThenByDescending(r => r.Id)
            .Take(Math.Clamp(take, 1, PortalConstants.Validation.MaxLicenseRecordTake))
            .ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<TenantMemberRow>> ListMembersAsync(
        long tenantId, CancellationToken cancellationToken)
    {
        return await db.TenantMembers
            .AsNoTracking()
            .Where(m => m.TenantId == tenantId)
            .Join(db.PortalUsers.AsNoTracking(),
                m => m.PortalUserId, u => u.Id,
                (m, u) => new TenantMemberRow(
                    m.Id, u.Id, u.LoginAccount, u.DisplayName, m.DisplayNameInTenant,
                    m.Role, m.Status, m.JoinAt, m.LastLoginAt))
            .ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task AddAsync(Tenant tenant, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(tenant);
        await db.Tenants.AddAsync(tenant, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task AddDatabaseAsync(TenantDatabase database, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(database);
        await db.TenantDatabases.AddAsync(database, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task AddModulesAsync(
        IReadOnlyCollection<TenantModule> modules, CancellationToken cancellationToken)
    {
        if (modules.Count > 0)
        {
            await db.TenantModules.AddRangeAsync(modules, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async Task AddLicenseRecordsAsync(
        IReadOnlyCollection<LicenseRecord> records, CancellationToken cancellationToken)
    {
        if (records.Count > 0)
        {
            await db.LicenseRecords.AddRangeAsync(records, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async Task AddMemberAsync(TenantMember member, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(member);
        await db.TenantMembers.AddAsync(member, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task SaveChangesAsync(CancellationToken cancellationToken)
        => db.SaveChangesAsync(cancellationToken);

    /// <summary>前端状态字面量 → 领域状态（未知值视为不过滤）。</summary>
    private static bool TryMapTenantStatus(string? value, out TenantStatus status)
    {
        switch (value)
        {
            case PortalConstants.Frontend.Enabled: status = TenantStatus.Active; return true;
            case PortalConstants.Frontend.Disabled: status = TenantStatus.Disabled; return true;
            case PortalConstants.Frontend.Pending: status = TenantStatus.Provisioning; return true;
            case PortalConstants.Frontend.ProvisioningFailed: status = TenantStatus.ProvisioningFailed; return true;
            case PortalConstants.Frontend.Expired: status = TenantStatus.GracePeriod; return true;
            default: status = default; return false;
        }
    }
}

/// <summary>门户用户管理仓储实现。</summary>
public sealed class PortalUserAdminRepository(PortalDbContext db) : IPortalUserAdminRepository
{
    /// <inheritdoc />
    public Task<PagedResult<PortalUser>> QueryAsync(
        string? keyword, string? statusFilter, PagedRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var query = db.PortalUsers.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            var kw = keyword.Trim();
            query = query.Where(u => u.LoginAccount.Contains(kw)
                || u.DisplayName.Contains(kw)
                || (u.Phone != null && u.Phone.Contains(kw))
                || (u.Email != null && u.Email.Contains(kw)));
        }

        if (string.Equals(statusFilter, PortalConstants.Frontend.Enabled, StringComparison.Ordinal))
        {
            query = query.Where(u => u.Status == UserStatus.Enabled);
        }
        else if (string.Equals(statusFilter, PortalConstants.Frontend.Disabled, StringComparison.Ordinal))
        {
            query = query.Where(u => u.Status == UserStatus.Disabled);
        }

        return query.OrderByDescending(u => u.Id).ToPagedAsync(request, cancellationToken);
    }

    /// <inheritdoc />
    public Task<PortalUser?> FindTrackedAsync(long userId, CancellationToken cancellationToken)
        => db.PortalUsers.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);

    /// <inheritdoc />
    public Task<PortalUser?> FindTrackedByAccountAsync(string loginAccount, CancellationToken cancellationToken)
        => db.PortalUsers.FirstOrDefaultAsync(u => u.LoginAccount == loginAccount, cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<UserMembershipRow>> ListMembershipsAsync(
        long userId, CancellationToken cancellationToken)
    {
        return await db.TenantMembers
            .AsNoTracking()
            .Where(m => m.PortalUserId == userId)
            .Join(db.PortalUsers.AsNoTracking(),
                m => m.PortalUserId, u => u.Id,
                (m, u) => new { m, u })
            .Join(db.Tenants.AsNoTracking(),
                mu => mu.m.TenantId, t => t.Id,
                (mu, t) => new UserMembershipRow(
                    mu.m.Id, mu.u.Id, mu.u.LoginAccount, t.Id, t.DisplayName, t.TenantCode,
                    mu.m.DisplayNameInTenant, mu.m.Role, mu.m.Status, mu.m.JoinAt, mu.m.LastLoginAt))
            .ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<PortalUserPasswordHistory>> GetRecentPasswordHistoriesAsync(
        long userId, int take, CancellationToken cancellationToken)
    {
        return await db.PortalUserPasswordHistories
            .AsNoTracking()
            .Where(h => h.PortalUserId == userId)
            .OrderByDescending(h => h.CreatedAt)
            .ThenByDescending(h => h.Id)
            .Take(Math.Clamp(take, 1, PortalConstants.Validation.MaxPasswordHistoryTake))
            .ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task AddPasswordHistoryAsync(
        PortalUserPasswordHistory history, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(history);
        await db.PortalUserPasswordHistories.AddAsync(history, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task TrimPasswordHistoriesAsync(
        long userId, int keepLatest, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(keepLatest, 0);

        var toDelete = await db.PortalUserPasswordHistories
            .Where(h => h.PortalUserId == userId)
            .OrderByDescending(h => h.CreatedAt)
            .ThenByDescending(h => h.Id)
            .Skip(Math.Clamp(keepLatest, 0, PortalConstants.Validation.MaxPasswordHistoryTrim))
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        if (toDelete.Count > 0)
        {
            db.PortalUserPasswordHistories.RemoveRange(toDelete);
        }
    }

    /// <inheritdoc />
    public async Task AddAsync(PortalUser user, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        await db.PortalUsers.AddAsync(user, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task SaveChangesAsync(CancellationToken cancellationToken)
        => db.SaveChangesAsync(cancellationToken);
}

/// <summary>模块目录与许可管理仓储实现。</summary>
public sealed class PortalModuleAdminRepository(PortalDbContext db) : IPortalModuleAdminRepository
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<ModuleCatalogRow>> ListCatalogAsync(CancellationToken cancellationToken)
    {
        // 模块定义（门户侧登记）为主表；授权租户数按启用状态聚合；功能点取菜单权限名称。
        var modules = await db.ModuleDefinitions.AsNoTracking()
            .OrderBy(m => m.ModuleCode)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        var licensedCounts = await db.TenantModules.AsNoTracking()
            .Where(t => t.Status == TenantModuleStatus.Enabled)
            .GroupBy(t => t.ModuleCode)
            .Select(g => new { ModuleCode = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        var features = await db.PermissionDefinitions.AsNoTracking()
            .Where(p => p.Kind == PermissionKind.Menu)
            .Select(p => new { p.ModuleCode, p.DisplayName })
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        var countMap = licensedCounts.ToDictionary(x => x.ModuleCode, x => x.Count, StringComparer.Ordinal);

        var featureMap = features
            .GroupBy(f => f.ModuleCode, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<string>)g.Select(x => x.DisplayName).ToList(), StringComparer.Ordinal);

        return modules.Select(m => new ModuleCatalogRow(
            m.ModuleCode,
            m.DisplayName,
            m.CurrentVersion,
            m.Description,
            countMap.GetValueOrDefault(m.ModuleCode),
            featureMap.GetValueOrDefault(m.ModuleCode) ?? [])).ToList();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ModuleDefinitionRow>> ListDefinitionsAsync(
        CancellationToken cancellationToken)
    {
        return await db.ModuleDefinitions.AsNoTracking()
            .OrderBy(m => m.ModuleCode)
            .Select(m => new ModuleDefinitionRow(
                m.ModuleCode, m.DisplayName, m.CurrentVersion, m.Licensable, m.IsMandatory))
            .ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ModuleDefinition>> ListByCodesAsync(
        IReadOnlyCollection<string> moduleCodes, CancellationToken cancellationToken)
    {
        if (moduleCodes.Count == 0)
        {
            return [];
        }

        return await db.ModuleDefinitions.AsNoTracking()
            .Where(m => moduleCodes.Contains(m.ModuleCode))
            .ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<PortalWorkbenchCounters> GetWorkbenchCountersAsync(CancellationToken cancellationToken)
    {
        var tenantTotal = await db.Tenants.LongCountAsync(cancellationToken).ConfigureAwait(false);
        var tenantEnabled = await db.Tenants.LongCountAsync(
            t => t.Status == TenantStatus.Active, cancellationToken).ConfigureAwait(false);
        var tenantPending = await db.Tenants.LongCountAsync(
            t => t.Status == TenantStatus.Provisioning, cancellationToken).ConfigureAwait(false);
        var tenantFailed = await db.Tenants.LongCountAsync(
            t => t.Status == TenantStatus.ProvisioningFailed, cancellationToken).ConfigureAwait(false);
        var userTotal = await db.PortalUsers.LongCountAsync(cancellationToken).ConfigureAwait(false);
        var userDisabled = await db.PortalUsers.LongCountAsync(
            u => u.Status == UserStatus.Disabled, cancellationToken).ConfigureAwait(false);
        var licensed = await db.TenantModules.LongCountAsync(
            t => t.Status == TenantModuleStatus.Enabled, cancellationToken).ConfigureAwait(false);

        return new PortalWorkbenchCounters(
            tenantTotal, tenantEnabled, tenantPending, tenantFailed, userTotal, userDisabled, licensed);
    }
}
