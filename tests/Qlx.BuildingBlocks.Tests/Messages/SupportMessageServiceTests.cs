/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-23 09:35:00 +08:00 中国标准时间 星期五
 * 描述：信息中心门面服务测试——write-through 红线（消息+收件箱先持久化、纯站内即 Sent、
 *       外部渠道入队）、幂等去重窗口、SSE 尽力推送、收件箱合并视图（全局项正文不复制）、
 *       订阅强制锁定、发送记录 / 人工重试、状态机推进
 */

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Qlx.Abstractions.Exceptions;
using Qlx.Abstractions.Messages;
using Qlx.Abstractions.Sse;
using Qlx.BuildingBlocks.Expressions;
using Qlx.BuildingBlocks.Ids;
using Qlx.BuildingBlocks.Messaging;
using Qlx.BuildingBlocks.MultiTenancy.PlatformShared;
using Qlx.BuildingBlocks.Sse;
using Qlx.BuildingBlocks.Tests.Files;
using Qlx.BuildingBlocks.Tests.Jobs;
using Qlx.Modules.Support;
using Qlx.Modules.Support.Domain;
using Qlx.Modules.Support.Options;
using Qlx.Modules.Support.Services.Files;
using Qlx.Modules.Support.Services.Messages;

namespace Qlx.BuildingBlocks.Tests.Messages;

/// <summary>
/// <see cref="SupportMessageService"/> 单元测试（InMemory 提供程序 + 真实 SupportTenantDbContext +
/// 真实 SseConnectionManager + 分发入队桩）。
/// </summary>
public sealed class SupportMessageServiceTests
{
    private const long TenantId = 7L;
    private const string Sender = "2002";
    private const string Recipient = "1001";

    /// <summary>捕获入队请求的分发桩。</summary>
    private sealed class CapturingDispatchEnqueuer : IMessageDispatchEnqueuer
    {
        public List<(long MessageId, long TenantId)> Immediate { get; } = [];
        public List<(TimeSpan Delay, long MessageId, long TenantId)> Delayed { get; } = [];
        public List<(long MessageId, long TenantId)> ManualRetries { get; } = [];

        public string Enqueue(long messageId, long tenantId)
        {
            Immediate.Add((messageId, tenantId));
            return "immediate";
        }

        public string EnqueueIn(TimeSpan delay, long messageId, long tenantId)
        {
            Delayed.Add((delay, messageId, tenantId));
            return "delayed";
        }

        public string EnqueueManualRetry(long messageId, long tenantId)
        {
            ManualRetries.Add((messageId, tenantId));
            return "manual";
        }
    }

    /// <summary>共享库全局项读取桩（返回固定视图集合）。</summary>
    private sealed class StubSharedReader(params GlobalMessageView[] views) : IPlatformSharedReader
    {
        public Task<IReadOnlyList<GlobalMessageView>> GetActiveMessagesAsync(CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<GlobalMessageView>>(views);
    }

    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    private static SseConnectionManager CreateSseManager()
        => new(
            Options.Create(new QlxSseOptions { BufferCapacity = 10, ChannelCapacity = 8 }),
            NullLogger<SseConnectionManager>.Instance);

    private static (
        SupportMessageService Service,
        SupportTenantDbContext Db,
        CapturingDispatchEnqueuer Enqueuer,
        SseConnectionManager Sse) Build(
        string? userId = Sender, long? tenantId = TenantId, IPlatformSharedReader? sharedReader = null)
    {
        var db = new SupportTenantDbContext(
            new JobStubTenantDatabaseResolver(),
            tenantId ?? 0,
            new DbContextOptionsBuilder<SupportTenantDbContext>()
                .UseInMemoryDatabase($"msg-{Guid.NewGuid():N}")
                // 生产库（SQL Server）真实事务；InMemory 无事务概念，抑制默认抛出的忽略警告
                .ConfigureWarnings(static w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                .Options);
        var enqueuer = new CapturingDispatchEnqueuer();
        var sse = CreateSseManager();
        var service = new SupportMessageService(
            db,
            new TestTenantContext(tenantId),
            new TestCurrentUserContext(userId),
            new HashidsObfuscator("test-salt", minLength: 8),
            sse,
            enqueuer,
            sharedReader ?? new StubSharedReader(),
            Options.Create(new SupportMessagesOptions()),
            NullLogger<SupportMessageService>.Instance,
            new SupportSeedService(
                db, ConditionExpressionEngineFactory.Default, NullLogger<SupportSeedService>.Instance));
        return (service, db, enqueuer, sse);
    }

    private static MessageTemplateEntity BuildNoticeTemplate()
        => new(
            TenantId, "msg.notice.system", "系统通知", MessageKinds.Notice, MessageChannel.InApp,
            "${title}", "${content}",
            """[{"name":"title","type":"string","required":true},{"name":"content","type":"string","required":true}]""",
            version: 1, Now);

    private static MessageEntity BuildMessage(
        string sender = Sender, string recipient = Recipient,
        int channelMask = MessageChannelMask.InApp, DateTimeOffset? createdAt = null,
        MessageBizRef? bizRef = null, MessageStatus status = MessageStatus.Sent)
    {
        // 测试种子默认置 Sent（write-through 已送达的稳态；Queued 仅在渠道在途场景）
        var message = new MessageEntity(
            TenantId, MessageKinds.Notice, tplCode: null, "标题", "正文", channelMask,
            MessagePriority.Normal, sender, recipient, bizRef, createdAt ?? Now);
        if (status != MessageStatus.Queued)
        {
            message.SetStatus(status, createdAt ?? Now);
        }

        return message;
    }

    // ============ 发送（write-through） ============

    [Fact]
    public async Task SendTemplatePersistsMessageInboxAndPushesSse()
    {
        var (service, db, enqueuer, sse) = Build();
        db.MessageTemplates.Add(BuildNoticeTemplate());
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        await using var lease = await sse.OpenStreamAsync(1001, "conn-1", TenantId, TestContext.Current.CancellationToken);

        await service.SendTemplateAsync(
            new SendTemplateRequest(
                "msg.notice.system", Recipient,
                new Dictionary<string, object?> { ["title"] = "系统升级", ["content"] = "今晚 22:00 维护" }),
            TestContext.Current.CancellationToken);

        var message = Assert.Single(await db.Messages.ToListAsync(TestContext.Current.CancellationToken));
        Assert.Equal("系统升级", message.Subject);
        Assert.Equal(MessageStatus.Sent, message.Status); // 纯站内 write-through 即送达
        Assert.Equal(Sender, message.SenderUserId);
        var inbox = Assert.Single(await db.MessageInboxes.ToListAsync(TestContext.Current.CancellationToken));
        Assert.Equal(message.Id, inbox.MessageId);
        Assert.False(inbox.IsRead);
        Assert.Empty(enqueuer.Immediate); // 无外部渠道不分发

        // SSE 实时推送：收件人在线即收到（信封路由按 long 收件人）
        Assert.True(lease.Reader.TryRead(out var pushed));
        Assert.Equal(SseEventType.MessageCreated, pushed.Type);
    }

    [Fact]
    public async Task SendTemplateUnknownTemplateThrowsNotFound()
    {
        var (service, _, _, _) = Build();

        await Assert.ThrowsAsync<NotFoundException>(() => service.SendTemplateAsync(
            new SendTemplateRequest("msg.missing", Recipient, new Dictionary<string, object?>()),
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task SendTemplateLazySeedsDefaultTemplatesWhenTenantHasNone()
    {
        var (service, db, _, _) = Build();

        await service.SendTemplateAsync(
            new SendTemplateRequest("msg.notice.system", Recipient,
                new Dictionary<string, object?> { ["title"] = "系统升级", ["content"] = "今晚 22:00 维护" }),
            TestContext.Current.CancellationToken);

        Assert.Equal(8, await db.MessageTemplates.CountAsync(TestContext.Current.CancellationToken));
        var message = Assert.Single(await db.Messages.ToListAsync(TestContext.Current.CancellationToken));
        Assert.Equal("系统升级", message.Subject);
    }

    [Fact]
    public async Task SendTemplateMissingRequiredVariableThrowsValidation()
    {
        var (service, db, _, _) = Build();
        db.MessageTemplates.Add(BuildNoticeTemplate());
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<ValidationException>(() => service.SendTemplateAsync(
            new SendTemplateRequest("msg.notice.system", Recipient, new Dictionary<string, object?>()),
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task SendTemplateDuplicateBizRefWithinWindowIsSkipped()
    {
        var (service, db, _, _) = Build();
        db.MessageTemplates.Add(BuildNoticeTemplate());
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var bizRef = new MessageBizRef("pur", "1", "CG-202610-0001");

        await service.SendTemplateAsync(
            new SendTemplateRequest("msg.notice.system", Recipient,
                new Dictionary<string, object?> { ["title"] = "t", ["content"] = "c" }, BizRef: bizRef),
            TestContext.Current.CancellationToken);
        await service.SendTemplateAsync(
            new SendTemplateRequest("msg.notice.system", Recipient,
                new Dictionary<string, object?> { ["title"] = "t", ["content"] = "c" }, BizRef: bizRef),
            TestContext.Current.CancellationToken);

        Assert.Single(await db.Messages.ToListAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task SendTemplateDuplicateBizRefOutsideWindowIsSent()
    {
        var (service, db, _, _) = Build();
        db.MessageTemplates.Add(BuildNoticeTemplate());
        // 窗口外旧消息（默认去重窗口 5 分钟）：同 BizRef 允许再次发送
        db.Messages.Add(BuildMessage(
            bizRef: new MessageBizRef("pur", "1", "CG-202610-0001"),
            createdAt: DateTimeOffset.UtcNow.AddMinutes(-10)));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        await service.SendTemplateAsync(
            new SendTemplateRequest("msg.notice.system", Recipient,
                new Dictionary<string, object?> { ["title"] = "t", ["content"] = "c" },
                BizRef: new MessageBizRef("pur", "1", "CG-202610-0001")),
            TestContext.Current.CancellationToken);

        Assert.Equal(2, await db.Messages.CountAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task PushWithExternalChannelEnqueuesDispatchAndStaysQueued()
    {
        var (service, db, enqueuer, _) = Build();

        await service.PushAsync(
            new PushRequest(Recipient, "邮件通知", "内容", MessageChannel.Email),
            TestContext.Current.CancellationToken);

        var message = Assert.Single(await db.Messages.ToListAsync(TestContext.Current.CancellationToken));
        Assert.Equal(MessageStatus.Queued, message.Status); // 渠道在途，外部投递后才推进 Sent
        Assert.True(MessageChannelMask.Has(message.ChannelMask, MessageChannel.Email));
        var dispatch = Assert.Single(enqueuer.Immediate);
        Assert.Equal(message.Id, dispatch.MessageId);
        Assert.Equal(TenantId, dispatch.TenantId);
    }

    [Fact]
    public async Task PushPureInAppAdvancesToSentWithoutDispatch()
    {
        var (service, db, enqueuer, _) = Build();

        await service.PushAsync(
            new PushRequest(Recipient, "站内通知", "内容", MessageChannel.InApp),
            TestContext.Current.CancellationToken);

        var message = Assert.Single(await db.Messages.ToListAsync(TestContext.Current.CancellationToken));
        Assert.Equal(MessageStatus.Sent, message.Status);
        Assert.Empty(enqueuer.Immediate);
    }

    // ============ 已读 / 未读 ============

    [Fact]
    public async Task MarkReadAdvancesInboxAndMessageToRead()
    {
        var (service, db, _, _) = Build(userId: Recipient);
        var message = BuildMessage();
        db.Messages.Add(message);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var inbox = new MessageInboxEntity(TenantId, message.Id, Recipient, Now);
        db.MessageInboxes.Add(inbox);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var obfuscator = new HashidsObfuscator("test-salt", minLength: 8);

        await service.MarkReadAsync(obfuscator.Encode(inbox.Id), TestContext.Current.CancellationToken);

        Assert.True(inbox.IsRead);
        Assert.NotNull(inbox.ReadAt);
        Assert.Equal(MessageStatus.Read, message.Status);
    }

    [Fact]
    public async Task MarkReadWithInvalidIdThrowsNotFound()
    {
        var (service, _, _, _) = Build(userId: Recipient);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            service.MarkReadAsync("not-decodable", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task MarkAllReadUpdatesInboxesAndMessages()
    {
        var (service, db, _, _) = Build(userId: Recipient);
        var first = BuildMessage();
        var second = BuildMessage();
        db.Messages.AddRange(first, second);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        db.MessageInboxes.AddRange(
            new MessageInboxEntity(TenantId, first.Id, Recipient, Now),
            new MessageInboxEntity(TenantId, second.Id, Recipient, Now));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        await service.MarkAllReadAsync(Recipient, TestContext.Current.CancellationToken);

        Assert.All(await db.MessageInboxes.ToListAsync(TestContext.Current.CancellationToken), i => Assert.True(i.IsRead));
        Assert.All(await db.Messages.ToListAsync(TestContext.Current.CancellationToken), m => Assert.Equal(MessageStatus.Read, m.Status));
    }

    [Fact]
    public async Task GetUnreadCountExcludesReadAndDeleted()
    {
        var (service, db, _, _) = Build(userId: Recipient);
        var read = BuildMessage();
        var unread = BuildMessage();
        var deleted = BuildMessage();
        db.Messages.AddRange(read, unread, deleted);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var readInbox = new MessageInboxEntity(TenantId, read.Id, Recipient, Now);
        readInbox.MarkRead(Now);
        var deletedInbox = new MessageInboxEntity(TenantId, deleted.Id, Recipient, Now);
        deletedInbox.MarkDeleted();
        db.MessageInboxes.AddRange(readInbox, new MessageInboxEntity(TenantId, unread.Id, Recipient, Now), deletedInbox);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var count = await service.GetUnreadCountAsync(Recipient, TestContext.Current.CancellationToken);

        Assert.Equal(1, count);
    }

    // ============ 收件箱合并视图（全局项只读） ============

    [Fact]
    public async Task QueryInboxMergesGlobalItemsWithoutCopyingBody()
    {
        var view = new GlobalMessageView(
            "g-1", MessageKinds.Announcement, "平台公告", "公告正文", MessagePriority.High,
            ForceAccept: true, "platform", Now);
        var (service, db, _, _) = Build(userId: Recipient, sharedReader: new StubSharedReader(view));
        var message = BuildMessage();
        db.Messages.Add(message);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        db.MessageInboxes.Add(new MessageInboxEntity(TenantId, message.Id, Recipient, Now));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var page = await service.QueryInboxAsync(1, 20, null, false, TestContext.Current.CancellationToken);

        Assert.Equal(2, page.Total);
        var globalItem = Assert.Single(page.Items, static i => i.IsGlobal);
        Assert.Equal("g-1", globalItem.MessageId); // 全局项以 GlobalId 为键，正文不复制
        Assert.Null(globalItem.InboxId);
        Assert.True(globalItem.ForceAccept);
        Assert.Equal("公告正文", globalItem.Content);
    }

    [Fact]
    public async Task QueryInboxFiltersDeletedGlobalItems()
    {
        var view = new GlobalMessageView(
            "g-1", MessageKinds.Notice, "通知", "内容", MessagePriority.Normal,
            ForceAccept: false, "platform", Now);
        var (service, db, _, _) = Build(userId: Recipient, sharedReader: new StubSharedReader(view));
        db.MessageGlobalStates.Add(new MessageGlobalStateEntity(
            TenantId, "g-1", Recipient, GlobalItemStates.Deleted, Now));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var page = await service.QueryInboxAsync(1, 20, null, false, TestContext.Current.CancellationToken);

        Assert.Empty(page.Items);
        Assert.Equal(0, page.Total);
    }

    [Fact]
    public async Task MarkGlobalItemRejectsDeleteOnForceAccept()
    {
        var view = new GlobalMessageView(
            "g-1", MessageKinds.Announcement, "强制公告", "内容", MessagePriority.High,
            ForceAccept: true, "platform", Now);
        var (service, _, _, _) = Build(userId: Recipient, sharedReader: new StubSharedReader(view));

        await Assert.ThrowsAsync<ForbiddenException>(() => service.MarkGlobalItemAsync(
            "g-1", GlobalItemStates.Deleted, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task MarkGlobalItemUnknownGlobalIdThrowsNotFound()
    {
        var (service, _, _, _) = Build(userId: Recipient);

        await Assert.ThrowsAsync<NotFoundException>(() => service.MarkGlobalItemAsync(
            "g-unknown", GlobalItemStates.Read, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task MarkGlobalItemAcceptsForceAcceptAnnouncement()
    {
        var view = new GlobalMessageView(
            "g-1", MessageKinds.Announcement, "强制公告", "内容", MessagePriority.High,
            ForceAccept: true, "platform", Now);
        var (service, db, _, _) = Build(userId: Recipient, sharedReader: new StubSharedReader(view));

        await service.MarkGlobalItemAsync("g-1", GlobalItemStates.Accepted, TestContext.Current.CancellationToken);

        var state = Assert.Single(await db.MessageGlobalStates.ToListAsync(TestContext.Current.CancellationToken));
        Assert.Equal(GlobalItemStates.Accepted, state.State);
        Assert.Equal(Recipient, state.UserId);

        // 确认后 unreadOnly 不再出现该全局项
        var page = await service.QueryInboxAsync(1, 20, null, unreadOnly: true, TestContext.Current.CancellationToken);
        Assert.DoesNotContain(page.Items, static i => i.MessageId == "g-1");
    }

    // ============ 订阅 ============

    [Fact]
    public async Task ListSubscriptionsProvisionsEightKindsWithLockedTodoUrge()
    {
        var (service, db, _, _) = Build(userId: Recipient);

        var items = await service.ListSubscriptionsAsync(TestContext.Current.CancellationToken);

        Assert.Equal(8, items.Count);
        Assert.All(items, static i => Assert.True(i.Enabled));
        Assert.Contains(items, static i => i.MsgType == MessageKinds.Todo && i.Locked);
        Assert.Contains(items, static i => i.MsgType == MessageKinds.Urge && i.Locked);
        Assert.DoesNotContain(items, static i => i.MsgType != MessageKinds.Todo && i.MsgType != MessageKinds.Urge && i.Locked);
        Assert.Equal(8, await db.MessageSubscriptions.CountAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ToggleSubscriptionRejectsLockedUrge()
    {
        var (service, _, _, _) = Build(userId: Recipient);
        await service.ListSubscriptionsAsync(TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<ForbiddenException>(() => service.ToggleSubscriptionAsync(
            MessageKinds.Urge, enabled: false, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ToggleSubscriptionDisablesAlert()
    {
        var (service, db, _, _) = Build(userId: Recipient);
        await service.ListSubscriptionsAsync(TestContext.Current.CancellationToken);

        await service.ToggleSubscriptionAsync(MessageKinds.Alert, enabled: false, TestContext.Current.CancellationToken);

        var alert = await db.MessageSubscriptions
            .SingleAsync(s => s.MsgType == MessageKinds.Alert, TestContext.Current.CancellationToken);
        Assert.False(alert.Enabled);
    }

    // ============ 发送记录 / 人工重试 ============

    [Fact]
    public async Task ListSendLogsReturnsOrderedLogsForSenderOnly()
    {
        var (service, db, _, _) = Build(userId: Sender);
        var message = BuildMessage(channelMask: MessageChannelMask.InApp | MessageChannelMask.Email);
        db.Messages.Add(message);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var obfuscator = new HashidsObfuscator("test-salt", minLength: 8);
        db.MessageSendLogs.AddRange(
            new MessageSendLogEntity(TenantId, message.Id, MessageChannel.Email, "a@b.c",
                MessageSendLogStatus.Failed, "SMTP 不可达", 0, Now.AddMinutes(-1)),
            new MessageSendLogEntity(TenantId, message.Id, MessageChannel.Email, "a@b.c",
                MessageSendLogStatus.Success, null, 1, Now));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var logs = await service.ListSendLogsAsync(obfuscator.Encode(message.Id), TestContext.Current.CancellationToken);

        Assert.Equal(2, logs.Count);
        Assert.Equal(MessageSendLogStatus.Failed, logs[0].Status);
        Assert.Equal(1, logs[1].RetryCount);
        Assert.Equal(MessageSendLogStatus.Success, logs[1].Status);

        // 非发送方拒绝查看
        var (_, _, _, _) = Build(userId: "9999");
        var other = new SupportMessageService(
            db, new TestTenantContext(TenantId), new TestCurrentUserContext("9999"),
            obfuscator, CreateSseManager(), new CapturingDispatchEnqueuer(), new StubSharedReader(),
            Options.Create(new SupportMessagesOptions()), NullLogger<SupportMessageService>.Instance,
            new SupportSeedService(
                db, ConditionExpressionEngineFactory.Default, NullLogger<SupportSeedService>.Instance));
        await Assert.ThrowsAsync<ForbiddenException>(() => other.ListSendLogsAsync(
            obfuscator.Encode(message.Id), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task RetryFailedMessageEnqueuesManualRetryForExternalChannel()
    {
        var (service, db, enqueuer, _) = Build(userId: Sender);
        var message = BuildMessage(channelMask: MessageChannelMask.InApp | MessageChannelMask.Email);
        db.Messages.Add(message);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var obfuscator = new HashidsObfuscator("test-salt", minLength: 8);

        await service.RetryFailedMessageAsync(obfuscator.Encode(message.Id), TestContext.Current.CancellationToken);

        var retry = Assert.Single(enqueuer.ManualRetries);
        Assert.Equal(message.Id, retry.MessageId);
        Assert.Equal(TenantId, retry.TenantId);
    }

    [Fact]
    public async Task RetryFailedMessageRejectsPureInAppMessage()
    {
        var (service, db, enqueuer, _) = Build(userId: Sender);
        var message = BuildMessage();
        db.Messages.Add(message);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var obfuscator = new HashidsObfuscator("test-salt", minLength: 8);

        await Assert.ThrowsAsync<ValidationException>(() => service.RetryFailedMessageAsync(
            obfuscator.Encode(message.Id), TestContext.Current.CancellationToken));
        Assert.Empty(enqueuer.ManualRetries);
    }

    [Fact]
    public async Task RetryFailedMessageRejectsNonSender()
    {
        var (service, db, _, _) = Build(userId: "9999");
        var message = BuildMessage(channelMask: MessageChannelMask.InApp | MessageChannelMask.Email);
        db.Messages.Add(message);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var obfuscator = new HashidsObfuscator("test-salt", minLength: 8);

        await Assert.ThrowsAsync<ForbiddenException>(() => service.RetryFailedMessageAsync(
            obfuscator.Encode(message.Id), TestContext.Current.CancellationToken));
    }

    // ============ 上下文守卫 ============

    [Fact]
    public async Task MissingTenantContextThrowsUnauthorized()
    {
        var (service, _, _, _) = Build(tenantId: null);

        await Assert.ThrowsAsync<UnauthorizedException>(() => service.PushAsync(
            new PushRequest(Recipient, "标题", "正文"), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task UnauthenticatedInboxViewThrowsUnauthorized()
    {
        // 发送侧允许系统/后台无用户调用（SenderId 回落 system）；收件视角强制认证
        var (service, _, _, _) = Build(userId: null);

        await Assert.ThrowsAsync<UnauthorizedException>(() => service.ListSubscriptionsAsync(
            TestContext.Current.CancellationToken));
    }
}
