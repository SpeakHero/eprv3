/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-25 13:40:00 +08:00 中国标准时间 星期日
 * 描述：岗位主数据服务实现——全量扁平缓存 + 内存筛选分页 + 事务写 + 审计 + 超编审批挂起
 */

using System.Globalization;
using System.Text.Json;

using Qlx.Abstractions.Approvals;
using Qlx.Abstractions.Audit;
using Qlx.Abstractions.Constants;
using Qlx.Abstractions.Domain;
using Qlx.Abstractions.Exceptions;
using Qlx.Abstractions.Serialization;
using Qlx.Abstractions.Tenancy;
using Qlx.BuildingBlocks.Api;
using Qlx.BuildingBlocks.Caching;
using Qlx.BuildingBlocks.Ids;
using Qlx.BuildingBlocks.Numbering;
using Qlx.Modules.Bas.Domain;
using Qlx.Modules.Bas.Dtos;
using Qlx.Modules.Bas.Repositories;

namespace Qlx.Modules.Bas.Services;

/// <summary>
/// 岗位主数据服务。全量岗位经 <c>GetOrCreateAsync</c> 缓存后内存筛选分页（键空间有界，C-1/C-4）；
/// 写路径事务提交后落审计快照并失效 Bas 租户前缀（C-5 失效优先）。
/// </summary>
/// <param name="tenantCtx">当前租户上下文。</param>
/// <param name="repo">Bas 租户仓储。</param>
/// <param name="encodingGenerator">岗位编码生成器（pos- 前缀，N-1/N-2）。</param>
/// <param name="approvalService">审批中心契约（发起超编审批）。</param>
/// <param name="cache">统一缓存服务。</param>
/// <param name="audit">审计服务。</param>
/// <param name="snowflake">雪花 Id 生成器（审计事件 Id）。</param>
/// <param name="userContext">当前用户上下文（审计变更人）。</param>
internal sealed class PositionMasterDataService(
    ITenantContext tenantCtx,
    IBasTenantRepository repo,
    IEncodingGenerator encodingGenerator,
    IApprovalService approvalService,
    ICacheService cache,
    IAuditService audit,
    ISnowflakeIdGenerator snowflake,
    ICurrentUserContext userContext) : IPositionMasterDataService
{
    /// <summary>超编审批业务类型。</summary>
    internal const string OverstaffBizType = "bas.position.overstaff";

    private static readonly CacheEntryOptions s_cacheOptions = new()
    {
        AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5),
        NullValueExpiration = TimeSpan.FromSeconds(30),
    };

    private long TenantId => tenantCtx.TenantId
        ?? throw new ForbiddenException("未识别到租户上下文");

    private string ChangedBy => userContext.UserId ?? "system";

    // ============ 读路径 ============

    /// <inheritdoc />
    public async Task<PagedResult<PositionMasterRow>> ListAsync(
        long? orgId, string? keyword, PagedRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        IReadOnlyList<PositionMasterRow> all = await LoadAllCachedAsync(ct).ConfigureAwait(false);

        if (orgId.HasValue)
        {
            IReadOnlyList<Organization> orgs = await repo.ListAllOrgsAsync(ct).ConfigureAwait(false);
            HashSet<long> subtree = [.. CollectSubtreeOrgIds(orgs, orgId.Value)];
            all = [.. all.Where(p => subtree.Contains(p.OrgId))];
        }

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            string trimmed = keyword.Trim();
            all = [.. all.Where(p =>
                p.Name.Contains(trimmed, StringComparison.OrdinalIgnoreCase)
                || p.Code.Contains(trimmed, StringComparison.OrdinalIgnoreCase)
                || (p.Description?.Contains(trimmed, StringComparison.OrdinalIgnoreCase) ?? false)
                || (p.Duty?.Contains(trimmed, StringComparison.OrdinalIgnoreCase) ?? false))];
        }

        int total = all.Count;
        List<PositionMasterRow> page = [.. all.Skip(request.Skip).Take(Math.Clamp(request.PageSize, 1, 200))];
        return new PagedResult<PositionMasterRow>(page, total, request.Page, request.PageSize);
    }

    /// <inheritdoc />
    public async Task<PositionStatsRow> GetStatsAsync(long id, CancellationToken ct)
    {
        string cacheKey = QlxCacheKey.BuildTenantKey(
            TenantId, QlxModuleCodes.Bas, $"pos:stats:{id.ToString(CultureInfo.InvariantCulture)}");

        PositionStatsRow? row = await cache.GetOrCreateAsync<PositionStatsRow?>(
            cacheKey,
            factory: async token =>
            {
                Position? position = await repo.FindPositionAsync(id, token).ConfigureAwait(false);
                return position is null
                    ? null
                    : new PositionStatsRow(position.Id, position.Headcount, position.CurrentCount);
            },
            cancellationToken: ct,
            options: s_cacheOptions).ConfigureAwait(false);

        return row ?? throw new NotFoundException("岗位", id.ToString(CultureInfo.InvariantCulture));
    }

    // ============ 写路径 ============

    /// <inheritdoc />
    public async Task<PositionMasterRow> SaveAsync(SavePositionCommand payload, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(payload);
        if (string.IsNullOrWhiteSpace(payload.Name))
        {
            throw new ValidationException("name", "岗位名称不能为空");
        }

        string name = payload.Name.Trim();
        (PositionMasterRow Row, string Operation, string? OldJson) result = await repo.ExecuteInTransactionAsync(
            token => SaveCoreAsync(payload, name, token), ct).ConfigureAwait(false);

        await CaptureAuditAsync(result.Operation, result.Row, result.OldJson, ct).ConfigureAwait(false);
        await InvalidateCacheAsync(ct).ConfigureAwait(false);
        return result.Row;
    }

    /// <inheritdoc />
    public async Task<PositionMasterRow> ToggleAsync(long id, CancellationToken ct)
    {
        (PositionMasterRow Row, string? OldJson) result = await repo.ExecuteInTransactionAsync(
            token => ToggleCoreAsync(id, token), ct).ConfigureAwait(false);

        await CaptureAuditAsync("UPDATE", result.Row, result.OldJson, ct).ConfigureAwait(false);
        await InvalidateCacheAsync(ct).ConfigureAwait(false);
        return result.Row;
    }

    /// <inheritdoc />
    public async Task<PositionMasterRow> AdjustHeadcountAsync(long id, int headcount, CancellationToken ct)
        => await ApplyHeadcountCoreAsync(id, headcount, ct).ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<ApprovalRequestRow> SubmitOverstaffRequestAsync(
        long id, SubmitOverstaffPayload payload, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(payload);
        Position position = await repo.FindPositionAsync(id, ct).ConfigureAwait(false)
            ?? throw new NotFoundException("岗位", id.ToString(CultureInfo.InvariantCulture));

        if (payload.Headcount <= position.Headcount)
        {
            throw new ValidationException("headcount", "申请编制须大于当前编制人数");
        }

        OverstaffPayload requestPayload = new(position.Id, payload.Headcount, payload.Reason);
        BasApprovalRequest request = new(
            TenantId,
            OverstaffBizType,
            JsonSerializer.Serialize(requestPayload, QlxJsonDefaults.PreserveNull));
        await repo.AddApprovalRequestAsync(request, ct).ConfigureAwait(false);
        _ = await repo.SaveChangesAsync(ct).ConfigureAwait(false);

        string instanceId = await approvalService.StartApprovalAsync(
            new StartApprovalRequest(
                ApprovalDefinitionCode: OverstaffBizType,
                BizType: OverstaffBizType,
                BizId: request.Id.ToString(CultureInfo.InvariantCulture),
                BizNo: position.Code,
                InitiatorUserId: ChangedBy,
                Title: $"岗位超编申请：{position.Name}",
                Description: payload.Reason,
                BizData: new Dictionary<string, object?>
                {
                    ["positionId"] = position.Id,
                    ["headcount"] = payload.Headcount,
                }),
            ct).ConfigureAwait(false);

        request.BindInstance(instanceId);
        _ = await repo.SaveChangesAsync(ct).ConfigureAwait(false);
        return Map(request);
    }

    /// <inheritdoc />
    public async Task ApplyHeadcountAsync(long positionId, int headcount, CancellationToken ct)
    {
        _ = await ApplyHeadcountCoreAsync(positionId, headcount, ct).ConfigureAwait(false);
    }

    // ============ 内部实现 ============

    private async Task<IReadOnlyList<PositionMasterRow>> LoadAllCachedAsync(CancellationToken ct)
    {
        string cacheKey = QlxCacheKey.BuildTenantKey(TenantId, QlxModuleCodes.Bas, "pos:all");
        return await cache.GetOrCreateAsync<IReadOnlyList<PositionMasterRow>>(
            cacheKey,
            factory: async token =>
            {
                IReadOnlyList<Position> positions = await repo.ListAllPositionsAsync(token).ConfigureAwait(false);
                return [.. positions.Select(Map)];
            },
            cancellationToken: ct,
            options: s_cacheOptions).ConfigureAwait(false);
    }

    private async Task<(PositionMasterRow Row, string Operation, string? OldJson)> SaveCoreAsync(
        SavePositionCommand payload, string name, CancellationToken ct)
    {
        _ = await repo.FindOrgAsync(payload.OrgId, ct).ConfigureAwait(false)
            ?? throw new NotFoundException("所属组织", payload.OrgId.ToString(CultureInfo.InvariantCulture));

        if (payload.Id.HasValue)
        {
            Position position = await repo.FindPositionAsync(payload.Id.Value, ct).ConfigureAwait(false)
                ?? throw new NotFoundException("岗位", payload.Id.Value.ToString(CultureInfo.InvariantCulture));
            string oldJson = Serialize(Map(position));

            EnsureHeadcountNotBelowCurrent(position.CurrentCount, payload.Headcount);
            position.Rename(name);
            position.ChangeOrg(payload.OrgId);
            position.SetHeadcount(payload.Headcount);
            position.SetDescriptions(payload.Description, payload.Duty);

            await repo.UpdatePositionAsync(position, ct).ConfigureAwait(false);
            _ = await repo.SaveChangesAsync(ct).ConfigureAwait(false);
            return (Map(position), "UPDATE", oldJson);
        }

        // 岗位编码由系统生成（前端不传码，N-1）：pos- 前缀 + 岗位名拼音首字母，冲突追加序号
        string code = await encodingGenerator.GenerateAsync(
            name,
            scope: "position",
            uniquenessChecker: (candidate, token) => repo.IsPositionCodeExistsAsync(candidate, null, token),
            options: new EncodingGenerationOptions
            {
                Prefix = "pos-",
                PinyinMode = PinyinMode.Initials,
                SequenceDigits = 3,
                MaxLength = 50,
            },
            cancellationToken: ct).ConfigureAwait(false);

        Position created = new(TenantId, payload.OrgId, name, code, payload.Headcount);
        created.SetDescriptions(payload.Description, payload.Duty);
        await repo.AddPositionAsync(created, ct).ConfigureAwait(false);
        _ = await repo.SaveChangesAsync(ct).ConfigureAwait(false);
        return (Map(created), "INSERT", null);
    }

    private async Task<(PositionMasterRow Row, string? OldJson)> ToggleCoreAsync(long id, CancellationToken ct)
    {
        Position position = await repo.FindPositionAsync(id, ct).ConfigureAwait(false)
            ?? throw new NotFoundException("岗位", id.ToString(CultureInfo.InvariantCulture));
        string oldJson = Serialize(Map(position));

        if (position.Status == EntityStatus.Enabled)
        {
            if (position.CurrentCount > 0)
            {
                throw new ConflictException(
                    "岗位仍有在岗人员，不能停用",
                    "POSITION_HAS_MEMBERS",
                    new { PositionName = position.Name, position.CurrentCount });
            }

            position.Disable();
        }
        else
        {
            position.Enable();
        }

        await repo.UpdatePositionAsync(position, ct).ConfigureAwait(false);
        _ = await repo.SaveChangesAsync(ct).ConfigureAwait(false);
        return (Map(position), oldJson);
    }

    private async Task<PositionMasterRow> ApplyHeadcountCoreAsync(long id, int headcount, CancellationToken ct)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(headcount);

        (PositionMasterRow Row, string? OldJson) result = await repo.ExecuteInTransactionAsync(
            async token =>
            {
                Position position = await repo.FindPositionAsync(id, token).ConfigureAwait(false)
                    ?? throw new NotFoundException("岗位", id.ToString(CultureInfo.InvariantCulture));
                string oldJson = Serialize(Map(position));

                EnsureHeadcountNotBelowCurrent(position.CurrentCount, headcount);
                position.SetHeadcount(headcount);
                await repo.UpdatePositionAsync(position, token).ConfigureAwait(false);
                _ = await repo.SaveChangesAsync(token).ConfigureAwait(false);
                return (Map(position), oldJson);
            },
            ct).ConfigureAwait(false);

        await CaptureAuditAsync("UPDATE", result.Row, result.OldJson, ct).ConfigureAwait(false);
        await InvalidateCacheAsync(ct).ConfigureAwait(false);
        return result.Row;
    }

    private Task<long> InvalidateCacheAsync(CancellationToken ct)
        => cache.RemoveByPrefixAsync(
            $"{QlxCacheKey.TenantKeyPrefix}:{TenantId.ToString(CultureInfo.InvariantCulture)}:{QlxModuleCodes.Bas}:",
            ct);

    private Task CaptureAuditAsync(string operation, PositionMasterRow row, string? oldJson, CancellationToken ct)
        => audit.CaptureChangeAsync(
            new AuditLogEntry(
                snowflake.NextId(),
                nameof(Position),
                row.Id,
                operation,
                oldJson,
                Serialize(row),
                ChangedBy,
                DateTimeOffset.UtcNow,
                TenantId),
            ct);

    private static void EnsureHeadcountNotBelowCurrent(int currentCount, int headcount)
    {
        if (headcount < currentCount)
        {
            throw new ConflictException(
                "编制人数不能低于当前在岗人数",
                "POSITION_HEADCOUNT_BELOW_CURRENT",
                new { CurrentCount = currentCount });
        }
    }

    private static PositionMasterRow Map(Position position)
        => new(
            position.Id,
            position.OrgId,
            position.Name,
            position.Code,
            position.Headcount,
            position.CurrentCount,
            (int)position.Status,
            position.Description,
            position.Duty,
            position.UpdatedAt);

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

    private static string Serialize(PositionMasterRow row)
        => JsonSerializer.Serialize(row, QlxJsonDefaults.PreserveNull);

    /// <summary>展开组织子树 Id 集合（广度优先，含自身；防脏数据环）。</summary>
    private static List<long> CollectSubtreeOrgIds(IReadOnlyList<Organization> orgs, long rootId)
    {
        Dictionary<long, List<long>> childrenByParent = [];
        foreach (IGrouping<long, Organization> group in orgs
            .Where(o => o.ParentId.HasValue)
            .GroupBy(o => o.ParentId!.Value))
        {
            childrenByParent[group.Key] = [.. group.Select(o => o.Id)];
        }

        List<long> result = [];
        HashSet<long> visited = [];
        Queue<long> queue = new();
        queue.Enqueue(rootId);
        while (queue.Count > 0)
        {
            long current = queue.Dequeue();
            if (!visited.Add(current))
            {
                continue;
            }

            result.Add(current);
            if (childrenByParent.TryGetValue(current, out List<long>? children))
            {
                foreach (long child in children)
                {
                    queue.Enqueue(child);
                }
            }
        }

        return result;
    }
}
