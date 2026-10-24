/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-22 15:55:00 +08:00 中国标准时间 星期三
 * 修改时间：2026-10-22 15:55:00 +08:00 中国标准时间 星期三
 * 描述：租户主体查询契约（聚合返回用户 + 角色 + 组织归属 + 是否租户管理员）
 */

namespace Qlx.Abstractions.Tenancy;

/// <summary>
/// 租户主体查询接口。供权限判断、ACL 主体扩展等场景聚合查询用户所属的租户主体。
/// </summary>
public interface ITenantSubjectQuery
{
    /// <summary>
    /// 查询租户内指定用户的全部主体归属。
    /// </summary>
    /// <param name="tenantId">租户 ID。</param>
    /// <param name="portalUserId">门户用户 ID。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>聚合主体信息（未找到用户时返回空列表 + IsTenantAdmin = false）。</returns>
    Task<TenantSubjects> GetSubjectsAsync(Guid tenantId, Guid portalUserId, CancellationToken cancellationToken);
}

/// <summary>
/// 租户主体聚合结果。
/// </summary>
/// <param name="MemberIds">用户所属成员组 ID 列表。</param>
/// <param name="RoleIds">用户持有角色 ID 列表。</param>
/// <param name="OrgIds">用户所属组织 ID 列表。</param>
/// <param name="IsTenantAdmin">是否租户管理员。</param>
public sealed record TenantSubjects(
    IReadOnlyList<string> MemberIds,
    IReadOnlyList<string> RoleIds,
    IReadOnlyList<string> OrgIds,
    bool IsTenantAdmin);
