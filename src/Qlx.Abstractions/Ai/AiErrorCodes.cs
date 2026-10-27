/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-25 10:00:00 +08:00 中国标准时间 星期日
 * 修改时间：2026-10-25 10:00:00 +08:00 中国标准时间 星期日
 * 描述：AI 调用层错误码常量
 */

namespace Qlx.Abstractions.Ai;

/// <summary>
/// AI 调用层错误码常量。门面级错误码用于 <c>AiInvocationException</c>，
/// 尝试级错误码用于 <see cref="AiInvocationAttempt.ErrorCode"/>。
/// </summary>
public static class AiErrorCodes
{
    /// <summary>AI 调用层未启用。</summary>
    public const string Disabled = "AI_DISABLED";

    /// <summary>厂商配置缺失（厂商名不存在或必要参数为空）。</summary>
    public const string ProviderNotConfigured = "AI_PROVIDER_NOT_CONFIGURED";

    /// <summary>厂商类型未注册。</summary>
    public const string ProviderTypeUnsupported = "AI_PROVIDER_TYPE_UNSUPPORTED";

    /// <summary>全部候选厂商调用失败。</summary>
    public const string InvocationFailed = "AI_INVOCATION_FAILED";

    /// <summary>请求参数非法。</summary>
    public const string RequestInvalid = "AI_REQUEST_INVALID";

    /// <summary>调用超时。</summary>
    public const string Timeout = "TIMEOUT";

    /// <summary>网络错误。</summary>
    public const string NetworkError = "NETWORK_ERROR";

    /// <summary>响应内容无效（空内容或非法 JSON）。</summary>
    public const string InvalidResponse = "INVALID_RESPONSE";

    /// <summary>未归类的意外错误。</summary>
    public const string UnexpectedError = "UNEXPECTED_ERROR";

    /// <summary>
    /// 由 HTTP 状态码生成尝试级错误码（如 <c>HTTP_429</c>）。
    /// </summary>
    /// <param name="statusCode">HTTP 状态码。</param>
    /// <returns>错误码。</returns>
    public static string HttpStatus(int statusCode) => $"HTTP_{statusCode}";
}
