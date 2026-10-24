/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-20 23:00:00 +08:00 中国标准时间 星期二
 * 修改时间：2026-10-21 06:30:00 +08:00 中国标准时间 星期三
 * 描述：租户上下文中间件：从请求头 / Token claim 解析租户（支持 Hashids 混淆串），注入 ITenantContext
 */

using System.Globalization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Qlx.Abstractions.Constants;
using Qlx.Abstractions.Tenancy;
using Qlx.BuildingBlocks.Ids;

namespace Qlx.BuildingBlocks.MultiTenancy;

/// <summary>
/// 租户上下文中间件。从请求头 <c>X-Tenant-Id</c> 或 JWT Token 的 tenant_id claim 解析当前租户，
/// 写入请求作用域的 <see cref="ITenantContext"/>，供后续 DbContext / 业务代码消费。
/// <para>
/// 身份边界规范（架构规格 2.3.8 / 6.4）：对外传输的租户 Id 一律为 Hashids 混淆串，
/// 因此请求头与 claim 值优先按混淆串解码，纯数字串直接解析（兼容内部服务调用）。
/// </para>
/// </summary>
/// <remarks>
/// 构造函数，由 ASP.NET Core 管线注册。
/// </remarks>
/// <param name="next">下一个中间件委托。</param>
public sealed class TenantContextMiddleware(RequestDelegate next)
{
    private const string TenantHeader = QlxHostConstants.TenantHeaderName;
    private const string TenantClaim = QlxClaimConstants.TenantId;

    private readonly RequestDelegate _next = next;


    /// <inheritdoc />
    public async Task InvokeAsync(HttpContext context, ITenantContext tenantContext, IIdObfuscator obfuscator)
    {
        // 手动触发认证：遍历所有注册的 scheme，找到第一个能成功验证 Bearer token 的
        // （OpenIddict Validation scheme 未设为默认 scheme，UseAuthentication 不会自动验证它）
        // 注：单元测试环境可能未注册 IAuthenticationSchemeProvider，此时跳过手动认证直接走 header/claim 解析
        var schemeProvider = context.RequestServices.GetService<IAuthenticationSchemeProvider>();
        if (schemeProvider is not null)
        {
            var allSchemes = await schemeProvider.GetAllSchemesAsync().ConfigureAwait(false);
            foreach (var scheme in allSchemes)
            {
                // 跳过 OpenIddict Server scheme——它只处理 Server 自己的 /connect/* 端点，对自定义 API 端点会抛异常
                if (scheme.Name?.Contains("OpenIddict.Server", StringComparison.OrdinalIgnoreCase) == true) continue;
                if (string.IsNullOrEmpty(scheme.Name)) continue;
                try
                {
                    var result = await context.AuthenticateAsync(scheme.Name).ConfigureAwait(false);
                    if (result.Succeeded)
                    {
                        context.User = result.Principal;
                        break;
                    }
                }
                catch
                {
                    // 某个 scheme 验证失败不代表下一个也失败，继续尝试
                }
            }
        }

        var tenantId = ResolveTenantId(context, obfuscator);
        tenantContext.SetTenantId(tenantId);

        context.Response.OnStarting(() =>
        {
            if (tenantId > 0)
            {
                // 回显混淆串（对外边界一律混淆，不暴露真实 Id）
                context.Response.Headers[TenantHeader] = obfuscator.Encode(tenantId);
            }

            return Task.CompletedTask;
        });

        await _next(context).ConfigureAwait(false);
    }

    private static long ResolveTenantId(HttpContext context, IIdObfuscator obfuscator)
    {
        // 1. 请求头（前端直传混淆串；内部服务间调用可能传纯数字）
        if (context.Request.Headers.TryGetValue(TenantHeader, out var headerValue))
        {
            var value = headerValue.ToString();
            var tenantId = ParseTenantId(value, obfuscator);
            if (tenantId > 0)
            {
                return tenantId;
            }
        }

        // 2. JWT Claim（认证后注入，租户上下文 Token 的 tenant_id 为混淆串）
        var claim = context.User?.FindFirst(TenantClaim);
        if (claim is not null)
        {
            return ParseTenantId(claim.Value, obfuscator);
        }

        return 0;
    }

    /// <summary>解析租户 Id：纯数字直接解析；否则尝试 Hashids 解码；失败返回 0。</summary>
    private static long ParseTenantId(string? value, IIdObfuscator obfuscator)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return 0;
        }

        if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var direct))
        {
            return direct;
        }

        return obfuscator.TryDecode(value, out var decoded) && decoded > 0 ? decoded : 0;
    }
}

/// <summary>
/// 中间件注册扩展。
/// </summary>
public static class TenantContextMiddlewareExtensions
{
    /// <summary>
    /// 注册租户上下文中间件。
    /// </summary>
    public static IApplicationBuilder UseTenantContext(this IApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.UseMiddleware<TenantContextMiddleware>();
    }
}
