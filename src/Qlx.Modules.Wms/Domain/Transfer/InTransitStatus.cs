/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-26
 * 描述：在途库存行状态数值枚举（1 在途 / 2 已关闭）
 */

namespace Qlx.Modules.Wms.Domain.Transfer;

/// <summary>
/// 在途库存行状态。数值一经发布不可变更（超期标记由前端按 预计到达 计算）。
/// </summary>
public enum InTransitStatus
{
    /// <summary>在途（发货后、收货前）。</summary>
    InTransit = 1,

    /// <summary>已关闭（收货完成）。</summary>
    Closed = 2,
}
