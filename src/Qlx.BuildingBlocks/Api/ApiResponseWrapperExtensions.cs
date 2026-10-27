/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-27 10:30:00 +08:00 中国标准时间 星期二
 * 修改时间：2026-10-27 10:30:00 +08:00 中国标准时间 星期二
 * 描述：统一响应包装注册扩展——DI 侧 ConfigureApiBehaviorOptions（[ApiController] 自动 400 包装），管线侧注册包装中间件
 */

using System.Net;
using System.Text.Json;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

using Qlx.Abstractions.Exceptions;

namespace Qlx.BuildingBlocks.Api;

/// <summary>
/// 统一响应包装注册扩展。<see cref="AddQlxApiResponseWrapping"/> 负责把 [ApiController] 自动 400
/// （JSON 反序列化失败 / 模型校验失败，框架短路为 ProblemDetails、不经异常处理器）改写为
/// <see cref="ApiResponse{T}"/> 信封；<see cref="UseQlxApiResponseWrapping"/> 注册
/// <see cref="ApiResponseWrapperMiddleware"/> 作为全局兜底（裸 JSON 成功响应包装、漏网 4xx 包装）。
/// </summary>
public static class ApiResponseWrapperExtensions
{
    /// <summary>
    /// 注册统一响应包装（IMvcBuilder 链式扩展，强制挂在 AddControllers 之后，保证覆盖 MVC 默认的
    /// InvalidModelStateResponseFactory）。模型校验失败统一返回 HTTP 400 + VALIDATION_ERROR 信封；
    /// JSON 反序列化失败（<see cref="JsonException"/>）消息归一为友好文案，不暴露框架内部细节。
    /// </summary>
    /// <param name="builder">MVC 构建器。</param>
    /// <returns>同一构建器，支持链式调用。</returns>
    public static IMvcBuilder AddQlxApiResponseWrapping(this IMvcBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        _ = builder.Services.Configure<ApiBehaviorOptions>(options =>
        {
            options.InvalidModelStateResponseFactory = context =>
            {
                List<FieldError> errors = [.. context.ModelState
                    .Where(pair => pair.Value?.Errors.Count > 0)
                    .SelectMany(pair => pair.Value!.Errors.Select(error => new FieldError(
                        pair.Key,
                        IsJsonError(pair.Key, error.Exception) ? "请求体 JSON 格式或字段类型非法" : error.ErrorMessage)))];

                return new BadRequestObjectResult(ApiResponse<object>.Fail(
                    "VALIDATION_ERROR", "请求数据校验失败", HttpStatusCode.BadRequest, errors));
            };
        });
        return builder;
    }

    /// <summary>
    /// 判定模型错误是否源自 JSON 反序列化。System.Text.Json 输入格式化器以 JSON 路径（<c>$</c> / <c>$.field</c>）
    /// 作为 ModelState 键，且部分版本仅入字符串消息（Exception 为空），故以键前缀为主、异常链兜底。
    /// </summary>
    private static bool IsJsonError(string key, Exception? exception)
    {
        if (key.StartsWith('$'))
        {
            return true;
        }

        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is JsonException)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>启用统一响应包装中间件（须在 UseRouting 之后、端点映射之前；依赖端点元数据识别豁免标记）。</summary>
    public static IApplicationBuilder UseQlxApiResponseWrapping(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);
        return app.UseMiddleware<ApiResponseWrapperMiddleware>();
    }
}
