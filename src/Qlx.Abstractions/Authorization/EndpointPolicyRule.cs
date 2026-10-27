/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-24
 * 描述：端点授权策略规则记录——集中式授权映射的原子单元，
 *       声明「控制器/动作 → 授权要求」绑定关系（迁移自原控制器硬编码 [Authorize] 标注）。
 *       合并语义（类级 ∪ 动作级）：RequireTenantContext 取或、策略并存叠加（AND）、
 *       AllowAnonymous 任一为真即真——与原「类级 + 方法级标注叠加」语义严格等价。
 */

namespace Qlx.Abstractions.Authorization;

/// <summary>
/// 端点授权策略规则（不可变）。由模块 <see cref="IEndpointPolicyMapProvider"/> 声明基线，
/// 可被 <c>config/common/authorization.json</c> 的 <c>endpointPolicies.rules</c> 按
/// (Controller, Action) 键同键覆盖。
/// </summary>
public sealed record EndpointPolicyRule
{
    /// <summary>控制器名（不含 <c>Controller</c> 后缀，序数不区分大小写匹配）。</summary>
    public required string Controller { get; init; }

    /// <summary>动作方法名；<c>null</c> 表示控制器级规则（对该控制器全部动作生效）。</summary>
    public string? Action { get; init; }

    /// <summary>
    /// 权限码（如 <c>bas.admin.org.action</c>），注入时经
    /// <c>Qlx.Abstractions.Constants.QlxPolicyNames.Permission</c> 转为策略名 <c>Permission:{code}</c>。
    /// </summary>
    public string? PermissionCode { get; init; }

    /// <summary>裸策略名（如 PortalAdmin 等非权限点策略），与 <see cref="PermissionCode"/> 可并存（AND 叠加）。</summary>
    public string? Policy { get; init; }

    /// <summary>是否叠加租户上下文前置策略（<c>QlxPolicyNames.TenantContext</c>，要求租户级 Token）。</summary>
    public bool RequireTenantContext { get; init; }

    /// <summary>显式匿名豁免（如登录端点）。为真时不注入任何授权过滤器。</summary>
    public bool AllowAnonymous { get; init; }
}
