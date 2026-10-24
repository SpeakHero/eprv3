/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-23
 * 修改时间：2026-10-23
 * 描述：ASP.NET Core Authorization 权限码需求契约（Abstractions 层仅放需求记录，
 *       AuthorizationHandler 具体实现位于 BuildingBlocks/Authorization）
 */

using Microsoft.AspNetCore.Authorization;

namespace Qlx.Abstractions.Authorization;

/// <summary>
/// ASP.NET Core 授权策略 "Permission" 的需求：要求 Token 中携带指定权限码。
/// <para>
/// Handler 实现位于 BuildingBlocks.Authorization 命名空间，
/// 会区分租户/门户上下文：
/// </para>
/// <list type="bullet">
///   <item>租户上下文：member_role = TenantAdmin → 直接放行；否则查 permissions claim Split 包含判断</item>
///   <item>门户上下文：is_portal_admin = 1 → 直接放行；否则查 permissions claim</item>
/// </list>
/// </summary>
/// <param name="PermissionCode">要求的权限码（如 portal.tenant.view / bas.role.edit）。</param>
public sealed record PermissionRequirement(string PermissionCode) : IAuthorizationRequirement
{
    /// <summary>
    /// 是否要求的是 "任意一个给定权限码"。当给定多个权限码时，Handler 按 OR 语义判定。
    /// 默认 single-arg 构造生成单码；也可通过 with 扩展多码（Handler 自行 Split）。
    /// </summary>
    public static implicit operator PermissionRequirement(string code) => new(code);
}
