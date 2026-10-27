/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-26 10:00:00 +08:00 中国标准时间 星期一
 * 修改时间：2026-10-26 10:00:00 +08:00 中国标准时间 星期一
 * 描述：期初过账完成集成事件契约，期初流水写入并锁定批次后经 Outbox 持久发布。
 */

namespace Qlx.Abstractions.Events.Stk;

/// <summary>
/// 期初过账完成事件。财务期初存货核对与下游消费方按 <see cref="IntegrationEvent.EventId"/> 幂等处理。
/// </summary>
public sealed record OpeningPostedEvent : IntegrationEvent
{
    /// <summary>期初批次 Id。</summary>
    public long OpeningBatchId { get; init; }

    /// <summary>期初批次号。</summary>
    public required string BatchNo { get; init; }

    /// <summary>仓库 Id。</summary>
    public long WarehouseId { get; init; }

    /// <summary>期初总金额。</summary>
    public decimal TotalAmount { get; init; }
}
