/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-22 22:20:00 +08:00 中国标准时间 星期四
 * 描述：IMessageService 阶段二 NoOp 降级桩（2C 任务中心通知链路默认实现，2D 信息中心落地后替换）
 */

using Microsoft.Extensions.Logging;
using Qlx.Abstractions.Messages;

namespace Qlx.BuildingBlocks.Messaging;

/// <summary>
/// <see cref="IMessageService"/> 未接入降级桩：2C 任务中心的通知链路（任务终态 / SYSTEM_ALERT /
/// task.progress）经本实现落日志后丢弃，不做持久化与渠道分发。
/// 2D 信息中心（模板渲染 + SSE 推送）落地后由 DI 覆盖注册真实实现，调用方零改动。
/// </summary>
public sealed partial class NotConfiguredMessageService(ILogger<NotConfiguredMessageService> logger) : IMessageService
{
    private readonly ILogger<NotConfiguredMessageService> _logger = logger;

    /// <inheritdoc />
    public Task SendTemplateAsync(SendTemplateRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        LogNotConfigured(nameof(SendTemplateAsync), request.TemplateCode, request.RecipientUserId);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task PushAsync(PushRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        // 2C 任务终态 / 告警推送在 NoOp 阶段仅落 Information 日志（2D 接入后经 SSE 分发）
        LogPush(request.Subject, request.RecipientUserId, request.Content);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task MarkReadAsync(string messageInboxId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(messageInboxId);
        LogNotConfigured(nameof(MarkReadAsync), messageInboxId, string.Empty);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task MarkAllReadAsync(string userId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        LogNotConfigured(nameof(MarkAllReadAsync), userId, string.Empty);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<int> GetUnreadCountAsync(string userId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        LogNotConfigured(nameof(GetUnreadCountAsync), userId, string.Empty);
        return Task.FromResult(0);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<MessageInfo>> GetInboxAsync(MessageQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        LogNotConfigured(nameof(GetInboxAsync), string.Empty, string.Empty);
        return Task.FromResult<IReadOnlyList<MessageInfo>>([]);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<MessageInfo>> GetSentListAsync(MessageQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        LogNotConfigured(nameof(GetSentListAsync), string.Empty, string.Empty);
        return Task.FromResult<IReadOnlyList<MessageInfo>>([]);
    }

    [LoggerMessage(Level = LogLevel.Information,
        Message = "消息服务未接入（2D 信息中心落地前为 NoOp），调用已降级记录：{Operation} / {Key} / {Detail}")]
    private partial void LogNotConfigured(string operation, string key, string detail);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "消息推送已降级记录（IMessageService NoOp）：[{Subject}] → {Recipient}：{Content}")]
    private partial void LogPush(string subject, string recipient, string content);
}
