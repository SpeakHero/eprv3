/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-26 12:00:00 +08:00 中国标准时间 星期一
 * 修改时间：2026-10-26 12:00:00 +08:00 中国标准时间 星期一
 * 描述：统一异常处理注册扩展——DI 侧 AddExceptionHandler + 管线侧框架 UseExceptionHandler
 */

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace Qlx.BuildingBlocks.Api;

/// <summary>
/// 统一异常处理注册扩展（官方 <see cref="IExceptionHandler"/> 模式）：
/// <see cref="AddQlxExceptionHandling"/> 注册处理器，<see cref="UseQlxExceptionHandling"/> 启用框架管线。
/// </summary>
public static class QlxExceptionHandlingExtensions
{
    /// <summary>
    /// 注册统一异常处理器（须在构建管线前调用；处理器无状态，默认单例）。
    /// 附带注册 <c>IProblemDetailsService</c>：.NET 10 框架 ExceptionHandlerMiddleware 启动校验要求
    /// ExceptionHandlingPath / ExceptionHandler 选项 / ProblemDetails 三者之一（处理器按请求作用域惰性解析，
    /// 不参与启动校验）；本处理器始终返回 true 全量接管，ProblemDetails 服务不会实际写响应。
    /// </summary>
    public static IServiceCollection AddQlxExceptionHandling(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        return services
            .AddExceptionHandler<QlxApiExceptionHandler>()
            .AddProblemDetails();
    }

    /// <summary>启用统一异常处理管线（建议放在 ForwardedHeaders 之后、其他中间件之前；无错误路径，依赖已注册的 <see cref="IExceptionHandler"/>）。</summary>
    public static IApplicationBuilder UseQlxExceptionHandling(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);
        return app.UseExceptionHandler();
    }
}
