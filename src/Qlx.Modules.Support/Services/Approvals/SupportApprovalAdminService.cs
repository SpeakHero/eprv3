/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-23 09:50:00 +08:00 中国标准时间 星期五
 * 描述：审批中心模块门面服务——流程定义查询 / 已办列表 / 委托管理（批量查询，禁止 N+1）
 */

using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Qlx.Abstractions.Approvals;
using Qlx.Abstractions.Exceptions;
using Qlx.Abstractions.Serialization;
using Qlx.Modules.Support.Domain;

namespace Qlx.Modules.Support.Services.Approvals;

/// <summary>
/// 审批中心模块门面服务实现。<see cref="ISupportApprovalAdminService"/>。
/// 委托 Id 对外混淆串（按 ID 收发生命周期约定，Service 层解析）。
/// </summary>
/// <param name="db">租户上下文。</param>
/// <param name="seedService">种子服务（新租户零流程定义时懒种子默认流程）。</param>
public sealed class SupportApprovalAdminService(
    SupportTenantDbContext db,
    Files.SupportSeedService seedService) : ISupportApprovalAdminService
{
    private readonly SupportTenantDbContext _db = db;
    private readonly Files.SupportSeedService _seedService = seedService;

    private static readonly CultureInfo s_culture = CultureInfo.InvariantCulture;

    /// <inheritdoc />
    public async Task<IReadOnlyList<SupportFlowSummary>> ListFlowsAsync(CancellationToken cancellationToken)
    {
        var definitions = await _db.FlowDefinitions
            .AsNoTracking()
            .OrderByDescending(d => d.UpdatedAt)
            .Take(200)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        // 懒种子：租户零流程定义时补种默认流程后重查一次（与注册表解析路径同模式）
        if (definitions.Count == 0)
        {
            await _seedService.EnsureApprovalFlowsSeededAsync(_db.CurrentTenantId, cancellationToken)
                .ConfigureAwait(false);
            definitions = await _db.FlowDefinitions
                .AsNoTracking()
                .OrderByDescending(d => d.UpdatedAt)
                .Take(200)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
            if (definitions.Count == 0)
            {
                return [];
            }
        }

        // 节点 / 分支计数一次批量聚合（GroupBy 投影，禁止逐定义查询）
        var defIds = definitions.Select(static d => d.Id).ToList();
        var nodeCounts = await _db.FlowNodes
            .AsNoTracking()
            .Where(n => defIds.Contains(n.DefId))
            .GroupBy(static n => n.DefId)
            .Select(static g => new { DefId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(static x => x.DefId, static x => x.Count, cancellationToken)
            .ConfigureAwait(false);
        var branchCounts = await _db.FlowBranches
            .AsNoTracking()
            .Where(b => defIds.Contains(b.DefId))
            .GroupBy(static b => b.DefId)
            .Select(static g => new { DefId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(static x => x.DefId, static x => x.Count, cancellationToken)
            .ConfigureAwait(false);

        return [.. definitions.Select(d => new SupportFlowSummary(
            d.DefCode,
            d.DefName,
            d.BizType,
            d.Version,
            d.Status.ToString(),
            d.OrgScopeJson,
            nodeCounts.GetValueOrDefault(d.Id),
            branchCounts.GetValueOrDefault(d.Id),
            d.UpdatedAt))];
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ApprovalTaskInfo>> ListMyDoneTasksAsync(string userId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        var tasks = await _db.Tasks
            .AsNoTracking()
            .Where(t => t.ApproverId == userId && t.Status != ApprovalTaskStatus.Pending)
            .OrderByDescending(t => t.HandledAt)
            .Take(200)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        if (tasks.Count == 0)
        {
            return [];
        }

        var instIds = tasks.Select(static t => t.InstId).Distinct().ToList();
        var instances = await _db.Instances
            .AsNoTracking()
            .Where(i => instIds.Contains(i.Id))
            .ToDictionaryAsync(static i => i.Id, cancellationToken)
            .ConfigureAwait(false);

        return [.. tasks.Select(t => new ApprovalTaskInfo(
            t.Id.ToString(s_culture),
            t.InstId.ToString(s_culture),
            t.NodeKey,
            instances.TryGetValue(t.InstId, out var inst) ? $"{inst.Title}（{t.NodeName}）" : t.NodeName,
            t.ApproverId,
            t.Status,
            t.CreatedAt,
            t.HandledAt,
            t.Opinion))];
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ApprovalDelegateInfo>> ListMyDelegatesAsync(string userId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        var delegations = await _db.Delegates
            .AsNoTracking()
            .Where(d => d.DelegatorId == userId)
            .OrderByDescending(d => d.StartTime)
            .Take(100)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return [.. delegations.Select(d => new ApprovalDelegateInfo(
            d.Id.ToString(s_culture),
            d.DelegatorId,
            d.AgentId,
            d.StartTime,
            d.EndTime,
            d.Reason))];
    }

    /// <inheritdoc />
    public async Task<string> CreateDelegateAsync(
        string delegatorId, SupportCreateDelegateRequest request, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(delegatorId);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.AgentUserId);
        if (string.Equals(request.AgentUserId, delegatorId, StringComparison.Ordinal))
        {
            throw new Qlx.Abstractions.Exceptions.ConflictException("代理人不能是委托人本人。");
        }

        var bizTypesJson = request.BizTypes is { Count: > 0 }
            ? JsonSerializer.Serialize(request.BizTypes, QlxJsonDefaults.Default)
            : "[]";
        var delegation = new ApprovalDelegation(
            _db.CurrentTenantId, delegatorId, request.AgentUserId, bizTypesJson,
            request.StartAt, request.EndAt, request.Reason, DateTimeOffset.UtcNow);
        await _db.Delegates.AddAsync(delegation, cancellationToken).ConfigureAwait(false);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return delegation.Id.ToString(s_culture);
    }

    /// <inheritdoc />
    public async Task RevokeDelegateAsync(string delegatorId, string delegateId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(delegatorId);

        if (!long.TryParse(delegateId, NumberStyles.Integer, s_culture, out var id) || id <= 0)
        {
            throw new ValidationException(nameof(delegateId), $"无效的委托 Id：{delegateId}。");
        }

        var delegation = await _db.Delegates
            .FirstOrDefaultAsync(d => d.Id == id, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new NotFoundException("审批委托", delegateId);
        if (!string.Equals(delegation.DelegatorId, delegatorId, StringComparison.Ordinal))
        {
            throw new ForbiddenException("仅委托人本人可撤销委托。");
        }

        delegation.Revoke(DateTimeOffset.UtcNow);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
