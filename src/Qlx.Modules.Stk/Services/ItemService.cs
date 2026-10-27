/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-26
 * 描述：商品档案服务实现——读穿缓存 + 事务写入 + 审计 + 失效优先（C-5）+ 成本字段裁剪（B-3-1）+
 *       新建/关键属性变更/停用审批挂起与幂等应用（V-1）
 */

using System.Globalization;
using System.Security.Claims;
using System.Text.Json;

using Microsoft.AspNetCore.Http;

using Qlx.Abstractions.Approvals;
using Qlx.Abstractions.Audit;
using Qlx.Abstractions.Constants;
using Qlx.Abstractions.Domain;
using Qlx.Abstractions.Exceptions;
using Qlx.Abstractions.Queries;
using Qlx.Abstractions.Serialization;
using Qlx.Abstractions.Tenancy;
using Qlx.BuildingBlocks.Caching;
using Qlx.BuildingBlocks.Ids;
using Qlx.Modules.Stk.Domain.Item;
using Qlx.Modules.Stk.Domain.Numbering;
using Qlx.Modules.Stk.Dtos;
using Qlx.Modules.Stk.Repositories;
using Qlx.Modules.Stk.Services.Numbering;

namespace Qlx.Modules.Stk.Services;

/// <summary>
/// 商品档案服务。编码由 <see cref="IStkItemCodeGenerator"/> 后端生成（N-1）；
/// 商品新建、税率/单位/批次/有效期变更、停用均先挂起审批请求，审批通过事件到达后在同一事务内应用并登记 AppliedAt。
/// </summary>
/// <param name="tenantCtx">当前租户上下文。</param>
/// <param name="repo">Stk 租户仓储。</param>
/// <param name="codeGenerator">商品编码生成器。</param>
/// <param name="categoryQuery">跨模块商品分类只读查询（分类存在性校验与编码来源）。</param>
/// <param name="stockQuery">跨模块库存只读查询（停用前置校验）。</param>
/// <param name="approvalService">审批中心契约（发起审批）。</param>
/// <param name="cache">统一缓存服务。</param>
/// <param name="audit">审计服务。</param>
/// <param name="snowflake">雪花 Id 生成器（审计事件 Id）。</param>
/// <param name="userContext">当前用户上下文（审计变更人）。</param>
/// <param name="httpContextAccessor">HTTP 上下文访问器（成本字段权限判定）。</param>
internal sealed class ItemService(
    ITenantContext tenantCtx,
    IStkTenantRepository repo,
    IStkItemCodeGenerator codeGenerator,
    IProductCategoryQueryService categoryQuery,
    IStockQueryService stockQuery,
    IApprovalService approvalService,
    ICacheService cache,
    IAuditService audit,
    ISnowflakeIdGenerator snowflake,
    ICurrentUserContext userContext,
    IHttpContextAccessor httpContextAccessor) : IItemService
{
    /// <summary>商品新建审批业务类型。</summary>
    internal const string ItemCreateBizType = "stk.item.create";

    /// <summary>商品关键属性变更审批业务类型。</summary>
    internal const string ItemKeyFieldChangeBizType = "stk.item.keyfield.change";

    /// <summary>商品停用审批业务类型。</summary>
    internal const string ItemDisableBizType = "stk.item.disable";

    /// <summary>Stk 商品审批业务类型统一前缀（事件消费者入口过滤）。</summary>
    internal const string ItemBizTypePrefix = "stk.item.";

    /// <summary>商品新建审批流程定义码。</summary>
    private const string ItemCreateFlowCode = "flow.stk.item.create";

    /// <summary>商品关键属性变更审批流程定义码。</summary>
    private const string ItemKeyFieldChangeFlowCode = "flow.stk.item.keyfield.change";

    /// <summary>商品停用审批流程定义码。</summary>
    private const string ItemDisableFlowCode = "flow.stk.item.disable";

    /// <summary>成本字段查看权限码（字段级安全过滤）。</summary>
    private const string CostFieldPermission = "stk.cost.field";

    /// <summary>价格日志字段：参考成本。</summary>
    internal const int PriceFieldRefCost = 1;

    /// <summary>价格日志字段：指导价。</summary>
    internal const int PriceFieldGuidePrice = 2;

    /// <summary>价格日志字段：税率。</summary>
    internal const int PriceFieldTaxRate = 3;

    /// <summary>价格日志字段：单位。</summary>
    internal const int PriceFieldUnit = 4;

    private const int NameMaxLength = 128;
    private const int SpecMaxLength = 128;
    private const int BarcodeMaxLength = 32;

    private static readonly CacheEntryOptions s_cacheOptions = new()
    {
        AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5),
        NullValueExpiration = TimeSpan.FromSeconds(30),
    };

    private readonly ITenantContext _tenantCtx = tenantCtx;
    private readonly IStkTenantRepository _repo = repo;
    private readonly IStkItemCodeGenerator _codeGenerator = codeGenerator;
    private readonly IProductCategoryQueryService _categoryQuery = categoryQuery;
    private readonly IStockQueryService _stockQuery = stockQuery;
    private readonly IApprovalService _approvalService = approvalService;
    private readonly ICacheService _cache = cache;
    private readonly IAuditService _audit = audit;
    private readonly ISnowflakeIdGenerator _snowflake = snowflake;
    private readonly ICurrentUserContext _userContext = userContext;
    private readonly IHttpContextAccessor _httpContextAccessor = httpContextAccessor;

    private long TenantId => _tenantCtx.TenantId
        ?? throw new ForbiddenException("未识别到租户上下文");

    private string ChangedBy => _userContext.UserId ?? "system";

    /// <summary>模块缓存前缀（含冒号边界，C-1-2）。</summary>
    private string CachePrefix
        => $"{QlxCacheKey.TenantKeyPrefix}:{TenantId.ToString(CultureInfo.InvariantCulture)}:{QlxModuleCodes.Stk}:";

    // ============ 读路径（GetOrCreateAsync / 空值缓存 / TTL 抖动）============

    /// <inheritdoc />
    public async Task<IReadOnlyList<ItemRow>> ListAsync(CancellationToken ct)
    {
        string cacheKey = QlxCacheKey.BuildTenantKey(TenantId, QlxModuleCodes.Stk, "item:all");
        IReadOnlyList<ItemRow> rows = await _cache.GetOrCreateAsync<IReadOnlyList<ItemRow>>(
            cacheKey,
            factory: async token =>
            {
                IReadOnlyList<Item> entities = await _repo.ListItemsAsync(token).ConfigureAwait(false);
                return [.. entities.Select(static item => Map(item, includeCost: true))];
            },
            cancellationToken: ct,
            options: s_cacheOptions).ConfigureAwait(false);

        // 成本字段属字段级安全过滤（B-3-1）：缓存只保存完整原始行（C-7-2），按当前请求权限在出参处裁剪
        return HasCostFieldAccess() ? rows : [.. rows.Select(TrimCost)];
    }

    /// <inheritdoc />
    public async Task<ItemRow> GetAsync(long id, CancellationToken ct)
    {
        string cacheKey = QlxCacheKey.BuildTenantKey(
            TenantId, QlxModuleCodes.Stk, $"item:{id.ToString(CultureInfo.InvariantCulture)}");

        ItemRow? row = await _cache.GetOrCreateAsync<ItemRow?>(
            cacheKey,
            factory: async token =>
            {
                Item? entity = await _repo.FindItemAsync(id, token).ConfigureAwait(false);
                return entity is null ? null : Map(entity, includeCost: true);
            },
            cancellationToken: ct,
            options: s_cacheOptions).ConfigureAwait(false);

        if (row is null)
        {
            throw new NotFoundException("商品", id.ToString(CultureInfo.InvariantCulture));
        }

        return HasCostFieldAccess() ? row : TrimCost(row);
    }

    // ============ 新建：审批挂起（V-1 / 审批通过后落库）============

    /// <inheritdoc />
    public async Task<StkApprovalRequestRow> CreateAsync(CreateItemPayload payload, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(payload);

        CreateItemPayload normalized = ValidatePayload(payload);
        await EnsureCategoryExistsAsync(normalized.CategoryId, ct).ConfigureAwait(false);
        if (normalized.Barcode is not null
            && await _repo.IsBarcodeExistsAsync(normalized.Barcode, excludeId: null, ct).ConfigureAwait(false))
        {
            throw new ConflictException("条码已存在", "STK_BARCODE_EXISTS");
        }

        StkApprovalRequest request = new(
            TenantId,
            ItemCreateBizType,
            JsonSerializer.Serialize(normalized, QlxJsonDefaults.PreserveNull));
        await _repo.AddApprovalRequestAsync(request, ct).ConfigureAwait(false);
        _ = await _repo.SaveChangesAsync(ct).ConfigureAwait(false);

        string instanceId = await _approvalService.StartApprovalAsync(
            new StartApprovalRequest(
                ApprovalDefinitionCode: ItemCreateFlowCode,
                BizType: ItemCreateBizType,
                BizId: request.Id.ToString(CultureInfo.InvariantCulture),
                BizNo: "-",
                InitiatorUserId: ChangedBy,
                Title: $"商品新建申请：{normalized.Name}",
                Description: null,
                BizData: new Dictionary<string, object?>
                {
                    ["categoryId"] = normalized.CategoryId,
                }),
            ct).ConfigureAwait(false);

        request.BindInstance(instanceId);
        _ = await _repo.SaveChangesAsync(ct).ConfigureAwait(false);
        return Map(request);
    }

    // ============ 更新：关键属性变化转审批挂起，否则直接生效 ============

    /// <inheritdoc />
    public async Task<UpdateItemResult> UpdateAsync(long id, UpdateItemPayload payload, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(payload);

        Item item = await FindItemOrThrowAsync(id, ct).ConfigureAwait(false);
        UpdateItemPayload normalized = ValidatePayload(payload);
        await EnsureCategoryExistsAsync(normalized.CategoryId, ct).ConfigureAwait(false);
        if (normalized.Barcode is not null
            && await _repo.IsBarcodeExistsAsync(normalized.Barcode, excludeId: id, ct).ConfigureAwait(false))
        {
            throw new ConflictException("条码已存在", "STK_BARCODE_EXISTS");
        }

        bool keyFieldChanged = item.TaxRate != normalized.TaxRate
            || item.UnitId != normalized.UnitId
            || item.BatchEnabled != normalized.BatchEnabled
            || item.ExpiryEnabled != normalized.ExpiryEnabled;

        if (keyFieldChanged)
        {
            StkApprovalRequest request = new(
                TenantId,
                ItemKeyFieldChangeBizType,
                JsonSerializer.Serialize(
                    new ItemKeyFieldChangePayload(id, normalized), QlxJsonDefaults.PreserveNull));
            await _repo.AddApprovalRequestAsync(request, ct).ConfigureAwait(false);
            _ = await _repo.SaveChangesAsync(ct).ConfigureAwait(false);

            string instanceId = await _approvalService.StartApprovalAsync(
                new StartApprovalRequest(
                    ApprovalDefinitionCode: ItemKeyFieldChangeFlowCode,
                    BizType: ItemKeyFieldChangeBizType,
                    BizId: request.Id.ToString(CultureInfo.InvariantCulture),
                    BizNo: item.ItemCode,
                    InitiatorUserId: ChangedBy,
                    Title: $"商品关键属性变更申请：{item.Name}",
                    Description: null,
                    BizData: new Dictionary<string, object?>
                    {
                        ["itemId"] = id,
                    }),
                ct).ConfigureAwait(false);

            request.BindInstance(instanceId);
            _ = await _repo.SaveChangesAsync(ct).ConfigureAwait(false);
            return new UpdateItemResult(PendingApproval: true, Row: null, Request: Map(request));
        }

        bool includeCost = HasCostFieldAccess();
        (ItemRow Row, ItemRow Audit, string OldJson) result = await _repo.ExecuteInTransactionAsync(
            async token =>
            {
                Item tracked = await FindItemOrThrowAsync(id, token).ConfigureAwait(false);
                string oldJson = Serialize(Map(tracked, includeCost: true));
                decimal? oldRefCost = tracked.RefCostPrice;
                decimal? oldGuidePrice = tracked.GuidePrice;

                tracked.UpdateProfile(
                    normalized.Name, normalized.Spec, normalized.Barcode, normalized.CategoryId,
                    normalized.RefCostPrice, normalized.GuidePrice);
                await _repo.UpdateItemAsync(tracked, token).ConfigureAwait(false);

                if (oldRefCost != normalized.RefCostPrice)
                {
                    await AppendPriceLogAsync(
                        tracked.Id, PriceFieldRefCost, Format(oldRefCost), Format(normalized.RefCostPrice),
                        ChangedBy, token).ConfigureAwait(false);
                }

                if (oldGuidePrice != normalized.GuidePrice)
                {
                    await AppendPriceLogAsync(
                        tracked.Id, PriceFieldGuidePrice, Format(oldGuidePrice), Format(normalized.GuidePrice),
                        ChangedBy, token).ConfigureAwait(false);
                }

                _ = await _repo.SaveChangesAsync(token).ConfigureAwait(false);
                return (Map(tracked, includeCost), Map(tracked, includeCost: true), oldJson);
            },
            ct).ConfigureAwait(false);

        await CaptureAuditAsync("UPDATE", result.Audit, result.OldJson, ct).ConfigureAwait(false);
        await InvalidateCacheAsync(ct).ConfigureAwait(false);
        return new UpdateItemResult(PendingApproval: false, Row: result.Row, Request: null);
    }

    // ============ 启停（停用前置：有库存拒绝直接停用）============

    /// <inheritdoc />
    public async Task<ItemRow> ToggleAsync(long id, CancellationToken ct)
    {
        bool includeCost = HasCostFieldAccess();
        (ItemRow Row, ItemRow Audit, string OldJson) result = await _repo.ExecuteInTransactionAsync(
            async token =>
            {
                Item item = await FindItemOrThrowAsync(id, token).ConfigureAwait(false);
                string oldJson = Serialize(Map(item, includeCost: true));

                if (item.Status == ItemStatus.Enabled)
                {
                    // 停用前置（V-1-2）：仍存在非零库存时禁止直接停用，须提交停用审批
                    IReadOnlyList<StockBalanceRow> balances = await _stockQuery
                        .ListBalancesByItemAsync(id, token).ConfigureAwait(false);
                    if (balances.Any(static balance => balance.QtyOnhand is > 0))
                    {
                        throw new ConflictException("商品存在库存，请提交停用审批", "STK_ITEM_HAS_STOCK");
                    }

                    item.Disable();
                }
                else
                {
                    item.Enable();
                }

                await _repo.UpdateItemAsync(item, token).ConfigureAwait(false);
                _ = await _repo.SaveChangesAsync(token).ConfigureAwait(false);
                return (Map(item, includeCost), Map(item, includeCost: true), oldJson);
            },
            ct).ConfigureAwait(false);

        await CaptureAuditAsync("UPDATE", result.Audit, result.OldJson, ct).ConfigureAwait(false);
        await InvalidateCacheAsync(ct).ConfigureAwait(false);
        return result.Row;
    }

    // ============ 停用审批挂起 ============

    /// <inheritdoc />
    public async Task<StkApprovalRequestRow> SubmitDisableRequestAsync(long id, CancellationToken ct)
    {
        Item item = await FindItemOrThrowAsync(id, ct).ConfigureAwait(false);
        if (item.Status == ItemStatus.Disabled)
        {
            throw new ValidationException("status", "商品已停用，无需重复申请");
        }

        StkApprovalRequest request = new(
            TenantId,
            ItemDisableBizType,
            JsonSerializer.Serialize(new ItemDisablePayload(id), QlxJsonDefaults.PreserveNull));
        await _repo.AddApprovalRequestAsync(request, ct).ConfigureAwait(false);
        _ = await _repo.SaveChangesAsync(ct).ConfigureAwait(false);

        string instanceId = await _approvalService.StartApprovalAsync(
            new StartApprovalRequest(
                ApprovalDefinitionCode: ItemDisableFlowCode,
                BizType: ItemDisableBizType,
                BizId: request.Id.ToString(CultureInfo.InvariantCulture),
                BizNo: item.ItemCode,
                InitiatorUserId: ChangedBy,
                Title: $"商品停用申请：{item.Name}",
                Description: null,
                BizData: new Dictionary<string, object?>
                {
                    ["itemId"] = id,
                }),
            ct).ConfigureAwait(false);

        request.BindInstance(instanceId);
        _ = await _repo.SaveChangesAsync(ct).ConfigureAwait(false);
        return Map(request);
    }

    // ============ 价格日志 ============

    /// <inheritdoc />
    public async Task<IReadOnlyList<ItemPriceLogRow>> ListPriceLogsAsync(long id, CancellationToken ct)
    {
        _ = await FindItemOrThrowAsync(id, ct).ConfigureAwait(false);
        IReadOnlyList<ItemPriceLog> logs = await _repo.ListPriceLogsAsync(id, ct).ConfigureAwait(false);
        return [.. logs.Select(Map)];
    }

    // ============ 审批完成事件应用（幂等）============

    /// <inheritdoc />
    public async Task ApplyAsync(ApprovalCompletedEvent completed, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(completed);
        if (string.IsNullOrWhiteSpace(completed.BizType)
            || !completed.BizType.StartsWith(ItemBizTypePrefix, StringComparison.Ordinal))
        {
            return;
        }

        StkApprovalRequest? request = await _repo
            .FindApprovalRequestByInstanceAsync(completed.ApprovalInstanceId, ct).ConfigureAwait(false);
        if (request is null || !request.IsPending)
        {
            // 幂等：同一 ApprovalInstanceId 重复事件 / 非待审批请求直接返回
            return;
        }

        if (completed.FinalStatus != ApprovalStatus.Approved)
        {
            request.MarkRejected();
            _ = await _repo.SaveChangesAsync(ct).ConfigureAwait(false);
            return;
        }

        string approvedBy = string.IsNullOrWhiteSpace(completed.InitiatorUserId)
            ? ChangedBy
            : completed.InitiatorUserId;

        (ItemRow Audit, string Operation, string? OldJson) applied = await _repo.ExecuteInTransactionAsync(
            async token =>
            {
                (ItemRow Audit, string Operation, string? OldJson) outcome = await ApplyApprovedPayloadAsync(
                    request, approvedBy, token).ConfigureAwait(false);
                request.MarkApplied(DateTimeOffset.UtcNow);
                _ = await _repo.SaveChangesAsync(token).ConfigureAwait(false);
                return outcome;
            },
            ct).ConfigureAwait(false);

        await CaptureAuditAsync(applied.Operation, applied.Audit, applied.OldJson, ct).ConfigureAwait(false);
        await InvalidateCacheAsync(ct).ConfigureAwait(false);
    }

    // ============ 内部实现 ============

    private async Task<(ItemRow Audit, string Operation, string? OldJson)> ApplyApprovedPayloadAsync(
        StkApprovalRequest request, string approvedBy, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.PayloadJson))
        {
            throw new InvalidOperationException($"审批请求载荷为空：requestId={request.Id}。");
        }

        if (string.Equals(request.BizType, ItemCreateBizType, StringComparison.Ordinal))
        {
            CreateItemPayload payload = Deserialize<CreateItemPayload>(request.PayloadJson, request.Id);
            CreateItemPayload normalized = ValidatePayload(payload);
            await EnsureCategoryExistsAsync(normalized.CategoryId, ct).ConfigureAwait(false);
            if (normalized.Barcode is not null
                && await _repo.IsBarcodeExistsAsync(normalized.Barcode, excludeId: null, ct).ConfigureAwait(false))
            {
                throw new ConflictException("条码已存在", "STK_BARCODE_EXISTS");
            }

            string itemCode = await _codeGenerator.NextAsync(normalized.CategoryId, ct).ConfigureAwait(false);
            Item item = new(
                TenantId,
                itemCode,
                normalized.Name,
                normalized.Spec,
                normalized.Barcode,
                normalized.CategoryId,
                normalized.UnitId,
                normalized.BatchEnabled,
                normalized.ExpiryEnabled,
                normalized.TaxRate,
                normalized.RefCostPrice,
                normalized.GuidePrice);
            await _repo.AddItemAsync(item, ct).ConfigureAwait(false);
            _ = await _repo.SaveChangesAsync(ct).ConfigureAwait(false);
            return (Map(item, includeCost: true), "INSERT", null);
        }

        if (string.Equals(request.BizType, ItemKeyFieldChangeBizType, StringComparison.Ordinal))
        {
            ItemKeyFieldChangePayload change = Deserialize<ItemKeyFieldChangePayload>(
                request.PayloadJson, request.Id);
            UpdateItemPayload normalized = ValidatePayload(change.Payload);
            Item item = await FindItemOrThrowAsync(change.ItemId, ct).ConfigureAwait(false);
            await EnsureCategoryExistsAsync(normalized.CategoryId, ct).ConfigureAwait(false);
            if (normalized.Barcode is not null
                && await _repo.IsBarcodeExistsAsync(normalized.Barcode, excludeId: item.Id, ct).ConfigureAwait(false))
            {
                throw new ConflictException("条码已存在", "STK_BARCODE_EXISTS");
            }

            string oldJson = Serialize(Map(item, includeCost: true));
            decimal oldTaxRate = item.TaxRate;
            long oldUnitId = item.UnitId;
            decimal? oldRefCost = item.RefCostPrice;
            decimal? oldGuidePrice = item.GuidePrice;

            item.SetKeyAttributes(
                normalized.UnitId, normalized.BatchEnabled, normalized.ExpiryEnabled, normalized.TaxRate);
            item.UpdateProfile(
                normalized.Name, normalized.Spec, normalized.Barcode, normalized.CategoryId,
                normalized.RefCostPrice, normalized.GuidePrice);
            await _repo.UpdateItemAsync(item, ct).ConfigureAwait(false);

            if (oldRefCost != normalized.RefCostPrice)
            {
                await AppendPriceLogAsync(
                    item.Id, PriceFieldRefCost, Format(oldRefCost), Format(normalized.RefCostPrice),
                    approvedBy, ct).ConfigureAwait(false);
            }

            if (oldGuidePrice != normalized.GuidePrice)
            {
                await AppendPriceLogAsync(
                    item.Id, PriceFieldGuidePrice, Format(oldGuidePrice), Format(normalized.GuidePrice),
                    approvedBy, ct).ConfigureAwait(false);
            }

            if (oldTaxRate != normalized.TaxRate)
            {
                await AppendPriceLogAsync(
                    item.Id, PriceFieldTaxRate, Format(oldTaxRate), Format(normalized.TaxRate),
                    approvedBy, ct).ConfigureAwait(false);
            }

            if (oldUnitId != normalized.UnitId)
            {
                await AppendPriceLogAsync(
                    item.Id, PriceFieldUnit,
                    oldUnitId.ToString(CultureInfo.InvariantCulture),
                    normalized.UnitId.ToString(CultureInfo.InvariantCulture),
                    approvedBy, ct).ConfigureAwait(false);
            }

            return (Map(item, includeCost: true), "UPDATE", oldJson);
        }

        if (string.Equals(request.BizType, ItemDisableBizType, StringComparison.Ordinal))
        {
            ItemDisablePayload payload = Deserialize<ItemDisablePayload>(request.PayloadJson, request.Id);
            Item item = await FindItemOrThrowAsync(payload.ItemId, ct).ConfigureAwait(false);
            string oldJson = Serialize(Map(item, includeCost: true));
            if (item.Status == ItemStatus.Enabled)
            {
                item.Disable();
                await _repo.UpdateItemAsync(item, ct).ConfigureAwait(false);
            }

            return (Map(item, includeCost: true), "UPDATE", oldJson);
        }

        throw new InvalidOperationException($"未支持的审批业务类型：{request.BizType}。");
    }

    private async Task<Item> FindItemOrThrowAsync(long id, CancellationToken ct)
        => await _repo.FindItemAsync(id, ct).ConfigureAwait(false)
            ?? throw new NotFoundException("商品", id.ToString(CultureInfo.InvariantCulture));

    private async Task EnsureCategoryExistsAsync(long categoryId, CancellationToken ct)
    {
        if (categoryId <= 0)
        {
            throw new ValidationException("categoryId", "商品分类不能为空");
        }

        _ = await _categoryQuery.FindAsync(categoryId, ct).ConfigureAwait(false)
            ?? throw new ValidationException("categoryId", "商品分类不存在");
    }

    private Task AppendPriceLogAsync(
        long itemId, int field, string? oldValue, string? newValue, string approvedBy, CancellationToken ct)
        => _repo.AddPriceLogAsync(
            new ItemPriceLog(TenantId, itemId, field, oldValue, newValue, approvedBy), ct);

    private static CreateItemPayload ValidatePayload(CreateItemPayload payload)
        => payload with
        {
            Name = RequireName(payload.Name),
            Spec = NormalizeOptional(payload.Spec, SpecMaxLength, "spec"),
            Barcode = NormalizeOptional(payload.Barcode, BarcodeMaxLength, "barcode"),
            TaxRate = ValidateTaxRate(payload.TaxRate),
            RefCostPrice = ValidateNonNegativePrice(payload.RefCostPrice, "refCostPrice"),
            GuidePrice = ValidateNonNegativePrice(payload.GuidePrice, "guidePrice"),
        };

    private static UpdateItemPayload ValidatePayload(UpdateItemPayload payload)
        => payload with
        {
            Name = RequireName(payload.Name),
            Spec = NormalizeOptional(payload.Spec, SpecMaxLength, "spec"),
            Barcode = NormalizeOptional(payload.Barcode, BarcodeMaxLength, "barcode"),
            TaxRate = ValidateTaxRate(payload.TaxRate),
            RefCostPrice = ValidateNonNegativePrice(payload.RefCostPrice, "refCostPrice"),
            GuidePrice = ValidateNonNegativePrice(payload.GuidePrice, "guidePrice"),
        };

    private static string RequireName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ValidationException("name", "商品名称不能为空");
        }

        string trimmed = name.Trim();
        if (trimmed.Length > NameMaxLength)
        {
            throw new ValidationException("name", $"商品名称长度不能超过 {NameMaxLength}");
        }

        return trimmed;
    }

    private static string? NormalizeOptional(string? value, int maxLength, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        string trimmed = value.Trim();
        if (trimmed.Length > maxLength)
        {
            throw new ValidationException(fieldName, $"{fieldName} 长度不能超过 {maxLength}");
        }

        return trimmed;
    }

    private static decimal ValidateTaxRate(decimal taxRate)
    {
        if (taxRate is < 0m or > 1m)
        {
            throw new ValidationException("taxRate", "税率必须在 0~1 之间");
        }

        return taxRate;
    }

    private static decimal? ValidateNonNegativePrice(decimal? price, string fieldName)
    {
        if (price is < 0m)
        {
            throw new ValidationException(fieldName, "价格不能为负数");
        }

        return price;
    }

    private static T Deserialize<T>(string payloadJson, long requestId)
        where T : class
    {
        T? payload = JsonSerializer.Deserialize<T>(payloadJson, QlxJsonDefaults.PreserveNull);
        return payload ?? throw new InvalidOperationException($"审批请求载荷反序列化为空：requestId={requestId}。");
    }

    private static string? Format(decimal? value)
        => value?.ToString(CultureInfo.InvariantCulture);

    private static string Format(decimal value)
        => value.ToString(CultureInfo.InvariantCulture);

    private bool HasCostFieldAccess()
    {
        HttpContext? context = _httpContextAccessor.HttpContext;
        ClaimsPrincipal? user = context?.User;
        if (user?.Identity?.IsAuthenticated != true)
        {
            return false;
        }

        string? role = user.FindFirst(QlxClaimConstants.MemberRole)?.Value;
        if (string.Equals(role, QlxMemberRoleValues.TenantAdmin, StringComparison.Ordinal))
        {
            return true;
        }

        string? permissions = user.FindFirst(QlxClaimConstants.Permissions)?.Value;
        if (string.IsNullOrWhiteSpace(permissions))
        {
            return false;
        }

        return permissions
            .Split([';', ',', ' '], StringSplitOptions.RemoveEmptyEntries)
            .Any(permission => string.Equals(
                permission.Trim(), CostFieldPermission, StringComparison.Ordinal));
    }

    private Task CaptureAuditAsync(string operation, ItemRow row, string? oldJson, CancellationToken ct)
        => _audit.CaptureChangeAsync(
            new AuditLogEntry(
                _snowflake.NextId(),
                nameof(Item),
                row.Id,
                operation,
                oldJson,
                Serialize(row),
                ChangedBy,
                DateTimeOffset.UtcNow,
                TenantId),
            ct);

    private Task<long> InvalidateCacheAsync(CancellationToken ct)
        => _cache.RemoveByPrefixAsync(CachePrefix, ct);

    private static string Serialize<T>(T value)
        => JsonSerializer.Serialize(value, QlxJsonDefaults.PreserveNull);

    private static ItemRow Map(Item entity, bool includeCost)
        => new(
            entity.Id,
            entity.ItemCode,
            entity.Name,
            entity.Spec,
            entity.Barcode,
            entity.CategoryId,
            entity.UnitId,
            entity.BatchEnabled,
            entity.ExpiryEnabled,
            entity.TaxRate,
            includeCost ? entity.RefCostPrice : null,
            includeCost ? entity.GuidePrice : null,
            (int)entity.Status);

    private static ItemRow TrimCost(ItemRow row)
        => row with { RefCostPrice = null, GuidePrice = null };

    private static ItemPriceLogRow Map(ItemPriceLog entity)
        => new(
            entity.Id,
            entity.ItemId,
            entity.Field,
            entity.OldValue,
            entity.NewValue,
            entity.ApprovedBy,
            entity.CreatedAt);

    private static StkApprovalRequestRow Map(StkApprovalRequest request)
        => new(
            request.Id,
            request.ApprovalInstanceId,
            request.BizType,
            request.BizId,
            (int)request.Status);
}
