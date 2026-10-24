/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-22 16:05:00 +08:00 中国标准时间 星期三
 * 修改时间：2026-10-22 16:05:00 +08:00 中国标准时间 星期三
 * 描述：SMS 发送契约 + 阶段二 NotConfigured 降级桩实现
 */

using Microsoft.Extensions.Logging;

#pragma warning disable CA1848 // 使用 LoggerMessage 委托提高性能（阶段二暂用内联日志）

namespace Qlx.BuildingBlocks.Messaging;

/// <summary>
/// SMS 发送契约。实现类由阶段三按供应商（阿里云 / 腾讯云等）接入。
/// </summary>
public interface ISmsSender
{
    /// <summary>
    /// 发送 SMS。
    /// </summary>
    /// <param name="message">SMS 消息。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>发送结果（阶段二桩实现返回 NotConfigured）。</returns>
    Task<SmsSendResult> SendAsync(SmsMessage message, CancellationToken cancellationToken);
}

/// <summary>
/// SMS 消息。
/// </summary>
public sealed record SmsMessage(
    string Phone,
    string TemplateCode,
    IReadOnlyDictionary<string, string>? TemplateParams = null,
    string? SignName = null);

/// <summary>
/// SMS 发送状态。
/// </summary>
public enum SmsSendStatus
{
    /// <summary>发送成功。</summary>
    Sent,

    /// <summary>供应商未接入（阶段二桩默认返回）。</summary>
    NotConfigured,

    /// <summary>发送失败。</summary>
    Failed
}

/// <summary>
/// SMS 发送结果。
/// </summary>
public sealed record SmsSendResult(SmsSendStatus Status, string? ErrorMessage = null);

/// <summary>
/// SMS 供应商未接入时的降级桩实现：所有调用记录 Warning 日志 + 返回 NotConfigured。
/// 阶段三接入真实供应商后由 DI 覆盖注册。
/// </summary>
public sealed class NotConfiguredSmsSender : ISmsSender
{
    private readonly ILogger<NotConfiguredSmsSender> _logger;

    /// <summary>
    /// 构造函数。
    /// </summary>
    /// <param name="logger">日志器。</param>
    public NotConfiguredSmsSender(ILogger<NotConfiguredSmsSender> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    /// <inheritdoc />
    public Task<SmsSendResult> SendAsync(SmsMessage message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        _logger.LogWarning("SMS 供应商未接入，消息已降级站内。Phone={Phone}", MaskPhone(message.Phone));
        return Task.FromResult(new SmsSendResult(SmsSendStatus.NotConfigured, "SMS 供应商未接入"));
    }

    private static string MaskPhone(string phone) => phone.Length >= 7
        ? phone[..3] + "****" + phone[^4..]
        : "***";
}
