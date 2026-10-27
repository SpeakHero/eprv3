/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-25 13:20:00 +08:00 中国标准时间 星期日
 * 描述：组织架构主数据服务实现——事务写入 + 版本快照 + 审计 + 失效优先缓存 + 组织创建事件持久发布
 */

using System.Globalization;
using System.Text.Json;

using Qlx.Abstractions.Approvals;
using Qlx.Abstractions.Audit;
using Qlx.Abstractions.Constants;
using Qlx.Abstractions.Domain;
using Qlx.Abstractions.Events.Bas;
using Qlx.Abstractions.Exceptions;
using Qlx.Abstractions.Queries;
using Qlx.Abstractions.Serialization;
using Qlx.Abstractions.Tenancy;
using Qlx.BuildingBlocks.Caching;
using Qlx.BuildingBlocks.EventBus;
using Qlx.BuildingBlocks.Ids;
using Qlx.Modules.Bas.Domain;
using Qlx.Modules.Bas.Dtos;
using Qlx.Modules.Bas.Repositories;
using Qlx.Modules.Bas.Services.Numbering;

namespace Qlx.Modules.Bas.Services;

/// <summary>
/// 组织架构主数据服务。所有写操作在数据库事务内完成，事务提交后调用审计并失效缓存前缀。
/// </summary>
/// <param name="tenantCtx">当前租户上下文。</param>
/// <param name="repo">Bas 租户仓储。</param>
/// <param name="codeNumberGenerator">统一发号器（组织编码）。</param>
/// <param name="approvalService">审批中心契约（发起组织调整审批）。</param>
/// <param name="eventBus">事件总线（组织创建事件持久发布）。</param>
/// <param name="cache">统一缓存服务。</param>
/// <param name="audit">审计服务。</param>
/// <param name="snowflake">雪花 Id 生成器（审计事件 Id）。</param>
/// <param name="userContext">当前用户上下文（审计变更人）。</param>
/// <param name="memberQuery">租户成员只读查询（停用前置校验）。</param>
internal sealed class OrgMasterDataService(
    ITenantContext tenantCtx,
    IBasTenantRepository repo,
    IBasCodeNumberGenerator codeNumberGenerator,
    IApprovalService approvalService,
    IEventBus eventBus,
    ICacheService cache,
    IAuditService audit,
    ISnowflakeIdGenerator snowflake,
    ICurrentUserContext userContext,
    ITenantMemberQuery memberQuery) : IOrgMasterDataService
{
    /// <summary>组织相关业务类型（审批 BizType 与编码规则 BizType 一致）。</summary>
    internal const string OrgBizType = "bas.org.adjust";

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
    public async Task<IReadOnlyList<OrgRow>> ListAsync(DateOnly? asOfDate, CancellationToken ct)
    {
        string businessKey = asOfDate.HasValue
            ? $"org:asof:{asOfDate.Value.ToString("yyyyMMdd", CultureInfo.InvariantCulture)}"
            : "org:all";
        string cacheKey = QlxCacheKey.BuildTenantKey(TenantId, QlxModuleCodes.Bas, businessKey);

        return await cache.GetOrCreateAsync<IReadOnlyList<OrgRow>>(
            cacheKey,
            factory: token => asOfDate.HasValue ? LoadAsOfAsync(asOfDate.Value, token) : LoadCurrentAsync(token),
            cancellationToken: ct,
            options: s_cacheOptions).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<OrgRow> GetAsync(long id, CancellationToken ct)
    {
        string cacheKey = QlxCacheKey.BuildTenantKey(
            TenantId, QlxModuleCodes.Bas, $"org:{id.ToString(CultureInfo.InvariantCulture)}");

        OrgRow? row = await cache.GetOrCreateAsync<OrgRow?>(
            cacheKey,
            factory: async token =>
            {
                Organization? org = await repo.FindOrgAsync(id, token).ConfigureAwait(false);
                return org is null ? null : Map(org);
            },
            cancellationToken: ct,
            options: s_cacheOptions).ConfigureAwait(false);

        return row ?? throw new NotFoundException("组织", id.ToString(CultureInfo.InvariantCulture));
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<OrgHistoryRow>> GetHistoryAsync(long id, CancellationToken ct)
        => GetHistoryCoreAsync(id, ct);

    // ============ 写路径（事务 → 审计 → 失效）============

    /// <inheritdoc />
    public async Task<OrgRow> CreateAsync(SaveOrgPayload payload, CancellationToken ct)
    {
        OrgRow row = await repo.ExecuteInTransactionAsync(
            token => CreateCoreAsync(payload, token), ct).ConfigureAwait(false);

        await CaptureAuditAsync("INSERT", row, oldJson: null, ct).ConfigureAwait(false);
        await InvalidateCacheAsync(ct).ConfigureAwait(false);

        await eventBus.PublishDurableAsync(
            new OrganizationCreatedEvent
            {
                OrgId = row.Id,
                OrgCode = row.Code,
                ParentId = row.ParentId,
                Type = row.Type,
                TenantCode = tenantCtx.TenantCode ?? TenantId.ToString(CultureInfo.InvariantCulture),
            },
            ct).ConfigureAwait(false);

        return row;
    }

    /// <inheritdoc />
    public async Task<OrgRow> UpdateAsync(long id, SaveOrgPayload payload, CancellationToken ct)
    {
        (OrgRow Row, string? OldJson) result = await repo.ExecuteInTransactionAsync(
            token => UpdateCoreAsync(id, payload, token), ct).ConfigureAwait(false);

        await CaptureAuditAsync("UPDATE", result.Row, result.OldJson, ct).ConfigureAwait(false);
        await InvalidateCacheAsync(ct).ConfigureAwait(false);
        return result.Row;
    }

    /// <inheritdoc />
    public async Task<OrgRow> ToggleAsync(long id, CancellationToken ct)
    {
        (OrgRow Row, string? OldJson) result = await repo.ExecuteInTransactionAsync(
            token => ToggleCoreAsync(id, token), ct).ConfigureAwait(false);

        await CaptureAuditAsync("UPDATE", result.Row, result.OldJson, ct).ConfigureAwait(false);
        await InvalidateCacheAsync(ct).ConfigureAwait(false);
        return result.Row;
    }

    /// <inheritdoc />
    public async Task<OrgRow> MergeAsync(long sourceId, long targetId, CancellationToken ct)
    {
        (OrgRow Row, string? OldJson) result = await repo.ExecuteInTransactionAsync(
            token => MergeCoreAsync(sourceId, targetId, token), ct).ConfigureAwait(false);

        await CaptureAuditAsync("SOFT_DELETE", result.Row, result.OldJson, ct).ConfigureAwait(false);
        await InvalidateCacheAsync(ct).ConfigureAwait(false);
        return result.Row;
    }

    // ============ 审批挂起 ============

    /// <inheritdoc />
    public async Task<ApprovalRequestRow> SubmitAdjustRequestAsync(
        SubmitOrgAdjustPayload payload, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(payload);
        if (string.IsNullOrWhiteSpace(payload.Action))
        {
            throw new ValidationException("action", "请指定组织调整动作");
        }

        BasApprovalRequest request = new(
            TenantId,
            OrgBizType,
            JsonSerializer.Serialize(payload, QlxJsonDefaults.PreserveNull));
        await repo.AddApprovalRequestAsync(request, ct).ConfigureAwait(false);
        _ = await repo.SaveChangesAsync(ct).ConfigureAwait(false);

        string bizNo = string.Empty;
        if (payload.OrgId.HasValue)
        {
            Organization? org = await repo.FindOrgAsync(payload.OrgId.Value, ct).ConfigureAwait(false);
            bizNo = org?.Code ?? string.Empty;
        }

        string title = string.IsNullOrWhiteSpace(payload.Name)
            ? "组织调整申请"
            : $"组织调整申请：{payload.Name}";

        string instanceId = await approvalService.StartApprovalAsync(
            new StartApprovalRequest(
                ApprovalDefinitionCode: OrgBizType,
                BizType: OrgBizType,
                BizId: request.Id.ToString(CultureInfo.InvariantCulture),
                BizNo: bizNo,
                InitiatorUserId: ChangedBy,
                Title: title,
                Description: payload.Reason,
                BizData: new Dictionary<string, object?>
                {
                    ["orgId"] = payload.OrgId,
                    ["action"] = payload.Action,
                }),
            ct).ConfigureAwait(false);

        request.BindInstance(instanceId);
        _ = await repo.SaveChangesAsync(ct).ConfigureAwait(false);
        return Map(request);
    }

    /// <inheritdoc />
    public async Task ApplyAsync(SubmitOrgAdjustPayload payload, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(payload);

        // 审批载荷按动作分派到已有的写路径（审批通过后复用同一套事务 + 审计 + 失效逻辑）
        if (!payload.Type.HasValue && payload.Action is "create" or "update")
        {
            throw new ValidationException("type", "组织调整申请缺少节点类型");
        }

        switch (payload.Action)
        {
            case "create":
                _ = await CreateAsync(ToSavePayload(payload), ct).ConfigureAwait(false);
                break;
            case "update":
                _ = await UpdateAsync(RequireOrgId(payload), ToSavePayload(payload), ct).ConfigureAwait(false);
                break;
            case "disable":
                _ = await DisableAsync(RequireOrgId(payload), ct).ConfigureAwait(false);
                break;
            case "merge":
                long sourceId = RequireOrgId(payload);
                _ = await MergeAsync(
                    sourceId,
                    payload.TargetOrgId ?? throw new ValidationException("targetOrgId", "合并目标组织不能为空"),
                    ct).ConfigureAwait(false);
                break;
            default:
                throw new ValidationException("action", $"不支持的组织调整动作：{payload.Action}");
        }
    }

    // ============ 内部实现 ============

    private async Task<IReadOnlyList<OrgRow>> LoadCurrentAsync(CancellationToken ct)
    {
        IReadOnlyList<Organization> orgs = await repo.ListAllOrgsAsync(ct).ConfigureAwait(false);
        return [.. orgs.Select(Map)];
    }

    /// <summary>按日期回看：取该日期前最近一次写操作的版本快照，缺失时回退当前行。</summary>
    private async Task<IReadOnlyList<OrgRow>> LoadAsOfAsync(DateOnly asOfDate, CancellationToken ct)
    {
        IReadOnlyList<Organization> orgs = await repo.ListAllOrgsAsync(ct).ConfigureAwait(false);
        IReadOnlyList<OrgNodeHistory> histories = await repo.ListAllOrgHistoriesAsync(ct).ConfigureAwait(false);

        Dictionary<long, OrgNodeHistory> snapshots = histories
            .Where(h => DateOnly.FromDateTime(h.EffectiveFrom.UtcDateTime) <= asOfDate)
            .GroupBy(h => h.OrgId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(h => h.Version).First());

        List<OrgRow> rows = [];
        foreach (Organization org in orgs)
        {
            if (snapshots.TryGetValue(org.Id, out OrgNodeHistory? history))
            {
                OrgRow? snapshot = JsonSerializer.Deserialize<OrgRow>(history.SnapshotJson, QlxJsonDefaults.PreserveNull);
                if (snapshot is not null)
                {
                    rows.Add(snapshot);
                    continue;
                }
            }

            if (DateOnly.FromDateTime(org.CreatedAt.UtcDateTime) <= asOfDate)
            {
                rows.Add(Map(org));
            }
        }

        return [.. rows];
    }

    private async Task<IReadOnlyList<OrgHistoryRow>> GetHistoryCoreAsync(long id, CancellationToken ct)
    {
        _ = await repo.FindOrgAsync(id, ct).ConfigureAwait(false)
            ?? throw new NotFoundException("组织", id.ToString(CultureInfo.InvariantCulture));

        IReadOnlyList<OrgNodeHistory> histories = await repo.ListOrgHistoriesAsync(id, ct).ConfigureAwait(false);
        return [.. histories.Select(h => new OrgHistoryRow(
            h.Id, h.OrgId, h.Version, h.SnapshotJson, h.EffectiveFrom, h.ChangedBy))];
    }

    private async Task<OrgRow> CreateCoreAsync(SaveOrgPayload payload, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(payload);
        OrgNodeType type = ParseType(payload.Type);
        string name = RequireName(payload.Name);

        Organization? parent = null;
        if (payload.ParentId.HasValue)
        {
            parent = await repo.FindOrgAsync(payload.ParentId.Value, ct).ConfigureAwait(false)
                ?? throw new NotFoundException("上级组织", payload.ParentId.Value.ToString(CultureInfo.InvariantCulture));
        }

        string code = await codeNumberGenerator.NextAsync(
            "org",
            TenantId,
            parent?.Code,
            BasCodeRuleDefaults.GetOrgTypeCode(type),
            isConflict: (candidate, token) => repo.IsOrgCodeExistsAsync(candidate, token),
            ct: ct).ConfigureAwait(false);

        Organization org = new(TenantId, name, type, parent?.Id, payload.SortOrder, code);
        org.SetLeader(payload.LeaderPersonId);
        org.ChangePeriod(
            payload.EffectDate ?? DateOnly.FromDateTime(DateTime.UtcNow),
            payload.ExpiryDate);

        await repo.AddOrgAsync(org, ct).ConfigureAwait(false);
        _ = await repo.SaveChangesAsync(ct).ConfigureAwait(false);
        await WriteSnapshotAsync(org, ct).ConfigureAwait(false);
        return Map(org);
    }

    private async Task<(OrgRow Row, string? OldJson)> UpdateCoreAsync(
        long id, SaveOrgPayload payload, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(payload);
        Organization org = await repo.FindOrgAsync(id, ct).ConfigureAwait(false)
            ?? throw new NotFoundException("组织", id.ToString(CultureInfo.InvariantCulture));

        OrgNodeType type = ParseType(payload.Type);
        string name = RequireName(payload.Name);
        string oldJson = Serialize(Map(org));

        if (payload.ParentId.HasValue)
        {
            if (payload.ParentId.Value == id)
            {
                throw new ConflictException("上级组织不能选择自身", "ORG_PARENT_SELF");
            }

            _ = await repo.FindOrgAsync(payload.ParentId.Value, ct).ConfigureAwait(false)
                ?? throw new NotFoundException("上级组织", payload.ParentId.Value.ToString(CultureInfo.InvariantCulture));

            IReadOnlyList<Organization> allOrgs = await repo.ListAllOrgsAsync(ct).ConfigureAwait(false);
            if (CollectSubtreeOrgIds(allOrgs, id).Contains(payload.ParentId.Value))
            {
                throw new ConflictException("上级组织不能选择自身的下级", "ORG_PARENT_CYCLE");
            }
        }

        org.Rename(name);
        org.ChangeType(type);
        org.ChangeParent(payload.ParentId);
        org.SetLeader(payload.LeaderPersonId);
        org.ChangePeriod(
            payload.EffectDate ?? org.EffectDate,
            payload.ExpiryDate);
        org.SortOrder = payload.SortOrder;
        org.TouchVersion();

        await repo.UpdateOrgAsync(org, ct).ConfigureAwait(false);
        _ = await repo.SaveChangesAsync(ct).ConfigureAwait(false);
        await WriteSnapshotAsync(org, ct).ConfigureAwait(false);
        return (Map(org), oldJson);
    }

    private async Task<(OrgRow Row, string? OldJson)> ToggleCoreAsync(long id, CancellationToken ct)
    {
        Organization org = await repo.FindOrgAsync(id, ct).ConfigureAwait(false)
            ?? throw new NotFoundException("组织", id.ToString(CultureInfo.InvariantCulture));

        string oldJson = Serialize(Map(org));

        if (org.Status == EntityStatus.Enabled)
        {
            await ApplyDisableAsync(org, ct).ConfigureAwait(false);
        }
        else
        {
            org.Enable();
            org.TouchVersion();
            await repo.UpdateOrgAsync(org, ct).ConfigureAwait(false);
            _ = await repo.SaveChangesAsync(ct).ConfigureAwait(false);
            await WriteSnapshotAsync(org, ct).ConfigureAwait(false);
        }

        return (Map(org), oldJson);
    }

    private async Task<OrgRow> DisableAsync(long id, CancellationToken ct)
    {
        (OrgRow Row, string? OldJson) result = await repo.ExecuteInTransactionAsync(
            async token =>
            {
                Organization org = await repo.FindOrgAsync(id, token).ConfigureAwait(false)
                    ?? throw new NotFoundException("组织", id.ToString(CultureInfo.InvariantCulture));
                string oldJson = Serialize(Map(org));
                if (org.Status == EntityStatus.Enabled)
                {
                    await ApplyDisableAsync(org, token).ConfigureAwait(false);
                }

                return (Map(org), oldJson);
            },
            ct).ConfigureAwait(false);

        await CaptureAuditAsync("UPDATE", result.Row, result.OldJson, ct).ConfigureAwait(false);
        await InvalidateCacheAsync(ct).ConfigureAwait(false);
        return result.Row;
    }

    private async Task<(OrgRow Row, string? OldJson)> MergeCoreAsync(
        long sourceId, long targetId, CancellationToken ct)
    {
        if (sourceId == targetId)
        {
            throw new ConflictException("不能将组织合并到自身", "ORG_MERGE_SELF");
        }

        Organization source = await repo.FindOrgAsync(sourceId, ct).ConfigureAwait(false)
            ?? throw new NotFoundException("组织", sourceId.ToString(CultureInfo.InvariantCulture));
        _ = await repo.FindOrgAsync(targetId, ct).ConfigureAwait(false)
            ?? throw new NotFoundException("目标组织", targetId.ToString(CultureInfo.InvariantCulture));

        if (source.IsBuiltin)
        {
            throw new ConflictException("系统内置根节点不可合并", "ORG_ROOT_PROTECTED");
        }

        if (await repo.HasChildOrgsAsync(sourceId, ct).ConfigureAwait(false))
        {
            throw new ConflictException("存在下级组织，不能合并", "ORG_HAS_CHILDREN");
        }

        string oldJson = Serialize(Map(source));
        await EnsureNoActiveReferencesAsync(sourceId, ct).ConfigureAwait(false);

        source.SoftDelete();
        source.Disable();
        source.TouchVersion();
        await repo.UpdateOrgAsync(source, ct).ConfigureAwait(false);
        _ = await repo.SaveChangesAsync(ct).ConfigureAwait(false);
        await WriteSnapshotAsync(source, ct).ConfigureAwait(false);
        return (Map(source), oldJson);
    }

    private async Task ApplyDisableAsync(Organization org, CancellationToken ct)
    {
        if (org.ParentId is null)
        {
            throw new ConflictException("公司根节点不可停用", "ORG_ROOT_PROTECTED");
        }

        await EnsureNoActiveReferencesAsync(org.Id, ct).ConfigureAwait(false);

        org.Disable();
        org.TouchVersion();
        await repo.UpdateOrgAsync(org, ct).ConfigureAwait(false);
        _ = await repo.SaveChangesAsync(ct).ConfigureAwait(false);
        await WriteSnapshotAsync(org, ct).ConfigureAwait(false);
    }

    /// <summary>停用/合并前置：子树内无启用岗位且无在职人员（details 下发岗位名列表）。</summary>
    private async Task EnsureNoActiveReferencesAsync(long orgId, CancellationToken ct)
    {
        IReadOnlyList<Organization> allOrgs = await repo.ListAllOrgsAsync(ct).ConfigureAwait(false);
        List<long> subtree = CollectSubtreeOrgIds(allOrgs, orgId);
        IReadOnlyList<Position> positions = await repo
            .ListEnabledPositionsByOrgIdsAsync(subtree, ct).ConfigureAwait(false);

        IReadOnlyList<TenantMemberSummaryRow> members = await memberQuery
            .ListAllMembersAsync(TenantId, ct).ConfigureAwait(false);
        int memberCount = members.Count(m => m.OrgId.HasValue && subtree.Contains(m.OrgId.Value));

        // Task 3.4：组织（含下级）存在在职人员时禁止停用（人员主数据引用完整性）
        if (await repo.HasActivePersonsUnderOrgsAsync(subtree, ct).ConfigureAwait(false))
        {
            throw new ConflictException(
                "组织（含下级）存在在职人员，不能停用",
                "ORG_HAS_ACTIVE_PERSONS",
                new { OrgIds = subtree });
        }

        if (positions.Count == 0 && memberCount == 0)
        {
            return;
        }

        throw new ConflictException(
            "组织（含下级）存在启用岗位或在职人员，不能停用",
            "ORG_HAS_ACTIVE_REFS",
            new
            {
                Positions = positions.Select(p => p.Name).ToList(),
                MemberCount = memberCount,
            });
    }

    private async Task WriteSnapshotAsync(Organization org, CancellationToken ct)
    {
        OrgNodeHistory history = new(
            TenantId,
            org.Id,
            org.Version,
            Serialize(Map(org)),
            DateTimeOffset.UtcNow,
            ChangedBy);
        await repo.AddOrgHistoryAsync(history, ct).ConfigureAwait(false);
        _ = await repo.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    private Task CaptureAuditAsync(string operation, OrgRow row, string? oldJson, CancellationToken ct)
        => audit.CaptureChangeAsync(
            new AuditLogEntry(
                snowflake.NextId(),
                nameof(Organization),
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

    // ============ 映射与工具 ============

    private static OrgRow Map(Organization org)
        => new(
            org.Id,
            org.ParentId,
            org.Code,
            org.Name,
            (int)org.Type,
            org.LeaderPersonId,
            org.EffectDate,
            org.ExpiryDate,
            org.Version,
            (int)org.Status,
            org.SortOrder,
            org.IsBuiltin,
            org.CreatedAt,
            org.UpdatedAt);

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

    private static string Serialize(OrgRow row)
        => JsonSerializer.Serialize(row, QlxJsonDefaults.PreserveNull);

    private static OrgNodeType ParseType(int type)
        => Enum.IsDefined(typeof(OrgNodeType), type)
            ? (OrgNodeType)type
            : throw new ValidationException("type", $"未知的组织节点类型：{type.ToString(CultureInfo.InvariantCulture)}");

    private static string RequireName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ValidationException("name", "组织名称不能为空");
        }

        return name.Trim();
    }

    private static long RequireOrgId(SubmitOrgAdjustPayload payload)
        => payload.OrgId ?? throw new ValidationException("orgId", "组织调整申请缺少组织 Id");

    private static SaveOrgPayload ToSavePayload(SubmitOrgAdjustPayload payload)
        => new(
            payload.ParentId,
            payload.Name ?? string.Empty,
            payload.Type ?? (int)OrgNodeType.Department,
            payload.LeaderPersonId,
            payload.SortOrder ?? 0);

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
