/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-25 10:00:00 +08:00 中国标准时间 星期日
 * 修改时间：2026-10-25 10:00:00 +08:00 中国标准时间 星期日
 * 描述：AI 调用全部失败标准化业务异常（携带完整厂商调用链）
 */

using Qlx.Abstractions.Ai;

namespace Qlx.Abstractions.Exceptions;

/// <summary>
/// AI 调用全部失败标准化业务异常。<see cref="Exception.Message"/> 内联调用链摘要，
/// <see cref="Attempts"/> 与 <see cref="BusinessException.Details"/> 提供逐厂商结构化明细。
/// </summary>
/// <remarks>
/// 构造函数。
/// </remarks>
/// <param name="errorCode">业务错误码（见 <see cref="AiErrorCodes"/>）。</param>
/// <param name="message">面向业务用户的可读消息（含逐厂商失败摘要，不含密钥）。</param>
/// <param name="attempts">本次调用的逐厂商尝试明细。</param>
/// <param name="innerException">内部异常（可选）。</param>
public sealed class AiInvocationException(
    string errorCode,
    string message,
    IReadOnlyList<AiInvocationAttempt> attempts,
    Exception? innerException = null)
    : BusinessException(errorCode, message, new { Attempts = attempts }, innerException)
{
    /// <summary>本次调用的逐厂商尝试明细。</summary>
    public IReadOnlyList<AiInvocationAttempt> Attempts { get; } = attempts;
}
