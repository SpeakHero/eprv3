/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-22 16:05:00 +08:00 中国标准时间 星期三
 * 修改时间：2026-10-22 16:05:00 +08:00 中国标准时间 星期三
 * 描述：SMTP 邮件发送契约与消息 DTO
 */

namespace Qlx.BuildingBlocks.Messaging;

/// <summary>
/// SMTP 邮件发送契约。实现类负责连接 SMTP 服务器、认证、发送 HTML 邮件并返回分类化的发送结果。
/// 所有公开方法以 Async 后缀命名并接受 CancellationToken 参数。
/// </summary>
public interface ISmtpSender
{
    /// <summary>
    /// 发送邮件。
    /// </summary>
    /// <param name="message">邮件消息。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>发送结果（失败分类返回 SmtpSendResult，不抛异常）。</returns>
    Task<SmtpSendResult> SendAsync(SmtpMessage message, CancellationToken cancellationToken);
}

/// <summary>
/// SMTP 邮件消息。
/// </summary>
public sealed record SmtpMessage(
    string ToEmail,
    string ToName,
    string Subject,
    string HtmlBody,
    string? TextBody = null,
    string? FromEmail = null,
    string? FromName = null);

/// <summary>
/// SMTP 发送状态枚举（分类化结果，实现按异常类型映射）。
/// </summary>
public enum SmtpSendStatus
{
    /// <summary>发送成功。</summary>
    Sent,

    /// <summary>SMTP 配置缺失（未启用 / Host 为空）。</summary>
    ConfigurationMissing,

    /// <summary>认证失败（用户名 / 密码错误）。</summary>
    AuthenticationFailed,

    /// <summary>网络错误（连接超时 / DNS 解析失败 / Socket 异常）。</summary>
    NetworkError,

    /// <summary>其他 SMTP 错误（服务器拒绝 / 协议错误等）。</summary>
    ProtocolError
}

/// <summary>
/// SMTP 发送结果。
/// </summary>
public sealed record SmtpSendResult(SmtpSendStatus Status, string? ErrorMessage = null);
