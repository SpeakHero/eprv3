/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-25 22:30:00 +08:00 中国标准时间 星期日
 * 描述：跨模块人员只读查询实现——租户级缓存（GetOrCreateAsync + 空值缓存 + TTL 抖动）
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
/// 人员只读查询实现（<see cref="IPersonQueryService"/>）。仅下发扁平原始字段与数值枚举（B-2），
/// 不含证件号 / 手机号 / 银行卡号等敏感字段；读路径统一 <c>GetOrCreateAsync</c>（C-2/C-3/C-4），
/// 写路径由人员主数据服务按前缀失效（C-5）。
/// </summary>
/// <param name="tenantCtx">当前租户上下文。</param>
/// <param name="repo">Bas 租户仓储。</param>
/// <param name="cache">统一缓存服务。</param>
internal sealed class PersonQueryService(
    ITenantContext tenantCtx,
    IBasTenantRepository repo,
    ICacheService cache) : IPersonQueryService
{
    private static readonly CacheEntryOptions s_cacheOptions = new()
    {
        AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5),
        NullValueExpiration = TimeSpan.FromSeconds(30),
    };

    private long TenantId => tenantCtx.TenantId
        ?? throw new ForbiddenException("未识别到租户上下文");

    /// <inheritdoc />
    public async Task<PersonQueryRow?> FindAsync(long id, CancellationToken cancellationToken)
    {
        if (id <= 0)
        {
            return null;
        }

        string cacheKey = QlxCacheKey.BuildTenantKey(
            TenantId, QlxModuleCodes.Bas, $"person:{id.ToString(CultureInfo.InvariantCulture)}");

        return await cache.GetOrCreateAsync<PersonQueryRow?>(
            cacheKey,
            factory: async token =>
            {
                Domain.Person.Person? person = await repo.FindPersonAsync(id, token).ConfigureAwait(false);
                return person is null ? null : Map(person);
            },
            cancellationToken: cancellationToken,
            options: s_cacheOptions).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<PersonQueryRow>> ListByPositionAsync(
        long positionId, CancellationToken cancellationToken)
    {
        string cacheKey = QlxCacheKey.BuildTenantKey(
            TenantId, QlxModuleCodes.Bas, $"person:position:{positionId.ToString(CultureInfo.InvariantCulture)}");

        return cache.GetOrCreateAsync<IReadOnlyList<PersonQueryRow>>(
            cacheKey,
            factory: async token =>
            {
                IReadOnlyList<Domain.Person.Person> persons = await repo
                    .ListActivePersonsByPositionAsync(positionId, token).ConfigureAwait(false);
                return [.. persons.Select(Map)];
            },
            cancellationToken: cancellationToken,
            options: s_cacheOptions);
    }

    private static PersonQueryRow Map(Domain.Person.Person person)
        => new(
            person.Id,
            person.Code,
            person.Name,
            (int)person.PersonType,
            person.OrgId,
            person.PositionId,
            (int)person.EmployStatus,
            person.IsDeleted);
}
