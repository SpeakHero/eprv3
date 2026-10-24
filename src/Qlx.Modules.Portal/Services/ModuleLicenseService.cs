/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-21 06:30:00 +08:00 中国标准时间 星期三
 * 修改时间：2026-10-21 06:30:00 +08:00 中国标准时间 星期三
 * 描述：模块许可服务实现：Cache-Aside 查询租户模块许可（键 _portal:license:{tenantId}）
 */

using System.Collections.Immutable;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Qlx.Abstractions.Modules;
using Qlx.BuildingBlocks.Caching;

namespace Qlx.Modules.Portal.Services;

/// <summary>
/// 模块许可服务（Portal 实现）。以租户为单位 Cache-Aside 缓存已启用模块编码集合，
/// 键规范 <c>_portal:license:{tenantId}</c>（门户级数据用 _portal 保留前缀）；
/// 许可变更（模块启停）时由管理端点负责 RemoveByPrefix 失效。
/// </summary>
public sealed class ModuleLicenseService(PortalDbContext db, ICacheService cache) : IModuleLicenseService
{
    private static readonly TimeSpan CacheTtl = PortalConstants.CacheKeys.DefaultTtl;

    /// <summary>
    /// 构建租户许可缓存键（<c>_portal:license:{tenantId}</c>）。
    /// 读路径与管理端写路径共用此唯一入口，避免散装字符串拼 key（C-1-2）。
    /// </summary>
    /// <param name="tenantId">租户主键。</param>
    /// <returns>规范化缓存键。</returns>
    public static string BuildCacheKey(long tenantId)
        => QlxCacheKey.BuildPortalKey("license", tenantId.ToString(CultureInfo.InvariantCulture));

    /// <inheritdoc />
    public async Task<bool> HasLicenseAsync(long tenantId, string moduleCode, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(moduleCode);

        var modules = await GetLicensedModuleCodesAsync(tenantId, cancellationToken).ConfigureAwait(false);
        return modules.Contains(moduleCode);
    }

    /// <summary>
    /// 获取租户已启用模块编码集合（Cache-Aside；登录响应 modules claim 复用）。
    /// </summary>
    public async Task<ImmutableHashSet<string>> GetLicensedModuleCodesAsync(long tenantId, CancellationToken cancellationToken)
    {
        // 读穿缓存（C-2：并发合并防击穿，禁止 Get → null → 查库 → Set 裸序列）
        var cacheKey = BuildCacheKey(tenantId);
        return await cache.GetOrCreateAsync(
            cacheKey,
            factory: async ct =>
            {
                var codes = await db.TenantModules
                    .AsNoTracking()
                    .Where(t => t.TenantId == tenantId && t.Status == Domain.Tenancy.TenantModuleStatus.Enabled)
                    .Select(t => t.ModuleCode)
                    .ToListAsync(ct).ConfigureAwait(false);
                return codes.ToImmutableHashSet();
            },
            cancellationToken: cancellationToken,
            options: new CacheEntryOptions { AbsoluteExpirationRelativeToNow = CacheTtl })
            .ConfigureAwait(false);
    }
}
