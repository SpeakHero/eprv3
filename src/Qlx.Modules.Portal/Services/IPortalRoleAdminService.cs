/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-23 23:00:00 +08:00 中国标准时间 星期四
 * 修改时间：2026-10-23 23:00:00 +08:00 中国标准时间 星期四
 * 描述：门户角色 CRUD 服务契约
 */

using Qlx.BuildingBlocks.Api;
using Qlx.Modules.Portal.Dtos;

namespace Qlx.Modules.Portal.Services;

/// <summary>
/// 门户角色管理服务契约（por_roles 表 CRUD）。
/// IsSystem=true 角色禁止修改 / 删除（Conflict 409）。
/// 删除前需确认无成员（por_user_roles 关联存在则 Conflict 409）。
/// </summary>
public interface IPortalRoleAdminService
{
    /// <summary>
    /// 分页查询门户角色（keyword 匹配 RoleCode / RoleName）。
    /// </summary>
    /// <param name="keyword">关键字（可选）。</param>
    /// <param name="page">页码（从 1 开始）。</param>
    /// <param name="pageSize">每页大小（默认 20，上限 100）。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>分页结果（含 PermissionCount / MemberCount / UpdatedAt）。</returns>
    Task<PagedResult<PortalRoleListItem>> ListAsync(
        string? keyword, int page, int pageSize, CancellationToken ct);

    /// <summary>
    /// 新建门户角色（IsSystem 强制 false；RoleCode 唯一校验）。
    /// </summary>
    /// <param name="req">新建请求。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>新建后的角色详情。</returns>
    Task<PortalRoleDetail> CreateAsync(CreatePortalRoleRequest req, CancellationToken ct);

    /// <summary>
    /// 更新门户角色（IsSystem=true 拒绝修改，Conflict 409）。
    /// </summary>
    /// <param name="roleId">角色 Id。</param>
    /// <param name="req">更新请求。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>更新后的角色详情。</returns>
    Task<PortalRoleDetail> UpdateAsync(long roleId, UpdatePortalRoleRequest req, CancellationToken ct);

    /// <summary>
    /// 删除门户角色（IsSystem=true 拒绝；有成员拒绝，Conflict 409）。
    /// 删除顺序：先删 PortalRolePermissions → 再删 PortalRole。
    /// </summary>
    /// <param name="roleId">角色 Id。</param>
    /// <param name="ct">取消令牌。</param>
    Task DeleteAsync(long roleId, CancellationToken ct);
}
