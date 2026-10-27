/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-27 10:30:00 +08:00 中国标准时间 星期二
 * 修改时间：2026-10-27 10:30:00 +08:00 中国标准时间 星期二
 * 描述：全局 API 响应包装中间件——/api 路由 JSON 响应统一封装 ApiResponse 信封；
 *       已包装响应（含 success 字段）与 500（异常处理器领地）原样透传，杜绝重复包装
 */

using Microsoft.AspNetCore.Http;

using Qlx.Abstractions.Constants;
using Qlx.Abstractions.Exceptions;
using Qlx.Abstractions.Serialization;

using System.Net;
using System.Text.Json;

namespace Qlx.BuildingBlocks.Api;

/// <summary>
/// 全局 API 响应包装中间件。职责边界：
/// <list type="bullet">
/// <item>已包装响应（JSON 根含 <c>success</c> 字段：Controller 手动 <see cref="ApiResponse{T}.Ok"/>、
///     异常处理器输出、模型校验工厂输出）原样透传，不重复包装；</item>
/// <item>HTTP 500 属 <see cref="QlxApiExceptionHandler"/> 领地，原样透传（异常冒泡时恢复原始响应流后上抛）；</item>
/// <item>4xx 裸 JSON（ProblemDetails 等框架短路输出）包装为 <c>success=false</c> 信封，ProblemDetails errors 映射为字段级错误列表；</item>
/// <item>2xx 裸 JSON（漏包装的 Controller 返回值）兜底包装为 <c>success=true</c> 信封；</item>
/// <item>非 /api 路由、非 JSON 响应、空响应体、SSE 协商请求、<see cref="SkipApiResponseWrapAttribute"/> 标记端点一律直通（不缓冲）。</item>
/// </list>
/// 注册位置须在 UseRouting 之后（依赖端点元数据），经 <see cref="ApiResponseWrapperExtensions.UseQlxApiResponseWrapping"/> 启用。
/// </summary>
public sealed class ApiResponseWrapperMiddleware
{
    private static readonly JsonSerializerOptions s_jsonOptions = QlxJsonDefaults.Default;
    private readonly RequestDelegate _next;

    /// <summary>
    /// 构造函数。
    /// </summary>
    /// <param name="next">下游请求委托。</param>
    public ApiResponseWrapperMiddleware(RequestDelegate next)
    {
        ArgumentNullException.ThrowIfNull(next);
        _next = next;
    }

    /// <summary>
    /// 处理请求：直通场景直接放行；其余缓冲响应体后按需包装。
    /// </summary>
    /// <param name="context">HTTP 上下文。</param>
    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (ShouldBypass(context))
        {
            await _next(context).ConfigureAwait(false);
            return;
        }

        Stream originalBody = context.Response.Body;
        MemoryStream buffer = new();
        context.Response.Body = buffer;
        try
        {
            await _next(context).ConfigureAwait(false);
        }
        catch
        {
            // 异常（含 500 场景）由外层 QlxApiExceptionHandler 统一接管：恢复原始响应流后原样上抛，不写响应
            context.Response.Body = originalBody;
            await buffer.DisposeAsync().ConfigureAwait(false);
            throw;
        }

        context.Response.Body = originalBody;
        buffer.Position = 0;
        await using (buffer.ConfigureAwait(false))
        {
            // 500 属异常处理器领地；已开始发送 / 空体 / 非 JSON 响应不在包装范围
            if (context.Response.StatusCode == StatusCodes.Status500InternalServerError
                || context.Response.HasStarted
                || buffer.Length == 0
                || !IsJson(context.Response.ContentType))
            {
                await buffer.CopyToAsync(originalBody, context.RequestAborted).ConfigureAwait(false);
                return;
            }

            await WrapAsync(context, buffer, originalBody).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// 直通判定：非 /api 路由（OpenIddict 端点 / OpenAPI / 健康检查等）、显式标记豁免、SSE 协商请求。
    /// </summary>
    private static bool ShouldBypass(HttpContext context)
    {
        return !context.Request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase)
                || context.GetEndpoint()?.Metadata.GetMetadata<SkipApiResponseWrapAttribute>() is not null
                || context.Request.Headers.Accept.Any(value => value?.Contains("text/event-stream", StringComparison.OrdinalIgnoreCase) == true);
    }

    private static bool IsJson(string? contentType)
    {
        return contentType is not null
                && (contentType.StartsWith("application/json", StringComparison.OrdinalIgnoreCase)
                    || contentType.StartsWith("application/problem+json", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// 包装判定与输出：已含 success 字段的透传；4xx 包装为 Fail；其余 2xx 裸 JSON 包装为 Ok。
    /// </summary>
    private static async Task WrapAsync(HttpContext context, MemoryStream buffer, Stream originalBody)
    {
        JsonDocument? document;
        try
        {
            document = await JsonDocument.ParseAsync(buffer, cancellationToken: context.RequestAborted).ConfigureAwait(false);
        }
        catch (JsonException)
        {
            // ContentType 声明为 JSON 但内容非法：不做二次猜测，原样透传
            document = null;
        }

        using (document)
        {
            if (document is null || IsAlreadyWrapped(document.RootElement))
            {
                buffer.Position = 0;
                await buffer.CopyToAsync(originalBody, context.RequestAborted).ConfigureAwait(false);
                return;
            }

            int statusCode = context.Response.StatusCode;
            ApiResponse<object> wrapped = statusCode switch
            {
                StatusCodes.Status400BadRequest => ApiResponse<object>.Fail(
                    "VALIDATION_ERROR", "请求数据校验失败", HttpStatusCode.BadRequest, ExtractFieldErrors(document.RootElement)),
                >= StatusCodes.Status400BadRequest and < StatusCodes.Status500InternalServerError => ApiResponse<object>.Fail(
                    $"HTTP_{statusCode}", ExtractTitle(document.RootElement) ?? "请求处理失败", (HttpStatusCode)statusCode),
                _ => ApiResponse<object>.Ok(document.RootElement.Clone()),
            };

            byte[] payload = JsonSerializer.SerializeToUtf8Bytes(wrapped, s_jsonOptions);
            context.Response.ContentType = QlxContentTypesConstants.Json;
            context.Response.ContentLength = payload.Length;
            await originalBody.WriteAsync(payload, context.RequestAborted).ConfigureAwait(false);
        }
    }

    /// <summary>已包装判定：JSON 根为对象且含 success 字段（ApiResponse 信封的必备字段）。</summary>
    private static bool IsAlreadyWrapped(JsonElement root)
    {
        return root.ValueKind == JsonValueKind.Object && root.TryGetProperty(nameof(ApiResponse.Success), out _);
    }

    /// <summary>从 ProblemDetails 的 errors 字典提取字段级错误列表（非 ProblemDetails 结构返回 null）。</summary>
    private static List<FieldError>? ExtractFieldErrors(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("errors", out JsonElement errorsElement)
            || errorsElement.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        List<FieldError> errors = [];
        foreach (JsonProperty property in errorsElement.EnumerateObject())
        {
            if (property.Value.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement item in property.Value.EnumerateArray())
                {
                    errors.Add(new FieldError(property.Name, item.GetString() ?? string.Empty));
                }
            }
            else if (property.Value.ValueKind == JsonValueKind.String)
            {
                errors.Add(new FieldError(property.Name, property.Value.GetString() ?? string.Empty));
            }
        }

        return errors.Count > 0 ? errors : null;
    }

    /// <summary>提取 ProblemDetails 的 title 作为错误消息（无 title 返回 null 走默认文案）。</summary>
    private static string? ExtractTitle(JsonElement root)
    {
        return root.ValueKind == JsonValueKind.Object && root.TryGetProperty("title", out JsonElement title)
                ? title.GetString()
                : null;
    }
}
