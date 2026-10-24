/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-23
 * 修改时间：2026-10-23
 * 描述：Permission 授权策略的 AuthorizationHandler 实现。
 *       区分租户上下文（tenant_id claim 存在）与门户上下文；
 *       Admin 零计算旁路 + permissions claim Split 包含匹配。
 */

using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Qlx.Abstractions.Authorization;
using Qlx.Abstractions.Constants;

namespace Qlx.BuildingBlocks.Authorization;

/// <summary>
/// <c>Permission</c> 授权策略的 Handler：区分租户/门户上下文，Admin 旁路 + 权限码 Split 包含匹配。
/// <para>
/// 判定顺序：
/// <list type="number">
///   <item>User 无身份 → Fail（框架已拦截，但 Handler 仍保险返回 Fail）</item>
///   <item>存在 tenant_id claim → 租户上下文：member_role = TenantAdmin 直接 Succeed；否则取 permissions claim Split 判断包含</item>
///   <item>不存在 tenant_id → 门户上下文：is_portal_admin = 1 直接 Succeed；否则取 permissions claim Split 判断包含</item>
/// </list>
/// </para>
/// <para>claim 常量全部走 <see cref="QlxClaimConstants"/> / <see cref="QlxMemberRoleValues"/>，禁止硬编码。</para>
/// </summary>
public sealed class PermissionAuthorizationHandler
    : AuthorizationHandler<PermissionRequirement>
{
    /// <inheritdoc />
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        PermissionRequirement requirement)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(requirement);

        var user = context.User;
        if (user?.Identity?.IsAuthenticated != true)
        {
            context.Fail();
            return Task.CompletedTask;
        }

        // ====== 租户上下文：tenant_id claim 存在 ======
        var hasTenant = user.FindFirst(QlxClaimConstants.TenantId) is not null;
        if (hasTenant)
        {
            // TenantAdmin 零计算旁路
            var memberRole = user.FindFirstValue(QlxClaimConstants.MemberRole);
            if (string.Equals(memberRole, QlxMemberRoleValues.TenantAdmin, StringComparison.Ordinal))
            {
                context.Succeed(requirement);
                return Task.CompletedTask;
            }

            if (ClaimContainsPermission(user, requirement.PermissionCode))
            {
                context.Succeed(requirement);
                return Task.CompletedTask;
            }

            context.Fail();
            return Task.CompletedTask;
        }

        // ====== 门户上下文：tenant_id claim 不存在 ======
        var portalAdminFlag = user.FindFirstValue(QlxClaimConstants.PortalAdminFlag);
        if (string.Equals(portalAdminFlag, "1", StringComparison.Ordinal))
        {
            context.Succeed(requirement);
            return Task.CompletedTask;
        }

        if (ClaimContainsPermission(user, requirement.PermissionCode))
        {
            context.Succeed(requirement);
            return Task.CompletedTask;
        }

        context.Fail();
        return Task.CompletedTask;
    }

    /// <summary>
    /// 从 <see cref="QlxClaimConstants.Permissions"/> claim（分号分隔字符串）中
    /// Split 后判断是否包含目标权限码。不可为 null 的匹配语义。
    /// </summary>
    private static bool ClaimContainsPermission(ClaimsPrincipal user, string requiredCode)
    {
        var claimValue = user.FindFirstValue(QlxClaimConstants.Permissions);
        if (string.IsNullOrWhiteSpace(claimValue)) return false;

        // Split 支持多种分隔符：分号 / 逗号 / 空格
        var tokens = claimValue.Split(new[] { ';', ',', ' ' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var token in tokens)
        {
            if (string.Equals(token, requiredCode, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
