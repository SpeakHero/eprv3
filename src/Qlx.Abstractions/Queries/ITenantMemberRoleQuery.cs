/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-23 23:30:00 +08:00 中国标准时间 星期四
 * 修改时间：2026-10-23 23:30:00 +08:00 中国标准时间 星期四
 * 描述：租户成员角色只读查询契约，Portal 选择角色步骤与认证服务消费、Bas 模块实现，禁止跨库 Join
 */

namespace Qlx.Abstractions.Queries;

/// <summary>
/// 租户成员可进入的角色项。
/// <para>RoleId 在进程内为真实 long，对外下发前由 Portal 经 Hashids 混淆。</para>
/// </summary>
/// <param name="RoleId">角色 Id。</param>
/// <param name="RoleCode">角色编码（租户内唯一）。</param>
/// <param name="RoleName">角色名称。</param>
/// <param name="IsBuiltin">是否系统内置角色。</param>
public sealed record TenantMemberRoleItem(
    long RoleId,
    string RoleCode,
    string RoleName,
    bool IsBuiltin);

/// <summary>
/// 租户成员角色只读查询契约。
/// <para>Portal 侧在选择角色步骤（身份级 Token）与租户上下文签发阶段消费；Bas 模块负责实现。</para>
/// <para>消费方禁止自行跨库 Join；Bas 实现必须强制 TenantId 过滤。</para>
/// </summary>
public interface ITenantMemberRoleQuery : IModuleQuery
{
    /// <summary>
    /// 查询某租户成员关联的全部有效角色（过滤已软删角色）。
    /// </summary>
    /// <param name="tenantMemberId">租户成员记录 Id。</param>
    /// <param name="tenantId">租户 Id（必须过滤，禁止跨租户查询）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>角色列表（按名称排序）；无角色返回空数组。</returns>
    Task<IReadOnlyList<TenantMemberRoleItem>> ListMemberRolesAsync(
        long tenantMemberId, long tenantId, CancellationToken cancellationToken);
}
