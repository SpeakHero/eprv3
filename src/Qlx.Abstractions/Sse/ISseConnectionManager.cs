/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-22 15:50:00 +08:00 中国标准时间 星期三
 * 修改时间：2026-10-22 15:50:00 +08:00 中国标准时间 星期三
 * 描述：SSE（Server-Sent Events）连接管理契约
 */

namespace Qlx.Abstractions.Sse;

/// <summary>
/// SSE 事件类型枚举。
/// </summary>
public enum SseEventType
{
    /// <summary>站内消息已创建。</summary>
    MessageCreated,

    /// <summary>后台任务进度更新。</summary>
    TaskProgress,

    /// <summary>后台任务完成。</summary>
    TaskCompleted,

    /// <summary>后台任务失败。</summary>
    TaskFailed,

    /// <summary>审批待办提醒。</summary>
    ApprovalPending,

    /// <summary>审批完成。</summary>
    ApprovalCompleted,

    /// <summary>全局广播。</summary>
    GlobalPublish,

    /// <summary>系统告警。</summary>
    SystemAlert,

    /// <summary>文件上传完成。</summary>
    FileUploadCompleted
}

/// <summary>
/// SSE 事件 record（载荷用 Json 字符串承载，避免 Abstractions 引用具体 DTO）。
/// </summary>
public sealed record SseEvent(
    long EventId,
    SseEventType Type,
    string DataJson,
    DateTimeOffset OccurredAt);

/// <summary>
/// SSE 连接管理接口。负责注册 / 注销连接、租户推送、断线续传事件拉取。
/// </summary>
public interface ISseConnectionManager
{
    /// <summary>
    /// 注册连接（用户建立 SSE 通道时调用）。
    /// </summary>
    /// <param name="userId">用户 ID。</param>
    /// <param name="connectionId">连接 ID（前端生成）。</param>
    /// <param name="tenantId">租户 ID。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task RegisterConnectionAsync(long userId, string connectionId, long tenantId, CancellationToken cancellationToken);

    /// <summary>
    /// 注销连接（SSE 断开时调用）。
    /// </summary>
    /// <param name="userId">用户 ID。</param>
    /// <param name="connectionId">连接 ID。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task UnregisterConnectionAsync(long userId, string connectionId, CancellationToken cancellationToken);

    /// <summary>
    /// 查询租户当前活跃连接数。
    /// </summary>
    /// <param name="tenantId">租户 ID。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>活跃连接数。</returns>
    Task<int> GetConnectionCountAsync(long tenantId, CancellationToken cancellationToken);

    /// <summary>
    /// 推送事件（实现按事件类型与租户路由至目标用户连接）。
    /// </summary>
    /// <param name="sseEvent">事件。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task PushAsync(SseEvent sseEvent, CancellationToken cancellationToken);

    /// <summary>
    /// 断线续传：获取指定事件 ID 之后未被消费的事件列表。
    /// </summary>
    /// <param name="tenantId">租户 ID。</param>
    /// <param name="lastEventId">客户端最后收到的事件 ID。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>遗漏事件列表。</returns>
    Task<IReadOnlyList<SseEvent>> GetEventsSinceAsync(long tenantId, long lastEventId, CancellationToken cancellationToken);
}
