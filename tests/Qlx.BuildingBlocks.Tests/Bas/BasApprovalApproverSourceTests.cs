/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-25 23:55:00 +08:00 中国标准时间 星期日
 * 描述：Bas 组织侧审批人来源测试——ROLE / DEPT_LEADER 返回的用户 Id 必须与 ICurrentUserContext.UserId 同编码
 *       （token sub claim 的 Hashids 混淆串），禁止落原始 long；禁用成员 / 其他角色不返回。
 */

using Qlx.Abstractions.Approvals;
using Qlx.Abstractions.Queries;
using Qlx.BuildingBlocks.Ids;
using Qlx.Modules.Bas.Services;

namespace Qlx.BuildingBlocks.Tests.Bas;

/// <summary>
/// <see cref="BasApprovalApproverSource"/> 单元测试：审批人用户 Id 混淆一致性（缺陷回归）。
/// </summary>
public sealed class BasApprovalApproverSourceTests
{
    private const long TenantId = 42L;

    private const int EnabledMemberStatus = 1;

    private const int DisabledMemberStatus = 2;

    private const int TenantAdminRole = 1;

    private const int OrdinaryMemberRole = 2;

    /// <summary>真实 Hashids 混淆器（与宿主 AddQlxIds 同一实现），用于逐项比对 Encode 输出。</summary>
    private static readonly HashidsObfuscator s_obfuscator =
        new("bas-approver-test-salt", minLength: 8);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static TenantMemberSummaryRow Member(
        long memberId, long portalUserId, long? orgId, int role, int status)
        => new(
            memberId,
            TenantId,
            portalUserId,
            $"user{memberId}",
            $"成员{memberId}",
            Phone: null,
            Email: null,
            OrgId: orgId,
            MemberRole: role,
            MemberStatus: status,
            JoinAt: null,
            LastLoginAt: null);

    private static ApprovalApproverResolveRequest Request(
        string approverType, string? approverRef, long orgId = 0)
        => new(
            approverType,
            approverRef,
            TenantId,
            "initiator-user",
            orgId,
            new Dictionary<string, object?>(StringComparer.Ordinal));

    [Fact]
    public async Task RoleResolvesOnlyEnabledAdminsAsObfuscatedIds()
    {
        var query = new StubTenantMemberQuery();
        query.Members.AddRange(
        [
            // 真实缺陷场景：管理员 PortalUserId 为上亿雪花 Id，落原始 long 会与 token sub 不一致
            Member(1, 106001476018507776L, orgId: 10, role: TenantAdminRole, status: EnabledMemberStatus),
            Member(2, 7002L, orgId: 10, role: TenantAdminRole, status: DisabledMemberStatus),
            Member(3, 7003L, orgId: 10, role: OrdinaryMemberRole, status: EnabledMemberStatus),
        ]);

        var source = new BasApprovalApproverSource(query, s_obfuscator);
        IReadOnlyList<string> resolved = await source.ResolveAsync(
            Request(ApprovalApproverTypes.Role, "admin"), Ct);

        Assert.Single(resolved);
        Assert.Equal(s_obfuscator.Encode(106001476018507776L), resolved[0]);
        Assert.DoesNotContain(resolved, static id => id == "106001476018507776");
        Assert.True(s_obfuscator.TryDecode(resolved[0], out long decoded));
        Assert.Equal(106001476018507776L, decoded);
    }

    [Fact]
    public async Task DeptLeaderResolvesOrgAdminsAsObfuscatedIds()
    {
        var query = new StubTenantMemberQuery();
        query.Members.AddRange(
        [
            Member(11, 106001476018507776L, orgId: 10, role: TenantAdminRole, status: EnabledMemberStatus),
            Member(12, 7002L, orgId: 10, role: TenantAdminRole, status: DisabledMemberStatus),
            Member(13, 7003L, orgId: 10, role: OrdinaryMemberRole, status: EnabledMemberStatus),
            Member(14, 7004L, orgId: 20, role: TenantAdminRole, status: EnabledMemberStatus),
        ]);

        var source = new BasApprovalApproverSource(query, s_obfuscator);
        IReadOnlyList<string> resolved = await source.ResolveAsync(
            Request(ApprovalApproverTypes.DeptLeader, "10", orgId: 10), Ct);

        Assert.Single(resolved);
        Assert.Equal(s_obfuscator.Encode(106001476018507776L), resolved[0]);
        Assert.DoesNotContain(resolved, static id => id == "106001476018507776");
    }
}
