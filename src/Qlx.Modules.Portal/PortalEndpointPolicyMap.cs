/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-24
 * 描述：Portal 模块端点授权规则基线——原控制器硬编码 [Authorize] 标注的 1:1 快照
 *       （4 个门户管理控制器类级 QlxPortalAdmin 策略；4 个控制器仅认证；
 *       Authorization.Exchange 登录端点匿名豁免——兜底注入会把登录打成 401，必须显式豁免）。
 *       规则调整优先改 config/common/authorization.json 覆盖，勿改本基线。
 */

using Qlx.Abstractions.Authorization;
using Qlx.Modules.Portal.Auth;

namespace Qlx.Modules.Portal;

/// <summary>Portal 模块端点授权规则映射提供方。</summary>
public sealed class PortalEndpointPolicyMap : IEndpointPolicyMapProvider
{
    /// <inheritdoc />
    public IReadOnlyList<EndpointPolicyRule> GetRules() =>
    [
        // ======= 门户管理（类级 QlxPortalAdmin 策略：身份级 Token + is_portal_admin=1）=======
        new() { Controller = "PortalAdmin", Policy = QlxAuthConstants.Policies.PortalAdmin },
        new() { Controller = "PortalRole", Policy = QlxAuthConstants.Policies.PortalAdmin },
        new() { Controller = "PortalRolePermission", Policy = QlxAuthConstants.Policies.PortalAdmin },
        new() { Controller = "PortalUserRole", Policy = QlxAuthConstants.Policies.PortalAdmin },

        // ======= 仅认证（原类级 [Authorize(AuthenticationSchemes=...)]，无策略）=======
        new() { Controller = "Me" },
        new() { Controller = "PortalMenu" },
        new() { Controller = "TenantMemberRole" },
        new() { Controller = "SystemParameters" },

        // ======= 登录端点匿名豁免（原控制器无任何授权标注）=======
        new() { Controller = "Authorization", Action = "Exchange", AllowAnonymous = true },
    ];
}
