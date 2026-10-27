/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-25 10:00:00 +08:00 中国标准时间 星期日
 * 修改时间：2026-10-25 10:00:00 +08:00 中国标准时间 星期日
 * 描述：AI 单次调用尝试明细 record
 */

namespace Qlx.Abstractions.Ai;

/// <summary>
/// AI 单次厂商调用尝试明细（用于异常链路与日志排障）。
/// </summary>
/// <param name="Index">尝试序号（从 1 开始）。</param>
/// <param name="ProviderName">厂商名。</param>
/// <param name="ProviderType">厂商类型标识。</param>
/// <param name="Model">本次尝试使用的模型（解析后，可为空）。</param>
/// <param name="DurationMilliseconds">本次尝试耗时（毫秒）。</param>
/// <param name="Outcome">尝试结果。</param>
/// <param name="HttpStatusCode">HTTP 状态码（未发起请求时为 <see langword="null"/>）。</param>
/// <param name="ErrorCode">失败错误码（成功时为 <see langword="null"/>）。</param>
/// <param name="ErrorMessage">失败原因摘要（截断、不含密钥；成功时为 <see langword="null"/>）。</param>
public sealed record AiInvocationAttempt(
    int Index,
    string ProviderName,
    string ProviderType,
    string? Model,
    long DurationMilliseconds,
    AiAttemptOutcome Outcome,
    int? HttpStatusCode,
    string? ErrorCode,
    string? ErrorMessage);
