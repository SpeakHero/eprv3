/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-23 06:15:00 +08:00 中国标准时间 星期五
 * 描述：Support 模块信息中心门面契约——收件箱合并视图（本地+全局）/ 已发送分页 /
 *       全局项状态叠加 / 订阅管理（2A 冻结契约 IMessageService 的模块侧补充能力）
 */

using Qlx.Abstractions.Messages;
using Qlx.Modules.Support.Domain;

namespace Qlx.Modules.Support.Services.Messages;

/// <summary>
/// Support 信息中心门面服务。与 <see cref="IMessageService"/> 同实例承载
/// （<see cref="SupportMessageService"/>），本接口补充收件箱合并 / 全局项状态 / 订阅管理能力。
/// </summary>
public interface ISupportMessageService
{
    /// <summary>
    /// 分页查询当前用户收件箱（本地消息 ∪ 共享库全局项应用层合并，去重键 GlobalId；
    /// 全局项 DELETED 状态过滤，正文不复制）。
    /// </summary>
    /// <param name="pageIndex">页码（1 起）。</param>
    /// <param name="pageSize">页大小（上限 100）。</param>
    /// <param name="keyword">标题/正文关键字（可空）。</param>
    /// <param name="unreadOnly">仅未读。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>合并收件箱分页。</returns>
    Task<SupportInboxPage> QueryInboxAsync(
        int pageIndex, int pageSize, string? keyword, bool unreadOnly, CancellationToken cancellationToken);

    /// <summary>
    /// 分页查询当前用户已发送消息列表。
    /// </summary>
    /// <param name="pageIndex">页码（1 起）。</param>
    /// <param name="pageSize">页大小（上限 100）。</param>
    /// <param name="keyword">标题/正文关键字（可空）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>已发送分页。</returns>
    Task<SupportMessagePage> QuerySentAsync(
        int pageIndex, int pageSize, string? keyword, CancellationToken cancellationToken);

    /// <summary>
    /// 记录全局项用户状态（READ / ACCEPTED / DELETED；ForceAccept 公告拒绝 DELETED）。
    /// </summary>
    /// <param name="globalId">全局项 Id（混淆串）。</param>
    /// <param name="state">目标状态。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task MarkGlobalItemAsync(string globalId, string state, CancellationToken cancellationToken);

    /// <summary>
    /// 个人删除收件箱项（本地项置 Deleted；不影响消息主档与他人视图）。
    /// </summary>
    /// <param name="inboxId">收件箱项 Id（混淆串）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task DeleteInboxItemAsync(string inboxId, CancellationToken cancellationToken);

    /// <summary>
    /// 查询当前用户订阅配置清单（按消息类别）。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    Task<IReadOnlyList<SupportSubscriptionItem>> ListSubscriptionsAsync(CancellationToken cancellationToken);

    /// <summary>
    /// 调整订阅开关（Locked 强制订阅拒绝关闭）。
    /// </summary>
    /// <param name="msgType">消息类别。</param>
    /// <param name="enabled">目标开关。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task ToggleSubscriptionAsync(string msgType, bool enabled, CancellationToken cancellationToken);

    /// <summary>
    /// 查询消息渠道发送记录（只读 msg_send_logs，仅发送方可查）。
    /// </summary>
    /// <param name="messageId">消息 Id（混淆串）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>渠道投递尝试明细（按时间升序，只增不改）。</returns>
    Task<IReadOnlyList<SupportSendLogItem>> ListSendLogsAsync(string messageId, CancellationToken cancellationToken);

    /// <summary>
    /// 人工重试外部渠道投递（仅发送方可触发；绕过自动重试上限强制尝试一轮，send_logs 追加新行）。
    /// </summary>
    /// <param name="messageId">消息 Id（混淆串）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task RetryFailedMessageAsync(string messageId, CancellationToken cancellationToken);
}

/// <summary>收件箱合并项。</summary>
/// <param name="MessageId">消息 Id（全局项为共享库 GlobalId 混淆串）。</param>
/// <param name="InboxId">收件箱项 Id（全局项为 null，状态走 msg_global_state）。</param>
/// <param name="MsgType">消息类别。</param>
/// <param name="Subject">标题。</param>
/// <param name="Content">正文。</param>
/// <param name="Priority">优先级。</param>
/// <param name="Status">状态。</param>
/// <param name="SenderUserId">发送方。</param>
/// <param name="CreatedAt">发布时间（UTC）。</param>
/// <param name="ReadAt">已读时间（UTC；可空）。</param>
/// <param name="BizType">业务引用类型（可空）。</param>
/// <param name="BizId">业务引用 Id（可空）。</param>
/// <param name="BizNo">业务引用单号（可空）。</param>
/// <param name="IsGlobal">是否全局项（共享库来源）。</param>
/// <param name="ForceAccept">全局项是否强制确认（true=不可删除）。</param>
public sealed record SupportInboxItem(
    string MessageId,
    string? InboxId,
    string MsgType,
    string Subject,
    string Content,
    MessagePriority Priority,
    MessageStatus Status,
    string SenderUserId,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ReadAt,
    string? BizType,
    string? BizId,
    string? BizNo,
    bool IsGlobal,
    bool ForceAccept);

/// <summary>合并收件箱分页结果。</summary>
/// <param name="Items">当前页项。</param>
/// <param name="Total">合并后总条数。</param>
/// <param name="PageIndex">页码。</param>
/// <param name="PageSize">页大小。</param>
public sealed record SupportInboxPage(
    IReadOnlyList<SupportInboxItem> Items, int Total, int PageIndex, int PageSize);

/// <summary>已发送分页结果。</summary>
/// <param name="Items">当前页项。</param>
/// <param name="Total">总条数。</param>
/// <param name="PageIndex">页码。</param>
/// <param name="PageSize">页大小。</param>
public sealed record SupportMessagePage(
    IReadOnlyList<MessageInfo> Items, int Total, int PageIndex, int PageSize);

/// <summary>渠道发送记录项（msg_send_logs 只增不改，每次尝试一行）。</summary>
/// <param name="MessageId">消息 Id（混淆串）。</param>
/// <param name="Channel">投递渠道。</param>
/// <param name="Recipient">收件目标（用户 Id / 邮箱 / 手机号）。</param>
/// <param name="Status">发送状态。</param>
/// <param name="FailReason">失败原因（可空）。</param>
/// <param name="RetryCount">重试序号（0=首次尝试）。</param>
/// <param name="SentAt">发送完成时间（UTC；可空）。</param>
/// <param name="CreatedAt">记录时间（UTC）。</param>
public sealed record SupportSendLogItem(
    string MessageId,
    MessageChannel Channel,
    string Recipient,
    MessageSendLogStatus Status,
    string? FailReason,
    int RetryCount,
    DateTimeOffset? SentAt,
    DateTimeOffset CreatedAt);

/// <summary>订阅配置项。</summary>
/// <param name="MsgType">消息类别。</param>
/// <param name="ChannelsMask">订阅渠道掩码。</param>
/// <param name="Locked">强制订阅（不可关闭）。</param>
/// <param name="Enabled">当前开关。</param>
public sealed record SupportSubscriptionItem(string MsgType, int ChannelsMask, bool Locked, bool Enabled);
