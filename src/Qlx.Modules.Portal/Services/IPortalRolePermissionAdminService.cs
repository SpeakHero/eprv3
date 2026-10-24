/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-23 23:00:00 +08:00 中国标准时间 星期四
 * 修改时间：2026-10-23 23:00:00 +08:00 中国标准时间 星期四
 * 描述：门户角色权限分配服务契约（por_role_permissions 表全量替换）
 */

using Qlx.Modules.Portal.Dtos;

namespace Qlx.Modules.Portal.Services;

/// <summary>
/// 门户角色权限分配服务契约。
/// IsSystem=true 角色禁止修改权限（Conflict 409）。
/// 写路径全量替换（事务包裹：BeginTransactionAsync → ExecuteDeleteAsync → AddRangeAsync）。
/// </summary>
public interface IPortalRolePermissionAdminService
{
    /// <summary>
    /// 查询指定角色已分配的权限码列表。
    /// </summary>
    /// <param name="roleId">角色 Id。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>权限码集合（按字母排序）。</returns>
    Task<IReadOnlyList<string>> GetPermissionCodesAsync(long roleId, CancellationToken ct);

    /// <summary>
    /// 查询可选权限码清单（por_permission_definitions.IsDeprecated=false）。
    /// </summary>
    /// <param name="ct">取消令牌。</param>
    /// <returns>可选权限点列表（按 SortOrder / PermissionCode 排序）。</returns>
    Task<IReadOnlyList<PermissionOption>> GetPermissionOptionsAsync(CancellationToken ct);

    /// <summary>
    /// 全量替换指定角色的权限码（事务包裹，先删后插）。
    /// </summary>
    /// <param name="roleId">角色 Id。</param>
    /// <param name="req">新的权限码集合（以本次提交为准）。</param>
    /// <param name="ct">取消令牌。</param>
    Task ReplaceAsync(long roleId, ReplaceRolePermissionsRequest req, CancellationToken ct);
}
