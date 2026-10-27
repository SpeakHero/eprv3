/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.1.0 beta
 * 创建时间：2026-10-23 10:20:00 +08:00 中国标准时间 星期五
 * 修改时间：2026-10-25 21:55:00 +08:00 中国标准时间 星期日
 * 描述：Bas 组织侧审批人来源（ROLE / DEPT_LEADER，经 ITenantMemberQuery 跨模块只读查询；
 *       POSITION 岗位体系未落地不支持）。实现 Abstractions 契约，宿主 DI 聚合进组合解析器，模块间零引用。
 *       审批人用户 Id 与 ICurrentUserContext.UserId 同编码（Hashids 混淆串），禁止落原始 long。
 */

using System.Globalization;
using System.Runtime.CompilerServices;
using System.Collections.ObjectModel;

using Qlx.Abstractions.Approvals;
using Qlx.Abstractions.Queries;
using Qlx.BuildingBlocks.Ids;

namespace Qlx.Modules.Bas.Services;

/// <summary>
/// Bas 组织侧审批人来源：
/// ROLE——成员主要角色承担（approver_ref=admin/member，映射 TenantMemberRole；岗位体系落地前的简化实现）；
/// DEPT_LEADER——组织管理员承担（approver_ref 为组织 Id，空引用时取实例发起组织）。
/// 仅返回启用（Enabled）成员；解析不到任何人返回空列表（由引擎按策略处置）。
/// </summary>
/// <param name="memberQuery">跨模块租户成员只读查询（Portal 实现，Bas 经 Abstractions 契约消费）。</param>
/// <param name="obfuscator">ID 混淆器：契约要求的用户 Id 与 <c>ICurrentUserContext.UserId</c> 同编码
/// （token sub claim 为 Hashids 混淆串），不得落原始 long。</param>
public sealed class BasApprovalApproverSource(
    ITenantMemberQuery memberQuery,
    IIdObfuscator obfuscator) : IApprovalApproverSource
{
    #region 常量定义
    /// <summary>启用成员状态值（MemberStatus.Enabled）。</summary>
    private const int EnabledMemberStatus = 1;

    /// <summary>租户管理员角色值（TenantMemberRole.TenantAdmin）。</summary>
    private const int TenantAdminRole = 1;

    /// <summary>普通成员角色值（TenantMemberRole.OrdinaryMember）。</summary>
    private const int OrdinaryMemberRole = 2;
    #endregion

    /// <inheritdoc />
    public bool CanResolve(string approverType)
    {
        return string.Equals(approverType, ApprovalApproverTypes.Role, StringComparison.Ordinal)
            || string.Equals(approverType, ApprovalApproverTypes.DeptLeader, StringComparison.Ordinal);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> ResolveAsync(
        ApprovalApproverResolveRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        IReadOnlyList<TenantMemberSummaryRow> members = await memberQuery
            .ListAllMembersAsync(request.TenantId, cancellationToken)
            .ConfigureAwait(false);

        if (members.Count == 0)
            return [];

        return request.ApproverType switch
        {
            ApprovalApproverTypes.Role => ResolveRole(request, members),
            ApprovalApproverTypes.DeptLeader => ResolveDeptLeader(request, members),
            _ => []
        };
    }

    /// <summary>
    /// 按角色编码解析（admin→租户管理员 / member→普通成员，大小写不敏感；
    /// POSITION 岗位体系落地前不支持，返回空列表）。
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private ReadOnlyCollection<string> ResolveRole(
        ApprovalApproverResolveRequest request,
        IReadOnlyList<TenantMemberSummaryRow> members)
    {
        var approverRef = request.ApproverRef;
        if (string.IsNullOrWhiteSpace(approverRef))
            return Array.Empty<string>().AsReadOnly();

        ReadOnlySpan<char> refSpan = approverRef.AsSpan().Trim();
        int targetRole = ResolveTargetRole(refSpan);

        if (targetRole == 0)
            return Array.Empty<string>().AsReadOnly();

        var result = new List<string>(members.Count);
        foreach (var m in members)
        {
            if (m.MemberStatus == EnabledMemberStatus && m.MemberRole == targetRole)
            {
                result.Add(obfuscator.Encode(m.PortalUserId));
            }
        }

        return result.AsReadOnly();
    }

    /// <summary>
    /// 零分配解析目标角色编码，大小写不敏感。
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int ResolveTargetRole(ReadOnlySpan<char> refSpan)
    {
        if (refSpan.Equals("admin", StringComparison.OrdinalIgnoreCase)
            || refSpan.Equals("tenantadmin", StringComparison.OrdinalIgnoreCase)
            || refSpan.Equals("1", StringComparison.OrdinalIgnoreCase))
        {
            return TenantAdminRole;
        }

        if (refSpan.Equals("member", StringComparison.OrdinalIgnoreCase)
            || refSpan.Equals("ordinarymember", StringComparison.OrdinalIgnoreCase)
            || refSpan.Equals("2", StringComparison.OrdinalIgnoreCase))
        {
            return OrdinaryMemberRole;
        }

        return 0;
    }

    ///<summary>按组织解析负责人：目标组织内启用管理员；组织内无管理员时回退为组织内全部启用成员。</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private ReadOnlyCollection<string> ResolveDeptLeader(
        ApprovalApproverResolveRequest request,
        IReadOnlyList<TenantMemberSummaryRow> members)
    {
        long orgId = ResolveOrgId(request);
        if (orgId <= 0)
            return Array.Empty<string>().AsReadOnly();

        var admins = new List<TenantMemberSummaryRow>();
        var allOrgMembers = new List<TenantMemberSummaryRow>();

        foreach (var m in members)
        {
            if (m.OrgId != orgId || m.MemberStatus != EnabledMemberStatus)
                continue;

            allOrgMembers.Add(m);
            if (m.MemberRole == TenantAdminRole)
            {
                admins.Add(m);
            }
        }

        var picked = admins.Count > 0 ? admins : allOrgMembers;
        if (picked.Count == 0)
            return Array.Empty<string>().AsReadOnly();

        var result = new List<string>(picked.Count);
        foreach (var m in picked)
        {
            result.Add(obfuscator.Encode(m.PortalUserId));
        }

        return result.AsReadOnly();
    }

    /// <summary>
    /// 解析目标组织Id，解析失败或空引用时回退到请求发起组织。
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static long ResolveOrgId(ApprovalApproverResolveRequest request)
    {
        var orgRef = request.ApproverRef;
        if (!string.IsNullOrWhiteSpace(orgRef)
            && long.TryParse(orgRef, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedOrgId)
            && parsedOrgId > 0)
        {
            return parsedOrgId;
        }

        return request.OrgId;
    }
}
