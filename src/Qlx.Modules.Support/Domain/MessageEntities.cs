/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-23 05:55:00 +08:00 中国标准时间 星期五
 * 描述：信息中心领域实体（msg_ 前缀 6 张表）：
 *       消息主档 / 收件箱 / 消息模板 / 订阅配置 / 渠道发送日志（只增不改）/ 全局项状态叠加
 */

using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using Qlx.Abstractions.Messages;
using Qlx.BuildingBlocks.MultiTenancy;

namespace Qlx.Modules.Support.Domain;

/// <summary>消息渠道位标志（多渠道并行发送时按位组合；降级标记独立置位）。</summary>
public static class MessageChannelMask
{
    /// <summary>站内信位。</summary>
    public const int InApp = 1;

    /// <summary>邮件位。</summary>
    public const int Email = 2;

    /// <summary>短信位。</summary>
    public const int Sms = 4;

    /// <summary>App 推送位。</summary>
    public const int Push = 8;

    /// <summary>短信/推送降级为站内信标记位（渠道未接入或失败时置位，send_logs 记录原因）。</summary>
    public const int DegradedToInApp = 64;

    /// <summary>单渠道枚举 → 位标志。</summary>
    /// <param name="channel">渠道枚举。</param>
    /// <returns>位标志值。</returns>
    public static int ToFlag(MessageChannel channel) => channel switch
    {
        MessageChannel.InApp => InApp,
        MessageChannel.Email => Email,
        MessageChannel.Sms => Sms,
        MessageChannel.Push => Push,
        _ => 0,
    };

    /// <summary>判定掩码是否包含指定渠道位。</summary>
    /// <param name="mask">渠道掩码。</param>
    /// <param name="channel">渠道枚举。</param>
    /// <returns>包含返回 true。</returns>
    public static bool Has(int mask, MessageChannel channel) => (mask & ToFlag(channel)) != 0;
}

/// <summary>消息类别（msg_type 字典，对齐信息中心文档）。</summary>
public static class MessageKinds
{
    /// <summary>待办。</summary>
    public const string Todo = "TODO";
    /// <summary>结果通知。</summary>
    public const string Result = "RESULT";
    /// <summary>催办。</summary>
    public const string Urge = "URGE";
    /// <summary>预警。</summary>
    public const string Alert = "ALERT";
    /// <summary>任务通知。</summary>
    public const string Task = "TASK";
    /// <summary>系统通知。</summary>
    public const string Notice = "NOTICE";
    /// <summary>公告。</summary>
    public const string Announcement = "ANN";
    /// <summary>私信。</summary>
    public const string Pm = "PM";
}

/// <summary>模板状态。</summary>
public enum MessageTemplateStatus
{
    /// <summary>启用（渲染解析只取本状态）。</summary>
    Active = 0,

    /// <summary>停用。</summary>
    Disabled = 1,
}

/// <summary>渠道发送日志状态（只增不改）。</summary>
public enum MessageSendLogStatus
{
    /// <summary>待发送。</summary>
    Pending = 0,

    /// <summary>发送成功。</summary>
    Success = 1,

    /// <summary>发送失败（进入退避重试）。</summary>
    Failed = 2,
}

/// <summary>全局项用户状态（msg_global_state 状态叠加，正文不复制）。</summary>
public static class GlobalItemStates
{
    /// <summary>已读。</summary>
    public const string Read = "READ";
    /// <summary>已确认（ForceAccept 公告）。</summary>
    public const string Accepted = "ACCEPTED";
    /// <summary>已删除（普通全局项个人隐藏；ForceAccept 项拒绝删除）。</summary>
    public const string Deleted = "DELETED";
}

/// <summary>
/// 消息主档实体（表 <c>msg_messages</c>）。一条消息面向单一收件人（广播场景按收件人逐行落库）；
/// 渠道投递明细走 <see cref="MessageSendLogEntity"/>（只增不改）。
/// </summary>
[Index(nameof(TenantId), nameof(RecipientUserId), nameof(CreatedAt))]
[Index(nameof(TenantId), nameof(IdempotencyRef), nameof(CreatedAt))]
[Index(nameof(TenantId), nameof(Status), nameof(CreatedAt))]
[TenantTablePrefix("msg_")]
public sealed class MessageEntity : Qlx.Abstractions.Domain.Entity
{
    /// <summary>所属租户 Id。</summary>
    public long TenantId { get; internal set; }

    /// <summary>消息类别（TODO/RESULT/URGE/ALERT/TASK/NOTICE/ANN/PM）。</summary>
    [MaxLength(32)]
    public string MsgType { get; internal set; } = MessageKinds.Notice;

    /// <summary>来源模板编码（模板发送时记录；直接推送为 null）。</summary>
    [MaxLength(100)]
    public string? TplCode { get; internal set; }

    /// <summary>标题（渲染后）。</summary>
    [MaxLength(200)]
    public string Subject { get; internal set; } = string.Empty;

    /// <summary>正文（渲染后；站内信展示内容）。</summary>
    [MaxLength(4000)]
    public string Content { get; internal set; } = string.Empty;

    /// <summary>渠道掩码（MessageChannelMask 位组合，含降级标记位）。</summary>
    public int ChannelMask { get; internal set; }

    /// <summary>优先级。</summary>
    public MessagePriority Priority { get; internal set; } = MessagePriority.Normal;

    /// <summary>发送状态（状态机由 <c>MessageStateMachine</c> 守卫）。</summary>
    public MessageStatus Status { get; internal set; } = MessageStatus.Queued;

    /// <summary>发送方用户 Id（系统调用为 system）。</summary>
    [MaxLength(64)]
    public string SenderUserId { get; internal set; } = string.Empty;

    /// <summary>收件人用户 Id。</summary>
    [MaxLength(64)]
    public string RecipientUserId { get; internal set; } = string.Empty;

    /// <summary>业务引用类型（可空）。</summary>
    [MaxLength(64)]
    public string? BizType { get; internal set; }

    /// <summary>业务引用 Id（可空）。</summary>
    [MaxLength(64)]
    public string? BizId { get; internal set; }

    /// <summary>业务引用单号（可空）。</summary>
    [MaxLength(64)]
    public string? BizNo { get; internal set; }

    /// <summary>幂等去重键（BizType:BizId:BizNo；窗口内重复写入被跳过，可空）。</summary>
    [MaxLength(256)]
    public string? IdempotencyRef { get; internal set; }

    /// <summary>创建时间（UTC）。</summary>
    public DateTimeOffset CreatedAt { get; internal set; }

    /// <summary>最近更新时间（UTC）。</summary>
    public DateTimeOffset UpdatedAt { get; internal set; }

    /// <summary>归档时间（UTC；可空）。</summary>
    public DateTimeOffset? ArchivedAt { get; internal set; }

    /// <summary>EF Core 反序列化专用无参构造。</summary>
    public MessageEntity() { }

    /// <summary>创建消息主档（初始状态 Queued，渠道掩码至少含站内信位——write-through 兜底渠道）。</summary>
    /// <param name="tenantId">所属租户 Id。</param>
    /// <param name="msgType">消息类别。</param>
    /// <param name="tplCode">来源模板编码（可空）。</param>
    /// <param name="subject">标题。</param>
    /// <param name="content">正文。</param>
    /// <param name="channelMask">渠道掩码（自动补站内信位）。</param>
    /// <param name="priority">优先级。</param>
    /// <param name="senderUserId">发送方用户 Id。</param>
    /// <param name="recipientUserId">收件人用户 Id。</param>
    /// <param name="bizRef">业务引用（可空）。</param>
    /// <param name="now">当前时间（UTC）。</param>
    public MessageEntity(
        long tenantId, string msgType, string? tplCode, string subject, string content,
        int channelMask, MessagePriority priority, string senderUserId, string recipientUserId,
        MessageBizRef? bizRef, DateTimeOffset now)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(msgType);
        ArgumentException.ThrowIfNullOrWhiteSpace(subject);
        ArgumentException.ThrowIfNullOrWhiteSpace(content);
        ArgumentException.ThrowIfNullOrWhiteSpace(recipientUserId);

        TenantId = tenantId;
        MsgType = msgType;
        TplCode = tplCode;
        Subject = subject;
        Content = content;
        ChannelMask = channelMask | MessageChannelMask.InApp; // 站内信兜底：write-through 红线
        Priority = priority;
        Status = MessageStatus.Queued;
        SenderUserId = senderUserId;
        RecipientUserId = recipientUserId;
        BizType = bizRef?.BizType;
        BizId = bizRef?.BizId;
        BizNo = bizRef?.BizNo;
        IdempotencyRef = bizRef is null ? null : $"{bizRef.BizType}:{bizRef.BizId}:{bizRef.BizNo ?? "-"}";
        CreatedAt = now;
        UpdatedAt = now;
    }

    /// <summary>推进发送状态（调用方负责经 <c>MessageStateMachine.EnsureTransition</c> 校验）。</summary>
    /// <param name="status">目标状态。</param>
    /// <param name="now">当前时间（UTC）。</param>
    public void SetStatus(MessageStatus status, DateTimeOffset now)
    {
        Status = status;
        if (status == MessageStatus.Archived)
        {
            ArchivedAt = now;
        }

        UpdatedAt = now;
    }
}

/// <summary>
/// 收件箱实体（表 <c>msg_message_inboxes</c>）。按收件人维度记录已读 / 个人删除状态；
/// 未读数、收件箱列表的权威数据源。
/// </summary>
[Index(nameof(TenantId), nameof(RecipientUserId), nameof(IsRead))]
[Index(nameof(TenantId), nameof(MessageId))]
[TenantTablePrefix("msg_")]
public sealed class MessageInboxEntity : Qlx.Abstractions.Domain.Entity
{
    /// <summary>所属租户 Id。</summary>
    public long TenantId { get; internal set; }

    /// <summary>消息主档 Id。</summary>
    public long MessageId { get; internal set; }

    /// <summary>收件人用户 Id。</summary>
    [MaxLength(64)]
    public string RecipientUserId { get; internal set; } = string.Empty;

    /// <summary>是否已读。</summary>
    public bool IsRead { get; internal set; }

    /// <summary>已读时间（UTC；可空）。</summary>
    public DateTimeOffset? ReadAt { get; internal set; }

    /// <summary>个人删除标记（不影响消息主档与他人收件箱）。</summary>
    public bool Deleted { get; internal set; }

    /// <summary>创建时间（UTC）。</summary>
    public DateTimeOffset CreatedAt { get; internal set; }

    /// <summary>EF Core 反序列化专用无参构造。</summary>
    public MessageInboxEntity() { }

    /// <summary>创建收件箱记录。</summary>
    /// <param name="tenantId">所属租户 Id。</param>
    /// <param name="messageId">消息主档 Id。</param>
    /// <param name="recipientUserId">收件人用户 Id。</param>
    /// <param name="now">当前时间（UTC）。</param>
    public MessageInboxEntity(long tenantId, long messageId, string recipientUserId, DateTimeOffset now)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(tenantId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(messageId);
        ArgumentException.ThrowIfNullOrWhiteSpace(recipientUserId);

        TenantId = tenantId;
        MessageId = messageId;
        RecipientUserId = recipientUserId;
        IsRead = false;
        CreatedAt = now;
    }

    /// <summary>标记已读（幂等：已读不覆盖首次时间）。</summary>
    /// <param name="now">当前时间（UTC）。</param>
    public void MarkRead(DateTimeOffset now)
    {
        if (IsRead)
        {
            return;
        }

        IsRead = true;
        ReadAt = now;
    }

    /// <summary>个人删除（收件箱隐藏）。</summary>
    public void MarkDeleted() => Deleted = true;
}

/// <summary>
/// 消息模板实体（表 <c>msg_message_templates</c>）。按（编码，渠道，版本）唯一定位；
/// 渲染解析取 <see cref="MessageTemplateStatus.Active"/> 最高版本。
/// </summary>
[Index(nameof(TenantId), nameof(TplCode), nameof(Channel), nameof(Version), IsUnique = true)]
[TenantTablePrefix("msg_")]
public sealed class MessageTemplateEntity : Qlx.Abstractions.Domain.Entity
{
    /// <summary>所属租户 Id。</summary>
    public long TenantId { get; internal set; }

    /// <summary>模板编码（系统调用引用键）。</summary>
    [MaxLength(100)]
    public string TplCode { get; internal set; } = string.Empty;

    /// <summary>模板名称。</summary>
    [MaxLength(200)]
    public string Name { get; internal set; } = string.Empty;

    /// <summary>消息类别。</summary>
    [MaxLength(32)]
    public string MsgType { get; internal set; } = MessageKinds.Notice;

    /// <summary>适用渠道（按渠道各一份模板）。</summary>
    public MessageChannel Channel { get; internal set; } = MessageChannel.InApp;

    /// <summary>标题模板（支持 <c>${var}</c> 占位符；站内信也用做标题）。</summary>
    [MaxLength(500)]
    public string SubjectTemplate { get; internal set; } = string.Empty;

    /// <summary>正文模板（支持 <c>${var}</c> 占位符）。</summary>
    [MaxLength(2000)]
    public string ContentTemplate { get; internal set; } = string.Empty;

    /// <summary>变量模式 JSON（[{name,type,required}] 数组）。</summary>
    [MaxLength(2000)]
    public string VarSchemaJson { get; internal set; } = "[]";

    /// <summary>版本号（同码同渠道可多版本，渲染取 Active 最高版本）。</summary>
    public int Version { get; internal set; } = 1;

    /// <summary>模板状态。</summary>
    public MessageTemplateStatus Status { get; internal set; } = MessageTemplateStatus.Active;

    /// <summary>创建时间（UTC）。</summary>
    public DateTimeOffset CreatedAt { get; internal set; }

    /// <summary>最近更新时间（UTC）。</summary>
    public DateTimeOffset UpdatedAt { get; internal set; }

    /// <summary>EF Core 反序列化专用无参构造。</summary>
    public MessageTemplateEntity() { }

    /// <summary>创建消息模板。</summary>
    /// <param name="tenantId">所属租户 Id。</param>
    /// <param name="tplCode">模板编码。</param>
    /// <param name="name">模板名称。</param>
    /// <param name="msgType">消息类别。</param>
    /// <param name="channel">适用渠道。</param>
    /// <param name="subjectTemplate">标题模板。</param>
    /// <param name="contentTemplate">正文模板。</param>
    /// <param name="varSchemaJson">变量模式 JSON。</param>
    /// <param name="version">版本号。</param>
    /// <param name="now">当前时间（UTC）。</param>
    public MessageTemplateEntity(
        long tenantId, string tplCode, string name, string msgType, MessageChannel channel,
        string subjectTemplate, string contentTemplate, string varSchemaJson, int version, DateTimeOffset now)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(tplCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(msgType);
        ArgumentException.ThrowIfNullOrWhiteSpace(subjectTemplate);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentTemplate);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(version);

        TenantId = tenantId;
        TplCode = tplCode;
        Name = name;
        MsgType = msgType;
        Channel = channel;
        SubjectTemplate = subjectTemplate;
        ContentTemplate = contentTemplate;
        VarSchemaJson = varSchemaJson;
        Version = version;
        Status = MessageTemplateStatus.Active;
        CreatedAt = now;
        UpdatedAt = now;
    }

    /// <summary>启用 / 停用模板。</summary>
    /// <param name="status">目标状态。</param>
    /// <param name="now">当前时间（UTC）。</param>
    public void SetStatus(MessageTemplateStatus status, DateTimeOffset now)
    {
        Status = status;
        UpdatedAt = now;
    }
}

/// <summary>
/// 消息订阅配置实体（表 <c>msg_message_subscriptions</c>）。用户按消息类别的渠道订阅开关；
/// 关键类别（审批待办/催办）种入 <c>Locked=true</c>，前端不可关闭（R-Sub 红线）。
/// </summary>
[Index(nameof(TenantId), nameof(UserId), nameof(MsgType), IsUnique = true)]
[TenantTablePrefix("msg_")]
public sealed class MessageSubscriptionEntity : Qlx.Abstractions.Domain.Entity
{
    /// <summary>所属租户 Id。</summary>
    public long TenantId { get; internal set; }

    /// <summary>用户 Id。</summary>
    [MaxLength(64)]
    public string UserId { get; internal set; } = string.Empty;

    /// <summary>消息类别。</summary>
    [MaxLength(32)]
    public string MsgType { get; internal set; } = string.Empty;

    /// <summary>订阅渠道掩码（MessageChannelMask 位组合）。</summary>
    public int ChannelsMask { get; internal set; } = MessageChannelMask.InApp;

    /// <summary>锁定标记（true=强制订阅，禁止关闭；种子按类别配置）。</summary>
    public bool Locked { get; internal set; }

    /// <summary>是否启用。</summary>
    public bool Enabled { get; internal set; } = true;

    /// <summary>最近更新时间（UTC）。</summary>
    public DateTimeOffset UpdatedAt { get; internal set; }

    /// <summary>EF Core 反序列化专用无参构造。</summary>
    public MessageSubscriptionEntity() { }

    /// <summary>创建订阅配置。</summary>
    /// <param name="tenantId">所属租户 Id。</param>
    /// <param name="userId">用户 Id。</param>
    /// <param name="msgType">消息类别。</param>
    /// <param name="channelsMask">订阅渠道掩码。</param>
    /// <param name="locked">是否锁定。</param>
    /// <param name="now">当前时间（UTC）。</param>
    public MessageSubscriptionEntity(
        long tenantId, string userId, string msgType, int channelsMask, bool locked, DateTimeOffset now)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        ArgumentException.ThrowIfNullOrWhiteSpace(msgType);

        TenantId = tenantId;
        UserId = userId;
        MsgType = msgType;
        ChannelsMask = channelsMask;
        Locked = locked;
        Enabled = true;
        UpdatedAt = now;
    }

    /// <summary>调整订阅开关（锁定项拒绝关闭，返回 false；其他项允许开/关）。</summary>
    /// <param name="enabled">目标开关。</param>
    /// <param name="now">当前时间（UTC）。</param>
    /// <returns>是否生效（锁定项关闭请求被拒绝）。</returns>
    public bool SetEnabled(bool enabled, DateTimeOffset now)
    {
        if (Locked && !enabled)
        {
            return false;
        }

        Enabled = enabled;
        UpdatedAt = now;
        return true;
    }
}

/// <summary>
/// 渠道发送日志实体（表 <c>msg_send_logs</c>，只增不改）。
/// 每条消息每渠道一次投递尝试一行；失败按指数退避重试（新行记录 RetryCount 递增）。
/// </summary>
[Index(nameof(TenantId), nameof(MessageId), nameof(CreatedAt))]
[TenantTablePrefix("msg_")]
public sealed class MessageSendLogEntity : Qlx.Abstractions.Domain.Entity
{
    /// <summary>所属租户 Id。</summary>
    public long TenantId { get; internal set; }

    /// <summary>消息主档 Id。</summary>
    public long MessageId { get; internal set; }

    /// <summary>投递渠道。</summary>
    public MessageChannel Channel { get; internal set; }

    /// <summary>收件目标（用户 Id / 邮箱 / 手机号）。</summary>
    [MaxLength(256)]
    public string Recipient { get; internal set; } = string.Empty;

    /// <summary>发送状态。</summary>
    public MessageSendLogStatus Status { get; internal set; } = MessageSendLogStatus.Pending;

    /// <summary>失败原因（可空）。</summary>
    [MaxLength(1024)]
    public string? FailReason { get; internal set; }

    /// <summary>重试序号（0=首次尝试）。</summary>
    public int RetryCount { get; internal set; }

    /// <summary>发送完成时间（UTC；可空）。</summary>
    public DateTimeOffset? SentAt { get; internal set; }

    /// <summary>创建时间（UTC）。</summary>
    public DateTimeOffset CreatedAt { get; internal set; }

    /// <summary>EF Core 反序列化专用无参构造。</summary>
    public MessageSendLogEntity() { }

    /// <summary>创建发送日志。</summary>
    /// <param name="tenantId">所属租户 Id。</param>
    /// <param name="messageId">消息主档 Id。</param>
    /// <param name="channel">投递渠道。</param>
    /// <param name="recipient">收件目标。</param>
    /// <param name="status">发送状态。</param>
    /// <param name="failReason">失败原因（可空）。</param>
    /// <param name="retryCount">重试序号。</param>
    /// <param name="now">当前时间（UTC）。</param>
    public MessageSendLogEntity(
        long tenantId, long messageId, MessageChannel channel, string recipient,
        MessageSendLogStatus status, string? failReason, int retryCount, DateTimeOffset now)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(tenantId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(messageId);
        ArgumentException.ThrowIfNullOrWhiteSpace(recipient);

        TenantId = tenantId;
        MessageId = messageId;
        Channel = channel;
        Recipient = recipient;
        Status = status;
        FailReason = failReason;
        RetryCount = retryCount;
        CreatedAt = now;
        SentAt = status == MessageSendLogStatus.Pending ? null : now;
    }
}

/// <summary>
/// 全局项状态叠加实体（表 <c>msg_global_state</c>）。平台全局公告/消息只存共享库 glb_* 一份，
/// 租户库仅按（GlobalId, 用户）记录已读/确认/删除叠加状态，禁止复制正文（§5.11 红线）。
/// </summary>
[Index(nameof(TenantId), nameof(GlobalId), nameof(UserId), IsUnique = true)]
[TenantTablePrefix("msg_")]
public sealed class MessageGlobalStateEntity : Qlx.Abstractions.Domain.Entity
{
    /// <summary>所属租户 Id。</summary>
    public long TenantId { get; internal set; }

    /// <summary>共享库全局项 Id（glb_msg_messages 主键的混淆串）。</summary>
    [MaxLength(64)]
    public string GlobalId { get; internal set; } = string.Empty;

    /// <summary>用户 Id（空=租户级状态，预留）。</summary>
    [MaxLength(64)]
    public string UserId { get; internal set; } = string.Empty;

    /// <summary>状态（READ / ACCEPTED / DELETED）。</summary>
    [MaxLength(32)]
    public string State { get; internal set; } = GlobalItemStates.Read;

    /// <summary>状态时间（UTC）。</summary>
    public DateTimeOffset StateAt { get; internal set; }

    /// <summary>EF Core 反序列化专用无参构造。</summary>
    public MessageGlobalStateEntity() { }

    /// <summary>创建全局项状态。</summary>
    /// <param name="tenantId">所属租户 Id。</param>
    /// <param name="globalId">全局项 Id。</param>
    /// <param name="userId">用户 Id。</param>
    /// <param name="state">状态。</param>
    /// <param name="now">当前时间（UTC）。</param>
    public MessageGlobalStateEntity(long tenantId, string globalId, string userId, string state, DateTimeOffset now)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(globalId);
        ArgumentException.ThrowIfNullOrWhiteSpace(state);

        TenantId = tenantId;
        GlobalId = globalId;
        UserId = userId;
        State = state;
        StateAt = now;
    }
}
