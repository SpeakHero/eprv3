/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-21
 * 描述：跨模块租户成员只读查询契约——业务模块（如 Bas）需实时查询 Portal 库 por_tenant_members + por_users 基础信息时使用。
 *       Portal 模块负责实现并注册；消费方禁止自行跨库 Join，一律经本接口。
 */

namespace Qlx.Abstractions.Queries;

/// <summary>
/// 租户成员+关联 PortalUser 的扁平只读视图（Portal 库跨表投影，供业务模块消费）。
/// </summary>
/// <param name="TenantMemberId">租户成员行 Id（por_tenant_members.id）。</param>
/// <param name="TenantId">所属租户 Id。</param>
/// <param name="PortalUserId">关联自然人 Id（por_users.id）。</param>
/// <param name="LoginAccount">全局登录账号。</param>
/// <param name="DisplayName">租户内展示名（为空回落 PortalUser.DisplayName）。</param>
/// <param name="Phone">手机号（可能为空）。</param>
/// <param name="Email">邮箱（可能为空）。</param>
/// <param name="OrgId">主属组织 Id（预留字段）。</param>
/// <param name="MemberRole">租户内主要角色枚举值。</param>
/// <param name="MemberStatus">租户内本地状态枚举值。</param>
/// <param name="JoinAt">加入租户时间。</param>
/// <param name="LastLoginAt">最近一次进入本租户时间。</param>
public record TenantMemberSummaryRow(
    long TenantMemberId,
    long TenantId,
    long PortalUserId,
    string LoginAccount,
    string DisplayName,
    string? Phone,
    string? Email,
    long? OrgId,
    int MemberRole,
    int MemberStatus,
    DateTimeOffset? JoinAt,
    DateTimeOffset? LastLoginAt);

/// <summary>
/// 跨模块租户成员只读查询接口（标记 IModuleQuery 以表示实时一致的只读查询契约）。
/// </summary>
public interface ITenantMemberQuery : IModuleQuery
{
    /// <summary>
    /// 检查某登录账号是否已是本租户成员（用于邀请前校验）。
    /// </summary>
    /// <param name="tenantId">租户 Id。</param>
    /// <param name="account">登录账号（手机号或邮箱），大小写不敏感。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task<bool> IsMemberExistsAsync(long tenantId, string account, CancellationToken cancellationToken);

    /// <summary>
    /// 根据登录账号查找或创建 PortalUser 预览（用于邀请流程）。
    /// </summary>
    /// <param name="account">登录账号。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>PortalUser 预览；若账号不存在则返回 null。</returns>
    Task<PortalUserPreview?> FindPortalUserByAccountAsync(string account, CancellationToken cancellationToken);

    /// <summary>
    /// 分页查询租户成员列表（含 PortalUser 基础信息投影）。
    /// </summary>
    /// <param name="tenantId">租户 Id。</param>
    /// <param name="keyword">搜索关键字（账号/显示名/手机/邮箱）。</param>
    /// <param name="orgId">筛选主属组织（含下级由消费方自行展开）。</param>
    /// <param name="role">按主要角色筛选（null=不限）。</param>
    /// <param name="status">按本地状态筛选（null=不限）。</param>
    /// <param name="skip">跳过条数。</param>
    /// <param name="take">返回条数。</param>
    /// <param name="orgIds">数据范围组织 Id 集（null=不限；X-5 整改：消费方按角色 DefaultDataScope 展开后传入）。</param>
    /// <param name="onlyMemberId">仅返回该成员（Self 数据范围；null=不限）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task<(IReadOnlyList<TenantMemberSummaryRow> Items, int Total)> QueryMembersAsync(
        long tenantId, string? keyword, long? orgId, int? role, int? status,
        int skip, int take, CancellationToken cancellationToken,
        IReadOnlyCollection<long>? orgIds = null, long? onlyMemberId = null);

    /// <summary>
    /// 查询单个租户成员详情（含 PortalUser 基础信息）。
    /// </summary>
    /// <param name="tenantMemberId">租户成员行 Id。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task<TenantMemberSummaryRow?> GetMemberDetailAsync(long tenantMemberId, CancellationToken cancellationToken);

    /// <summary>
    /// 查询指定租户的全部成员摘要（不分页，用于角色成员下拉等小场景）。
    /// </summary>
    Task<IReadOnlyList<TenantMemberSummaryRow>> ListAllMembersAsync(long tenantId, CancellationToken cancellationToken);
}

/// <summary>
/// PortalUser 预览记录（仅邀请流程需要的最小字段集）。
/// </summary>
/// <param name="PortalUserId">自然人 Id。</param>
/// <param name="LoginAccount">登录账号。</param>
/// <param name="DisplayName">显示名。</param>
/// <param name="Phone">手机号（可能为空）。</param>
/// <param name="Email">邮箱（可能为空）。</param>
public record PortalUserPreview(
    long PortalUserId,
    string LoginAccount,
    string DisplayName,
    string? Phone,
    string? Email);
