/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-26 10:00:00 +08:00 中国标准时间 星期一
 * 修改时间：2026-10-26 10:00:00 +08:00 中国标准时间 星期一
 * 描述：入库作业完成集成事件契约，Wms 作业确认后经 Outbox 持久发布。
 */

namespace Qlx.Abstractions.Events.Wms;

/// <summary>
/// 入库作业完成事件。库存写入经命令契约成功后发布，消费方按 <see cref="IntegrationEvent.EventId"/> 幂等处理。
/// </summary>
public sealed record StockInCompletedEvent : IntegrationEvent
{
    /// <summary>入库作业 Id。</summary>
    public long TaskId { get; init; }

    /// <summary>入库作业单号。</summary>
    public required string TaskNo { get; init; }

    /// <summary>入库仓库 Id。</summary>
    public long WarehouseId { get; init; }

    /// <summary>来源单据号。</summary>
    public required string SourceDocNo { get; init; }
}
