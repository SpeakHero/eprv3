/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-20 22:00:00 +08:00 中国标准时间 星期二
 * 修改时间：2026-10-20 22:00:00 +08:00 中国标准时间 星期二
 * 描述：Outbox 持久化消息记录，承载待投递事件的负载、租约与重试状态
 */

namespace Qlx.BuildingBlocks.EventBus.Outbox;

/// <summary>
/// Outbox 消息记录。消息只增不改状态字段（重试除外），
/// 处理成功后置 <see cref="ProcessedAt"/>，达到最大重试次数后由运维人工介入。
/// </summary>
public sealed record OutboxMessage
{
    /// <summary>
    /// 消息唯一标识（雪花 ID）。
    /// </summary>
    public required long Id { get; init; }

    /// <summary>
    /// 事件类型的程序集限定名，用于调度器反序列化。
    /// </summary>
    public required string EventType { get; init; }

    /// <summary>
    /// 事件负载（JSON）。
    /// </summary>
    public required string Payload { get; init; }

    /// <summary>
    /// 来源租户编码；门户级事件为 <see langword="null"/>。
    /// </summary>
    public string? TenantCode { get; init; }

    /// <summary>
    /// 事件发生时间（UTC）。
    /// </summary>
    public DateTimeOffset OccurredAt { get; init; }

    /// <summary>
    /// 下次可投递时间（UTC）；失败重试时按退避后移。
    /// </summary>
    public DateTimeOffset AvailableAt { get; set; }

    /// <summary>
    /// 处理成功时间（UTC）；未处理为 <see langword="null"/>。
    /// </summary>
    public DateTimeOffset? ProcessedAt { get; set; }

    /// <summary>
    /// 已尝试投递次数。
    /// </summary>
    public int AttemptCount { get; set; }

    /// <summary>
    /// 最近一次投递失败原因。
    /// </summary>
    public string? LastError { get; set; }

    /// <summary>
    /// 当前持有租约的调度实例标识；空闲为 <see langword="null"/>。
    /// </summary>
    public string? LeaseOwner { get; set; }

    /// <summary>
    /// 租约到期时间（UTC）；到期未完成的租约可被其它实例接管。
    /// </summary>
    public DateTimeOffset? LeaseUntil { get; set; }
}
