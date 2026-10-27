/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.1.0 beta
 * 创建时间：2026-10-25 23:55:00 +08:00 中国标准时间 星期日
 * 修改时间：2026-10-25 23:55:00 +08:00 中国标准时间 星期日
 * 描述：数据字典服务实现——两级可见性（SQL 层过滤）+ 集团级/子公司级写保护 + 系统内置保护 +
 *       失效优先缓存（写路径只删缓存，读路径 GetOrCreateAsync）
 */

using System.Globalization;

using Qlx.Abstractions.Constants;
using Qlx.Abstractions.Domain;
using Qlx.Abstractions.Exceptions;
using Qlx.Abstractions.Tenancy;
using Qlx.BuildingBlocks.Caching;
using Qlx.Modules.Bas.Domain;
using Qlx.Modules.Bas.Domain.Dictionary;
using Qlx.Modules.Bas.Dtos;
using Qlx.Modules.Bas.Repositories;

namespace Qlx.Modules.Bas.Services;

/// <summary>
/// 数据字典服务实现。读路径经 <c>GetOrCreateAsync</c> 读穿缓存（C-2/C-3/C-4），
/// 写路径提交后按 <c>bas:dict:</c> 前缀失效（C-5 失效优先）；字典值只缓存扁平原始字段（C-7-2）。
/// </summary>
/// <param name="tenantCtx">当前租户上下文。</param>
/// <param name="repo">Bas 租户仓储。</param>
/// <param name="scopeResolver">当前成员数据范围解析（null = TenantAdmin 全范围）。</param>
/// <param name="cache">统一缓存服务。</param>
internal sealed class DictionaryService(
    ITenantContext tenantCtx,
    IBasTenantRepository repo,
    ICurrentMemberDataScopeResolver scopeResolver,
    ICacheService cache) : IDictionaryService
{
    private static readonly CacheEntryOptions s_cacheOptions = new()
    {
        AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5),
        NullValueExpiration = TimeSpan.FromSeconds(30),
    };

    private long TenantId => tenantCtx.TenantId
        ?? throw new ForbiddenException("未识别到租户上下文");

    // ============ 读路径 ============

    /// <inheritdoc />
    public async Task<IReadOnlyList<DictTypeRow>> ListTypesAsync(
        int? ownerLevel, int? status, CancellationToken ct)
    {
        MemberDataScopeContext? scope = await scopeResolver.ResolveAsync(ct).ConfigureAwait(false);
        // scope 为 null = TenantAdmin 全范围 / 无成员上下文的门户模式：不做层级可见性过滤
        bool unrestricted = scope is null;
        long? orgId = scope?.OrgId;

        // 缓存键必须区分范围（C-5/C-7）：全范围与各组织范围互不覆盖
        string cacheKey = QlxCacheKey.BuildTenantKey(
            TenantId,
            QlxModuleCodes.Bas,
            $"dict:types:{(unrestricted ? "all" : FormatId(orgId))}:{FormatId(ownerLevel)}:{FormatId(status)}");

        return await cache.GetOrCreateAsync<IReadOnlyList<DictTypeRow>>(
            cacheKey,
            factory: async token =>
            {
                IReadOnlyList<DictType> types = await repo
                    .QueryDictTypesAsync(unrestricted, orgId, ownerLevel, status, token).ConfigureAwait(false);
                return [.. types.Select(Map)];
            },
            cancellationToken: ct,
            options: s_cacheOptions).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<DictTypeRow> GetTypeAsync(long id, CancellationToken ct)
    {
        string cacheKey = QlxCacheKey.BuildTenantKey(
            TenantId, QlxModuleCodes.Bas, $"dict:type:{id.ToString(CultureInfo.InvariantCulture)}");

        DictTypeRow? row = await cache.GetOrCreateAsync<DictTypeRow?>(
            cacheKey,
            factory: async token =>
            {
                DictType? type = await repo.FindDictTypeAsync(id, token).ConfigureAwait(false);
                return type is null ? null : Map(type);
            },
            cancellationToken: ct,
            options: s_cacheOptions).ConfigureAwait(false);

        if (row is null)
        {
            throw new NotFoundException("字典类型", id.ToString(CultureInfo.InvariantCulture));
        }

        await EnsureVisibleAsync(row.OwnerLevel, row.OrgId, ct).ConfigureAwait(false);
        return row;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<DictItemRow>> ListItemsAsync(
        string typeCode, int? status, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(typeCode);
        DictType type = await FindVisibleTypeByCodeAsync(typeCode, ct).ConfigureAwait(false);

        long? orgId = (await scopeResolver.ResolveAsync(ct).ConfigureAwait(false))?.OrgId;
        string cacheKey = QlxCacheKey.BuildTenantKey(
            TenantId,
            QlxModuleCodes.Bas,
            $"dict:items:{FormatId(orgId)}:{type.TypeCode}:{FormatId(status)}");

        return await cache.GetOrCreateAsync<IReadOnlyList<DictItemRow>>(
            cacheKey,
            factory: async token =>
            {
                IReadOnlyList<DictItem> items = await repo
                    .QueryDictItemsAsync(type.TypeCode, status, token).ConfigureAwait(false);
                return [.. items.Select(Map)];
            },
            cancellationToken: ct,
            options: s_cacheOptions).ConfigureAwait(false);
    }

    // ============ 写路径：字典类型 ============

    /// <inheritdoc />
    public async Task<DictTypeRow> CreateTypeAsync(SaveDictTypePayload payload, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(payload);

        string typeCode = Require(payload.TypeCode, "typeCode", "字典类型编码不能为空");
        string typeName = Require(payload.TypeName, "typeName", "字典类型名称不能为空");
        DictOwnerLevel ownerLevel = ParseOwnerLevel(payload.OwnerLevel);

        if (ownerLevel == DictOwnerLevel.Subsidiary && !payload.OrgId.HasValue)
        {
            throw new ValidationException("orgId", "子公司级字典必须指定所属组织");
        }

        if (ownerLevel == DictOwnerLevel.Subsidiary)
        {
            _ = await repo.FindOrgAsync(payload.OrgId!.Value, ct).ConfigureAwait(false)
                ?? throw new NotFoundException(
                    "组织", payload.OrgId!.Value.ToString(CultureInfo.InvariantCulture));
        }

        await EnsureCanWriteAsync(ownerLevel, payload.OrgId, ct).ConfigureAwait(false);

        if (await repo.IsDictTypeCodeExistsAsync(typeCode, excludeId: null, ct).ConfigureAwait(false))
        {
            throw new ConflictException("字典类型编码已存在", "DICT_TYPE_CODE_DUPLICATED");
        }

        DictType type = new(TenantId, typeCode, typeName, ownerLevel, payload.OrgId);
        await repo.AddDictTypeAsync(type, ct).ConfigureAwait(false);
        _ = await repo.SaveChangesAsync(ct).ConfigureAwait(false);

        await InvalidateCacheAsync("dict:", ct).ConfigureAwait(false);
        return Map(type);
    }

    /// <inheritdoc />
    public async Task<DictTypeRow> UpdateTypeAsync(
        long id, UpdateDictTypePayload payload, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(payload);

        DictType type = await FindTypeOrThrowAsync(id, ct).ConfigureAwait(false);
        await EnsureVisibleAsync((int)type.OwnerLevel, type.OrgId, ct).ConfigureAwait(false);
        await EnsureCanWriteAsync(type.OwnerLevel, type.OrgId, ct).ConfigureAwait(false);

        if (type.IsSystem)
        {
            throw new ConflictException("系统内置字典类型仅可启停，不可修改", "DICT_TYPE_BUILTIN");
        }

        type.Rename(Require(payload.TypeName, "typeName", "字典类型名称不能为空"));
        await repo.UpdateDictTypeAsync(type, ct).ConfigureAwait(false);
        _ = await repo.SaveChangesAsync(ct).ConfigureAwait(false);

        await InvalidateCacheAsync("dict:", ct).ConfigureAwait(false);
        return Map(type);
    }

    /// <inheritdoc />
    public async Task<DictTypeRow> ToggleTypeAsync(long id, CancellationToken ct)
    {
        DictType type = await FindTypeOrThrowAsync(id, ct).ConfigureAwait(false);
        await EnsureVisibleAsync((int)type.OwnerLevel, type.OrgId, ct).ConfigureAwait(false);
        await EnsureCanWriteAsync(type.OwnerLevel, type.OrgId, ct).ConfigureAwait(false);

        if (type.Status == EntityStatus.Enabled)
        {
            type.Disable();
        }
        else
        {
            type.Enable();
        }

        await repo.UpdateDictTypeAsync(type, ct).ConfigureAwait(false);
        _ = await repo.SaveChangesAsync(ct).ConfigureAwait(false);

        await InvalidateCacheAsync("dict:", ct).ConfigureAwait(false);
        return Map(type);
    }

    // ============ 写路径：字典项 ============

    /// <inheritdoc />
    public async Task<DictItemRow> CreateItemAsync(
        string typeCode, SaveDictItemPayload payload, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(payload);

        DictType type = await FindVisibleTypeByCodeAsync(typeCode, ct).ConfigureAwait(false);
        await EnsureCanWriteAsync(type.OwnerLevel, type.OrgId, ct).ConfigureAwait(false);

        string itemCode = Require(payload.ItemCode, "itemCode", "字典项编码不能为空");
        string itemName = Require(payload.ItemName, "itemName", "字典项名称不能为空");

        if (payload.SortNo < 0)
        {
            throw new ValidationException("sortNo", "排序号不能为负数");
        }

        if (await repo.IsDictItemCodeExistsAsync(type.TypeCode, itemCode, excludeId: null, ct)
            .ConfigureAwait(false))
        {
            throw new ConflictException("字典项编码已存在", "DICT_ITEM_CODE_DUPLICATED");
        }

        DictItem item = new(TenantId, type.TypeCode, itemCode, itemName, payload.SortNo);
        await repo.AddDictItemAsync(item, ct).ConfigureAwait(false);
        _ = await repo.SaveChangesAsync(ct).ConfigureAwait(false);

        await InvalidateCacheAsync("dict:", ct).ConfigureAwait(false);
        return Map(item);
    }

    /// <inheritdoc />
    public async Task<DictItemRow> UpdateItemAsync(
        long id, UpdateDictItemPayload payload, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(payload);

        DictItem item = await FindItemOrThrowAsync(id, ct).ConfigureAwait(false);
        DictType type = await FindVisibleTypeByCodeAsync(item.TypeCode, ct).ConfigureAwait(false);
        await EnsureCanWriteAsync(type.OwnerLevel, type.OrgId, ct).ConfigureAwait(false);

        if (item.IsSystem)
        {
            throw new ConflictException("系统内置字典项仅可启停，不可修改", "DICT_ITEM_BUILTIN");
        }

        string itemName = Require(payload.ItemName, "itemName", "字典项名称不能为空");
        if (payload.SortNo < 0)
        {
            throw new ValidationException("sortNo", "排序号不能为负数");
        }

        item.UpdateDetails(itemName, payload.SortNo);
        await repo.UpdateDictItemAsync(item, ct).ConfigureAwait(false);
        _ = await repo.SaveChangesAsync(ct).ConfigureAwait(false);

        await InvalidateCacheAsync("dict:", ct).ConfigureAwait(false);
        return Map(item);
    }

    /// <inheritdoc />
    public async Task<DictItemRow> ToggleItemAsync(long id, CancellationToken ct)
    {
        DictItem item = await FindItemOrThrowAsync(id, ct).ConfigureAwait(false);
        DictType type = await FindVisibleTypeByCodeAsync(item.TypeCode, ct).ConfigureAwait(false);
        await EnsureCanWriteAsync(type.OwnerLevel, type.OrgId, ct).ConfigureAwait(false);

        if (item.Status == EntityStatus.Enabled)
        {
            item.Disable();
        }
        else
        {
            item.Enable();
        }

        await repo.UpdateDictItemAsync(item, ct).ConfigureAwait(false);
        _ = await repo.SaveChangesAsync(ct).ConfigureAwait(false);

        await InvalidateCacheAsync("dict:", ct).ConfigureAwait(false);
        return Map(item);
    }

    /// <inheritdoc />
    public async Task DeleteItemAsync(long id, CancellationToken ct)
    {
        DictItem item = await FindItemOrThrowAsync(id, ct).ConfigureAwait(false);
        DictType type = await FindVisibleTypeByCodeAsync(item.TypeCode, ct).ConfigureAwait(false);
        await EnsureCanWriteAsync(type.OwnerLevel, type.OrgId, ct).ConfigureAwait(false);

        if (item.IsSystem)
        {
            throw new ConflictException("系统内置字典项不可删除，仅可停用", "DICT_ITEM_BUILTIN");
        }

        if (await IsDictItemReferencedAsync(item.TypeCode, item.ItemCode, ct).ConfigureAwait(false))
        {
            throw new ConflictException("字典项已被业务引用，仅可停用", "DICT_ITEM_REFERENCED");
        }

        // 字典项无子表引用且属配置类主数据，硬删（操作日志由 Controller [AuditOperation] 记录）
        await repo.RemoveDictItemAsync(item, ct).ConfigureAwait(false);
        _ = await repo.SaveChangesAsync(ct).ConfigureAwait(false);

        await InvalidateCacheAsync("dict:", ct).ConfigureAwait(false);
    }

    /// <summary>
    /// 字典项引用检查。下游业务（商品 / 采购 / 销售等）尚未落地引用表，
    /// 当前恒返回 <see langword="false"/>；引用表就绪后在此按 <c>typeCode + itemCode</c> 批量查引用表。
    /// </summary>
    private static Task<bool> IsDictItemReferencedAsync(
        string typeCode, string itemCode, CancellationToken ct)
    {
        _ = typeCode;
        _ = itemCode;
        _ = ct;
        return Task.FromResult(false);
    }

    // ============ 可见性与写保护 ============

    private async Task<DictType> FindVisibleTypeByCodeAsync(string typeCode, CancellationToken ct)
    {
        DictType type = await repo.FindDictTypeByCodeAsync(typeCode.Trim(), ct).ConfigureAwait(false)
            ?? throw new NotFoundException("字典类型", typeCode);
        await EnsureVisibleAsync((int)type.OwnerLevel, type.OrgId, ct).ConfigureAwait(false);
        return type;
    }

    private async Task<DictType> FindTypeOrThrowAsync(long id, CancellationToken ct)
        => await repo.FindDictTypeAsync(id, ct).ConfigureAwait(false)
           ?? throw new NotFoundException("字典类型", id.ToString(CultureInfo.InvariantCulture));

    private async Task<DictItem> FindItemOrThrowAsync(long id, CancellationToken ct)
        => await repo.FindDictItemAsync(id, ct).ConfigureAwait(false)
           ?? throw new NotFoundException("字典项", id.ToString(CultureInfo.InvariantCulture));

    /// <summary>
    /// 两级可见性：集团级全租户可见；子公司级仅本组织可见。
    /// <para>scope 为 null（TenantAdmin 全范围 / 无成员上下文的门户模式）直接放行，可见全部组织字典。</para>
    /// </summary>
    private async Task EnsureVisibleAsync(int ownerLevel, long? orgId, CancellationToken ct)
    {
        if (ownerLevel == (int)DictOwnerLevel.Group)
        {
            return;
        }

        MemberDataScopeContext? scope = await scopeResolver.ResolveAsync(ct).ConfigureAwait(false);
        if (scope is null)
        {
            return;
        }

        if (!orgId.HasValue || scope.OrgId != orgId)
        {
            throw new ForbiddenException("无权访问其他组织的字典");
        }
    }

    /// <summary>
    /// 写保护：集团级字典仅租户根组织（<c>Organization.ParentId == null</c>）可维护；
    /// 子公司级字典仅所属组织可维护。数据范围解析返回 null（TenantAdmin / 门户模式）视为全范围。
    /// </summary>
    private async Task EnsureCanWriteAsync(
        DictOwnerLevel ownerLevel, long? orgId, CancellationToken ct)
    {
        MemberDataScopeContext? scope = await scopeResolver.ResolveAsync(ct).ConfigureAwait(false);
        if (scope is null)
        {
            return;
        }

        if (ownerLevel == DictOwnerLevel.Subsidiary)
        {
            if (orgId.HasValue && scope.OrgId == orgId)
            {
                return;
            }

            throw new ForbiddenException("无权修改其他组织的字典");
        }

        if (!scope.OrgId.HasValue)
        {
            throw new ForbiddenException("集团级字典仅集团根组织可维护");
        }

        // 一次取全量组织后在内存判定根节点（禁 N+1）
        IReadOnlyList<Organization> orgs = await repo.ListAllOrgsAsync(ct).ConfigureAwait(false);
        bool isRoot = orgs.Any(o => o.Id == scope.OrgId.Value && o.ParentId == null);
        if (!isRoot)
        {
            throw new ForbiddenException("集团级字典仅集团根组织可维护");
        }
    }

    // ============ 缓存与映射 ============

    /// <summary>失效优先：写库提交后按字典缓存子前缀删除（C-5-1，禁止双写）。</summary>
    private Task<long> InvalidateCacheAsync(string subPrefix, CancellationToken ct)
        => cache.RemoveByPrefixAsync(
            $"{QlxCacheKey.TenantKeyPrefix}:{TenantId.ToString(CultureInfo.InvariantCulture)}:{QlxModuleCodes.Bas}:{subPrefix}",
            ct);

    private static string FormatId(long? value)
        => value?.ToString(CultureInfo.InvariantCulture) ?? "-";

    private static string FormatId(int? value)
        => value?.ToString(CultureInfo.InvariantCulture) ?? "-";

    private static DictTypeRow Map(DictType type)
        => new(
            type.Id,
            type.TypeCode,
            type.TypeName,
            (int)type.OwnerLevel,
            type.OrgId,
            (int)type.Status,
            type.IsSystem,
            type.UpdatedAt);

    private static DictItemRow Map(DictItem item)
        => new(
            item.Id,
            item.TypeCode,
            item.ItemCode,
            item.ItemName,
            item.SortNo,
            (int)item.Status,
            item.IsSystem,
            item.UpdatedAt);

    private static DictOwnerLevel ParseOwnerLevel(int value)
        => Enum.IsDefined(typeof(DictOwnerLevel), value)
            ? (DictOwnerLevel)value
            : throw new ValidationException(
                "ownerLevel", $"未知的字典归属层级：{value.ToString(CultureInfo.InvariantCulture)}");

    private static string Require(string? value, string field, string message)
        => string.IsNullOrWhiteSpace(value)
            ? throw new ValidationException(field, message)
            : value.Trim();
}
