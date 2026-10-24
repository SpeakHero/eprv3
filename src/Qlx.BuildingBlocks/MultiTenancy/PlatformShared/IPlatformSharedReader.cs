/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-23 06:40:00 +08:00 中国标准时间 星期五
 * 描述：共享库全局项只读读取器契约——只读 + 进程内短缓存（InboxCacheSeconds）+ 故障降级空集
 */

using Qlx.Abstractions.Messages;

namespace Qlx.BuildingBlocks.MultiTenancy.PlatformShared;

/// <summary>
/// 共享库全局项读取器（租户侧消费）。收件箱合并数据源之一：
/// 本地 msg_messages ∪ 全局 glb_messages（应用层合并，去重键 GlobalId，禁止跨库 JOIN）。
/// 只读：本接口无任何写方法；共享库写入口在平台侧（后续阶段）。
/// </summary>
public interface IPlatformSharedReader
{
    /// <summary>
    /// 拉取当前生效的全局消息（未撤回、生效窗口内，按发布时间倒序）。
    /// 结果经短缓存（<see cref="PlatformSharedOptions.InboxCacheSeconds"/>）复用；
    /// 共享库故障时降级返回空集并告警（本地项不受影响）。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>生效全局消息列表。</returns>
    Task<IReadOnlyList<GlobalMessageView>> GetActiveMessagesAsync(CancellationToken cancellationToken);
}

/// <summary>全局消息只读视图。</summary>
/// <param name="GlobalId">全局项 Id（雪花 ID 混淆串，租户侧去重/状态叠加键）。</param>
/// <param name="MsgType">消息类别。</param>
/// <param name="Subject">标题。</param>
/// <param name="Content">正文。</param>
/// <param name="Priority">优先级。</param>
/// <param name="ForceAccept">是否强制确认公告。</param>
/// <param name="PublishedBy">发布人。</param>
/// <param name="PublishedAt">发布时间（UTC）。</param>
public sealed record GlobalMessageView(
    string GlobalId,
    string MsgType,
    string Subject,
    string Content,
    MessagePriority Priority,
    bool ForceAccept,
    string PublishedBy,
    DateTimeOffset PublishedAt);
