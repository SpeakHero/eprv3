/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-26 10:00:00 +08:00 中国标准时间 星期一
 * 修改时间：2026-10-26 10:00:00 +08:00 中国标准时间 星期一
 * 描述：调拨收货集成事件契约，调入仓库存增加与在途关闭后经 Outbox 持久发布。
 */

namespace Qlx.Abstractions.Events.Wms;

/// <summary>
/// 调拨收货事件（在途结束）。财务内部结算与下游消费方按 <see cref="IntegrationEvent.EventId"/> 幂等处理。
/// </summary>
public sealed record TransferReceivedEvent : IntegrationEvent
{
    /// <summary>调拨单 Id。</summary>
    public long TransferId { get; init; }

    /// <summary>调拨单号。</summary>
    public required string TransferNo { get; init; }

    /// <summary>调出仓库 Id。</summary>
    public long FromWarehouseId { get; init; }

    /// <summary>调入仓库 Id。</summary>
    public long ToWarehouseId { get; init; }
}
