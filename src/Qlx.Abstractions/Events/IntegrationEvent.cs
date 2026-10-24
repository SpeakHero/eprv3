/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-20 22:00:00 +08:00 中国标准时间 星期二
 * 修改时间：2026-10-20 22:00:00 +08:00 中国标准时间 星期二
 * 描述：集成事件契约基类，跨模块事件通信的唯一载体，进程内与 Outbox 共用
 */

namespace Qlx.Abstractions.Events;

/// <summary>
/// 集成事件契约基类。业务事件以过去式命名并继承本记录。
/// 事件在进程内分发与 Outbox 持久化投递之间共享同一契约。
/// </summary>
public abstract record IntegrationEvent
{
    /// <summary>
    /// 事件唯一标识（雪花 ID）。
    /// </summary>
    public long EventId { get; init; }

    /// <summary>
    /// 事件发生时间（UTC）。
    /// </summary>
    public DateTimeOffset OccurredAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// 事件来源租户编码；门户级全局事件为 <see langword="null"/>。
    /// </summary>
    public string? TenantCode { get; init; }
}
