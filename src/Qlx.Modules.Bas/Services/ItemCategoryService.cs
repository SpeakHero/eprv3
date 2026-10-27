/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-25 16:30:00 +08:00 中国标准时间 星期日
 * 描述：商品分类主数据服务实现——层级编码 + 层级上限参数 + 读穿缓存 + 事务写入 + 审计 + 停用审批挂起
 */

using System.Globalization;
using System.Text.Json;

using Qlx.Abstractions.Approvals;
using Qlx.Abstractions.Audit;
using Qlx.Abstractions.Constants;
using Qlx.Abstractions.Domain;
using Qlx.Abstractions.Exceptions;
using Qlx.Abstractions.Modules;
using Qlx.Abstractions.Serialization;
using Qlx.Abstractions.Tenancy;
using Qlx.BuildingBlocks.Caching;
using Qlx.BuildingBlocks.Ids;
using Qlx.Modules.Bas.Domain;
using Qlx.Modules.Bas.Domain.Goods;
using Qlx.Modules.Bas.Dtos;
using Qlx.Modules.Bas.Repositories;
using Qlx.Modules.Bas.Services.Numbering;

namespace Qlx.Modules.Bas.Services;

/// <summary>
/// 商品分类主数据服务。分类编码按 <c>{parent}{seq:2}</c> 规则连续生成（父 01 → 子 0101 → 孙 010101）；
/// 层级上限读 <c>por_parameters.bas.category.maxLevel</c>（默认 4），超限抛 422 校验异常。
/// </summary>
/// <param name="tenantCtx">当前租户上下文。</param>
/// <param name="repo">Bas 租户仓储。</param>
/// <param name="codeNumberGenerator">统一发号器（分类编码）。</param>
/// <param name="approvalService">审批中心契约（发起分类停用审批）。</param>
/// <param name="parameters">系统参数读取（分类层级上限）。</param>
/// <param name="cache">统一缓存服务。</param>
/// <param name="audit">审计服务。</param>
/// <param name="snowflake">雪花 Id 生成器（审计事件 Id）。</param>
/// <param name="userContext">当前用户上下文（审计变更人）。</param>
internal sealed class ItemCategoryService(
    ITenantContext tenantCtx,
    IBasTenantRepository repo,
    IBasCodeNumberGenerator codeNumberGenerator,
    IApprovalService approvalService,
    ISystemParameterProvider parameters,
    ICacheService cache,
    IAuditService audit,
    ISnowflakeIdGenerator snowflake,
    ICurrentUserContext userContext) : IItemCategoryService
{
    /// <summary>分类停用审批业务类型。</summary>
    internal const string CategoryDisableBizType = "bas.category.disable";

    /// <summary>分类层级上限参数键（por_parameters）。</summary>
    internal const string MaxLevelParamKey = "bas.category.maxLevel";

    /// <summary>分类层级上限默认值。</summary>
    internal const int DefaultMaxLevel = 4;

    private const string CategoryBizType = "category";

    private static readonly CacheEntryOptions s_cacheOptions = new()
    {
        AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5),
        NullValueExpiration = TimeSpan.FromSeconds(30),
    };

    private long TenantId => tenantCtx.TenantId
        ?? throw new ForbiddenException("未识别到租户上下文");

    private string ChangedBy => userContext.UserId ?? "system";

    // ============ 读路径（GetOrCreateAsync / 空值缓存 / TTL 抖动）============

    /// <inheritdoc />
    public async Task<IReadOnlyList<ItemCategoryRow>> ListAsync(CancellationToken ct)
    {
        string cacheKey = QlxCacheKey.BuildTenantKey(TenantId, QlxModuleCodes.Bas, "category:all");
        return await cache.GetOrCreateAsync<IReadOnlyList<ItemCategoryRow>>(
            cacheKey,
            factory: async token =>
            {
                IReadOnlyList<ItemCategory> categories = await repo.ListAllCategoriesAsync(token).ConfigureAwait(false);
                return [.. categories.Select(Map)];
            },
            cancellationToken: ct,
            options: s_cacheOptions).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<ItemCategoryRow> GetAsync(long id, CancellationToken ct)
    {
        string cacheKey = QlxCacheKey.BuildTenantKey(
            TenantId, QlxModuleCodes.Bas, $"category:{id.ToString(CultureInfo.InvariantCulture)}");

        ItemCategoryRow? row = await cache.GetOrCreateAsync<ItemCategoryRow?>(
            cacheKey,
            factory: async token =>
            {
                ItemCategory? category = await repo.FindCategoryAsync(id, token).ConfigureAwait(false);
                return category is null ? null : Map(category);
            },
            cancellationToken: ct,
            options: s_cacheOptions).ConfigureAwait(false);

        return row ?? throw new NotFoundException("商品分类", id.ToString(CultureInfo.InvariantCulture));
    }

    // ============ 写路径（事务 → 审计 → 失效）============

    /// <inheritdoc />
    public async Task<ItemCategoryRow> CreateAsync(CreateItemCategoryPayload payload, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(payload);

        ItemCategoryRow row = await repo.ExecuteInTransactionAsync(
            token => CreateCoreAsync(payload, token), ct).ConfigureAwait(false);

        await CaptureAuditAsync("INSERT", row, oldJson: null, ct).ConfigureAwait(false);
        await InvalidateCacheAsync(ct).ConfigureAwait(false);
        return row;
    }

    /// <inheritdoc />
    public async Task<ItemCategoryRow> UpdateAsync(
        long id, UpdateItemCategoryPayload payload, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(payload);

        (ItemCategoryRow Row, string OldJson) result = await repo.ExecuteInTransactionAsync(
            token => UpdateCoreAsync(id, payload, token), ct).ConfigureAwait(false);

        await CaptureAuditAsync("UPDATE", result.Row, result.OldJson, ct).ConfigureAwait(false);
        await InvalidateCacheAsync(ct).ConfigureAwait(false);
        return result.Row;
    }

    /// <inheritdoc />
    public async Task<ItemCategoryRow> ToggleAsync(long id, CancellationToken ct)
    {
        (ItemCategoryRow Row, string OldJson) result = await repo.ExecuteInTransactionAsync(
            async token =>
            {
                ItemCategory category = await FindCategoryOrThrowAsync(id, token).ConfigureAwait(false);
                string oldJson = Serialize(Map(category));

                if (category.Status == EntityStatus.Enabled)
                {
                    category.Disable();
                }
                else
                {
                    category.Enable();
                }

                await repo.UpdateCategoryAsync(category, token).ConfigureAwait(false);
                _ = await repo.SaveChangesAsync(token).ConfigureAwait(false);
                return (Map(category), oldJson);
            },
            ct).ConfigureAwait(false);

        await CaptureAuditAsync("UPDATE", result.Row, result.OldJson, ct).ConfigureAwait(false);
        await InvalidateCacheAsync(ct).ConfigureAwait(false);
        return result.Row;
    }

    /// <inheritdoc />
    public async Task DeleteAsync(long id, CancellationToken ct)
    {
        (ItemCategoryRow Row, string OldJson) result = await repo.ExecuteInTransactionAsync(
            async token =>
            {
                ItemCategory category = await FindCategoryOrThrowAsync(id, token).ConfigureAwait(false);
                if (await repo.HasChildCategoriesAsync(id, token).ConfigureAwait(false))
                {
                    throw new ConflictException("存在下级分类，不能删除", "CATEGORY_HAS_CHILDREN");
                }

                // 模块 02（商品主数据）落地后在此补商品引用校验：被商品引用的分类只允许停用，不允许删除
                string oldJson = Serialize(Map(category));
                category.SoftDelete();
                await repo.UpdateCategoryAsync(category, token).ConfigureAwait(false);
                _ = await repo.SaveChangesAsync(token).ConfigureAwait(false);
                return (Map(category), oldJson);
            },
            ct).ConfigureAwait(false);

        await CaptureAuditAsync("SOFT_DELETE", result.Row, result.OldJson, ct).ConfigureAwait(false);
        await InvalidateCacheAsync(ct).ConfigureAwait(false);
    }

    // ============ 审批挂起与应用 ============

    /// <inheritdoc />
    public async Task<ApprovalRequestRow> SubmitDisableRequestAsync(long id, CancellationToken ct)
    {
        ItemCategory category = await repo.FindCategoryAsync(id, ct).ConfigureAwait(false)
            ?? throw new NotFoundException("商品分类", id.ToString(CultureInfo.InvariantCulture));

        if (category.Status == EntityStatus.Disabled)
        {
            throw new ValidationException("status", "分类已停用，无需重复申请");
        }

        BasApprovalRequest request = new(
            TenantId,
            CategoryDisableBizType,
            JsonSerializer.Serialize(new CategoryDisablePayload(category.Id), QlxJsonDefaults.PreserveNull));
        await repo.AddApprovalRequestAsync(request, ct).ConfigureAwait(false);
        _ = await repo.SaveChangesAsync(ct).ConfigureAwait(false);

        string instanceId = await approvalService.StartApprovalAsync(
            new StartApprovalRequest(
                ApprovalDefinitionCode: CategoryDisableBizType,
                BizType: CategoryDisableBizType,
                BizId: request.Id.ToString(CultureInfo.InvariantCulture),
                BizNo: category.Code,
                InitiatorUserId: ChangedBy,
                Title: $"商品分类停用申请：{category.Name}",
                Description: null,
                BizData: new Dictionary<string, object?>
                {
                    ["categoryId"] = category.Id,
                }),
            ct).ConfigureAwait(false);

        request.BindInstance(instanceId);
        _ = await repo.SaveChangesAsync(ct).ConfigureAwait(false);
        return Map(request);
    }

    /// <inheritdoc />
    public async Task ApplyDisableAsync(long id, CancellationToken ct)
    {
        ItemCategory category = await FindCategoryOrThrowAsync(id, ct).ConfigureAwait(false);
        if (category.Status == EntityStatus.Disabled)
        {
            return;
        }

        string oldJson = Serialize(Map(category));
        category.Disable();
        await repo.UpdateCategoryAsync(category, ct).ConfigureAwait(false);
        _ = await repo.SaveChangesAsync(ct).ConfigureAwait(false);
        await CaptureAuditAsync("UPDATE", Map(category), oldJson, ct).ConfigureAwait(false);
    }

    // ============ 内部实现 ============

    private async Task<ItemCategoryRow> CreateCoreAsync(
        CreateItemCategoryPayload payload, CancellationToken ct)
    {
        string name = RequireName(payload.Name);

        ItemCategory? parent = null;
        if (payload.ParentId.HasValue)
        {
            parent = await repo.FindCategoryAsync(payload.ParentId.Value, ct).ConfigureAwait(false)
                ?? throw new NotFoundException(
                    "上级商品分类", payload.ParentId.Value.ToString(CultureInfo.InvariantCulture));
        }

        int levelNo = parent is null ? 1 : parent.LevelNo + 1;
        int maxLevel = await parameters
            .GetIntAsync(MaxLevelParamKey, DefaultMaxLevel, ct).ConfigureAwait(false);
        if (levelNo > maxLevel)
        {
            throw new ValidationException("levelNo", "层级超过上限");
        }

        await EnsureUnitGroupExistsAsync(payload.UnitGroupId, ct).ConfigureAwait(false);

        string code = await codeNumberGenerator.NextAsync(
            CategoryBizType,
            TenantId,
            parent?.Code,
            typeCode: null,
            isConflict: (candidate, token) => repo.IsCategoryCodeExistsAsync(candidate, token),
            ct: ct).ConfigureAwait(false);

        ItemCategory category = new(TenantId, name, parent?.Id, levelNo, code, payload.UnitGroupId);
        await repo.AddCategoryAsync(category, ct).ConfigureAwait(false);
        _ = await repo.SaveChangesAsync(ct).ConfigureAwait(false);
        return Map(category);
    }

    private async Task<(ItemCategoryRow Row, string OldJson)> UpdateCoreAsync(
        long id, UpdateItemCategoryPayload payload, CancellationToken ct)
    {
        ItemCategory category = await FindCategoryOrThrowAsync(id, ct).ConfigureAwait(false);
        string name = RequireName(payload.Name);
        string oldJson = Serialize(Map(category));

        await EnsureUnitGroupExistsAsync(payload.UnitGroupId, ct).ConfigureAwait(false);

        category.Rename(name);
        category.SetUnitGroup(payload.UnitGroupId);
        await repo.UpdateCategoryAsync(category, ct).ConfigureAwait(false);
        _ = await repo.SaveChangesAsync(ct).ConfigureAwait(false);
        return (Map(category), oldJson);
    }

    private async Task<ItemCategory> FindCategoryOrThrowAsync(long id, CancellationToken ct)
        => await repo.FindCategoryAsync(id, ct).ConfigureAwait(false)
           ?? throw new NotFoundException("商品分类", id.ToString(CultureInfo.InvariantCulture));

    private async Task EnsureUnitGroupExistsAsync(long? unitGroupId, CancellationToken ct)
    {
        if (!unitGroupId.HasValue)
        {
            return;
        }

        _ = await repo.FindUnitGroupAsync(unitGroupId.Value, ct).ConfigureAwait(false)
            ?? throw new NotFoundException(
                "计量单位组", unitGroupId.Value.ToString(CultureInfo.InvariantCulture));
    }

    private Task CaptureAuditAsync(string operation, ItemCategoryRow row, string? oldJson, CancellationToken ct)
        => audit.CaptureChangeAsync(
            new AuditLogEntry(
                snowflake.NextId(),
                nameof(ItemCategory),
                row.Id,
                operation,
                oldJson,
                Serialize(row),
                ChangedBy,
                DateTimeOffset.UtcNow,
                TenantId),
            ct);

    private Task<long> InvalidateCacheAsync(CancellationToken ct)
        => cache.RemoveByPrefixAsync(
            $"{QlxCacheKey.TenantKeyPrefix}:{TenantId.ToString(CultureInfo.InvariantCulture)}:{QlxModuleCodes.Bas}:",
            ct);

    private static string RequireName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ValidationException("name", "分类名称不能为空");
        }

        return name.Trim();
    }

    private static ItemCategoryRow Map(ItemCategory category)
        => new(
            category.Id,
            category.ParentId,
            category.Code,
            category.Name,
            category.LevelNo,
            category.UnitGroupId,
            (int)category.Status,
            category.UpdatedAt);

    private static ApprovalRequestRow Map(BasApprovalRequest request)
        => new(
            request.Id,
            request.ApprovalInstanceId,
            request.BizType,
            request.BizId,
            request.PayloadJson,
            (int)request.Status,
            request.AppliedAt,
            request.CreatedAt);

    private static string Serialize(ItemCategoryRow row)
        => JsonSerializer.Serialize(row, QlxJsonDefaults.PreserveNull);
}
