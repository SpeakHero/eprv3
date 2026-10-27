/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-26 10:00:00 +08:00 中国标准时间 星期一
 * 描述：仓库/库区/库位主数据服务实现——编码后端生成（N-1）+ 读穿缓存 + 事务写入 + 审计 +
 *       失效优先（C-5）+ 停用前置校验（有库存/有未完成作业拒绝，通过后转审批挂起）
 */

using System.Globalization;
using System.Text.Json;

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
using Qlx.BuildingBlocks.Numbering;
using Qlx.Modules.Wms.Domain.Numbering;
using Qlx.Modules.Wms.Domain.Warehouse;
using Qlx.Modules.Wms.Dtos;
using Qlx.Modules.Wms.Repositories;

namespace Qlx.Modules.Wms.Services;

/// <summary>
/// 仓库主数据服务。仓库编码经 <see cref="IDocumentNumberGenerator"/>（<c>WH-</c> 前缀）生成，
/// 前端不传码（N-1）；停用前置校验经 <see cref="IStockQueryService"/> 判断仓库是否仍有库存。
/// </summary>
/// <param name="tenantCtx">当前租户上下文。</param>
/// <param name="repo">Wms 租户仓储。</param>
/// <param name="numberGenerator">单据号生成器（仓库编码）。</param>
/// <param name="orgQuery">跨模块组织只读查询（解析组织编码参与编码）。</param>
/// <param name="approvalService">审批中心契约（仓库停用申请）。</param>
/// <param name="stockQuery">跨模块库存只读查询（停用前置校验）。</param>
/// <param name="cache">统一缓存服务。</param>
/// <param name="audit">审计服务。</param>
/// <param name="snowflake">雪花 Id 生成器（审计事件 Id）。</param>
/// <param name="userContext">当前用户上下文（审计变更人）。</param>
internal sealed class WarehouseService(
    ITenantContext tenantCtx,
    IWmsTenantRepository repo,
    IDocumentNumberGenerator numberGenerator,
    IOrgQueryService orgQuery,
    IApprovalService approvalService,
    IStockQueryService stockQuery,
    ICacheService cache,
    IAuditService audit,
    ISnowflakeIdGenerator snowflake,
    ICurrentUserContext userContext) : IWarehouseService
{
    /// <summary>仓库编码前缀。</summary>
    internal const string WarehouseCodePrefix = "WH";

    /// <summary>仓库停用审批业务类型。</summary>
    internal const string WarehouseDisableBizType = "wms.warehouse.disable";

    /// <summary>仓库停用审批流程定义码。</summary>
    internal const string WarehouseDisableFlowCode = "flow.wms.warehouse.disable";

    private const int NameMaxLength = 64;
    private const int CodeMaxLength = 32;
    private const int BarcodeMaxLength = 64;
    private const int TextMaxLength = 256;

    private static readonly CacheEntryOptions s_cacheOptions = new()
    {
        AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5),
        NullValueExpiration = TimeSpan.FromSeconds(30),
    };

    private long TenantId => tenantCtx.TenantId
        ?? throw new ForbiddenException("未识别到租户上下文");

    private string ChangedBy => userContext.UserId ?? "system";

    /// <summary>模块缓存前缀（含冒号边界，C-1-2）。</summary>
    private string CachePrefix
        => $"{QlxCacheKey.TenantKeyPrefix}:{TenantId.ToString(CultureInfo.InvariantCulture)}:{QlxModuleCodes.Wms}:";

    // ============ 仓库读路径 ============

    /// <inheritdoc />
    public async Task<IReadOnlyList<WarehouseRow>> ListAsync(CancellationToken ct)
    {
        string cacheKey = QlxCacheKey.BuildTenantKey(TenantId, QlxModuleCodes.Wms, "warehouse:all");
        return await cache.GetOrCreateAsync<IReadOnlyList<WarehouseRow>>(
            cacheKey,
            factory: async token =>
            {
                IReadOnlyList<Warehouse> rows = await repo.ListWarehousesAsync(token).ConfigureAwait(false);
                return [.. rows.Select(Map)];
            },
            cancellationToken: ct,
            options: s_cacheOptions).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<WarehouseRow> GetAsync(long id, CancellationToken ct)
    {
        string cacheKey = QlxCacheKey.BuildTenantKey(
            TenantId, QlxModuleCodes.Wms, $"warehouse:{id.ToString(CultureInfo.InvariantCulture)}");

        WarehouseRow? row = await cache.GetOrCreateAsync<WarehouseRow?>(
            cacheKey,
            factory: async token =>
            {
                Warehouse? entity = await repo.FindWarehouseAsync(id, token).ConfigureAwait(false);
                return entity is null ? null : Map(entity);
            },
            cancellationToken: ct,
            options: s_cacheOptions).ConfigureAwait(false);

        return row ?? throw new NotFoundException("仓库", id.ToString(CultureInfo.InvariantCulture));
    }

    // ============ 仓库写路径（事务 → 审计 → 失效）============

    /// <inheritdoc />
    public async Task<WarehouseRow> CreateAsync(CreateWarehousePayload payload, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(payload);

        WarehouseRow row = await repo.ExecuteInTransactionAsync(async token =>
        {
            string name = RequireName(payload.Name);
            ValidateWhType(payload.WhType);

            string orgCode = await ResolveOrgCodeAsync(payload.OrgId, token).ConfigureAwait(false);
            string code = await numberGenerator
                .NextAsync(WarehouseCodePrefix, orgCode, token).ConfigureAwait(false);

            Warehouse entity = new(
                TenantId, code, name, payload.WhType, payload.OrgId,
                NormalizeOptional(payload.Address, TextMaxLength, "address"), payload.KeeperId,
                NormalizeOptional(payload.Remark, TextMaxLength, "remark"));
            await repo.AddWarehouseAsync(entity, token).ConfigureAwait(false);
            _ = await repo.SaveChangesAsync(token).ConfigureAwait(false);
            return Map(entity);
        }, ct).ConfigureAwait(false);

        await CaptureAuditAsync("INSERT", row, oldJson: null, ct).ConfigureAwait(false);
        await InvalidateCacheAsync(ct).ConfigureAwait(false);
        return row;
    }

    /// <inheritdoc />
    public async Task<WarehouseRow> UpdateAsync(long id, UpdateWarehousePayload payload, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(payload);

        (WarehouseRow Row, string OldJson) result = await repo.ExecuteInTransactionAsync(async token =>
        {
            Warehouse entity = await FindWarehouseOrThrowAsync(id, token).ConfigureAwait(false);
            string oldJson = Serialize(Map(entity));

            string name = RequireName(payload.Name);
            ValidateWhType(payload.WhType);
            entity.UpdateProfile(
                name, payload.WhType, payload.OrgId,
                NormalizeOptional(payload.Address, TextMaxLength, "address"),
                payload.KeeperId,
                NormalizeOptional(payload.Remark, TextMaxLength, "remark"));

            await repo.UpdateWarehouseAsync(entity, token).ConfigureAwait(false);
            _ = await repo.SaveChangesAsync(token).ConfigureAwait(false);
            return (Map(entity), oldJson);
        }, ct).ConfigureAwait(false);

        await CaptureAuditAsync("UPDATE", result.Row, result.OldJson, ct).ConfigureAwait(false);
        await InvalidateCacheAsync(ct).ConfigureAwait(false);
        return result.Row;
    }

    /// <inheritdoc />
    public async Task<WarehouseRow> ToggleAsync(long id, CancellationToken ct)
    {
        (WarehouseRow Row, string OldJson, bool Changed) result = await repo.ExecuteInTransactionAsync(
            async token =>
            {
                Warehouse entity = await FindWarehouseOrThrowAsync(id, token).ConfigureAwait(false);

                if (entity.Status != WarehouseStatus.Enabled)
                {
                    string enableOldJson = Serialize(Map(entity));
                    entity.Enable();
                    await repo.UpdateWarehouseAsync(entity, token).ConfigureAwait(false);
                    _ = await repo.SaveChangesAsync(token).ConfigureAwait(false);
                    return (Map(entity), enableOldJson, true);
                }

                // 停用前置（V-1-2）：仓库仍有库存或未完成作业时拒绝，必须先在 2B 作业域清空
                if (await stockQuery.HasStockInWarehouseAsync(id, token).ConfigureAwait(false))
                {
                    throw new ConflictException("仓库存在库存，不能停用", "WMS_WAREHOUSE_HAS_STOCK");
                }

                if (await repo.HasUnfinishedOperationsAsync(id, token).ConfigureAwait(false))
                {
                    throw new ConflictException("仓库存在未完成作业单，不能停用", "WMS_WAREHOUSE_HAS_OPEN_TASKS");
                }

                WmsApprovalRequest? pending = await repo
                    .FindPendingApprovalRequestAsync(WarehouseDisableBizType, id, token).ConfigureAwait(false);
                if (pending is not null)
                {
                    throw new ConflictException("仓库停用审批中，请勿重复提交", "WMS_APPROVAL_PENDING");
                }

                // 停用属关键状态变更：先序列化期望变更挂起，审批通过事件到达后由 WmsApprovalApplier 落地
                WmsApprovalRequest request = new(
                    TenantId, WarehouseDisableBizType, entity.Id,
                    JsonSerializer.Serialize(entity.Id, QlxJsonDefaults.PreserveNull));
                await repo.AddApprovalRequestAsync(request, token).ConfigureAwait(false);
                _ = await repo.SaveChangesAsync(token).ConfigureAwait(false);

                string instanceId = await approvalService.StartApprovalAsync(
                    new StartApprovalRequest(
                        ApprovalDefinitionCode: WarehouseDisableFlowCode,
                        BizType: WarehouseDisableBizType,
                        BizId: request.Id.ToString(CultureInfo.InvariantCulture),
                        BizNo: entity.Code,
                        InitiatorUserId: ChangedBy,
                        Title: $"仓库停用申请：{entity.Name}",
                        Description: null,
                        BizData: new Dictionary<string, object?>
                        {
                            ["warehouseId"] = entity.Id,
                        }),
                    token).ConfigureAwait(false);

                request.BindInstance(instanceId);
                _ = await repo.SaveChangesAsync(token).ConfigureAwait(false);

                // 审批未通过前状态仍为启用，仅回传「已挂起」标记供前端提示
                return (Map(entity) with { ApprovalRequired = true }, string.Empty, false);
            }, ct).ConfigureAwait(false);

        if (result.Changed)
        {
            await CaptureAuditAsync("UPDATE", result.Row, result.OldJson, ct).ConfigureAwait(false);
            await InvalidateCacheAsync(ct).ConfigureAwait(false);
        }

        return result.Row;
    }

    /// <inheritdoc />
    public async Task ApplyDisableApprovalAsync(long id, bool approved, CancellationToken ct)
    {
        if (!approved)
        {
            return;
        }

        (WarehouseRow Row, string OldJson, bool Disabled) result = await repo.ExecuteInTransactionAsync(
            async token =>
            {
                Warehouse entity = await FindWarehouseOrThrowAsync(id, token).ConfigureAwait(false);

                // 幂等：已停用不再重复处理
                if (entity.Status != WarehouseStatus.Enabled)
                {
                    return (Map(entity), string.Empty, false);
                }

                string oldJson = Serialize(Map(entity));
                entity.Disable();
                await repo.UpdateWarehouseAsync(entity, token).ConfigureAwait(false);
                _ = await repo.SaveChangesAsync(token).ConfigureAwait(false);
                return (Map(entity), oldJson, true);
            }, ct).ConfigureAwait(false);

        if (result.Disabled)
        {
            await CaptureAuditAsync("UPDATE", result.Row, result.OldJson, ct).ConfigureAwait(false);
            await InvalidateCacheAsync(ct).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async Task DeleteAsync(long id, CancellationToken ct)
    {
        (WarehouseRow Row, string OldJson) result = await repo.ExecuteInTransactionAsync(async token =>
        {
            Warehouse entity = await FindWarehouseOrThrowAsync(id, token).ConfigureAwait(false);
            if (await repo.CountZonesAsync(id, token).ConfigureAwait(false) > 0
                || await repo.CountLocationsAsync(id, token).ConfigureAwait(false) > 0)
            {
                throw new ConflictException("仓库存在库区或库位，不能删除", "WMS_WAREHOUSE_HAS_CHILDREN");
            }

            string oldJson = Serialize(Map(entity));
            entity.SoftDelete();
            await repo.UpdateWarehouseAsync(entity, token).ConfigureAwait(false);
            _ = await repo.SaveChangesAsync(token).ConfigureAwait(false);
            return (Map(entity), oldJson);
        }, ct).ConfigureAwait(false);

        await CaptureAuditAsync("SOFT_DELETE", result.Row, result.OldJson, ct).ConfigureAwait(false);
        await InvalidateCacheAsync(ct).ConfigureAwait(false);
    }

    // ============ 库区 ============

    /// <inheritdoc />
    public async Task<IReadOnlyList<ZoneRow>> ListZonesAsync(long warehouseId, CancellationToken ct)
    {
        _ = await FindWarehouseOrThrowAsync(warehouseId, ct).ConfigureAwait(false);
        IReadOnlyList<WarehouseZone> zones = await repo.ListZonesAsync(warehouseId, ct).ConfigureAwait(false);
        return [.. zones.Select(Map)];
    }

    /// <inheritdoc />
    public async Task<ZoneRow> CreateZoneAsync(
        long warehouseId, CreateZonePayload payload, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(payload);

        ZoneRow row = await repo.ExecuteInTransactionAsync(async token =>
        {
            _ = await FindWarehouseOrThrowAsync(warehouseId, token).ConfigureAwait(false);
            string code = RequireCode(payload.Code, "库区编码");
            if (await repo.IsZoneCodeExistsAsync(warehouseId, code, null, token).ConfigureAwait(false))
            {
                throw new ConflictException("库区编码已存在", "WMS_ZONE_CODE_EXISTS");
            }

            WarehouseZone zone = new(
                TenantId, warehouseId, code, RequireName(payload.Name), ValidateSortOrder(payload.SortOrder));
            await repo.AddZoneAsync(zone, token).ConfigureAwait(false);
            _ = await repo.SaveChangesAsync(token).ConfigureAwait(false);
            return Map(zone);
        }, ct).ConfigureAwait(false);

        await CaptureZoneAuditAsync("INSERT", row, oldJson: null, ct).ConfigureAwait(false);
        await InvalidateCacheAsync(ct).ConfigureAwait(false);
        return row;
    }

    /// <inheritdoc />
    public async Task<ZoneRow> UpdateZoneAsync(
        long zoneId, UpdateZonePayload payload, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(payload);

        (ZoneRow Row, string OldJson) result = await repo.ExecuteInTransactionAsync(async token =>
        {
            WarehouseZone zone = await FindZoneOrThrowAsync(zoneId, token).ConfigureAwait(false);
            string oldJson = Serialize(Map(zone));
            zone.UpdateProfile(RequireName(payload.Name), ValidateSortOrder(payload.SortOrder));
            await repo.UpdateZoneAsync(zone, token).ConfigureAwait(false);
            _ = await repo.SaveChangesAsync(token).ConfigureAwait(false);
            return (Map(zone), oldJson);
        }, ct).ConfigureAwait(false);

        await CaptureZoneAuditAsync("UPDATE", result.Row, result.OldJson, ct).ConfigureAwait(false);
        await InvalidateCacheAsync(ct).ConfigureAwait(false);
        return result.Row;
    }

    /// <inheritdoc />
    public async Task<ZoneRow> ToggleZoneAsync(long zoneId, CancellationToken ct)
    {
        (ZoneRow Row, string OldJson) result = await repo.ExecuteInTransactionAsync(async token =>
        {
            WarehouseZone zone = await FindZoneOrThrowAsync(zoneId, token).ConfigureAwait(false);
            string oldJson = Serialize(Map(zone));
            if (zone.Status == WarehouseStatus.Enabled)
            {
                zone.Disable();
            }
            else
            {
                zone.Enable();
            }

            await repo.UpdateZoneAsync(zone, token).ConfigureAwait(false);
            _ = await repo.SaveChangesAsync(token).ConfigureAwait(false);
            return (Map(zone), oldJson);
        }, ct).ConfigureAwait(false);

        await CaptureZoneAuditAsync("UPDATE", result.Row, result.OldJson, ct).ConfigureAwait(false);
        await InvalidateCacheAsync(ct).ConfigureAwait(false);
        return result.Row;
    }

    /// <inheritdoc />
    public async Task DeleteZoneAsync(long zoneId, CancellationToken ct)
    {
        (ZoneRow Row, string OldJson) result = await repo.ExecuteInTransactionAsync(async token =>
        {
            WarehouseZone zone = await FindZoneOrThrowAsync(zoneId, token).ConfigureAwait(false);
            if (await repo.CountLocationsUnderZoneAsync(zoneId, token).ConfigureAwait(false) > 0)
            {
                throw new ConflictException("库区下存在库位，不能删除", "WMS_ZONE_HAS_LOCATIONS");
            }

            string oldJson = Serialize(Map(zone));
            zone.SoftDelete();
            await repo.UpdateZoneAsync(zone, token).ConfigureAwait(false);
            _ = await repo.SaveChangesAsync(token).ConfigureAwait(false);
            return (Map(zone), oldJson);
        }, ct).ConfigureAwait(false);

        await CaptureZoneAuditAsync("SOFT_DELETE", result.Row, result.OldJson, ct).ConfigureAwait(false);
        await InvalidateCacheAsync(ct).ConfigureAwait(false);
    }

    // ============ 库位 ============

    /// <inheritdoc />
    public async Task<IReadOnlyList<LocationRow>> ListLocationsAsync(long warehouseId, CancellationToken ct)
    {
        _ = await FindWarehouseOrThrowAsync(warehouseId, ct).ConfigureAwait(false);
        IReadOnlyList<WarehouseLocation> rows = await repo.ListLocationsAsync(warehouseId, ct).ConfigureAwait(false);
        return [.. rows.Select(Map)];
    }

    /// <inheritdoc />
    public async Task<LocationRow> CreateLocationAsync(
        long warehouseId, long zoneId, CreateLocationPayload payload, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(payload);

        LocationRow row = await repo.ExecuteInTransactionAsync(async token =>
        {
            _ = await FindWarehouseOrThrowAsync(warehouseId, token).ConfigureAwait(false);
            WarehouseZone zone = await FindZoneOrThrowAsync(zoneId, token).ConfigureAwait(false);
            if (zone.WarehouseId != warehouseId)
            {
                throw new ValidationException("zoneId", "库区不属于该仓库");
            }

            string code = RequireCode(payload.Code, "库位编码");
            if (await repo.IsLocationCodeExistsAsync(zoneId, code, null, token).ConfigureAwait(false))
            {
                throw new ConflictException("库位编码已存在", "WMS_LOCATION_CODE_EXISTS");
            }

            WarehouseLocation location = new(
                TenantId, warehouseId, zoneId, code,
                NormalizeOptional(payload.Barcode, BarcodeMaxLength, "barcode"),
                ValidateSortOrder(payload.SortOrder));
            await repo.AddLocationAsync(location, token).ConfigureAwait(false);
            _ = await repo.SaveChangesAsync(token).ConfigureAwait(false);
            return Map(location);
        }, ct).ConfigureAwait(false);

        await CaptureLocationAuditAsync("INSERT", row, oldJson: null, ct).ConfigureAwait(false);
        await InvalidateCacheAsync(ct).ConfigureAwait(false);
        return row;
    }

    /// <inheritdoc />
    public async Task<LocationRow> UpdateLocationAsync(
        long locationId, UpdateLocationPayload payload, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(payload);

        (LocationRow Row, string OldJson) result = await repo.ExecuteInTransactionAsync(async token =>
        {
            WarehouseLocation location = await FindLocationOrThrowAsync(locationId, token).ConfigureAwait(false);
            string oldJson = Serialize(Map(location));
            location.UpdateProfile(
                NormalizeOptional(payload.Barcode, BarcodeMaxLength, "barcode"),
                ValidateSortOrder(payload.SortOrder));
            await repo.UpdateLocationAsync(location, token).ConfigureAwait(false);
            _ = await repo.SaveChangesAsync(token).ConfigureAwait(false);
            return (Map(location), oldJson);
        }, ct).ConfigureAwait(false);

        await CaptureLocationAuditAsync("UPDATE", result.Row, result.OldJson, ct).ConfigureAwait(false);
        await InvalidateCacheAsync(ct).ConfigureAwait(false);
        return result.Row;
    }

    /// <inheritdoc />
    public async Task<LocationRow> ToggleLocationAsync(long locationId, CancellationToken ct)
    {
        (LocationRow Row, string OldJson) result = await repo.ExecuteInTransactionAsync(async token =>
        {
            WarehouseLocation location = await FindLocationOrThrowAsync(locationId, token).ConfigureAwait(false);
            string oldJson = Serialize(Map(location));
            if (location.Status == WarehouseStatus.Enabled)
            {
                location.Disable();
            }
            else
            {
                location.Enable();
            }

            await repo.UpdateLocationAsync(location, token).ConfigureAwait(false);
            _ = await repo.SaveChangesAsync(token).ConfigureAwait(false);
            return (Map(location), oldJson);
        }, ct).ConfigureAwait(false);

        await CaptureLocationAuditAsync("UPDATE", result.Row, result.OldJson, ct).ConfigureAwait(false);
        await InvalidateCacheAsync(ct).ConfigureAwait(false);
        return result.Row;
    }

    /// <inheritdoc />
    public async Task DeleteLocationAsync(long locationId, CancellationToken ct)
    {
        (LocationRow Row, string OldJson) result = await repo.ExecuteInTransactionAsync(async token =>
        {
            WarehouseLocation location = await FindLocationOrThrowAsync(locationId, token).ConfigureAwait(false);
            if (await repo.HasOpenTaskLinesAtLocationAsync(locationId, token).ConfigureAwait(false))
            {
                throw new ConflictException("库位存在未完成作业单引用，不能删除", "WMS_LOCATION_HAS_OPEN_TASKS");
            }

            string oldJson = Serialize(Map(location));
            location.SoftDelete();
            await repo.UpdateLocationAsync(location, token).ConfigureAwait(false);
            _ = await repo.SaveChangesAsync(token).ConfigureAwait(false);
            return (Map(location), oldJson);
        }, ct).ConfigureAwait(false);

        await CaptureLocationAuditAsync("SOFT_DELETE", result.Row, result.OldJson, ct).ConfigureAwait(false);
        await InvalidateCacheAsync(ct).ConfigureAwait(false);
    }

    // ============ 内部实现 ============

    private async Task<Warehouse> FindWarehouseOrThrowAsync(long id, CancellationToken ct)
        => await repo.FindWarehouseAsync(id, ct).ConfigureAwait(false)
            ?? throw new NotFoundException("仓库", id.ToString(CultureInfo.InvariantCulture));

    private async Task<WarehouseZone> FindZoneOrThrowAsync(long id, CancellationToken ct)
        => await repo.FindZoneAsync(id, ct).ConfigureAwait(false)
            ?? throw new NotFoundException("库区", id.ToString(CultureInfo.InvariantCulture));

    private async Task<WarehouseLocation> FindLocationOrThrowAsync(long id, CancellationToken ct)
        => await repo.FindLocationAsync(id, ct).ConfigureAwait(false)
            ?? throw new NotFoundException("库位", id.ToString(CultureInfo.InvariantCulture));

    private async Task<string> ResolveOrgCodeAsync(long? orgId, CancellationToken ct)
    {
        if (orgId is null or <= 0)
        {
            return "SYS";
        }

        OrgQueryRow? org = await orgQuery.FindAsync(orgId.Value, ct).ConfigureAwait(false);
        return org?.Code ?? "SYS";
    }

    private static string RequireName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ValidationException("name", "名称不能为空");
        }

        string trimmed = name.Trim();
        if (trimmed.Length > NameMaxLength)
        {
            throw new ValidationException("name", $"名称长度不能超过 {NameMaxLength}");
        }

        return trimmed;
    }

    private static string RequireCode(string? code, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            throw new ValidationException("code", $"{fieldName}不能为空");
        }

        string trimmed = code.Trim();
        if (trimmed.Length > CodeMaxLength)
        {
            throw new ValidationException("code", $"{fieldName}长度不能超过 {CodeMaxLength}");
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

    private static int ValidateSortOrder(int sortOrder)
    {
        if (sortOrder < 0)
        {
            throw new ValidationException("sortOrder", "排序号不能为负数");
        }

        return sortOrder;
    }

    private static void ValidateWhType(int whType)
    {
        if (whType is < 1 or > 3)
        {
            throw new ValidationException("whType", "仓库类型必须为 1/2/3");
        }
    }

    private async Task InvalidateCacheAsync(CancellationToken ct)
        => await cache.RemoveByPrefixAsync(CachePrefix, ct).ConfigureAwait(false);

    private Task CaptureAuditAsync(string operation, WarehouseRow row, string? oldJson, CancellationToken ct)
        => audit.CaptureChangeAsync(
            new AuditLogEntry(
                snowflake.NextId(),
                nameof(Warehouse),
                row.Id,
                operation,
                oldJson,
                Serialize(row),
                ChangedBy,
                DateTimeOffset.UtcNow,
                TenantId),
            ct);

    private Task CaptureZoneAuditAsync(string operation, ZoneRow row, string? oldJson, CancellationToken ct)
        => audit.CaptureChangeAsync(
            new AuditLogEntry(
                snowflake.NextId(),
                nameof(WarehouseZone),
                row.Id,
                operation,
                oldJson,
                Serialize(row),
                ChangedBy,
                DateTimeOffset.UtcNow,
                TenantId),
            ct);

    private Task CaptureLocationAuditAsync(string operation, LocationRow row, string? oldJson, CancellationToken ct)
        => audit.CaptureChangeAsync(
            new AuditLogEntry(
                snowflake.NextId(),
                nameof(WarehouseLocation),
                row.Id,
                operation,
                oldJson,
                Serialize(row),
                ChangedBy,
                DateTimeOffset.UtcNow,
                TenantId),
            ct);

    private static string Serialize<T>(T value)
        => JsonSerializer.Serialize(value, QlxJsonDefaults.PreserveNull);

    private static WarehouseRow Map(Warehouse entity)
        => new(entity.Id, entity.Code, entity.Name, entity.WhType, entity.OrgId,
            entity.Address, entity.KeeperId, entity.Remark, (int)entity.Status);

    private static ZoneRow Map(WarehouseZone entity)
        => new(entity.Id, entity.WarehouseId, entity.Code, entity.Name, entity.SortOrder, (int)entity.Status);

    private static LocationRow Map(WarehouseLocation entity)
        => new(entity.Id, entity.WarehouseId, entity.ZoneId, entity.Code, entity.Barcode,
            entity.SortOrder, (int)entity.Status);
}
