/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-23 07:30:00 +08:00 中国标准时间 星期五
 * 描述：信息中心门面服务实现（IMessageService + ISupportMessageService 同实例）：
 *       write-through 红线（消息+收件箱先持久化，SSE best-effort，外部渠道经 Hangfire 异步补发）+
 *       幂等去重 + 收件箱合并视图（本地 ∪ 共享库全局项，正文不复制）+ 订阅管理（强制订阅锁定）
 */

using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Qlx.Abstractions.Domain;
using Qlx.Abstractions.Exceptions;
using Qlx.Abstractions.Messages;
using Qlx.Abstractions.Sse;
using Qlx.Abstractions.Tenancy;
using Qlx.BuildingBlocks.Ids;
using Qlx.BuildingBlocks.Jobs;
using Qlx.BuildingBlocks.Messaging;
using Qlx.BuildingBlocks.MultiTenancy.PlatformShared;
using Qlx.BuildingBlocks.Sse;
using Qlx.Modules.Support.Domain;
using Qlx.Modules.Support.Options;

namespace Qlx.Modules.Support.Services.Messages;

/// <summary>
/// 信息中心门面服务。消息主档 + 收件箱在单事务内先持久化（write-through），
/// 站内实时通知经 SSE 推送（尽力而为，失败不阻断），邮件/短信/推送渠道经
/// <see cref="IMessageDispatchEnqueuer"/> 提交 Hangfire 异步补发（刻意不走 job_executions 管线，
/// 避免 JobExecutionRunner 终态通知递归产生新消息）。
/// </summary>
public sealed partial class SupportMessageService(
    SupportTenantDbContext db,
    ITenantContext tenantContext,
    ICurrentUserContext currentUser,
    IIdObfuscator idObfuscator,
    ISseConnectionManager sseManager,
    IMessageDispatchEnqueuer dispatchEnqueuer,
    IPlatformSharedReader sharedReader,
    IOptions<SupportMessagesOptions> options,
    ILogger<SupportMessageService> logger,
    Files.SupportSeedService seedService) : IMessageService, ISupportMessageService
{
    private readonly SupportTenantDbContext _db = db;
    private readonly ITenantContext _tenantContext = tenantContext;
    private readonly ICurrentUserContext _currentUser = currentUser;
    private readonly IIdObfuscator _idObfuscator = idObfuscator;
    private readonly ISseConnectionManager _sseManager = sseManager;
    private readonly IMessageDispatchEnqueuer _dispatchEnqueuer = dispatchEnqueuer;
    private readonly IPlatformSharedReader _sharedReader = sharedReader;
    private readonly SupportMessagesOptions _options = options.Value;
    private readonly ILogger<SupportMessageService> _logger = logger;
    private readonly Files.SupportSeedService _seedService = seedService;

    /// <summary>订阅类别固定顺序（收件箱订阅清单展示次序）。</summary>
    private static readonly string[] s_subscriptionKinds =
        [MessageKinds.Todo, MessageKinds.Urge, MessageKinds.Alert, MessageKinds.Result,
         MessageKinds.Task, MessageKinds.Notice, MessageKinds.Announcement, MessageKinds.Pm];

    /// <summary>当前租户 Id（缺失租户上下文即拒绝）。</summary>
    private long TenantId => _tenantContext.TenantId is > 0
        ? _tenantContext.TenantId.Value
        : throw new UnauthorizedException("缺少租户上下文");

    /// <summary>当前操作人 Id（未认证即拒绝）。</summary>
    private string CurrentUserId => string.IsNullOrWhiteSpace(_currentUser.UserId)
        ? throw new UnauthorizedException()
        : _currentUser.UserId;

    /// <summary>发送方标识（后台任务 / 系统调用未携带用户上下文时归因 system）。</summary>
    private string SenderId => string.IsNullOrWhiteSpace(_currentUser.UserId)
        ? JobSubmitMetadata.SystemUserId
        : _currentUser.UserId;

    // ============ IMessageService（2A 冻结契约） ============

    /// <inheritdoc />
    public async Task SendTemplateAsync(SendTemplateRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.TemplateCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.RecipientUserId);

        var tenantId = TenantId;
        var now = DateTimeOffset.UtcNow;

        // 幂等去重：相同 BizRef 收件人在窗口内的重复发送直接跳过（业务重试 / 事件重放保护）
        if (request.BizRef is not null && await IsDuplicateAsync(
                tenantId, request.RecipientUserId, request.BizRef, now, cancellationToken).ConfigureAwait(false))
        {
            LogDedupSkipped(request.TemplateCode, request.RecipientUserId, request.BizRef.BizType, request.BizRef.BizId);
            return;
        }

        // 渲染解析取 InApp 渠道 Active 最高版本（站内信为 write-through 兜底渠道，模板按渠道各一份）
        var template = await _db.MessageTemplates
            .Where(t => t.TplCode == request.TemplateCode
                && t.Channel == MessageChannel.InApp
                && t.Status == MessageTemplateStatus.Active)
            .OrderByDescending(t => t.Version)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        // 懒种子：仅租户级零模板时补种默认模板后重查一次（与任务定义 / 审批流程懒种子同模式）；
        // 已有模板但本 tpl_code 未命中属正常"未配置"，不触发种子避免无效往返
        if (template is null
            && !await _db.MessageTemplates.AnyAsync(cancellationToken).ConfigureAwait(false))
        {
            await _seedService.EnsureMessageTemplatesSeededAsync(tenantId, cancellationToken)
                .ConfigureAwait(false);
            template = await _db.MessageTemplates
                .Where(t => t.TplCode == request.TemplateCode
                    && t.Channel == MessageChannel.InApp
                    && t.Status == MessageTemplateStatus.Active)
                .OrderByDescending(t => t.Version)
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);
        }

        if (template is null)
        {
            throw new NotFoundException("消息模板", request.TemplateCode);
        }

        var rendered = TemplateRenderer.Render(
            template.SubjectTemplate, template.ContentTemplate, template.VarSchemaJson,
            request.Variables, escapeHtml: false);

        var channelMask = (request.Channels ?? [MessageChannel.InApp])
            .Select(MessageChannelMask.ToFlag)
            .Aggregate(0, (acc, flag) => acc | flag);
        await PersistAndNotifyAsync(
            tenantId, template.MsgType, template.TplCode, rendered.Subject, rendered.Content,
            channelMask, request.Priority, request.RecipientUserId, request.BizRef, now, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task PushAsync(PushRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.RecipientUserId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Subject);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Content);

        var tenantId = TenantId;
        var now = DateTimeOffset.UtcNow;

        if (request.BizRef is not null && await IsDuplicateAsync(
                tenantId, request.RecipientUserId, request.BizRef, now, cancellationToken).ConfigureAwait(false))
        {
            LogDedupSkipped("push", request.RecipientUserId, request.BizRef.BizType, request.BizRef.BizId);
            return;
        }

        await PersistAndNotifyAsync(
            tenantId, MessageKinds.Notice, tplCode: null, request.Subject, request.Content,
            MessageChannelMask.ToFlag(request.Channel), request.Priority, request.RecipientUserId,
            request.BizRef, now, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task MarkReadAsync(string messageInboxId, CancellationToken cancellationToken)
    {
        var inboxId = DecodeId(messageInboxId, "收件箱");
        var inbox = await _db.MessageInboxes
            .FirstOrDefaultAsync(i => i.Id == inboxId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new NotFoundException("收件箱", messageInboxId);

        inbox.MarkRead(DateTimeOffset.UtcNow);

        // 消息主档同步推进（Sent/Delivered → Read；Queued 渠道在途时仅记收件箱已读）
        var message = await _db.Messages
            .FirstOrDefaultAsync(m => m.Id == inbox.MessageId, cancellationToken)
            .ConfigureAwait(false);
        if (message is not null && MessageStateMachine.CanTransition(message.Status, MessageStatus.Read))
        {
            message.SetStatus(MessageStatus.Read, DateTimeOffset.UtcNow);
        }

        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task MarkAllReadAsync(string userId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        var now = DateTimeOffset.UtcNow;

        // 物化未读收件箱后实体级推进（单次 SaveChanges 批量提交；InMemory 提供程序不支持 ExecuteUpdate）
        var inboxes = await _db.MessageInboxes
            .Where(i => i.RecipientUserId == userId && !i.IsRead)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        if (inboxes.Count == 0)
        {
            return;
        }

        var messageIds = inboxes.Select(static i => i.MessageId).Distinct().ToList();
        var messages = await _db.Messages
            .Where(m => messageIds.Contains(m.Id)
                && (m.Status == MessageStatus.Sent || m.Status == MessageStatus.Delivered))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        // 状态合法性由 Where 谓词保证（仅 Sent/Delivered → Read）
        foreach (var message in messages)
        {
            message.SetStatus(MessageStatus.Read, now);
        }

        foreach (var inbox in inboxes)
        {
            inbox.MarkRead(now);
        }

        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        LogAllRead(userId, inboxes.Count);
    }

    /// <inheritdoc />
    public Task<int> GetUnreadCountAsync(string userId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        return _db.MessageInboxes
            .Where(i => i.RecipientUserId == userId && !i.IsRead && !i.Deleted)
            .CountAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<MessageInfo>> GetInboxAsync(MessageQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var userId = CurrentUserId;
        var (skip, take) = NormalizePage(request.PageIndex, request.PageSize);

        var rows = await QueryInboxRows(userId, request.Keyword, unreadOnly: false, request.Status)
            .Skip(skip).Take(take)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return rows.Select(ToMessageInfo).ToList();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<MessageInfo>> GetSentListAsync(MessageQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var userId = CurrentUserId;
        var (skip, take) = NormalizePage(request.PageIndex, request.PageSize);

        var query = _db.Messages
            .Where(m => m.SenderUserId == userId);
        if (request.Status is { } status)
        {
            query = query.Where(m => m.Status == status);
        }

        if (!string.IsNullOrWhiteSpace(request.Keyword))
        {
            var keyword = request.Keyword.Trim();
            query = query.Where(m => m.Subject.Contains(keyword) || m.Content.Contains(keyword));
        }

        var rows = await query
            .OrderByDescending(m => m.CreatedAt)
            .Skip(skip).Take(take)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return rows.Select(ToSentMessageInfo).ToList();
    }

    // ============ ISupportMessageService（模块门面契约） ============

    /// <inheritdoc />
    public async Task<SupportInboxPage> QueryInboxAsync(
        int pageIndex, int pageSize, string? keyword, bool unreadOnly, CancellationToken cancellationToken)
    {
        var userId = CurrentUserId;
        pageSize = Math.Clamp(pageSize, 1, _options.InboxPageSizeMax);
        pageIndex = Math.Max(pageIndex, 1);

        // 全局项状态叠加（msg_global_state）：正文不复制，仅按状态过滤/标记（§5.11）
        var globalItems = await BuildGlobalInboxItemsAsync(userId, keyword, unreadOnly, cancellationToken)
            .ConfigureAwait(false);

        // 本地项：取到当前页跨度的窗口（pageIndex * pageSize 有界），与全局项内存归并排序后切片
        var windowSize = pageIndex * pageSize;
        var localRows = await QueryInboxRows(userId, keyword, unreadOnly, null)
            .Take(windowSize)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var localTotal = await QueryInboxRows(userId, keyword, unreadOnly, null)
            .CountAsync(cancellationToken)
            .ConfigureAwait(false);

        var merged = localRows.Select(static r => (Item: ToInboxItem(r), Time: r.Message.CreatedAt))
            .Concat(globalItems.Select(static g => (Item: g, Time: g.CreatedAt)))
            .OrderByDescending(static pair => pair.Time)
            .ToList();

        var total = localTotal + globalItems.Count;
        var items = merged
            .Skip((pageIndex - 1) * pageSize)
            .Take(pageSize)
            .Select(static pair => pair.Item)
            .ToList();
        return new SupportInboxPage(items, total, pageIndex, pageSize);
    }

    /// <inheritdoc />
    public async Task<SupportMessagePage> QuerySentAsync(
        int pageIndex, int pageSize, string? keyword, CancellationToken cancellationToken)
    {
        var userId = CurrentUserId;
        pageSize = Math.Clamp(pageSize, 1, _options.InboxPageSizeMax);
        pageIndex = Math.Max(pageIndex, 1);

        var query = _db.Messages.Where(m => m.SenderUserId == userId);
        if (!string.IsNullOrWhiteSpace(keyword))
        {
            var trimmed = keyword.Trim();
            query = query.Where(m => m.Subject.Contains(trimmed) || m.Content.Contains(trimmed));
        }

        var total = await query.CountAsync(cancellationToken).ConfigureAwait(false);
        var rows = await query
            .OrderByDescending(m => m.CreatedAt)
            .Skip((pageIndex - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return new SupportMessagePage(rows.Select(ToSentMessageInfo).ToList(), total, pageIndex, pageSize);
    }

    /// <inheritdoc />
    public async Task MarkGlobalItemAsync(string globalId, string state, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(globalId);
        ArgumentException.ThrowIfNullOrWhiteSpace(state);
        if (state is not (GlobalItemStates.Read or GlobalItemStates.Accepted or GlobalItemStates.Deleted))
        {
            throw new ValidationException(nameof(state), $"非法的全局项状态：{state}（仅 READ / ACCEPTED / DELETED）。");
        }

        var userId = CurrentUserId;

        // ForceAccept 校验依赖共享库视图（正文不落地租户库；读取走短缓存）
        var view = (await _sharedReader.GetActiveMessagesAsync(cancellationToken).ConfigureAwait(false))
            .FirstOrDefault(v => v.GlobalId == globalId)
            ?? throw new NotFoundException("全局消息", globalId);
        if (state == GlobalItemStates.Deleted && view.ForceAccept)
        {
            throw new ForbiddenException("强制确认公告不可删除，仅可确认（ACCEPTED）。");
        }

        var existing = await _db.MessageGlobalStates
            .FirstOrDefaultAsync(s => s.GlobalId == globalId && s.UserId == userId, cancellationToken)
            .ConfigureAwait(false);
        var now = DateTimeOffset.UtcNow;
        if (existing is null)
        {
            _db.MessageGlobalStates.Add(new MessageGlobalStateEntity(TenantId, globalId, userId, state, now));
        }
        else
        {
            existing.State = state;
            existing.StateAt = now;
        }

        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task DeleteInboxItemAsync(string inboxId, CancellationToken cancellationToken)
    {
        var id = DecodeId(inboxId, "收件箱");
        var inbox = await _db.MessageInboxes
            .FirstOrDefaultAsync(i => i.Id == id, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new NotFoundException("收件箱", inboxId);

        inbox.MarkDeleted();
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SupportSubscriptionItem>> ListSubscriptionsAsync(CancellationToken cancellationToken)
    {
        var userId = CurrentUserId;
        var items = await GetOrProvisionSubscriptionsAsync(userId, cancellationToken).ConfigureAwait(false);
        return s_subscriptionKinds
            .Select(kind => items.FirstOrDefault(i => i.MsgType == kind))
            .Where(static s => s is not null)
            .Select(static s => new SupportSubscriptionItem(s!.MsgType, s.ChannelsMask, s.Locked, s.Enabled))
            .ToList();
    }

    /// <inheritdoc />
    public async Task ToggleSubscriptionAsync(string msgType, bool enabled, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(msgType);
        if (!s_subscriptionKinds.Contains(msgType))
        {
            throw new ValidationException(nameof(msgType), $"未知消息类别：{msgType}。");
        }

        var userId = CurrentUserId;
        var items = await GetOrProvisionSubscriptionsAsync(userId, cancellationToken).ConfigureAwait(false);
        var subscription = items.First(i => i.MsgType == msgType);
        if (!subscription.SetEnabled(enabled, DateTimeOffset.UtcNow))
        {
            throw new ForbiddenException($"消息类别 {msgType} 为强制订阅（审批待办/催办红线），不可关闭。");
        }

        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SupportSendLogItem>> ListSendLogsAsync(
        string messageId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(messageId);
        var id = DecodeId(messageId, "消息");

        // 仅发送方可查看渠道投递明细（已发送视角；收件人走收件箱即可）
        var senderUserId = await _db.Messages
            .Where(m => m.Id == id)
            .Select(m => m.SenderUserId)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false)
            ?? throw new NotFoundException("消息", messageId);
        if (senderUserId != CurrentUserId)
        {
            throw new ForbiddenException("仅发送方可查看发送记录。");
        }

        var logs = await _db.MessageSendLogs
            .AsNoTracking()
            .Where(l => l.MessageId == id)
            .OrderBy(l => l.CreatedAt)
            .Select(l => new SupportSendLogItem(
                messageId, l.Channel, l.Recipient, l.Status, l.FailReason, l.RetryCount, l.SentAt, l.CreatedAt))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return logs;
    }

    /// <inheritdoc />
    public async Task RetryFailedMessageAsync(string messageId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(messageId);
        var id = DecodeId(messageId, "消息");
        var message = await _db.Messages
            .FirstOrDefaultAsync(m => m.Id == id, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new NotFoundException("消息", messageId);
        if (message.SenderUserId != CurrentUserId)
        {
            throw new ForbiddenException("仅发送方可重试外部渠道投递。");
        }

        // 降级标记位不属于投递渠道：仅外部渠道（邮件/短信/推送）可发起人工重试
        var externalMask = message.ChannelMask & ~MessageChannelMask.InApp & ~MessageChannelMask.DegradedToInApp;
        if (externalMask == 0)
        {
            throw new ValidationException("messageId", "该消息未请求外部渠道投递，无需重试。");
        }

        _dispatchEnqueuer.EnqueueManualRetry(message.Id, TenantId);
        LogManualRetryEnqueued(message.Id, TenantId);
    }

    // ============ 私有实现 ============

    /// <summary>write-through 核心：消息 + 收件箱先持久化（单事务）→ SSE 尽力推送 → 外部渠道入队。</summary>
    private async Task PersistAndNotifyAsync(
        long tenantId, string msgType, string? tplCode, string subject, string content,
        int channelMask, MessagePriority priority, string recipientUserId,
        MessageBizRef? bizRef, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var message = new MessageEntity(
            tenantId, msgType, tplCode, subject, content, channelMask, priority,
            SenderId, recipientUserId, bizRef, now);

        // 消息 Id 由雪花拦截器在 SaveChanges 期分配：先存主档取 Id，再建收件箱（单事务保证原子性）
        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        _db.Messages.Add(message);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        _db.MessageInboxes.Add(new MessageInboxEntity(tenantId, message.Id, recipientUserId, now));

        var hasExternalChannel = (message.ChannelMask & ~MessageChannelMask.InApp) != 0;
        if (!hasExternalChannel)
        {
            // 纯站内：write-through 即送达（Queued → Sent，状态机合法转移；后续 MarkRead 推进 Read）
            message.SetStatus(MessageStatus.Sent, now);
        }

        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

        // SSE 实时通知（best-effort：失败仅告警，不阻断 write-through 结果）
        try
        {
            // 信封收件人为 long?：收件人 Id 为数值串（雪花 Id），非数值串（异常数据）跳过推送，离线兜底走收件箱 API
            if (!long.TryParse(recipientUserId, NumberStyles.None, CultureInfo.InvariantCulture, out var recipientId))
            {
                LogSseRecipientUnresolved(message.Id, recipientUserId);
            }
            else
            {
                var envelope = new SseEventEnvelope(
                    tenantId, recipientId, subject, content,
                    bizRef?.BizType, bizRef?.BizId, bizRef?.BizNo);
                await _sseManager.PushAsync(
                    new SseEvent(0, SseEventType.MessageCreated, SseEventEnvelope.Serialize(envelope), now),
                    cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogSsePushFailed(ex, message.Id);
        }

        // 外部渠道（邮件/短信/推送）经 Hangfire 异步补发；重试与降级由 MessageDeliveryExecutor 负责
        if (hasExternalChannel)
        {
            _dispatchEnqueuer.Enqueue(message.Id, tenantId);
            LogExternalDispatchEnqueued(message.Id, tenantId, message.ChannelMask);
        }
    }

    /// <summary>幂等去重判定：同（租户，收件人，BizRef）在窗口内已存在即重复。</summary>
    private async Task<bool> IsDuplicateAsync(
        long tenantId, string recipientUserId, MessageBizRef bizRef, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var idempotencyRef = $"{bizRef.BizType}:{bizRef.BizId}:{bizRef.BizNo ?? "-"}";
        var windowStart = now - TimeSpan.FromMinutes(_options.DedupMinutes);
        return await _db.Messages
            .Where(m => m.TenantId == tenantId
                && m.RecipientUserId == recipientUserId
                && m.IdempotencyRef == idempotencyRef
                && m.CreatedAt >= windowStart)
            .AnyAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>收件箱联合查询（收件箱 ∪ 消息主档，统一过滤与排序）。</summary>
    private IQueryable<InboxMessageRow> QueryInboxRows(
        string userId, string? keyword, bool unreadOnly, MessageStatus? status)
    {
        var query = from inbox in _db.MessageInboxes
                    join message in _db.Messages on inbox.MessageId equals message.Id
                    where inbox.RecipientUserId == userId && !inbox.Deleted
                    select new { inbox, message };

        if (unreadOnly)
        {
            query = query.Where(r => !r.inbox.IsRead);
        }

        if (status is { } statusValue)
        {
            query = query.Where(r => r.message.Status == statusValue);
        }

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            var trimmed = keyword.Trim();
            query = query.Where(r => r.message.Subject.Contains(trimmed) || r.message.Content.Contains(trimmed));
        }

        return query
            .OrderByDescending(static r => r.message.CreatedAt)
            .Select(static r => new InboxMessageRow { Inbox = r.inbox, Message = r.message });
    }

    /// <summary>构建全局项收件箱视图（过滤已删除 / 未读筛选；状态叠加 msg_global_state）。</summary>
    private async Task<List<SupportInboxItem>> BuildGlobalInboxItemsAsync(
        string userId, string? keyword, bool unreadOnly, CancellationToken cancellationToken)
    {
        var views = await _sharedReader.GetActiveMessagesAsync(cancellationToken).ConfigureAwait(false);
        if (views.Count == 0)
        {
            return [];
        }

        var states = await _db.MessageGlobalStates
            .Where(s => s.UserId == userId)
            .ToDictionaryAsync(static s => s.GlobalId, static s => s.State, StringComparer.Ordinal, cancellationToken)
            .ConfigureAwait(false);

        List<SupportInboxItem> items = [];
        foreach (var view in views)
        {
            var state = states.GetValueOrDefault(view.GlobalId);
            if (state == GlobalItemStates.Deleted
                || (unreadOnly && state is GlobalItemStates.Read or GlobalItemStates.Accepted))
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(keyword)
                && !view.Subject.Contains(keyword.Trim(), StringComparison.OrdinalIgnoreCase)
                && !view.Content.Contains(keyword.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            items.Add(new SupportInboxItem(
                view.GlobalId, InboxId: null, view.MsgType, view.Subject, view.Content,
                view.Priority, state is GlobalItemStates.Read or GlobalItemStates.Accepted
                    ? MessageStatus.Read
                    : MessageStatus.Delivered,
                view.PublishedBy, view.PublishedAt, ReadAt: null,
                BizType: null, BizId: null, BizNo: null,
                IsGlobal: true, view.ForceAccept));
        }

        return items;
    }

    /// <summary>获取或按默认值补齐当前用户 8 类订阅配置（TODO/URGE 强制订阅红线）。</summary>
    private async Task<List<MessageSubscriptionEntity>> GetOrProvisionSubscriptionsAsync(
        string userId, CancellationToken cancellationToken)
    {
        var existing = await _db.MessageSubscriptions
            .Where(s => s.UserId == userId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var now = DateTimeOffset.UtcNow;
        var provisioned = false;
        foreach (var kind in s_subscriptionKinds)
        {
            if (existing.Any(s => s.MsgType == kind))
            {
                continue;
            }

            // 强制订阅默认集：TODO（审批待办）/ URGE（催办）锁定，其余可关闭
            var locked = kind is MessageKinds.Todo or MessageKinds.Urge;
            existing.Add(new MessageSubscriptionEntity(TenantId, userId, kind, MessageChannelMask.InApp, locked, now));
            _db.MessageSubscriptions.Add(existing[^1]);
            provisioned = true;
        }

        if (provisioned)
        {
            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        return existing;
    }

    /// <summary>解混淆收件箱项 Id；非法串按未找到处理（不泄露编码细节）。</summary>
    private long DecodeId(string obfuscated, string resourceType)
        => _idObfuscator.TryDecode(obfuscated, out var id)
            ? id
            : throw new NotFoundException(resourceType, obfuscated);

    /// <summary>归一化分页参数（页码 ≥1，页大小夹取上限）。</summary>
    private (int Skip, int Take) NormalizePage(int pageIndex, int pageSize)
        => ((Math.Max(pageIndex, 1) - 1) * Math.Clamp(pageSize, 1, _options.InboxPageSizeMax),
            Math.Clamp(pageSize, 1, _options.InboxPageSizeMax));

    /// <summary>收件箱行 → 2A MessageInfo（收件视角，带收件箱 Id）。</summary>
    private MessageInfo ToMessageInfo(InboxMessageRow row)
        => new(
            _idObfuscator.Encode(row.Message.Id),
            _idObfuscator.Encode(row.Inbox.Id),
            row.Message.Subject,
            row.Message.Content,
            MessageChannelMask.Has(row.Message.ChannelMask, MessageChannel.Email)
                ? MessageChannel.Email : MessageChannel.InApp,
            row.Message.Status,
            row.Message.Priority,
            row.Message.SenderUserId,
            row.Message.RecipientUserId,
            row.Message.CreatedAt,
            row.Inbox.ReadAt,
            row.Message.BizType is null ? null : new MessageBizRef(row.Message.BizType, row.Message.BizId ?? string.Empty, row.Message.BizNo));

    /// <summary>消息主档 → 2A MessageInfo（发送视角，无收件箱 Id）。</summary>
    private MessageInfo ToSentMessageInfo(MessageEntity message)
        => new(
            _idObfuscator.Encode(message.Id),
            InboxId: null,
            message.Subject,
            message.Content,
            MessageChannelMask.Has(message.ChannelMask, MessageChannel.Email)
                ? MessageChannel.Email : MessageChannel.InApp,
            message.Status,
            message.Priority,
            message.SenderUserId,
            message.RecipientUserId,
            message.CreatedAt,
            ReadAt: null,
            message.BizType is null ? null : new MessageBizRef(message.BizType, message.BizId ?? string.Empty, message.BizNo));

    /// <summary>收件箱行 → 合并视图项（本地来源）。</summary>
    private static SupportInboxItem ToInboxItem(InboxMessageRow row)
        => new(
            row.Message.Id.ToString(CultureInfo.InvariantCulture),
            row.Inbox.Id.ToString(CultureInfo.InvariantCulture),
            row.Message.MsgType, row.Message.Subject, row.Message.Content,
            row.Message.Priority, row.Message.Status, row.Message.SenderUserId, row.Message.CreatedAt, row.Inbox.ReadAt,
            row.Message.BizType, row.Message.BizId, row.Message.BizNo,
            IsGlobal: false, ForceAccept: false);

    /// <summary>收件箱联合查询投影行（EF 表达式树不支持元组字面量，具名 DTO 承载）。</summary>
    private sealed record InboxMessageRow
    {
        /// <summary>收件箱记录。</summary>
        public MessageInboxEntity Inbox { get; init; } = null!;

        /// <summary>消息主档。</summary>
        public MessageEntity Message { get; init; } = null!;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "消息发送幂等去重命中，已跳过：Tpl={TemplateCode}，收件人={Recipient}，BizRef={BizType}/{BizId}")]
    private partial void LogDedupSkipped(string templateCode, string recipient, string bizType, string bizId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "消息 {MessageId} SSE 实时推送失败（write-through 不受影响，离线用户走收件箱 API）")]
    private partial void LogSsePushFailed(Exception exception, long messageId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "消息 {MessageId} 收件人 Id {RecipientUserId} 非数值串，跳过 SSE 推送（走收件箱兜底）")]
    private partial void LogSseRecipientUnresolved(long messageId, string recipientUserId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "消息 {MessageId} 外部渠道已入队分发（ChannelMask={ChannelMask}，租户 {TenantId}）")]
    private partial void LogExternalDispatchEnqueued(long messageId, long tenantId, int channelMask);

    [LoggerMessage(Level = LogLevel.Information, Message = "消息 {MessageId} 已入队人工重试（租户 {TenantId}）")]
    private partial void LogManualRetryEnqueued(long messageId, long tenantId);

    [LoggerMessage(Level = LogLevel.Information, Message = "用户 {UserId} 全部标记已读完成：更新 {Updated} 条收件箱记录")]
    private partial void LogAllRead(string userId, int updated);
}
