/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-26 21:00:00 +08:00 中国标准时间 星期一
 * 描述：Wms → Stk 库存命令 Outbox 行状态（spec §14.3）
 */

namespace Qlx.Modules.Wms.Domain.Outbox;

/// <summary>库存命令 Outbox 行状态。数值一经发布不可变更。</summary>
public enum WmsStockCommandState
{
    /// <summary>待分发（含失败后等待重试的行）。</summary>
    Pending = 1,

    /// <summary>已分发（Stk 已成功写入流水，或幂等命中）。</summary>
    Dispatched = 2,

    /// <summary>分发失败（等待退避重试；重试耗尽后为终态，仅人工重推可恢复）。</summary>
    Failed = 3,
}
