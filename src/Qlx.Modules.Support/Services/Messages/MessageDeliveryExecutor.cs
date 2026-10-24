/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-23 07:50:00 +08:00 中国标准时间 星期五
 * 描述：消息渠道分发执行器（Hangfire Worker 作用域内执行）——邮件 SMTP 发送 + 退避重试 +
 *       短信/推送降级标记；send_logs 只增不改（每次尝试一行）；站内信已在 write-through 阶段持久化
 */

using System.Globalization;
using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Qlx.Abstractions.Messages;
using Qlx.Abstractions.Queries;
using Qlx.BuildingBlocks.Messaging;
using Qlx.Modules.Support.Domain;
using Qlx.Modules.Support.Options;

namespace Qlx.Modules.Support.Services.Messages;

/// <summary>
/// 渠道分发执行器（<see cref="IMessageDeliveryExecutor"/> 的 Support 实现）。
/// 由 <see cref="Qlx.BuildingBlocks.Messaging.MessageDeliveryInvoker"/> 在租户上下文填充后
/// 作用域内解析执行：按 ChannelMask 逐渠道投递并落 send_logs（只增不改），
/// 全部渠道处理后推进消息 Queued → Sent（状态机守卫），外部渠道永久失败时置降级标记。
/// </summary>
public sealed partial class MessageDeliveryExecutor(
    SupportTenantDbContext db,
    ISmtpSender smtpSender,
    ISmsSender smsSender,
    IServiceProvider serviceProvider,
    IMessageDispatchEnqueuer dispatchEnqueuer,
    IOptions<SupportMessagesOptions> options,
    ILogger<MessageDeliveryExecutor> logger) : IMessageDeliveryExecutor
{
    private readonly SupportTenantDbContext _db = db;
    private readonly ISmtpSender _smtpSender = smtpSender;
    private readonly ISmsSender _smsSender = smsSender;
    private readonly IServiceProvider _serviceProvider = serviceProvider;
    private readonly IMessageDispatchEnqueuer _dispatchEnqueuer = dispatchEnqueuer;
    private readonly SupportMessagesOptions _options = options.Value;
    private readonly ILogger<MessageDeliveryExecutor> _logger = logger;

    /// <inheritdoc />
    public async Task ExecuteDeliveryAsync(
        long messageId, long tenantId, bool forceRetry, CancellationToken cancellationToken)
    {
        var message = await _db.Messages
            .FirstOrDefaultAsync(m => m.Id == messageId, cancellationToken)
            .ConfigureAwait(false);
        if (message is null)
        {
            // 消息不存在（已清理 / 跨库错投）：记录后直接返回，避免 Worker 反复重试
            LogMessageMissing(messageId, tenantId);
            return;
        }

        var now = DateTimeOffset.UtcNow;
        List<MessageSendLogEntity> attempts = [];
        List<(MessageChannel Channel, int RetryCount)> retryCandidates = [];

        if (MessageChannelMask.Has(message.ChannelMask, MessageChannel.Email))
        {
            await DeliverEmailAsync(message, tenantId, now, attempts, retryCandidates, forceRetry, cancellationToken)
                .ConfigureAwait(false);
        }

        if (MessageChannelMask.Has(message.ChannelMask, MessageChannel.Sms))
        {
            await DeliverSmsAsync(message, tenantId, now, attempts, cancellationToken).ConfigureAwait(false);
        }

        if (MessageChannelMask.Has(message.ChannelMask, MessageChannel.Push))
        {
            DeliverPush(message, tenantId, now, attempts);
        }

        // 全部渠道处理完毕（成功或最终失败降级）：站内信 write-through 已达，推进 Queued → Sent
        if (MessageStateMachine.CanTransition(message.Status, MessageStatus.Sent))
        {
            message.SetStatus(MessageStatus.Sent, now);
        }

        if (attempts.Count > 0 || message.Status == MessageStatus.Sent)
        {
            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        // 重试入队在持久化之后（退避等待：base × 2^重试序号）
        foreach (var (channel, retryCount) in retryCandidates)
        {
            var delay = TimeSpan.FromSeconds(_options.RetryBaseDelaySeconds * Math.Pow(2, retryCount));
            _dispatchEnqueuer.EnqueueIn(delay, messageId, tenantId);
            LogRetryScheduled(messageId, channel, retryCount + 1, delay.TotalSeconds);
        }
    }

    /// <summary>邮件渠道投递：解析收件人邮箱 → SMTP 发送 → send_logs 记录 → 失败按上限调度重试。</summary>
    private async Task DeliverEmailAsync(
        MessageEntity message, long tenantId, DateTimeOffset now,
        List<MessageSendLogEntity> attempts, List<(MessageChannel, int)> retryCandidates, bool forceRetry,
        CancellationToken cancellationToken)
    {
        var (attemptCount, lastSuccess) = await GetChannelProgressAsync(
            message.Id, MessageChannel.Email, cancellationToken).ConfigureAwait(false);
        if (lastSuccess || (!forceRetry && attemptCount > _options.EmailRetryLimit))
        {
            return; // 已送达，或（非人工重试时）超出重试上限（最终失败，保持降级标记）
        }

        var contact = await ResolveRecipientContactAsync(tenantId, message.RecipientUserId, cancellationToken)
            .ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(contact?.Email))
        {
            attempts.Add(new MessageSendLogEntity(
                tenantId, message.Id, MessageChannel.Email, message.RecipientUserId,
                MessageSendLogStatus.Failed, "收件人邮箱缺失（成员资料未登记邮箱），已降级站内信。",
                attemptCount, now));
            MarkDegraded(message);
            return;
        }

        var result = await _smtpSender.SendAsync(
            new SmtpMessage(
                contact.Email, contact.DisplayName,
                message.Subject,
                WebUtility.HtmlEncode(message.Content),
                TextBody: message.Content),
            cancellationToken).ConfigureAwait(false);

        if (result.Status == SmtpSendStatus.Sent)
        {
            attempts.Add(new MessageSendLogEntity(
                tenantId, message.Id, MessageChannel.Email, contact.Email,
                MessageSendLogStatus.Success, null, attemptCount, now));
            return;
        }

        attempts.Add(new MessageSendLogEntity(
            tenantId, message.Id, MessageChannel.Email, contact.Email,
            MessageSendLogStatus.Failed, $"{result.Status}: {result.ErrorMessage}", attemptCount, now));
        if (attemptCount < _options.EmailRetryLimit)
        {
            retryCandidates.Add((MessageChannel.Email, attemptCount));
        }
        else
        {
            MarkDegraded(message);
        }
    }

    /// <summary>短信渠道投递：阶段二为 NotConfigured 桩，直接落失败日志并降级站内（不重试）。</summary>
    private async Task DeliverSmsAsync(
        MessageEntity message, long tenantId, DateTimeOffset now,
        List<MessageSendLogEntity> attempts, CancellationToken cancellationToken)
    {
        var contact = await ResolveRecipientContactAsync(tenantId, message.RecipientUserId, cancellationToken)
            .ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(contact?.Phone))
        {
            attempts.Add(new MessageSendLogEntity(
                tenantId, message.Id, MessageChannel.Sms, message.RecipientUserId,
                MessageSendLogStatus.Failed, "收件人手机号缺失（成员资料未登记手机号），已降级站内信。",
                0, now));
            MarkDegraded(message);
            return;
        }

        var result = await _smsSender.SendAsync(
            new SmsMessage(contact.Phone, TemplateCode: "NOTICE",
                TemplateParams: new Dictionary<string, string> { ["content"] = message.Content }),
            cancellationToken).ConfigureAwait(false);

        var success = result.Status == SmsSendStatus.Sent;
        attempts.Add(new MessageSendLogEntity(
            tenantId, message.Id, MessageChannel.Sms, contact.Phone,
            success ? MessageSendLogStatus.Success : MessageSendLogStatus.Failed,
            success ? null : $"{result.Status}: {result.ErrorMessage}", 0, now));
        if (!success)
        {
            MarkDegraded(message);
        }
    }

    /// <summary>推送渠道投递：阶段二无推送供应商，落失败日志并降级站内（不重试）。</summary>
    private static void DeliverPush(
        MessageEntity message, long tenantId, DateTimeOffset now, List<MessageSendLogEntity> attempts)
    {
        attempts.Add(new MessageSendLogEntity(
            tenantId, message.Id, MessageChannel.Push, message.RecipientUserId,
            MessageSendLogStatus.Failed, "推送渠道未接入（阶段二预留），已降级站内信。", 0, now));
        MarkDegraded(message);
    }

    /// <summary>渠道进度判定：最近一次尝试是否成功 + 已尝试次数（send_logs 只增不改，取最大 RetryCount）。</summary>
    private async Task<(int AttemptCount, bool LastSuccess)> GetChannelProgressAsync(
        long messageId, MessageChannel channel, CancellationToken cancellationToken)
    {
        var latest = await _db.MessageSendLogs
            .Where(l => l.MessageId == messageId && l.Channel == channel)
            .OrderByDescending(l => l.RetryCount)
            .Select(static l => new { l.RetryCount, l.Status })
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        return latest is null ? (0, false) : (latest.RetryCount + 1, latest.Status == MessageSendLogStatus.Success);
    }

    /// <summary>解析收件人联系方式（复用跨模块成员只读查询；UserId 匹配 PortalUserId / 登录账号）。</summary>
    private async Task<TenantMemberSummaryRow?> ResolveRecipientContactAsync(
        long tenantId, string recipientUserId, CancellationToken cancellationToken)
    {
        // Portal 成员查询未注册（纯单测宿主）时返回 null：渠道按"联系方式缺失"降级，不阻断执行
        var memberQuery = _serviceProvider.GetService<ITenantMemberQuery>();
        if (memberQuery is null)
        {
            LogMemberQueryMissing(tenantId);
            return null;
        }

        var members = await memberQuery.ListAllMembersAsync(tenantId, cancellationToken).ConfigureAwait(false);
        return members.FirstOrDefault(m =>
            m.PortalUserId.ToString(CultureInfo.InvariantCulture).Equals(recipientUserId, StringComparison.Ordinal)
            || m.LoginAccount.Equals(recipientUserId, StringComparison.Ordinal));
    }

    /// <summary>置降级标记位（渠道不可用 / 最终失败，前端提示该消息仅站内可达）。</summary>
    private static void MarkDegraded(MessageEntity message)
    {
        if (!MessageChannelMask.Has(message.ChannelMask, MessageChannel.InApp)
            || (message.ChannelMask & MessageChannelMask.DegradedToInApp) != 0)
        {
            return;
        }

        message.ChannelMask |= MessageChannelMask.DegradedToInApp;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "消息 {MessageId} 渠道分发时未找到消息行（租户 {TenantId}），跳过执行")]
    private partial void LogMessageMissing(long messageId, long tenantId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "消息 {MessageId} 渠道 {Channel} 第 {Retry} 次重试已调度（退避 {DelaySeconds} 秒）")]
    private partial void LogRetryScheduled(long messageId, MessageChannel channel, int retry, double delaySeconds);

    [LoggerMessage(Level = LogLevel.Debug, Message = "租户 {TenantId} 成员联系方式查询服务未注册（单测宿主），外部渠道按缺失处理")]
    private partial void LogMemberQueryMissing(long tenantId);
}
