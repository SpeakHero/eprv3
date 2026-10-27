/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-25 16:50:00 +08:00 中国标准时间 星期日
 * 描述：跨模块商品分类只读查询实现——租户级缓存（GetOrCreateAsync + 空值缓存 + TTL 抖动）
 */

using System.Globalization;

using Qlx.Abstractions.Constants;
using Qlx.Abstractions.Exceptions;
using Qlx.Abstractions.Queries;
using Qlx.Abstractions.Tenancy;
using Qlx.BuildingBlocks.Caching;
using Qlx.Modules.Bas.Domain.Goods;
using Qlx.Modules.Bas.Repositories;

namespace Qlx.Modules.Bas.Services;

/// <summary>
/// 商品分类只读查询实现（<see cref="IProductCategoryQueryService"/>）。仅下发扁平原始字段与数值枚举（B-2-1/B-2-5），
/// 读路径统一 <c>GetOrCreateAsync</c>（C-2/C-3/C-4），写路径由分类主数据服务按前缀失效（C-5）。
/// </summary>
/// <param name="tenantCtx">当前租户上下文。</param>
/// <param name="repo">Bas 租户仓储。</param>
/// <param name="cache">统一缓存服务。</param>
internal sealed class ProductCategoryQueryService(
    ITenantContext tenantCtx,
    IBasTenantRepository repo,
    ICacheService cache) : IProductCategoryQueryService
{
    private static readonly CacheEntryOptions s_cacheOptions = new()
    {
        AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5),
        NullValueExpiration = TimeSpan.FromSeconds(30),
    };

    private long TenantId => tenantCtx.TenantId
        ?? throw new ForbiddenException("未识别到租户上下文");

    /// <inheritdoc />
    public Task<IReadOnlyList<ProductCategoryQueryRow>> ListAllAsync(CancellationToken cancellationToken)
    {
        string cacheKey = QlxCacheKey.BuildTenantKey(TenantId, QlxModuleCodes.Bas, "categoryquery:all");
        return cache.GetOrCreateAsync<IReadOnlyList<ProductCategoryQueryRow>>(
            cacheKey,
            factory: async token =>
            {
                IReadOnlyList<ItemCategory> categories = await repo
                    .ListAllCategoriesAsync(token).ConfigureAwait(false);
                return [.. categories.Select(Map)];
            },
            cancellationToken: cancellationToken,
            options: s_cacheOptions);
    }

    /// <inheritdoc />
    public async Task<ProductCategoryQueryRow?> FindAsync(long id, CancellationToken cancellationToken)
    {
        if (id <= 0)
        {
            return null;
        }

        string cacheKey = QlxCacheKey.BuildTenantKey(
            TenantId, QlxModuleCodes.Bas, $"categoryquery:{id.ToString(CultureInfo.InvariantCulture)}");

        return await cache.GetOrCreateAsync<ProductCategoryQueryRow?>(
            cacheKey,
            factory: async token =>
            {
                ItemCategory? category = await repo.FindCategoryAsync(id, token).ConfigureAwait(false);
                return category is null ? null : Map(category);
            },
            cancellationToken: cancellationToken,
            options: s_cacheOptions).ConfigureAwait(false);
    }

    private static ProductCategoryQueryRow Map(ItemCategory category)
        => new(
            category.Id,
            category.ParentId,
            category.Code,
            category.Name,
            category.LevelNo,
            (int)category.Status);
}
