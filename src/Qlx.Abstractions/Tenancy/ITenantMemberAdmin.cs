/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-21
 * 描述：跨模块租户成员写入契约——Bas 等业务模块需要操作 Portal 库 por_tenant_members / por_users /
 *       por_user_password_histories 时经此接口，禁止跨模块引用 Portal 类型。
 *       实现注册由 Portal 模块提供。
 */

namespace Qlx.Abstractions.Tenancy;

/// <summary>
/// 租户成员管理（写入）接口。覆盖租户自管理域需要的全部 Portal 库成员侧写入操作：
/// 邀请、启停、移除、重置密码、组织归属变更。
/// </summary>
public interface ITenantMemberAdmin
{
    /// <summary>
    /// 邀请成员加入指定租户（创建 PortalUser + TenantMember；若 PortalUser 已存在则直接挂 TenantMember）。
    /// </summary>
    /// <returns>TenantMemberId + PortalUserId；失败（账号无效/已是成员）返回 null。</returns>
    Task<(long TenantMemberId, long PortalUserId)?> InviteMemberAsync(
        long tenantId, string account, string displayName, long orgId, CancellationToken cancellationToken);

    /// <summary>
    /// 租户内启停（TenantMember.Status 切换 + TokenVersion++ 使本租户 Token 失效）。
    /// </summary>
    Task ToggleMemberStatusAsync(long tenantMemberId, CancellationToken cancellationToken);

    /// <summary>
    /// 重发邀请（仅 <c>PendingInvite</c> 状态成员允许，否则 409）。
    /// </summary>
    /// <returns>关联的门户用户 Id（供调用方发送邀请通知）。</returns>
    Task<long> ResendInviteAsync(long tenantMemberId, CancellationToken cancellationToken);

    /// <summary>
    /// 从租户移除成员（软删 TenantMember + 清理关联）。
    /// </summary>
    Task RemoveMemberFromTenantAsync(long tenantMemberId, CancellationToken cancellationToken);

    /// <summary>
    /// 重置租户成员密码（PortalUser.PasswordHash + 历史禁重 + TokenVersion++）。
    /// </summary>
    Task ResetMemberPasswordAsync(long tenantMemberId, string newPassword, CancellationToken cancellationToken);

    /// <summary>
    /// 更新租户成员组织归属（TenantMember.OrgId）。
    /// </summary>
    Task UpdateMemberOrgAsync(long tenantMemberId, long? orgId, CancellationToken cancellationToken);
}
