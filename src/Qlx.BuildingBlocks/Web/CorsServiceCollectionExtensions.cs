/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-21 20:30:00 +08:00 中国标准时间 星期三
 * 修改时间：2026-10-21 20:30:00 +08:00 中国标准时间 星期三
 * 描述：CORS 跨域资源共享装配扩展（强类型 Options 驱动，禁止硬编码源）
 */

using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Qlx.BuildingBlocks.Web;

/// <summary>
/// CORS 装配扩展：从 <see cref="QlxCorsOptions.SectionName"/> 配置节读取允许的源/方法/头，
/// 注册命名策略 <see cref="QlxCorsOptions.PolicyName"/>，并提供 <c>UseQlxCors</c> 中间件扩展。
/// <para>前后端分离部署时，前端源通过 config/common/cors.json 配置，禁止硬编码。</para>
/// </summary>
public static class CorsServiceCollectionExtensions
{
    /// <summary>
    /// 注册 CORS 策略（从 <see cref="QlxCorsOptions"/> 配置驱动）。
    /// </summary>
    /// <param name="services">服务集合。</param>
    /// <param name="configuration">配置根。</param>
    /// <returns>服务集合。</returns>
    public static IServiceCollection AddQlxCors(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        // 强类型 Options + 启动校验（项目铁律：record + init + ValidateOnStart）
        services.AddOptions<QlxCorsOptions>()
            .BindConfiguration(QlxCorsOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // 构建服务提供者以读取配置（Options 绑定后可安全解析）
        var options = configuration.GetSection(QlxCorsOptions.SectionName).Get<QlxCorsOptions>()
            ?? new QlxCorsOptions();

        if (!options.Enabled)
        {
            // 未启用 CORS：仅注册空策略占位，UseQlxCors 时直接放行（同源部署）
            return services;
        }

        services.AddCors(opt =>
        {
            opt.AddPolicy(QlxCorsOptions.PolicyName, policy =>
            {
                policy.SetIsOriginAllowedToAllowWildcardSubdomains();

                if (options.AllowedOrigins is ["*"])
                {
                    policy.AllowAnyOrigin();
                }
                else
                {
                    policy.WithOrigins(options.AllowedOrigins);
                }

                if (options.AllowedMethods is ["*"])
                {
                    policy.AllowAnyMethod();
                }
                else
                {
                    policy.WithMethods(options.AllowedMethods);
                }

                if (options.AllowedHeaders is ["*"])
                {
                    policy.AllowAnyHeader();
                }
                else
                {
                    policy.WithHeaders(options.AllowedHeaders);
                }

                if (options.ExposedHeaders is not null && options.ExposedHeaders.Length > 0)
                {
                    policy.WithExposedHeaders(options.ExposedHeaders);
                }

                if (options.AllowCredentials)
                {
                    policy.AllowCredentials();
                }
                else
                {
                    policy.DisallowCredentials();
                }

                if (options.PreflightMaxAgeSeconds > 0)
                {
                    policy.SetPreflightMaxAge(TimeSpan.FromSeconds(options.PreflightMaxAgeSeconds));
                }
            });
        });

        return services;
    }

    /// <summary>
    /// 启用 CORS 中间件（须位于 UseRouting 之后、UseAuthorization 之前；或 UseRouting 之前也可，
    /// 按 ASP.NET Core 推荐放在 UseRouting 与 UseEndpoints 之间）。
    /// </summary>
    /// <param name="app">应用构建器。</param>
    /// <returns>应用构建器。</returns>
    public static IApplicationBuilder UseQlxCors(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var options = app.ApplicationServices.GetService<IOptions<QlxCorsOptions>>()?.Value;
        if (options is null || !options.Enabled)
        {
            // 未启用 CORS：不注册中间件
            return app;
        }

        app.UseCors(QlxCorsOptions.PolicyName);
        return app;
    }
}
