/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-23 03:10:00 +08:00 中国标准时间 星期五
 * 描述：租户主体查询 Bas 实现（ITenantSubjectQuery）——经 ITenantMemberQuery 聚合成员归属，供 ACL/权限判定消费
 */

using Qlx.Abstractions.Queries;
using Qlx.Abstractions.Tenancy;
using Qlx.BuildingBlocks.Ids;

namespace Qlx.Modules.Bas.Services;

/// <summary>
/// 租户主体查询实现。2B 阶段 Bas 仅承载成员归属（MemberIds = 成员行 Id、OrgIds = 主属组织），
/// 角色聚合随 Bas 角色体系落地后补充；管理员判定按成员主要角色（MemberRole = 1）。
/// 经 <see cref="ITenantMemberQuery.ListAllMembersAsync"/> 单次拉取（该接口约定用于小场景，成员规模可控）。
/// </summary>
public sealed class BasTenantSubjectQuery(ITenantMemberQuery memberQuery) : ITenantSubjectQuery
{
    /// <summary>租户管理员主要角色枚举值（Portal 侧约定：1 = 管理员）。</summary>
    private const int AdminMemberRole = 1;

    private readonly ITenantMemberQuery _memberQuery = memberQuery;

    /// <inheritdoc />
    public async Task<TenantSubjects> GetSubjectsAsync(
        Guid tenantId, Guid portalUserId, CancellationToken cancellationToken)
    {
        if (!GuidIdMapper.TryToLong(tenantId, out var tenantLong) || tenantLong <= 0
            || !GuidIdMapper.TryToLong(portalUserId, out var userLong) || userLong <= 0)
        {
            return new TenantSubjects([], [], [], IsTenantAdmin: false);
        }

        var members = await _memberQuery
            .ListAllMembersAsync(tenantLong, cancellationToken)
            .ConfigureAwait(false);
        var member = members.FirstOrDefault(m => m.PortalUserId == userLong);
        if (member is null)
        {
            return new TenantSubjects([], [], [], IsTenantAdmin: false);
        }

        var memberIds = new List<string> { member.TenantMemberId.ToString(System.Globalization.CultureInfo.InvariantCulture) };
        var orgIds = member.OrgId is > 0
            ? new List<string> { member.OrgId.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) }
            : [];

        return new TenantSubjects(memberIds, [], orgIds, member.MemberRole == AdminMemberRole);
    }
}
