/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-26
 * 描述：调拨单状态数值枚举（1 待审批 / 2 已生效 / 3 调出已发货 / 4 调入已收货 / 5 已作废）
 */

namespace Qlx.Modules.Wms.Domain.Transfer;

/// <summary>
/// 调拨单状态。数值一经发布不可变更（前端按数值映射中文，B-2-5）。
/// </summary>
public enum TransferStatus
{
    /// <summary>待审批（新建即挂起审批，审批通过后转已生效）。</summary>
    PendingApproval = 1,

    /// <summary>已生效（可发货）。</summary>
    Effective = 2,

    /// <summary>调出已发货（在途进行中）。</summary>
    Shipped = 3,

    /// <summary>调入已收货（在途已关闭）。</summary>
    Received = 4,

    /// <summary>已作废（仅待审批/已生效可作废）。</summary>
    Voided = 5,
}
