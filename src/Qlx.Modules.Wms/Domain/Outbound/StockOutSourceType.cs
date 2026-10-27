/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-26
 * 描述：出库作业来源类型数值枚举（前端维护中文映射，B-2）
 */

namespace Qlx.Modules.Wms.Domain.Outbound;

/// <summary>出库作业来源类型。数值一经发布不可变更。</summary>
public enum StockOutSourceType
{
    /// <summary>销售出库。</summary>
    Sale = 1,

    /// <summary>采购退货出库。</summary>
    PurchaseReturn = 2,

    /// <summary>调拨出库。</summary>
    Transfer = 3,

    /// <summary>盘亏出库。</summary>
    Loss = 4,

    /// <summary>领用出库。</summary>
    Use = 5,
}
