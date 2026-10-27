/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-26 10:00:00 +08:00 中国标准时间 星期一
 * 修改时间：2026-10-26 10:00:00 +08:00 中国标准时间 星期一
 * 描述：成本调整生效集成事件契约，红蓝字流水与台账均价更新后经 Outbox 持久发布。
 */

namespace Qlx.Abstractions.Events.Stk;

/// <summary>
/// 成本调整生效事件。财务成本凭证按 <see cref="IntegrationEvent.EventId"/> 幂等处理。
/// </summary>
public sealed record CostAdjustedEvent : IntegrationEvent
{
    /// <summary>成本调整单 Id。</summary>
    public long CostAdjustId { get; init; }

    /// <summary>成本调整单号。</summary>
    public required string AdjNo { get; init; }

    /// <summary>调整差额合计。</summary>
    public decimal TotalDiffAmount { get; init; }
}
