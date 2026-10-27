/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-20 23:30:00 +08:00 中国标准时间 星期二
 * 修改时间：2026-10-20 23:30:00 +08:00 中国标准时间 星期二
 * 描述：统一响应包装基类，所有 Controller 返回值经统一异常处理器（QlxApiExceptionHandler）自动封装
 */

using System.Net;
using System.Text.Json.Serialization;

using Microsoft.Extensions.Logging;

using Qlx.Abstractions.Exceptions;

namespace Qlx.BuildingBlocks.Api;

/// <summary>
/// API 统一响应包装。业务层只管抛异常，由
/// <see cref="QlxApiExceptionHandler"/> 拦截并封装为 Success=false 的响应；
/// 正常返回值经工厂方法创建。
/// </summary>
public sealed record ApiResponse
{
    /// <summary>请求是否成功。</summary>
    [JsonPropertyName("success")]
    public bool Success { get; init; }

    /// <summary>业务错误码（success=false 时必填；success=true 时为空）。</summary>
    [JsonPropertyName("code")]
    public string? Code { get; init; }

    /// <summary>人类可读消息（成功提示 / 错误描述）。</summary>
    [JsonPropertyName("message")]
    public string? Message { get; init; }

    /// <summary>时间戳（UTC）。</summary>
    [JsonPropertyName("timestamp")]
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>创建成功响应（无数据体）。</summary>
    [LoggerMessage(EventId = 0, Level = LogLevel.Information, Message = "业务成功：{Message}")]
    public static ApiResponse Ok(string? message = null) => new() { Success = true, Message = message };

    /// <summary>创建失败响应。</summary>
    [LoggerMessage(EventId = 0, Level = LogLevel.Error, Message = "业务错误：{Code} {Message}")]
    public static ApiResponse Fail(string code, string? message = null) => new() { Success = false, Code = code, Message = message };
}

/// <summary>
/// 泛型 API 响应包装，承载业务数据体。
/// </summary>
public sealed record ApiResponse<T>
{
    /// <summary>请求是否成功。</summary>
    [JsonPropertyName("success")]
    public bool Success { get; init; }

    /// <summary>业务错误码（success=false 时必填）。</summary>
    [JsonPropertyName("code")]
    public string? Code { get; init; }

    /// <summary>人类可读消息。</summary>
    [JsonPropertyName("message")]
    public string? Message { get; init; }

    /// <summary>业务数据体（success=true 时存在）。</summary>
    [JsonPropertyName("data")]
    public T? Data { get; init; }

    /// <summary>字段级错误列表（校验失败时）。</summary>
    [JsonPropertyName("errors")]
    public IReadOnlyList<FieldError>? Errors { get; init; }

    /// <summary>HTTP 状态码（仅中间件使用）。</summary>
    [JsonIgnore]
    public HttpStatusCode StatusCode { get; init; } = HttpStatusCode.OK;

    /// <summary>时间戳（UTC）。</summary>
    [JsonPropertyName("timestamp")]
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>创建成功响应。</summary>
    [LoggerMessage(EventId = 0, Level = LogLevel.Information, Message = "业务成功：{Message}")]
    public static ApiResponse<T> Ok(T data, string? message = null) => new()
    {
        Success = true,
        Data = data,
        Message = message,
    };

    /// <summary>创建失败响应。</summary>
    [LoggerMessage(EventId = 0, Level = LogLevel.Error, Message = "业务错误：{Code} {Message}")]
    public static ApiResponse<T> Fail(string code, string? message, HttpStatusCode status = HttpStatusCode.BadRequest, IReadOnlyList<FieldError>? errors = null) => new()
    {
        Success = false,
        Code = code,
        Message = message,
        StatusCode = status,
        Errors = errors,
    };
}
