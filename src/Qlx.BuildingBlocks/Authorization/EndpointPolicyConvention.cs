/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-24
 * 描述：全局授权约定（IApplicationModelConvention）——启动期遍历全部控制器/动作，
 *       按 EndpointPolicyCatalog 命中结果统一注入 AuthorizeFilter，替代控制器上
 *       硬编码的 [Authorize] 标注；未命中端点注入「仅认证」兜底过滤器（安全默认，
 *       防漏配裸奔）；显式标注 [Authorize]/[AllowAnonymous] 的端点跳过（逃生舱）。
 */

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.ApplicationModels;
using Microsoft.AspNetCore.Mvc.Authorization;
using Microsoft.AspNetCore.Mvc.Filters;
using OpenIddict.Validation.AspNetCore;
using Qlx.Abstractions.Constants;

namespace Qlx.BuildingBlocks.Authorization;

/// <summary>
/// 端点授权约定。注入语义与原「类级 + 方法级 [Authorize] 叠加」严格等价：
/// 单个 <see cref="AuthorizeFilter"/> 内携带多条 <see cref="IAuthorizeData"/>，
/// 执行时经 <c>AuthorizationPolicy.CombineAsync</c> 合并为单一策略单次评估（AND 叠加），
/// 避免多过滤器重复认证与多次策略评估。
/// <para>
/// 过滤器评估时机晚于 <c>AuthorizationMiddleware</c>（D-10 已论证：黑名单仍 401、
/// 许可拦截行为不变、已认证判定一致），与 <c>UseAuthorization</c> 管线兼容。
/// </para>
/// </summary>
/// <param name="catalog">启动期定稿的端点授权规则目录。</param>
public sealed class EndpointPolicyConvention(EndpointPolicyCatalog catalog) : IApplicationModelConvention
{
    /// <inheritdoc />
    public void Apply(ApplicationModel application)
    {
        ArgumentNullException.ThrowIfNull(application);

        foreach (var controller in application.Controllers)
        {
            foreach (var action in controller.Actions)
            {
                // 逃生舱：端点已显式标注授权元数据时不干预（保留手写路径）
                if (HasAuthMetadata(controller) || HasAuthMetadata(action))
                {
                    continue;
                }

                if (catalog.TryFind(controller.ControllerName, action.ActionName, out var policy)
                    && policy is not null)
                {
                    // 显式匿名豁免：不注入任何授权过滤器
                    if (policy.AllowAnonymous)
                    {
                        continue;
                    }

                    var authorizeData = new List<IAuthorizeData>(policy.PolicyNames.Count + 1);
                    if (policy.RequireTenantContext)
                    {
                        authorizeData.Add(NewAuthorizeData(QlxPolicyNames.TenantContext));
                    }

                    foreach (var policyName in policy.PolicyNames)
                    {
                        authorizeData.Add(NewAuthorizeData(policyName));
                    }

                    // 命中空规则（仅认证控制器）：与原 [Authorize(AuthenticationSchemes=...)]
                    // 行为等价——必须携带 scheme，空 AuthorizeData 会丢失 scheme 退化为默认认证方案
                    if (authorizeData.Count == 0)
                    {
                        authorizeData.Add(NewAuthorizeData(policy: null));
                    }

                    action.Filters.Add(new AuthorizeFilter(authorizeData));
                }
                else
                {
                    // 兜底：未配置端点一律要求「仅认证」（OpenIddict Validation scheme），防漏配裸奔
                    action.Filters.Add(new AuthorizeFilter([NewAuthorizeData(policy: null)]));
                }
            }
        }
    }

    private static AuthorizeAttribute NewAuthorizeData(string? policy) => new()
    {
        Policy = policy,
        AuthenticationSchemes = OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme,
    };

    private static bool HasAuthMetadata(ControllerModel controller)
        => controller.Attributes.Any(static a => a is IAuthorizeData or IAllowAnonymous)
           || controller.Filters.Any(static f => f is IAuthorizationFilter or IAsyncAuthorizationFilter);

    private static bool HasAuthMetadata(ActionModel action)
        => action.Attributes.Any(static a => a is IAuthorizeData or IAllowAnonymous)
           || action.Filters.Any(static f => f is IAuthorizationFilter or IAsyncAuthorizationFilter);
}
