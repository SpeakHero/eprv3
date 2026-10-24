/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-22 15:40:00 +08:00 中国标准时间 星期三
 * 修改时间：2026-10-22 15:40:00 +08:00 中国标准时间 星期三
 * 描述：消息服务契约（模板发送 / 推送 / 收件箱查询 / 标记已读 / 未读数）
 */

namespace Qlx.Abstractions.Messages;

/// <summary>
/// 消息服务公开接口。所有方法以 Async 后缀命名并接受 CancellationToken 参数。
/// 采用 write-through 模式：写操作直接持久化 + 分发到外部渠道。
/// </summary>
public interface IMessageService
{
    /// <summary>
    /// 按模板发送消息（支持多渠道并行发送：站内 + 邮件 + SMS + 推送）。
    /// </summary>
    /// <param name="request">模板发送请求。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task SendTemplateAsync(SendTemplateRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// 直接推送消息（不走模板，常用于系统通知 / 状态变更广播）。
    /// </summary>
    /// <param name="request">推送请求。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task PushAsync(PushRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// 标记单条消息为已读。
    /// </summary>
    /// <param name="messageInboxId">收件箱记录 ID。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task MarkReadAsync(string messageInboxId, CancellationToken cancellationToken);

    /// <summary>
    /// 标记用户全部消息为已读。
    /// </summary>
    /// <param name="userId">用户 ID。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task MarkAllReadAsync(string userId, CancellationToken cancellationToken);

    /// <summary>
    /// 查询用户未读消息数。
    /// </summary>
    /// <param name="userId">用户 ID。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>未读数。</returns>
    Task<int> GetUnreadCountAsync(string userId, CancellationToken cancellationToken);

    /// <summary>
    /// 查询用户收件箱消息列表。
    /// </summary>
    /// <param name="request">查询请求。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>消息列表。</returns>
    Task<IReadOnlyList<MessageInfo>> GetInboxAsync(MessageQuery request, CancellationToken cancellationToken);

    /// <summary>
    /// 查询用户已发送消息列表。
    /// </summary>
    /// <param name="request">查询请求。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>消息列表。</returns>
    Task<IReadOnlyList<MessageInfo>> GetSentListAsync(MessageQuery request, CancellationToken cancellationToken);
}

/// <summary>
/// 消息渠道。
/// </summary>
public enum MessageChannel
{
    /// <summary>站内信。</summary>
    InApp = 0,

    /// <summary>邮件。</summary>
    Email = 1,

    /// <summary>短信。</summary>
    Sms = 2,

    /// <summary>App 推送。</summary>
    Push = 3
}

/// <summary>
/// 消息优先级。
/// </summary>
public enum MessagePriority
{
    /// <summary>普通。</summary>
    Normal = 0,

    /// <summary>高。</summary>
    High = 1,

    /// <summary>紧急。</summary>
    Urgent = 2
}

/// <summary>
/// 消息发送状态。
/// </summary>
public enum MessageStatus
{
    /// <summary>草稿。</summary>
    Draft = 0,

    /// <summary>已入队。</summary>
    Queued = 1,

    /// <summary>已发送（渠道返回成功，不保证送达）。</summary>
    Sent = 2,

    /// <summary>已确认送达。</summary>
    Delivered = 3,

    /// <summary>已读。</summary>
    Read = 4,

    /// <summary>已归档。</summary>
    Archived = 5,

    /// <summary>发送失败。</summary>
    Failed = 6
}
