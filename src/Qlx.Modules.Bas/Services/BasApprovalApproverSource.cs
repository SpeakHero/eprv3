/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-23 10:20:00 +08:00 中国标准时间 星期五
 * 描述：Bas 组织侧审批人来源（ROLE / DEPT_LEADER，经 ITenantMemberQuery 跨模块只读查询；
 *       POSITION 岗位体系未落地不支持）。实现 Abstractions 契约，宿主 DI 聚合进组合解析器，模块间零引用。
 */

using Qlx.Abstractions.Approvals;
using Qlx.Abstractions.Queries;

using System.Globalization;

namespace Qlx.Modules.Bas.Services;

/// <summary>
/// Bas 组织侧审批人来源：
/// ROLE——成员主要角色承担（approver_ref=admin/member，映射 TenantMemberRole；岗位体系落地前的简化实现）；
/// DEPT_LEADER——组织管理员承担（approver_ref 为组织 Id，空引用时取实例发起组织）。
/// 仅返回启用（Enabled）成员；解析不到任何人返回空列表（由引擎按策略处置）。
/// </summary>
/// <param name="memberQuery">跨模块租户成员只读查询（Portal 实现，Bas 经 Abstractions 契约消费）。</param>
public sealed class BasApprovalApproverSource(ITenantMemberQuery memberQuery) : IApprovalApproverSource
{
    /// <summary>本来源支持的全部审批人类型。</summary>
    private static readonly string[] s_supportedTypes = [ApprovalApproverTypes.Role, ApprovalApproverTypes.DeptLeader];

    /// <summary>启用成员状态值（MemberStatus.Enabled）。</summary>
    private const int EnabledMemberStatus = 1;

    /// <summary>租户管理员角色值（TenantMemberRole.TenantAdmin）。</summary>
    private const int TenantAdminRole = 1;

    /// <summary>普通成员角色值（TenantMemberRole.OrdinaryMember）。</summary>
    private const int OrdinaryMemberRole = 2;

    private readonly ITenantMemberQuery _memberQuery = memberQuery;

    /// <inheritdoc />
    public bool CanResolve(string approverType)
    {
        return s_supportedTypes.Contains(approverType);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> ResolveAsync(
        ApprovalApproverResolveRequest request, CancellationToken cancellationToken)
    {
        IReadOnlyList<TenantMemberSummaryRow> members = await _memberQuery
            .ListAllMembersAsync(request.TenantId, cancellationToken)
            .ConfigureAwait(false);

        IReadOnlyList<string> resolved = request.ApproverType switch
        {
            ApprovalApproverTypes.Role => ResolveRole(request, members),
            ApprovalApproverTypes.DeptLeader => ResolveDeptLeader(request, members),
            _ => [],
        };
        return resolved;
    }

    /// <summary>
    /// 按角色编码解析（admin→租户管理员 / member→普通成员，大小写不敏感；
    /// POSITION 岗位体系落地前不支持，返回空列表）。
    /// </summary>
    private static IReadOnlyList<string> ResolveRole(
        ApprovalApproverResolveRequest request, IReadOnlyList<TenantMemberSummaryRow> members)
    {
        var targetRole = request.ApproverRef?.Trim().ToLowerInvariant() switch
        {
            "admin" or "tenantadmin" or "1" => TenantAdminRole,
            "member" or "ordinarymember" or "2" => OrdinaryMemberRole,
            _ => 0,
        };
        return targetRole == 0
            ? []
            : [.. members
            .Where(m => m.MemberStatus == EnabledMemberStatus && m.MemberRole == targetRole)
            .Select(m => m.PortalUserId.ToString(CultureInfo.InvariantCulture))
            .Distinct()];
    }

    /// <summary>按组织解析负责人：目标组织内启用管理员；组织内无管理员时回退为组织内全部启用成员。</summary>
    private static IReadOnlyList<string> ResolveDeptLeader(
        ApprovalApproverResolveRequest request, IReadOnlyList<TenantMemberSummaryRow> members)
    {
        var orgRef = request.ApproverRef;
        var orgId = long.TryParse(orgRef, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : request.OrgId;
        if (orgId <= 0)
        {
            return [];
        }

        var orgMembers = members
            .Where(m => m.OrgId == orgId && m.MemberStatus == EnabledMemberStatus)
            .ToList();
        var admins = orgMembers.Where(m => m.MemberRole == TenantAdminRole).ToList();
        List<TenantMemberSummaryRow> picked = admins.Count > 0 ? admins : orgMembers;
        return [.. picked
            .Select(m => m.PortalUserId.ToString(CultureInfo.InvariantCulture))
            .Distinct()];
    }
}
