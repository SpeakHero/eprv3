/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-22 19:40:00 +08:00 中国标准时间 星期四
 * 描述：审批流程注册表——代码默认定义内存注册（2A 契约）+ DB 启用版本解析（R1 唯一启用、R2 版本锁定）
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
/// 启用流程定义聚合（定义 + 节点 + 分支，一次批量查询装配，禁止 N+1）。
/// </summary>
/// <param name="Definition">流程定义行。</param>
/// <param name="Nodes">节点列表（SortNo 升序）。</param>
/// <param name="Branches">分支列表（Priority 降序）。</param>
public sealed record EnabledApprovalFlow(
    ApprovalFlowDefinition Definition,
    IReadOnlyList<ApprovalFlowNode> Nodes,
    IReadOnlyList<ApprovalFlowBranch> Branches)
{
    /// <summary>按节点键取节点（定义内不存在返回 null）。</summary>
    /// <param name="nodeKey">节点键。</param>
    /// <returns>节点实体。</returns>
    public ApprovalFlowNode? FindNode(string nodeKey)
        => Nodes.FirstOrDefault(n => string.Equals(n.NodeKey, nodeKey, StringComparison.Ordinal));

    /// <summary>取指定来源节点按优先级降序排好的分支线。</summary>
    /// <param name="fromNodeKey">来源节点键。</param>
    /// <returns>分支列表（Priority 降序）。</returns>
    public IReadOnlyList<ApprovalFlowBranch> FindBranches(string fromNodeKey)
        => [.. Branches
            .Where(b => string.Equals(b.FromNodeKey, fromNodeKey, StringComparison.Ordinal))
            .OrderByDescending(b => b.Priority)];
}

/// <summary>
/// 审批流程注册表。
/// 代码默认定义经 <see cref="Register"/> 内存注册（2A 契约能力）；
/// 单据提交时经 <see cref="ResolveEnabledAsync"/> 解析 DB 启用版本并锁定 def_version（R1/R2）。
/// </summary>
/// <param name="db">租户上下文。</param>
/// <param name="seedService">种子服务（新租户零流程定义时懒种子默认流程，与任务定义懒种子同模式）。</param>
public sealed class ApprovalFlowRegistry(SupportTenantDbContext db, Files.SupportSeedService seedService) : IApprovalFlowRegistry
{
    private readonly SupportTenantDbContext _db = db;
    private readonly Files.SupportSeedService _seedService = seedService;

    private readonly object _gate = new();
    private readonly Dictionary<string, string> _codeDefinitions = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public void Register(string definitionCode, string definitionJson)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(definitionCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(definitionJson);

        lock (_gate)
        {
            _codeDefinitions[definitionCode] = definitionJson;
        }
    }

    /// <inheritdoc />
    public string? GetDefinition(string definitionCode)
    {
        lock (_gate)
        {
            return _codeDefinitions.TryGetValue(definitionCode, out var json) ? json : null;
        }
    }

    /// <summary>
    /// 解析单据类型 + 发起组织的唯一启用流程定义（R1），节点与分支批量查询装配（禁止 N+1）。
    /// 组织匹配规则：定义 org_scope 为空数组 = 全租户；否则组织 Id 数组须包含发起组织。
    /// </summary>
    /// <param name="bizType">单据类型。</param>
    /// <param name="orgId">发起组织 Id。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>启用流程聚合（未配置任何启用流程返回 null）。</returns>
    /// <exception cref="BusinessException">同 biz_type + 组织范围存在多个启用版本（脏数据，R1 被破坏）时抛出。</exception>
    public async Task<EnabledApprovalFlow?> ResolveEnabledAsync(
        string bizType, long orgId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bizType);

        var definitions = await _db.FlowDefinitions
            .AsNoTracking()
            .Where(d => d.BizType == bizType && d.Status == ApprovalFlowStatus.Enabled)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        // 懒种子：仅租户级零流程定义时补种默认流程后重查一次（与任务定义懒种子同模式）；
        // 已有定义但本 bizType 未命中属正常"未配置"，不触发种子避免无效往返
        if (definitions.Count == 0
            && !await _db.FlowDefinitions.AnyAsync(cancellationToken).ConfigureAwait(false))
        {
            await _seedService.EnsureApprovalFlowsSeededAsync(_db.CurrentTenantId, cancellationToken)
                .ConfigureAwait(false);
            definitions = await _db.FlowDefinitions
                .AsNoTracking()
                .Where(d => d.BizType == bizType && d.Status == ApprovalFlowStatus.Enabled)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
        }

        var matched = definitions
            .Where(d => MatchesOrgScope(d.OrgScopeJson, orgId))
            .OrderByDescending(d => d.Version)
            .ToList();
        if (matched.Count == 0)
        {
            return null;
        }

        if (matched.Count > 1)
        {
            // R1：同 biz_type + 组织范围仅允许一个启用版本；多命中说明启用动作未互斥，属配置错误
            throw new ConflictException(
                $"单据类型 {bizType}（组织 {orgId}）命中 {matched.Count} 个启用流程定义（{string.Join("、", matched.Select(d => $"{d.DefCode}v{d.Version}"))}），违反唯一启用规则 R1。");
        }

        var definition = matched[0];
        var nodes = await _db.FlowNodes
            .AsNoTracking()
            .Where(n => n.DefId == definition.Id)
            .OrderBy(n => n.SortNo)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var branches = await _db.FlowBranches
            .AsNoTracking()
            .Where(b => b.DefId == definition.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return new EnabledApprovalFlow(definition, nodes, [.. branches.OrderByDescending(b => b.Priority)]);
    }

    /// <summary>
    /// 按实例锁定的定义 Id 装载流程聚合（R2 版本隔离：在途实例始终按提交时锁定版本流转，不受新版本影响）。
    /// </summary>
    /// <param name="defId">锁定的流程定义 Id。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>流程聚合。</returns>
    /// <exception cref="NotFoundException">定义行不存在（数据被异常删除）时抛出。</exception>
    public async Task<EnabledApprovalFlow> LoadByDefinitionIdAsync(long defId, CancellationToken cancellationToken)
    {
        var definition = await _db.FlowDefinitions
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == defId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new NotFoundException("流程定义", defId.ToString(CultureInfo.InvariantCulture));

        var nodes = await _db.FlowNodes
            .AsNoTracking()
            .Where(n => n.DefId == defId)
            .OrderBy(n => n.SortNo)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var branches = await _db.FlowBranches
            .AsNoTracking()
            .Where(b => b.DefId == defId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return new EnabledApprovalFlow(definition, nodes, [.. branches.OrderByDescending(b => b.Priority)]);
    }

    /// <summary>
    /// 校验启用流程定义完整性（R3）：每个审批节点必须有出边（链尾无出边会使运行期误入「未命中终止」），
    /// 且每条来源分支线必须存在兜底（is_default）。
    /// </summary>
    public static void EnsureBranchDefaults(EnabledApprovalFlow flow)
    {
        foreach (var node in flow.Nodes.Where(n => n.NodeType == ApprovalNodeType.Approval))
        {
            if (!flow.Branches.Any(b => b.FromNodeKey == node.NodeKey))
            {
                throw new ConflictException(
                    $"流程 {flow.Definition.DefCode} 节点 {node.NodeKey} 无任何出边分支，链尾必须以兜底分支收敛（R3）。");
            }
        }

        foreach (var group in flow.Branches.GroupBy(b => b.FromNodeKey))
        {
            if (!group.Any(b => b.IsDefault))
            {
                throw new ConflictException(
                    $"流程 {flow.Definition.DefCode} 节点 {group.Key} 的分支线缺少兜底分支（is_default），拒绝启用（R3）。");
            }
        }
    }

    /// <summary>判定定义组织范围是否覆盖发起组织（空数组 = 全租户）。</summary>
    /// <param name="orgScopeJson">组织范围 JSON。</param>
    /// <param name="orgId">发起组织 Id。</param>
    /// <returns>覆盖返回 true。</returns>
    private static bool MatchesOrgScope(string orgScopeJson, long orgId)
    {
        if (string.IsNullOrWhiteSpace(orgScopeJson) || orgScopeJson == "[]")
        {
            return true;
        }

        var scope = JsonSerializer.Deserialize<long[]>(orgScopeJson, QlxJsonDefaults.Default);
        return scope is null || scope.Length == 0 || scope.Contains(orgId);
    }
}
