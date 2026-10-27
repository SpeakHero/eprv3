/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-26 10:00:00 +08:00 中国标准时间 星期一
 * 修改时间：2026-10-26 10:00:00 +08:00 中国标准时间 星期一
 * 描述：库存预警生成集成事件契约，预警扫描任务落库后经 Outbox 持久发布。
 */

namespace Qlx.Abstractions.Events.Wms;

/// <summary>
/// 库存预警生成事件。通知中心与补货/去化建议消费方按 <see cref="IntegrationEvent.EventId"/> 幂等处理。
/// </summary>
public sealed record InventoryWarningRaisedEvent : IntegrationEvent
{
    /// <summary>预警 Id。</summary>
    public long AlertId { get; init; }

    /// <summary>预警类型数值枚举（1 安全库存 / 2 积压 / 3 临期 / 4 在途超期）。</summary>
    public int AlertType { get; init; }

    /// <summary>商品 Id。</summary>
    public long ItemId { get; init; }

    /// <summary>仓库 Id。</summary>
    public long WarehouseId { get; init; }
}
