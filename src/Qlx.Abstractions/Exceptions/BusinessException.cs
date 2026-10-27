/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-20 23:30:00 +08:00 中国标准时间 星期二
 * 修改时间：2026-10-20 23:30:00 +08:00 中国标准时间 星期二
 * 描述：业务异常基类，携带 ErrorCode + Message + 可选 Details，由中间件统一拦截包装
 */

namespace Qlx.Abstractions.Exceptions;

/// <summary>
/// 业务异常基类。业务层只管抛，由 API 层中间件统一拦截并包装为结构化返回。
/// </summary>
/// <remarks>
/// 构造函数。
/// </remarks>
/// <param name="errorCode">业务错误码。</param>
/// <param name="message">面向业务用户的可读消息。</param>
/// <param name="details">可选详细信息。</param>
/// <param name="innerException">内部异常（可选）。</param>
public abstract class BusinessException(string errorCode, string message, object? details = null, Exception? innerException = null) : Exception(message, innerException)
{
    /// <summary>业务错误码（用于前端精准定位错误类型）。</summary>
    public string ErrorCode { get; } = errorCode;

    /// <summary>可选的详细信息（调试用，生产敏感内容由异常子类内部拦截）。</summary>
    public object? Details { get; } = details;
}

/// <summary>
/// 资源不存在（HTTP 404）。
/// </summary>
/// <remarks>
/// 构造函数。
/// </remarks>
/// <param name="resourceType">资源类型（如 采购单）。</param>
/// <param name="resourceId">资源标识。</param>
public sealed class NotFoundException(string resourceType, string resourceId) : BusinessException("NOT_FOUND", $"未找到{resourceType}：{resourceId}", new { ResourceType = resourceType, ResourceId = resourceId })
{
}

/// <summary>
/// 资源冲突（HTTP 409），如唯一约束违反、状态机冲突等。
/// </summary>
/// <remarks>
/// 构造函数。
/// </remarks>
/// <param name="message">面向业务用户的可读消息。</param>
/// <param name="errorCode">业务错误码。</param>
/// <param name="details">可选详细信息（如停用前置校验命中的岗位名列表）。</param>
public sealed class ConflictException(string message, string errorCode = "CONFLICT", object? details = null) : BusinessException(errorCode, message, details)
{
}

/// <summary>
/// 字段校验错误（HTTP 400）。
/// </summary>
public sealed record FieldError(string FieldName, string ErrorMessage);

/// <summary>
/// 校验异常（HTTP 422），携带多字段错误列表。
/// </summary>
/// <remarks>
/// 构造函数。
/// </remarks>
public sealed class ValidationException(IEnumerable<FieldError> fieldErrors) : BusinessException("VALIDATION_ERROR", "请求数据校验失败", fieldErrors.ToList())
{
    /// <summary>字段级错误列表。</summary>
    public IReadOnlyList<FieldError> FieldErrors { get; } = [.. fieldErrors];

    /// <summary>
    /// 单字段便捷构造。
    /// </summary>
    public ValidationException(string fieldName, string errorMessage)
        : this([new FieldError(fieldName, errorMessage)])
    {
    }
}

/// <summary>
/// 未认证（HTTP 401）。
/// </summary>
/// <remarks>
/// 构造函数。
/// </remarks>
public sealed class UnauthorizedException(string message = "请先登录") : BusinessException("UNAUTHORIZED", message)
{
}

/// <summary>
/// 已认证但无权限（HTTP 403）。
/// </summary>
/// <remarks>
/// 构造函数。
/// </remarks>
public sealed class ForbiddenException(string message = "无权限执行此操作") : BusinessException("FORBIDDEN", message)
{
}
