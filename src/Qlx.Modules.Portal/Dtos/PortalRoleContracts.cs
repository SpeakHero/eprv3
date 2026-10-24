/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-23 23:00:00 +08:00 中国标准时间 星期四
 * 修改时间：2026-10-23 23:00:00 +08:00 中国标准时间 星期四
 * 描述：门户角色管理域 DTO 契约：CRUD + 权限分配 + 成员双向分配
 */

using System.ComponentModel.DataAnnotations;

namespace Qlx.Modules.Portal.Dtos;

/// <summary>
/// 新建门户角色入参（角色编码由系统后端根据 RoleName 自动生成，前端无需传入）。
/// </summary>
/// <param name="RoleName">角色显示名称（中文，1~64）。</param>
/// <param name="Description">角色说明（可选，≤256）。</param>
public sealed record CreatePortalRoleRequest(
    [Required(ErrorMessage = "角色名称必填")]
    [MinLength(1), MaxLength(64)] string RoleName,
    [MaxLength(256)] string? Description);

/// <summary>
/// 更新门户角色入参（RoleCode 因唯一索引禁止修改）。
/// </summary>
/// <param name="RoleName">角色显示名称。</param>
/// <param name="Description">角色说明（可选）。</param>
public sealed record UpdatePortalRoleRequest(
    [Required(ErrorMessage = "角色名称必填")]
    [MinLength(1), MaxLength(64)] string RoleName,
    [MaxLength(256)] string? Description);

/// <summary>
/// 门户角色列表项（含权限数 / 成员数 / 最近更新时间）。
/// </summary>
/// <param name="RoleId">角色 Id（内部 long，JSON 序列化时经全局 QlxIdJsonMetadata 混淆）。</param>
/// <param name="RoleCode">角色编码。</param>
/// <param name="RoleName">角色显示名称。</param>
/// <param name="Description">角色说明。</param>
/// <param name="IsSystem">是否系统内置（禁止修改 / 删除）。</param>
/// <param name="PermissionCount">已分配权限点数量。</param>
/// <param name="MemberCount">已分配成员数量。</param>
/// <param name="UpdatedAt">最近更新时间（UTC）。</param>
public sealed record PortalRoleListItem(
    long RoleId,
    string RoleCode,
    string RoleName,
    string? Description,
    bool IsSystem,
    int PermissionCount,
    int MemberCount,
    DateTimeOffset UpdatedAt);

/// <summary>
/// 门户角色详情（无 PermissionCount / MemberCount，但保留 UpdatedAt）。
/// </summary>
/// <param name="RoleId">角色 Id。</param>
/// <param name="RoleCode">角色编码。</param>
/// <param name="RoleName">角色显示名称。</param>
/// <param name="Description">角色说明。</param>
/// <param name="IsSystem">是否系统内置。</param>
/// <param name="UpdatedAt">最近更新时间（UTC）。</param>
public sealed record PortalRoleDetail(
    long RoleId,
    string RoleCode,
    string RoleName,
    string? Description,
    bool IsSystem,
    DateTimeOffset UpdatedAt);

/// <summary>
/// 全量替换角色权限入参（事务包裹，先删后插）。
/// </summary>
/// <param name="PermissionCodes">新的权限码集合（以本次提交为准）。</param>
public sealed record ReplaceRolePermissionsRequest(
    [Required(ErrorMessage = "权限码集合必填"), MinLength(1)] IReadOnlyList<string> PermissionCodes);

/// <summary>
/// 可选权限码清单项（por_permission_definitions.IsDeprecated=false）。
/// </summary>
/// <param name="PermissionCode">权限点编码。</param>
/// <param name="ModuleCode">所属模块编码。</param>
/// <param name="DisplayName">权限点显示名称。</param>
/// <param name="GroupName">权限点分组（同一模块内按功能分组）。</param>
public sealed record PermissionOption(
    string PermissionCode,
    string ModuleCode,
    string DisplayName,
    string? GroupName);

/// <summary>
/// 全量替换角色成员入参（事务包裹，先删后插）。
/// </summary>
/// <param name="UserIds">新的成员用户 Id 集合（内部 long，JSON 序列化时经全局 QlxIdJsonMetadata 混淆）。</param>
public sealed record ReplaceRoleMembersRequest(
    [Required(ErrorMessage = "成员用户 Id 集合必填")] IReadOnlyList<long> UserIds);

/// <summary>
/// 角色成员项（userId + 登录账号 + 显示名）。
/// </summary>
/// <param name="UserId">用户 Id。</param>
/// <param name="LoginAccount">登录账号。</param>
/// <param name="DisplayName">显示名称。</param>
public sealed record UserRoleMember(
    long UserId,
    string LoginAccount,
    string DisplayName);

/// <summary>
/// 可选用户清单项（所有已启用的 PortalUser）。
/// </summary>
/// <param name="UserId">用户 Id。</param>
/// <param name="LoginAccount">登录账号。</param>
/// <param name="DisplayName">显示名称。</param>
public sealed record UserOption(
    long UserId,
    string LoginAccount,
    string DisplayName);

/// <summary>
/// 全量替换某用户的角色入参（反向路由：PUT /api/portal/admin/users/{userId}/roles）。
/// </summary>
/// <param name="RoleIds">新的角色 Id 集合。</param>
public sealed record ReplaceUserRolesRequest(
    [Required(ErrorMessage = "角色 Id 集合必填")] IReadOnlyList<long> RoleIds);

/// <summary>
/// 反向：用户已分配的角色列表项。
/// </summary>
/// <param name="RoleId">角色 Id。</param>
/// <param name="RoleCode">角色编码。</param>
/// <param name="RoleName">角色显示名称。</param>
/// <param name="IsSystem">是否系统内置。</param>
public sealed record RoleMemberItem(
    long RoleId,
    string RoleCode,
    string RoleName,
    bool IsSystem);

/// <summary>
/// 反向：可选角色清单项。
/// </summary>
/// <param name="RoleId">角色 Id。</param>
/// <param name="RoleCode">角色编码。</param>
/// <param name="RoleName">角色显示名称。</param>
/// <param name="IsSystem">是否系统内置。</param>
public sealed record RoleOption(
    long RoleId,
    string RoleCode,
    string RoleName,
    bool IsSystem);
