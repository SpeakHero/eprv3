/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-26 16:10:00 +08:00 中国标准时间 星期一
 * 描述：入库作业来源类型（1 采购 / 2 销售退货 / 3 调拨 / 4 盘盈）
 */

namespace Qlx.Modules.Wms.Domain.Inbound;

/// <summary>入库作业来源类型。</summary>
public enum StockInSourceType
{
    /// <summary>采购入库。</summary>
    Purchase = 1,

    /// <summary>销售退货入库。</summary>
    SaleReturn = 2,

    /// <summary>调拨入库。</summary>
    Transfer = 3,

    /// <summary>盘盈入库。</summary>
    Gain = 4,
}
