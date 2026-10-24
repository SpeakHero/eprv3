/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-20 23:30:00 +08:00 中国标准时间 星期二
 * 修改时间：2026-10-20 23:30:00 +08:00 中国标准时间 星期二
 * 描述：统一异常处理中间件，拦截业务异常并包装为结构化 ApiResponse
 */

using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Qlx.Abstractions.Exceptions;
using Qlx.Abstractions.Serialization;

namespace Qlx.BuildingBlocks.Api;

/// <summary>
/// 统一异常处理中间件。业务层只管抛（<see cref="BusinessException"/> 子类），
/// 中间件按异常类型映射 HTTP 状态码 + 包装为 <see cref="ApiResponse{T}"/>；
/// 未捕获异常统一 500 + 日志全栈 + 生产隐藏详情。
/// </summary>
/// <remarks>
/// 构造函数，由 ASP.NET Core 管线注入。
/// </remarks>
public sealed partial class ExceptionHandlerMiddleware(RequestDelegate next, ILogger<ExceptionHandlerMiddleware> logger, IHostEnvironment? env = null)
{
    private static readonly JsonSerializerOptions s_jsonOptions = QlxJsonDefaults.Default;
    private readonly ILogger<ExceptionHandlerMiddleware> _logger = logger;
    private readonly IHostEnvironment? _env = env;
    private readonly RequestDelegate _next = next;

    /// <summary>
    /// 处理请求，捕获异常并包装为结构化响应。
    /// </summary>
    public async Task InvokeAsync(HttpContext context)
	{
	    try
	    {
	        await _next(context).ConfigureAwait(false);
	    }
	    catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
	    {
	        // 客户端主动断开请求（curl 超时 / 前端刷新）——不写响应，静默结束
	    }
	    catch (Exception exception)
	    {
	        await HandleExceptionAsync(context, exception).ConfigureAwait(false);
	    }
	}

private async Task HandleExceptionAsync(HttpContext context, Exception exception)
	{
	    var devMessage = (_env?.IsDevelopment() == true)
	        ? $"服务器内部错误：{exception.Message}{(exception.InnerException is not null ? " → " + exception.InnerException.Message : string.Empty)}"
	        : "服务器内部错误";

	    var (status, response) = exception switch
	    {
	        NotFoundException nf => ((int)HttpStatusCode.NotFound, ApiResponse<object>.Fail(nf.ErrorCode, nf.Message, HttpStatusCode.NotFound)),
	        ConflictException cf => ((int)HttpStatusCode.Conflict, ApiResponse<object>.Fail(cf.ErrorCode, cf.Message, HttpStatusCode.Conflict)),
	        ValidationException ve => ((int)HttpStatusCode.UnprocessableEntity, ApiResponse<object>.Fail(ve.ErrorCode, ve.Message, HttpStatusCode.UnprocessableEntity, ve.FieldErrors)),
	        UnauthorizedException ua => ((int)HttpStatusCode.Unauthorized, ApiResponse<object>.Fail(ua.ErrorCode, ua.Message, HttpStatusCode.Unauthorized)),
	        ForbiddenException fb => ((int)HttpStatusCode.Forbidden, ApiResponse<object>.Fail(fb.ErrorCode, fb.Message, HttpStatusCode.Forbidden)),
	        BusinessException be => ((int)HttpStatusCode.UnprocessableEntity, ApiResponse<object>.Fail(be.ErrorCode, be.Message, HttpStatusCode.UnprocessableEntity)),
	        _ => ((int)HttpStatusCode.InternalServerError, ApiResponse<object>.Fail("INTERNAL_ERROR", devMessage, HttpStatusCode.InternalServerError)),
	    };

    context.Response.StatusCode = status;
    context.Response.ContentType = "application/json; charset=utf-8";

    if (status == 500)
    {
        LogUnhandledException(exception, exception.ToString());
    }
    else
    {
        LogBusinessException(exception.GetType().Name, exception.Message);
    }

    await context.Response.WriteAsync(JsonSerializer.Serialize(response, s_jsonOptions)).ConfigureAwait(false);
}

[LoggerMessage(Level = LogLevel.Error, Message = "未处理异常：{ExceptionMessage}")]
private partial void LogUnhandledException(Exception exception, string exceptionMessage);

[LoggerMessage(Level = LogLevel.Warning, Message = "业务异常 {ExceptionType}: {Message}")]
private partial void LogBusinessException(string exceptionType, string message);
}

/// <summary>
/// 中间件注册扩展。
/// </summary>
public static class ExceptionHandlerMiddlewareExtensions
{
    /// <summary>注册统一异常处理中间件（建议放在 UseRouting 之后、其他中间件之前）。</summary>
    public static IApplicationBuilder UseExceptionHandlerMiddleware(this IApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.UseMiddleware<ExceptionHandlerMiddleware>();
    }
}
