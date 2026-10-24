/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-23 23:00:00 +08:00 中国标准时间 星期四
 * 修改时间：2026-10-23 23:00:00 +08:00 中国标准时间 星期四
 * 描述：门户用户-角色双向分配服务契约（por_user_roles 表双向全量替换）
 */

using Qlx.Modules.Portal.Dtos;

namespace Qlx.Modules.Portal.Services;

/// <summary>
/// 门户用户-角色双向分配服务契约。
/// 正向：按角色 → 成员；反向：按用户 → 角色。
/// 写路径全量替换（事务包裹，先删后插）。
/// </summary>
public interface IPortalUserRoleAdminService
{
    // ======= 正向：按角色 → 成员 =======

    /// <summary>
    /// 查询指定角色的成员列表（禁止跨模块 Join：先查 UserId 集合 → Batch 查 PortalUser）。
    /// </summary>
    /// <param name="roleId">角色 Id。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>角色成员列表（含登录账号 / 显示名）。</returns>
    Task<IReadOnlyList<UserRoleMember>> ListMembersAsync(long roleId, CancellationToken ct);

    /// <summary>
    /// 查询可选用户清单（所有已启用的 PortalUser）。
    /// </summary>
    /// <param name="ct">取消令牌。</param>
    /// <returns>可选用户列表（按 LoginAccount 排序）。</returns>
    Task<IReadOnlyList<UserOption>> GetUserOptionsAsync(CancellationToken ct);

    /// <summary>
    /// 全量替换指定角色的成员（事务包裹，先删后插）。
    /// </summary>
    /// <param name="roleId">角色 Id。</param>
    /// <param name="req">新的成员用户 Id 集合（以本次提交为准）。</param>
    /// <param name="ct">取消令牌。</param>
    Task ReplaceRoleMembersAsync(long roleId, ReplaceRoleMembersRequest req, CancellationToken ct);

    // ======= 反向：按用户 → 角色 =======

    /// <summary>
    /// 查询指定用户已分配的门户角色列表。
    /// </summary>
    /// <param name="userId">用户 Id。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>角色列表。</returns>
    Task<IReadOnlyList<RoleMemberItem>> ListRolesForUserAsync(long userId, CancellationToken ct);

    /// <summary>
    /// 查询可选角色清单（所有门户角色）。
    /// </summary>
    /// <param name="ct">取消令牌。</param>
    /// <returns>可选角色列表（按 RoleCode 排序）。</returns>
    Task<IReadOnlyList<RoleOption>> GetRoleOptionsAsync(CancellationToken ct);

    /// <summary>
    /// 全量替换指定用户的角色分配（事务包裹，先删后插）。
    /// </summary>
    /// <param name="userId">用户 Id。</param>
    /// <param name="req">新的角色 Id 集合（以本次提交为准）。</param>
    /// <param name="ct">取消令牌。</param>
    Task ReplaceRolesForUserAsync(long userId, ReplaceUserRolesRequest req, CancellationToken ct);
}
