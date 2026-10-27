/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-25 10:00:00 +08:00 中国标准时间 星期日
 * 修改时间：2026-10-25 10:00:00 +08:00 中国标准时间 星期日
 * 描述：AI 厂商调用失败信号异常
 */

namespace Qlx.Abstractions.Ai;

/// <summary>
/// AI 厂商适配器失败信号。由 <see cref="IAiChatClient"/> 实现抛出，
/// 由 <see cref="IAiInvoker"/> 归类为失败并继续尝试下一个候选厂商。
/// </summary>
/// <remarks>
/// 构造函数。
/// </remarks>
/// <param name="providerName">厂商名。</param>
/// <param name="errorCode">错误码（见 <see cref="AiErrorCodes"/>）。</param>
/// <param name="message">失败原因摘要（不得包含密钥）。</param>
/// <param name="httpStatusCode">HTTP 状态码（未发起请求时为 <see langword="null"/>）。</param>
/// <param name="innerException">内部异常（可选）。</param>
public sealed class AiProviderException(
    string providerName,
    string errorCode,
    string message,
    int? httpStatusCode = null,
    Exception? innerException = null) : Exception(message, innerException)
{
    /// <summary>厂商名。</summary>
    public string ProviderName { get; } = providerName;

    /// <summary>错误码。</summary>
    public string ErrorCode { get; } = errorCode;

    /// <summary>HTTP 状态码（未发起请求时为 <see langword="null"/>）。</summary>
    public int? HttpStatusCode { get; } = httpStatusCode;
}
