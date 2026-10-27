/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-21
 * 描述：Bas 模块 DTO——扁平列表返回形态，前端负责组装树结构和 join 填充显示名。
 *       业务 Id 的混淆由全局 JSON 元数据约定（QlxIdJsonMetadata，按 Id 命名自动挂载）统一处理，
 *       DTO 不再逐个标注 [JsonConverter]。
 */

using System.Text.Json.Serialization;

namespace Qlx.Modules.Bas.Dtos;

// ================ Organization ================

/// <summary>
/// 组织扁平节点（前端组装 children 树 + 从成员列表填 leaderName 等显示名）。
/// MemberCount 为直属成员聚合计数（原始字段，后端一次查询分组计数，禁 N+1）。
/// Code 由后端统一发号器生成（只读回显，N-1）。
/// </summary>
public sealed record OrganizationFlatRow(
    long Id,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] long? ParentId,
    string Code,
    string Name,
    string Type,          // Group/Subsidiary/Department/Project
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] long? LeaderPersonId,
    string Status,        // Enabled/Disabled
    int SortOrder,
    bool IsBuiltin,
    DateTimeOffset CreatedAt,
    int MemberCount);

public sealed record SaveOrganizationPayload(
    long? ParentId,
    string Name,
    string Type,          // Group/Subsidiary/Department/Project（兼容数值枚举）
    long? LeaderPersonId,
    int SortOrder);

// ================ Position ================

public sealed record PositionRow(
    long Id,
    long OrgId,
    string Name,
    string Code,
    int Headcount,
    int CurrentCount,
    string Status,        // Enabled/Disabled
    string? Description,
    string? Duty,
    DateTimeOffset UpdatedAt);

public sealed record SavePositionPayload(
    long? Id,
    string Name,
    long OrgId,
    int Headcount,
    string? Description,
    string? Duty = null);

// ================ Member（租户视角） ================

public sealed record MemberRow(
    long TenantMemberId,
    long PortalUserId,
    string LoginAccount,
    string DisplayName,
    string? Phone,
    string? Email,
    long? OrgId,
    IReadOnlyList<string> RoleCodes,   // ['role-admin', 'role-finance']
    int MemberRole,                    // 1=TenantAdmin, 2=Ordinary, 3=PendingInvite
    int MemberStatus,                  // 1=Enabled, 2=Disabled
    DateTimeOffset? JoinAt,
    DateTimeOffset? LastLoginAt);

public sealed record InviteMemberPayload(
    string Account,
    string DisplayName,
    long OrgId,
    IReadOnlyList<string> RoleCodes);

/// <summary>邀请成员结果（业务主键在响应边界自动混淆）。</summary>
public sealed record InviteMemberResult(
    long TenantMemberId,
    long PortalUserId);

public sealed record ImportMemberRow(
    int Line,
    string DisplayName,
    string Account,
    string OrgName,
    string RoleName);

public sealed record ImportMembersResult(
    int Invited,
    IReadOnlyList<(int Line, string Reason)> Failed);

/// <summary>成员在本租户的登录日志行（扁平原始字段，来源租户库 log_login）。</summary>
public sealed record MemberLoginLogRow(
    long EventId,
    DateTimeOffset LoginAt,
    string? IpAddress,
    bool IsSuccess,
    string? FailureReason);

// ================ Role ================

public sealed record RoleRow(
    long Id,
    string Code,
    string Name,
    string? Description,
    bool IsBuiltin,
    int DataScope,
    int MemberCount,
    int PermissionCount,
    DateTimeOffset UpdatedAt);

public sealed record SaveRolePayload(
    string Name,
    string? Description,
    int DataScope);

// ================ Permission 定义（来自 Portal 库） ================

public sealed record PermissionPointRow(string Code, string Name, string? Description);
public sealed record PermissionGroupRow(
    string ModuleCode,
    string ModuleName,
    string? Icon,
    IReadOnlyList<PermissionPointRow> Permissions);

// ================ 已授权模块卡片（来自 Portal 库） ================

public sealed record TenantModuleRow(
    string Code,
    string Name,
    string Description,
    string? Icon,
    string? Version,
    bool Licensed,
    DateTimeOffset? LicensedAt);
