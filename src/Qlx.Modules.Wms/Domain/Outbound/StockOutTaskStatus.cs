/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-26
 * 描述：出库作业状态数值枚举（待作业 → 拣货中 → 待复核 → 已完成；前置态可取消）
 */

namespace Qlx.Modules.Wms.Domain.Outbound;

/// <summary>出库作业状态。</summary>
public enum StockOutTaskStatus
{
    /// <summary>待作业。</summary>
    Pending = 1,

    /// <summary>拣货中。</summary>
    Picking = 2,

    /// <summary>待复核。</summary>
    PendingCheck = 3,

    /// <summary>已完成。</summary>
    Completed = 4,

    /// <summary>已取消。</summary>
    Cancelled = 5,
}
