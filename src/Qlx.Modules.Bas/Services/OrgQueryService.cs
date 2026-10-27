/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-25 13:55:00 +08:00 中国标准时间 星期日
 * 描述：跨模块组织只读查询实现——租户级缓存（GetOrCreateAsync + 空值缓存 + TTL 抖动）
 */

using System.Globalization;

using Qlx.Abstractions.Constants;
using Qlx.Abstractions.Exceptions;
using Qlx.Abstractions.Queries;
using Qlx.Abstractions.Tenancy;
using Qlx.BuildingBlocks.Caching;
using Qlx.Modules.Bas.Domain;
using Qlx.Modules.Bas.Repositories;

namespace Qlx.Modules.Bas.Services;

/// <summary>
/// 组织只读查询实现（<see cref="IOrgQueryService"/>）。仅下发扁平原始字段与数值枚举（B-2），
/// 读路径统一 <c>GetOrCreateAsync</c>（C-2/C-3/C-4），写路径由主数据服务按前缀失效（C-5）。
/// </summary>
/// <param name="tenantCtx">当前租户上下文。</param>
/// <param name="repo">Bas 租户仓储。</param>
/// <param name="cache">统一缓存服务。</param>
internal sealed class OrgQueryService(
    ITenantContext tenantCtx,
    IBasTenantRepository repo,
    ICacheService cache) : IOrgQueryService
{
    private static readonly CacheEntryOptions s_cacheOptions = new()
    {
        AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5),
        NullValueExpiration = TimeSpan.FromSeconds(30),
    };

    private long TenantId => tenantCtx.TenantId
        ?? throw new ForbiddenException("未识别到租户上下文");

    /// <inheritdoc />
    public Task<IReadOnlyList<OrgQueryRow>> ListAllAsync(CancellationToken cancellationToken)
    {
        string cacheKey = QlxCacheKey.BuildTenantKey(TenantId, QlxModuleCodes.Bas, "orgquery:all");
        return cache.GetOrCreateAsync<IReadOnlyList<OrgQueryRow>>(
            cacheKey,
            factory: async token =>
            {
                IReadOnlyList<Organization> orgs = await repo.ListAllOrgsAsync(token).ConfigureAwait(false);
                return [.. orgs.Select(static o => new OrgQueryRow(
                    o.Id, o.ParentId, o.Code, o.Name, (int)o.Type, (int)o.Status, o.SortOrder))];
            },
            cancellationToken: cancellationToken,
            options: s_cacheOptions);
    }

    /// <inheritdoc />
    public async Task<OrgQueryRow?> FindAsync(long id, CancellationToken cancellationToken)
    {
        if (id <= 0)
        {
            return null;
        }

        string cacheKey = QlxCacheKey.BuildTenantKey(
            TenantId, QlxModuleCodes.Bas, $"orgquery:{id.ToString(CultureInfo.InvariantCulture)}");

        return await cache.GetOrCreateAsync<OrgQueryRow?>(
            cacheKey,
            factory: async token =>
            {
                Organization? org = await repo.FindOrgAsync(id, token).ConfigureAwait(false);
                return org is null
                    ? null
                    : new OrgQueryRow(
                        org.Id, org.ParentId, org.Code, org.Name, (int)org.Type, (int)org.Status, org.SortOrder);
            },
            cancellationToken: cancellationToken,
            options: s_cacheOptions).ConfigureAwait(false);
    }
}
