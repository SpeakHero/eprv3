/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-26 10:00:00 +08:00 中国标准时间 星期一
 * 描述：跨模块仓库只读查询实现（IWarehouseQueryService）——只依赖 Wms DbContext（无跨模块注入环，S-5），
 *       读穿缓存 + 空值缓存 + 写后按前缀失效
 */

using System.Globalization;

using Qlx.Abstractions.Constants;
using Qlx.Abstractions.Queries;
using Qlx.Abstractions.Tenancy;
using Qlx.BuildingBlocks.Caching;
using Qlx.Modules.Wms.Repositories;

namespace Qlx.Modules.Wms.Services;

/// <summary>跨模块仓库只读查询实现。</summary>
/// <param name="tenantCtx">当前租户上下文。</param>
/// <param name="repo">Wms 租户仓储（只读）。</param>
/// <param name="cache">统一缓存服务。</param>
internal sealed class WarehouseQueryService(
    ITenantContext tenantCtx,
    IWmsTenantRepository repo,
    ICacheService cache) : IWarehouseQueryService
{
    private static readonly CacheEntryOptions s_cacheOptions = new()
    {
        AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5),
        NullValueExpiration = TimeSpan.FromSeconds(30),
    };

    private long TenantId => tenantCtx.TenantId
        ?? throw new Qlx.Abstractions.Exceptions.ForbiddenException("未识别到租户上下文");

    /// <inheritdoc />
    public async Task<IReadOnlyList<WarehouseQueryRow>> ListAllAsync(CancellationToken cancellationToken)
    {
        string cacheKey = QlxCacheKey.BuildTenantKey(TenantId, QlxModuleCodes.Wms, "warehouse:q:all");
        return await cache.GetOrCreateAsync<IReadOnlyList<WarehouseQueryRow>>(
            cacheKey,
            factory: async token =>
            {
                IReadOnlyList<Domain.Warehouse.Warehouse> rows =
                    await repo.ListWarehousesAsync(token).ConfigureAwait(false);
                return [.. rows.Select(Map)];
            },
            cancellationToken: cancellationToken,
            options: s_cacheOptions).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<WarehouseQueryRow>> ListByOrgAsync(
        long orgId, CancellationToken cancellationToken)
    {
        IReadOnlyList<WarehouseQueryRow> all = await ListAllAsync(cancellationToken).ConfigureAwait(false);
        return [.. all.Where(w => w.OrgId == orgId)];
    }

    /// <inheritdoc />
    public async Task<WarehouseQueryRow?> FindAsync(long id, CancellationToken cancellationToken)
    {
        string cacheKey = QlxCacheKey.BuildTenantKey(
            TenantId, QlxModuleCodes.Wms, $"warehouse:q:{id.ToString(CultureInfo.InvariantCulture)}");

        return await cache.GetOrCreateAsync<WarehouseQueryRow?>(
            cacheKey,
            factory: async token =>
            {
                Domain.Warehouse.Warehouse? entity =
                    await repo.FindWarehouseAsync(id, token).ConfigureAwait(false);
                return entity is null ? null : Map(entity);
            },
            cancellationToken: cancellationToken,
            options: s_cacheOptions).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<WarehouseQueryRow>> FindByIdsAsync(
        IReadOnlyList<long> ids, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ids);
        if (ids.Count == 0)
        {
            return [];
        }

        IReadOnlyList<WarehouseQueryRow> all = await ListAllAsync(cancellationToken).ConfigureAwait(false);
        HashSet<long> wanted = [.. ids];
        return [.. all.Where(w => wanted.Contains(w.Id))];
    }

    private static WarehouseQueryRow Map(Domain.Warehouse.Warehouse entity)
        => new(entity.Id, entity.OrgId, entity.Code, entity.Name, entity.WhType, (int)entity.Status);
}
