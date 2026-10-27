/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-25 19:45:00 +08:00 中国标准时间 星期日
 * 描述：跨模块客户只读查询实现——租户级缓存（GetOrCreateAsync + 空值缓存 + TTL 抖动）
 */

using System.Globalization;

using Qlx.Abstractions.Constants;
using Qlx.Abstractions.Exceptions;
using Qlx.Abstractions.Queries;
using Qlx.Abstractions.Tenancy;
using Qlx.BuildingBlocks.Caching;
using Qlx.Modules.Bas.Repositories;

namespace Qlx.Modules.Bas.Services;

/// <summary>
/// 客户只读查询实现（<see cref="ICustomerQueryService"/>）。仅下发扁平原始字段与数值枚举（B-2），
/// 金额为原始 decimal（B-2-2）；读路径统一 <c>GetOrCreateAsync</c>（C-2/C-3/C-4）。
/// </summary>
/// <param name="tenantCtx">当前租户上下文。</param>
/// <param name="repo">Bas 租户仓储。</param>
/// <param name="cache">统一缓存服务。</param>
internal sealed class CustomerQueryService(
    ITenantContext tenantCtx,
    IBasTenantRepository repo,
    ICacheService cache) : ICustomerQueryService
{
    private static readonly CacheEntryOptions s_cacheOptions = new()
    {
        AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5),
        NullValueExpiration = TimeSpan.FromSeconds(30),
    };

    private long TenantId => tenantCtx.TenantId
        ?? throw new ForbiddenException("未识别到租户上下文");

    /// <inheritdoc />
    public async Task<CustomerQueryRow?> FindAsync(long id, CancellationToken cancellationToken)
        => await LoadAsync(id, cancellationToken).ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<(decimal Limit, decimal Used)?> GetCreditAsync(
        long id, CancellationToken cancellationToken)
    {
        CustomerQueryRow? row = await LoadAsync(id, cancellationToken).ConfigureAwait(false);
        return row is null ? null : (row.CreditLimit, row.CreditUsed);
    }

    private Task<CustomerQueryRow?> LoadAsync(long id, CancellationToken cancellationToken)
    {
        if (id <= 0)
        {
            return Task.FromResult<CustomerQueryRow?>(null);
        }

        string cacheKey = QlxCacheKey.BuildTenantKey(
            TenantId, QlxModuleCodes.Bas, $"customerquery:{id.ToString(CultureInfo.InvariantCulture)}");

        return cache.GetOrCreateAsync<CustomerQueryRow?>(
            cacheKey,
            factory: async token =>
            {
                Domain.Customer.Customer? customer = await repo.FindCustomerAsync(id, token).ConfigureAwait(false);
                return customer is null
                    ? null
                    : new CustomerQueryRow(
                        customer.Id,
                        customer.Code,
                        customer.Name,
                        (int)customer.CreditGrade,
                        customer.CreditLimit,
                        customer.CreditUsed,
                        (int)customer.Status);
            },
            cancellationToken: cancellationToken,
            options: s_cacheOptions);
    }
}
