/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-21 06:30:00 +08:00 中国标准时间 星期三
 * 修改时间：2026-10-21 06:30:00 +08:00 中国标准时间 星期三
 * 描述：认证响应 DTO：租户摘要列表（tenants[]）与当前用户信息（/api/portal/me）
 */

namespace Qlx.Modules.Portal.Auth;

/// <summary>登录响应租户摘要项（tenants[] 数组元素；TenantId 为 Hashids 混淆串）。</summary>
/// <param name="TenantId">租户 Id（对外混淆串）。</param>
/// <param name="TenantCode">租户编码。</param>
/// <param name="DisplayName">租户显示名称。</param>
/// <param name="TaxNo">统一社会信用代码 / 税号（登录页企业标识匹配用）。</param>
/// <param name="MemberRole">租户内主要角色。</param>
/// <param name="LastLoginAt">最近一次进入该租户时间。</param>
public sealed record TenantSummaryItem(
    string TenantId,
    string TenantCode,
    string DisplayName,
    string? TaxNo,
    string MemberRole,
    DateTimeOffset? LastLoginAt);

/// <summary>
/// 租户成员查询投影行（进程内真实 long，不对外）。
/// 由 EF 查询投影产生，经 <see cref="IdentityMappers"/> 转换为 <see cref="TenantSummaryItem"/>。
/// </summary>
/// <param name="TenantId">租户 Id。</param>
/// <param name="TenantCode">租户编码。</param>
/// <param name="DisplayName">租户显示名称。</param>
/// <param name="TaxNo">统一社会信用代码 / 税号。</param>
/// <param name="MemberRole">租户内角色。</param>
/// <param name="LastLoginAt">最近进入时间。</param>
public sealed record TenantMemberRow(
    long TenantId,
    string TenantCode,
    string DisplayName,
    string? TaxNo,
    Domain.Identity.TenantMemberRole MemberRole,
    DateTimeOffset? LastLoginAt);

/// <summary>当前登录用户信息（/api/portal/me 响应体）。</summary>
public sealed record CurrentUserResponse
{
    /// <summary>门户用户 Id（混淆串）。</summary>
    public required string UserId { get; init; }

    /// <summary>登录账号。</summary>
    public required string LoginAccount { get; init; }

    /// <summary>显示名称（租户内显示名优先）。</summary>
    public required string DisplayName { get; init; }

    /// <summary>当前租户 Id（混淆串；身份级 Token 请求为 null）。</summary>
    public string? TenantId { get; init; }

    /// <summary>当前租户编码。</summary>
    public string? TenantCode { get; init; }

    /// <summary>租户内主要角色。</summary>
    public string? MemberRole { get; init; }

    /// <summary>授权模块编码列表。</summary>
    public IReadOnlyList<string> Modules { get; init; } = [];

    /// <summary>权限编码列表。</summary>
    public IReadOnlyList<string> Permissions { get; init; } = [];

    /// <summary>是否门户管理员。</summary>
    public bool IsPortalAdmin { get; init; }
}
