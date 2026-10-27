/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-25 16:40:00 +08:00 中国标准时间 星期日
 * 描述：计量单位主数据服务实现——单位组 / 单位 / 换算关系 CRUD + 读穿缓存 + 事务写入 + 审计留痕
 */

using System.Globalization;
using System.Text.Json;

using Qlx.Abstractions.Audit;
using Qlx.Abstractions.Constants;
using Qlx.Abstractions.Domain;
using Qlx.Abstractions.Exceptions;
using Qlx.Abstractions.Serialization;
using Qlx.Abstractions.Tenancy;
using Qlx.BuildingBlocks.Caching;
using Qlx.BuildingBlocks.Ids;
using Qlx.BuildingBlocks.Numbering;
using Qlx.Modules.Bas.Domain;
using Qlx.Modules.Bas.Domain.Goods;
using Qlx.Modules.Bas.Dtos;
using Qlx.Modules.Bas.Repositories;

namespace Qlx.Modules.Bas.Services;

/// <summary>
/// 计量单位主数据服务。单位组/单位/换算关系读路径经统一缓存读穿；
/// 写路径事务提交后落审计并失效 Bas 租户缓存前缀。换算率变更显式记录旧值/新值快照，历史单据不回溯。
/// </summary>
/// <param name="tenantCtx">当前租户上下文。</param>
/// <param name="repo">Bas 租户仓储。</param>
/// <param name="encodingGenerator">编码生成器（单位组 / 单位编码，前端不传码，N-1）。</param>
/// <param name="cache">统一缓存服务。</param>
/// <param name="audit">审计服务。</param>
/// <param name="snowflake">雪花 Id 生成器（审计事件 Id）。</param>
/// <param name="userContext">当前用户上下文（审计变更人）。</param>
internal sealed class UnitService(
    ITenantContext tenantCtx,
    IBasTenantRepository repo,
    IEncodingGenerator encodingGenerator,
    ICacheService cache,
    IAuditService audit,
    ISnowflakeIdGenerator snowflake,
    ICurrentUserContext userContext) : IUnitService
{
    private static readonly CacheEntryOptions s_cacheOptions = new()
    {
        AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5),
        NullValueExpiration = TimeSpan.FromSeconds(30),
    };

    private long TenantId => tenantCtx.TenantId
        ?? throw new ForbiddenException("未识别到租户上下文");

    private string ChangedBy => userContext.UserId ?? "system";

    // ============ 计量单位组 ============

    /// <inheritdoc />
    public async Task<IReadOnlyList<UnitGroupRow>> ListUnitGroupsAsync(CancellationToken ct)
    {
        string cacheKey = QlxCacheKey.BuildTenantKey(TenantId, QlxModuleCodes.Bas, "unitgroup:all");
        return await cache.GetOrCreateAsync<IReadOnlyList<UnitGroupRow>>(
            cacheKey,
            factory: async token =>
            {
                IReadOnlyList<UnitGroup> groups = await repo.ListAllUnitGroupsAsync(token).ConfigureAwait(false);
                return [.. groups.Select(Map)];
            },
            cancellationToken: ct,
            options: s_cacheOptions).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<UnitGroupRow> SaveUnitGroupAsync(SaveUnitGroupPayload payload, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(payload);
        string name = RequireName(payload.Name);

        (UnitGroupRow Row, string Operation, string? OldJson) result = await repo.ExecuteInTransactionAsync(
            token => SaveUnitGroupCoreAsync(payload.Id, name, token), ct).ConfigureAwait(false);

        await CaptureAuditAsync(
            nameof(UnitGroup), result.Row.Id, result.Operation, result.OldJson, Serialize(result.Row), ct)
            .ConfigureAwait(false);
        await InvalidateCacheAsync(ct).ConfigureAwait(false);
        return result.Row;
    }

    /// <inheritdoc />
    public async Task<UnitGroupRow> ToggleUnitGroupAsync(long id, CancellationToken ct)
    {
        (UnitGroupRow Row, string OldJson) result = await repo.ExecuteInTransactionAsync(
            async token =>
            {
                UnitGroup group = await FindUnitGroupOrThrowAsync(id, token).ConfigureAwait(false);
                string oldJson = Serialize(Map(group));

                if (group.Status == EntityStatus.Enabled)
                {
                    group.Disable();
                }
                else
                {
                    group.Enable();
                }

                await repo.UpdateUnitGroupAsync(group, token).ConfigureAwait(false);
                _ = await repo.SaveChangesAsync(token).ConfigureAwait(false);
                return (Map(group), oldJson);
            },
            ct).ConfigureAwait(false);

        await CaptureAuditAsync(
            nameof(UnitGroup), result.Row.Id, "UPDATE", result.OldJson, Serialize(result.Row), ct)
            .ConfigureAwait(false);
        await InvalidateCacheAsync(ct).ConfigureAwait(false);
        return result.Row;
    }

    // ============ 计量单位 ============

    /// <inheritdoc />
    public async Task<IReadOnlyList<UnitRow>> ListUnitsAsync(CancellationToken ct)
    {
        string cacheKey = QlxCacheKey.BuildTenantKey(TenantId, QlxModuleCodes.Bas, "unit:all");
        return await cache.GetOrCreateAsync<IReadOnlyList<UnitRow>>(
            cacheKey,
            factory: async token =>
            {
                IReadOnlyList<Unit> units = await repo.ListAllUnitsAsync(token).ConfigureAwait(false);
                return [.. units.Select(Map)];
            },
            cancellationToken: ct,
            options: s_cacheOptions).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<UnitRow> SaveUnitAsync(SaveUnitPayload payload, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(payload);
        string name = RequireName(payload.Name);
        EnsurePrecision(payload.Precision);

        (UnitRow Row, string Operation, string? OldJson) result = await repo.ExecuteInTransactionAsync(
            token => SaveUnitCoreAsync(payload, name, token), ct).ConfigureAwait(false);

        await CaptureAuditAsync(
            nameof(Unit), result.Row.Id, result.Operation, result.OldJson, Serialize(result.Row), ct)
            .ConfigureAwait(false);
        await InvalidateCacheAsync(ct).ConfigureAwait(false);
        return result.Row;
    }

    /// <inheritdoc />
    public async Task<UnitRow> ToggleUnitAsync(long id, CancellationToken ct)
    {
        (UnitRow Row, string OldJson) result = await repo.ExecuteInTransactionAsync(
            async token =>
            {
                Unit unit = await FindUnitOrThrowAsync(id, token).ConfigureAwait(false);
                string oldJson = Serialize(Map(unit));

                if (unit.Status == EntityStatus.Enabled)
                {
                    unit.Disable();
                }
                else
                {
                    unit.Enable();
                }

                await repo.UpdateUnitAsync(unit, token).ConfigureAwait(false);
                _ = await repo.SaveChangesAsync(token).ConfigureAwait(false);
                return (Map(unit), oldJson);
            },
            ct).ConfigureAwait(false);

        await CaptureAuditAsync(
            nameof(Unit), result.Row.Id, "UPDATE", result.OldJson, Serialize(result.Row), ct)
            .ConfigureAwait(false);
        await InvalidateCacheAsync(ct).ConfigureAwait(false);
        return result.Row;
    }

    // ============ 单位换算 ============

    /// <inheritdoc />
    public async Task<IReadOnlyList<UnitConversionRow>> ListConversionsAsync(
        long? unitGroupId, CancellationToken ct)
    {
        string cacheKey = QlxCacheKey.BuildTenantKey(TenantId, QlxModuleCodes.Bas, "unitconversion:all");
        IReadOnlyList<UnitConversionRow> all = await cache.GetOrCreateAsync<IReadOnlyList<UnitConversionRow>>(
            cacheKey,
            factory: async token =>
            {
                IReadOnlyList<UnitConversion> conversions = await repo
                    .ListAllUnitConversionsAsync(token).ConfigureAwait(false);
                return [.. conversions.Select(Map)];
            },
            cancellationToken: ct,
            options: s_cacheOptions).ConfigureAwait(false);

        return unitGroupId.HasValue
            ? [.. all.Where(c => c.UnitGroupId == unitGroupId.Value)]
            : all;
    }

    /// <inheritdoc />
    public async Task<UnitConversionRow> SaveConversionAsync(
        SaveUnitConversionPayload payload, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(payload);

        (UnitConversionRow Row, string Operation, string? OldJson) result = await repo.ExecuteInTransactionAsync(
            token => SaveConversionCoreAsync(payload, token), ct).ConfigureAwait(false);

        // 换算率变更：显式捕获旧值/新值快照（历史单据不回溯，仅留痕）
        await CaptureAuditAsync(
            nameof(UnitConversion), result.Row.Id, result.Operation, result.OldJson, Serialize(result.Row), ct)
            .ConfigureAwait(false);
        await InvalidateCacheAsync(ct).ConfigureAwait(false);
        return result.Row;
    }

    // ============ 内部实现 ============

    private async Task<(UnitGroupRow Row, string Operation, string? OldJson)> SaveUnitGroupCoreAsync(
        long? id, string name, CancellationToken ct)
    {
        if (id.HasValue)
        {
            UnitGroup group = await FindUnitGroupOrThrowAsync(id.Value, ct).ConfigureAwait(false);
            string oldJson = Serialize(Map(group));
            group.Rename(name);
            await repo.UpdateUnitGroupAsync(group, ct).ConfigureAwait(false);
            _ = await repo.SaveChangesAsync(ct).ConfigureAwait(false);
            return (Map(group), "UPDATE", oldJson);
        }

        string code = await encodingGenerator.GenerateAsync(
            name,
            scope: "unitgroup",
            uniquenessChecker: (candidate, token) => repo.IsUnitGroupCodeExistsAsync(candidate, token),
            options: new EncodingGenerationOptions
            {
                Prefix = "ug-",
                PinyinMode = PinyinMode.Initials,
                SequenceDigits = 3,
                MaxLength = 32,
            },
            cancellationToken: ct).ConfigureAwait(false);

        UnitGroup created = new(TenantId, name, code);
        await repo.AddUnitGroupAsync(created, ct).ConfigureAwait(false);
        _ = await repo.SaveChangesAsync(ct).ConfigureAwait(false);
        return (Map(created), "INSERT", null);
    }

    private async Task<(UnitRow Row, string Operation, string? OldJson)> SaveUnitCoreAsync(
        SaveUnitPayload payload, string name, CancellationToken ct)
    {
        _ = await repo.FindUnitGroupAsync(payload.UnitGroupId, ct).ConfigureAwait(false)
            ?? throw new NotFoundException(
                "计量单位组", payload.UnitGroupId.ToString(CultureInfo.InvariantCulture));

        if (payload.Id.HasValue)
        {
            Unit unit = await FindUnitOrThrowAsync(payload.Id.Value, ct).ConfigureAwait(false);
            string oldJson = Serialize(Map(unit));

            unit.Rename(name);
            unit.ChangeUnitGroup(payload.UnitGroupId);
            unit.SetPrecision(payload.Precision);
            await repo.UpdateUnitAsync(unit, ct).ConfigureAwait(false);
            _ = await repo.SaveChangesAsync(ct).ConfigureAwait(false);
            return (Map(unit), "UPDATE", oldJson);
        }

        string code = await encodingGenerator.GenerateAsync(
            name,
            scope: "unit",
            uniquenessChecker: (candidate, token) => repo.IsUnitCodeExistsAsync(candidate, token),
            options: new EncodingGenerationOptions
            {
                Prefix = "u-",
                PinyinMode = PinyinMode.Initials,
                SequenceDigits = 3,
                MaxLength = 32,
            },
            cancellationToken: ct).ConfigureAwait(false);

        Unit created = new(TenantId, name, code, payload.UnitGroupId, payload.Precision);
        await repo.AddUnitAsync(created, ct).ConfigureAwait(false);
        _ = await repo.SaveChangesAsync(ct).ConfigureAwait(false);
        return (Map(created), "INSERT", null);
    }

    private async Task<(UnitConversionRow Row, string Operation, string? OldJson)> SaveConversionCoreAsync(
        SaveUnitConversionPayload payload, CancellationToken ct)
    {
        if (payload.Rate <= 0)
        {
            throw new ValidationException("rate", "换算率必须大于 0");
        }

        if (payload.FromUnitId == payload.ToUnitId)
        {
            throw new ValidationException("toUnitId", "源单位与目标单位不能相同");
        }

        _ = await repo.FindUnitGroupAsync(payload.UnitGroupId, ct).ConfigureAwait(false)
            ?? throw new NotFoundException(
                "计量单位组", payload.UnitGroupId.ToString(CultureInfo.InvariantCulture));

        Unit from = await FindUnitOrThrowAsync(payload.FromUnitId, ct).ConfigureAwait(false);
        Unit to = await FindUnitOrThrowAsync(payload.ToUnitId, ct).ConfigureAwait(false);
        if (from.UnitGroupId != payload.UnitGroupId || to.UnitGroupId != payload.UnitGroupId)
        {
            throw new ValidationException("unitGroupId", "换算单位必须属于同一计量单位组");
        }

        if (payload.Id.HasValue)
        {
            UnitConversion conversion = await repo.FindUnitConversionAsync(payload.Id.Value, ct)
                .ConfigureAwait(false)
                ?? throw new NotFoundException(
                    "单位换算关系", payload.Id.Value.ToString(CultureInfo.InvariantCulture));

            if (conversion.UnitGroupId != payload.UnitGroupId
                || conversion.FromUnitId != payload.FromUnitId
                || conversion.ToUnitId != payload.ToUnitId)
            {
                throw new ValidationException("unitGroupId", "换算关系不可变更参与单位，请停用后重建");
            }

            string oldJson = Serialize(Map(conversion));
            conversion.ChangeRate(payload.Rate);
            await repo.UpdateUnitConversionAsync(conversion, ct).ConfigureAwait(false);
            _ = await repo.SaveChangesAsync(ct).ConfigureAwait(false);
            return (Map(conversion), "UPDATE", oldJson);
        }

        if (await repo.IsUnitConversionExistsAsync(
                payload.FromUnitId, payload.ToUnitId, excludeId: null, ct).ConfigureAwait(false))
        {
            throw new ConflictException("该源单位到目标单位的换算关系已存在", "UNIT_CONVERSION_EXISTS");
        }

        UnitConversion created = new(
            TenantId, payload.UnitGroupId, payload.FromUnitId, payload.ToUnitId, payload.Rate);
        await repo.AddUnitConversionAsync(created, ct).ConfigureAwait(false);
        _ = await repo.SaveChangesAsync(ct).ConfigureAwait(false);
        return (Map(created), "INSERT", null);
    }

    private async Task<UnitGroup> FindUnitGroupOrThrowAsync(long id, CancellationToken ct)
        => await repo.FindUnitGroupAsync(id, ct).ConfigureAwait(false)
           ?? throw new NotFoundException("计量单位组", id.ToString(CultureInfo.InvariantCulture));

    private async Task<Unit> FindUnitOrThrowAsync(long id, CancellationToken ct)
        => await repo.FindUnitAsync(id, ct).ConfigureAwait(false)
           ?? throw new NotFoundException("计量单位", id.ToString(CultureInfo.InvariantCulture));

    private Task CaptureAuditAsync(
        string entityType, long entityId, string operation, string? oldJson, string? newJson, CancellationToken ct)
        => audit.CaptureChangeAsync(
            new AuditLogEntry(
                snowflake.NextId(),
                entityType,
                entityId,
                operation,
                oldJson,
                newJson,
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
            throw new ValidationException("name", "名称不能为空");
        }

        return name.Trim();
    }

    private static void EnsurePrecision(int precision)
    {
        if (precision < 0 || precision > Unit.MaxPrecision)
        {
            throw new ValidationException("precision", "数量精度须为 0~8");
        }
    }

    private static UnitGroupRow Map(UnitGroup group)
        => new(group.Id, group.Code, group.Name, (int)group.Status, group.UpdatedAt);

    private static UnitRow Map(Unit unit)
        => new(
            unit.Id,
            unit.UnitGroupId,
            unit.Code,
            unit.Name,
            unit.Precision,
            (int)unit.Status,
            unit.UpdatedAt);

    private static UnitConversionRow Map(UnitConversion conversion)
        => new(
            conversion.Id,
            conversion.UnitGroupId,
            conversion.FromUnitId,
            conversion.ToUnitId,
            conversion.Rate,
            (int)conversion.Status,
            conversion.UpdatedAt);

    private static string Serialize(UnitGroupRow row)
        => JsonSerializer.Serialize(row, QlxJsonDefaults.PreserveNull);

    private static string Serialize(UnitRow row)
        => JsonSerializer.Serialize(row, QlxJsonDefaults.PreserveNull);

    private static string Serialize(UnitConversionRow row)
        => JsonSerializer.Serialize(row, QlxJsonDefaults.PreserveNull);
}
