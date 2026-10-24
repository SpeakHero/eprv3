/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-22 15:40:00 +08:00 中国标准时间 星期三
 * 修改时间：2026-10-22 15:40:00 +08:00 中国标准时间 星期三
 * 描述：消息请求 / 信息 / 业务引用 record
 */

namespace Qlx.Abstractions.Messages;

/// <summary>
/// 按模板发送消息请求。
/// </summary>
public sealed record SendTemplateRequest(
    string TemplateCode,
    string RecipientUserId,
    IReadOnlyDictionary<string, object?>? Variables = null,
    IReadOnlyList<MessageChannel>? Channels = null,
    MessagePriority Priority = MessagePriority.Normal,
    MessageBizRef? BizRef = null);

/// <summary>
/// 直接推送消息请求（不走模板，内容由调用方直接提供）。
/// </summary>
public sealed record PushRequest(
    string RecipientUserId,
    string Subject,
    string Content,
    MessageChannel Channel = MessageChannel.InApp,
    MessagePriority Priority = MessagePriority.Normal,
    MessageBizRef? BizRef = null);

/// <summary>
/// 消息查询请求。
/// </summary>
public sealed record MessageQuery(
    int PageIndex = 1,
    int PageSize = 20,
    MessageStatus? Status = null,
    string? Keyword = null);

/// <summary>
/// 收件箱 / 已发送列表中的消息项。
/// </summary>
public sealed record MessageInfo(
    string MessageId,
    string? InboxId,
    string Subject,
    string Content,
    MessageChannel Channel,
    MessageStatus Status,
    MessagePriority Priority,
    string SenderUserId,
    string RecipientUserId,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ReadAt,
    MessageBizRef? BizRef);

/// <summary>
/// 消息关联的业务引用（跨模块溯源用）。
/// </summary>
public sealed record MessageBizRef(
    string BizType,
    string BizId,
    string? BizNo = null);

/// <summary>
/// 消息模板定义。
/// </summary>
public sealed record MessageTemplateInfo(
    string TemplateCode,
    string Name,
    string SubjectTemplate,
    string ContentTemplate,
    MessageChannel DefaultChannel,
    IReadOnlyList<string> VarSchema);
