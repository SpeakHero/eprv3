/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-26
 * 描述：成本字段访问判定——字段级安全过滤（B-3-1）：租户管理员或持 stk.cost.field 权限者可见成本；
 *       无权限时成本字段一律不下发（置 null），与展示格式化无关
 */

using System.Security.Claims;

using Microsoft.AspNetCore.Http;

using Qlx.Abstractions.Constants;

namespace Qlx.Modules.Stk.Services;

/// <summary>
/// 成本字段访问判定助手。库存台账/流水/批次中的单价与金额属字段级权限数据，
/// 仅租户管理员（<see cref="QlxMemberRoleValues.TenantAdmin"/>）或显式授予
/// <c>stk.cost.field</c> 的成员可见；判定以令牌声明为准，不读缓存（C-5-5）。
/// </summary>
internal static class CostFieldAccess
{
    /// <summary>成本字段查看权限码。</summary>
    internal const string PermissionCode = "stk.cost.field";

    /// <summary>
    /// 判断当前请求是否可查看成本字段。
    /// </summary>
    /// <param name="httpContextAccessor">HTTP 上下文访问器。</param>
    /// <returns>可查看返回 <see langword="true"/>；未认证或无权限返回 <see langword="false"/>。</returns>
    public static bool HasCostAccess(IHttpContextAccessor httpContextAccessor)
    {
        ArgumentNullException.ThrowIfNull(httpContextAccessor);

        HttpContext? context = httpContextAccessor.HttpContext;
        ClaimsPrincipal? user = context?.User;
        if (user?.Identity?.IsAuthenticated != true)
        {
            return false;
        }

        string? role = user.FindFirst(QlxClaimConstants.MemberRole)?.Value;
        if (string.Equals(role, QlxMemberRoleValues.TenantAdmin, StringComparison.Ordinal))
        {
            return true;
        }

        string? permissions = user.FindFirst(QlxClaimConstants.Permissions)?.Value;
        if (string.IsNullOrWhiteSpace(permissions))
        {
            return false;
        }

        return permissions
            .Split([';', ',', ' '], StringSplitOptions.RemoveEmptyEntries)
            .Any(permission => string.Equals(permission.Trim(), PermissionCode, StringComparison.Ordinal));
    }
}
