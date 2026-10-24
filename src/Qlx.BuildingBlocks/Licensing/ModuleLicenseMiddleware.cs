/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.1.0 beta
 * 创建时间：2026-10-21 06:30:00 +08:00 中国标准时间 星期三
 * 修改时间：2026-10-22 22:40:00 +08:00 中国标准时间 星期四
 * 描述：许可过滤中间件：/api/{moduleCode}/... 无许可一律 404，support/tenant/portal 段与未解析租户直通
 */

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Qlx.Abstractions.Constants;
using Qlx.Abstractions.Modules;
using Qlx.Abstractions.Tenancy;

namespace Qlx.BuildingBlocks.Licensing;

/// <summary>
/// 模块许可过滤中间件（架构规格 9.5 许可运行时过滤）。
/// 对 <c>/api/{moduleCode}/...</c> 形态的请求：租户已解析且许可服务已注册时，
/// 校验当前租户是否拥有该模块许可——无许可直接返回 404（伪装成"资源不存在"，
/// 不暴露模块能力清单）；门户级请求（无租户上下文）与许可服务未注册（开发期）直通。
/// </summary>
public sealed class ModuleLicenseMiddleware(RequestDelegate next)
{
    /// <summary>无模块许可时返回的业务错误码（伪装为资源不存在，不暴露模块能力清单）。</summary>
    public const string NotLicensedErrorCode = "MODULE_NOT_LICENSED";

    /// <inheritdoc />
    public async Task InvokeAsync(
        HttpContext context,
        ITenantContext tenantContext,
        IModuleLicenseService? licenseService)
    {
        ArgumentNullException.ThrowIfNull(context);

        // 仅过滤租户级 API 请求；门户级 / 开发期无许可服务直通
        if (licenseService is not null
            && tenantContext.IsResolved
            && tenantContext.TenantId is long tenantId
            && TryExtractModuleCode(context.Request.Path, out var moduleCode))
        {
            var licensed = await licenseService
                .HasLicenseAsync(tenantId, moduleCode, context.RequestAborted)
                .ConfigureAwait(false);

            if (!licensed)
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                await context.Response.WriteAsJsonAsync(
                    new { success = false, code = NotLicensedErrorCode, message = "资源不存在" },
                    context.RequestAborted).ConfigureAwait(false);
                return;
            }
        }

        await next(context).ConfigureAwait(false);
    }

    /// <summary>
    /// 提取路径第二段作为模块编码（/api/pur/orders → pur）。
    /// 豁免：<c>tenant</c>（租户自管理域，所有租户基础能力）、<c>portal</c>（门户管理端）。
    /// </summary>
    private static bool TryExtractModuleCode(PathString path, out string moduleCode)
    {
        moduleCode = string.Empty;
        var segments = path.Value?.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments is not { Length: >= 2 }
            || !segments[0].Equals(QlxHostConstants.ApiRoutePrefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        moduleCode = segments[1];
        // 豁免：租户自管理域（所有租户基础能力，不需要模块许可）、门户管理端、支撑服务中心（平台公共能力）
        if (moduleCode.Equals(QlxModuleCodes.TenantSelfService, StringComparison.OrdinalIgnoreCase)
            || moduleCode.Equals(QlxModuleCodes.Portal, StringComparison.OrdinalIgnoreCase)
            || moduleCode.Equals(QlxModuleCodes.Support, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }
        return moduleCode.Length > 0;
    }
}

/// <summary>
/// 中间件注册扩展。
/// </summary>
public static class ModuleLicenseMiddlewareExtensions
{
    /// <summary>
    /// 注册模块许可过滤中间件。必须置于 <c>UseAuthentication</c> 之后、
    /// <c>UseRouting</c> 之前（依赖认证后的租户上下文解析结果）。
    /// </summary>
    public static IApplicationBuilder UseModuleLicenseFilter(this IApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.UseMiddleware<ModuleLicenseMiddleware>();
    }
}
