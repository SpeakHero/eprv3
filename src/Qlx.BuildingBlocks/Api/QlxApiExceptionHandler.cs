/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-26 12:00:00 +08:00 中国标准时间 星期一
 * 修改时间：2026-10-26 20:30:00 +08:00 中国标准时间 星期一
 * 描述：统一异常处理器（官方 IExceptionHandler 模式），按异常类型映射 HTTP 状态码并包装为结构化 ApiResponse
 */

using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using Qlx.Abstractions.Constants;
using Qlx.Abstractions.Exceptions;
using Qlx.Abstractions.Serialization;

using System.Net;
using System.Net.Mime;
using System.Text;
using System.Text.Json;

namespace Qlx.BuildingBlocks.Api;

/// <summary>
/// 统一异常处理器。业务层只管抛（<see cref="BusinessException"/> 子类），
/// 处理器按异常类型映射 HTTP 状态码 + 包装为 <see cref="ApiResponse{T}"/>；
/// 未捕获异常统一 500 + 日志全栈 + 生产隐藏详情。
/// 由框架 <see cref="ExceptionHandlerMiddleware"/> 调用（经
/// <see cref="QlxExceptionHandlingExtensions"/> 注册），自动获得
/// "响应已开始"防护与 DiagnosticSource 异常事件。
/// </summary>
/// <remarks>
/// 由 <c>AddExceptionHandler&lt;T&gt;</c> 注册进 DI，无状态、线程安全；客户端主动断开（请求已中止的
/// <see cref="OperationCanceledException"/>）静默结束，不写响应。
/// </remarks>
public sealed partial class QlxApiExceptionHandler(IHostEnvironment env, ILogger<QlxApiExceptionHandler> logger) : IExceptionHandler
{
    private static readonly JsonSerializerOptions s_jsonOptions = QlxJsonDefaults.Default;
    private readonly IHostEnvironment _env = env;
    private readonly ILogger<QlxApiExceptionHandler> _logger = logger;

    /// <summary>
    /// 处理异常并写结构化响应。始终返回 true——本处理器全量接管，异常不再上抛。
    /// </summary>
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        // 客户端主动断开请求（curl 超时 / 前端刷新）——不写响应，静默结束
        if (exception is OperationCanceledException && httpContext.RequestAborted.IsCancellationRequested)
        {
            return true;
        }

        var (status, response) = Map(exception, _env.IsDevelopment());

        httpContext.Response.StatusCode = status;
        httpContext.Response.ContentType = QlxContentTypesConstants.Json;

        if (status == 500)
        {
            LogUnhandledException(exception, exception.ToString());
        }
        else
        {
            LogBusinessException(exception.GetType().Name, exception.Message);
        }

        await httpContext.Response.WriteAsync(JsonSerializer.Serialize(response, s_jsonOptions), cancellationToken).ConfigureAwait(false);
        return true;
    }

    private static (int Status, ApiResponse<object> Response) Map(Exception exception, bool isDevelopment)
    {
        string devMessage = isDevelopment
            ? $"服务器内部错误：{exception.Message}{(exception.InnerException is not null ? " → " + exception.InnerException.Message : string.Empty)}"
            : "服务器内部错误";

        return exception switch
        {
            NotFoundException nf => ((int)HttpStatusCode.NotFound, ApiResponse<object>.Fail(nf.ErrorCode, nf.Message, HttpStatusCode.NotFound)),
            ConflictException cf => ((int)HttpStatusCode.Conflict, ApiResponse<object>.Fail(cf.ErrorCode, cf.Message, HttpStatusCode.Conflict)),
            ValidationException ve => ((int)HttpStatusCode.UnprocessableEntity, ApiResponse<object>.Fail(ve.ErrorCode, ve.Message, HttpStatusCode.UnprocessableEntity, ve.FieldErrors)),
            UnauthorizedException ua => ((int)HttpStatusCode.Unauthorized, ApiResponse<object>.Fail(ua.ErrorCode, ua.Message, HttpStatusCode.Unauthorized)),
            ForbiddenException fb => ((int)HttpStatusCode.Forbidden, ApiResponse<object>.Fail(fb.ErrorCode, fb.Message, HttpStatusCode.Forbidden)),
            // 请求格式非法（表单键/值超长、请求体解析失败等）：客户端错误一律 400，不暴露框架内部限制细节
            BadHttpRequestException or InvalidDataException => ((int)HttpStatusCode.BadRequest, ApiResponse<object>.Fail("BAD_REQUEST", "请求格式非法或超出大小限制", HttpStatusCode.BadRequest)),
            BusinessException be => ((int)HttpStatusCode.UnprocessableEntity, ApiResponse<object>.Fail(be.ErrorCode, be.Message, HttpStatusCode.UnprocessableEntity)),
            _ => ((int)HttpStatusCode.InternalServerError, ApiResponse<object>.Fail("INTERNAL_ERROR", devMessage, HttpStatusCode.InternalServerError)),
        };
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "未处理异常：{ExceptionMessage}")]
    private partial void LogUnhandledException(Exception exception, string exceptionMessage);

    [LoggerMessage(Level = LogLevel.Warning, Message = "业务异常 {ExceptionType}: {Message}")]
    private partial void LogBusinessException(string exceptionType, string message);
}
