/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-26 16:10:00 +08:00 中国标准时间 星期一
 * 描述：Wms 作业单据号前缀常量（04 物流作业全量 §14.1）
 */

namespace Qlx.Modules.Wms.Services;

/// <summary>Wms 单据号前缀（文本格式 <c>{prefix}-{orgCode}-{yyyyMM}-{serial}</c>）。</summary>
internal static class WmsDocumentPrefixes
{
    /// <summary>入库作业单。</summary>
    internal const string StockIn = "WL-RK";

    /// <summary>出库作业单。</summary>
    internal const string StockOut = "WL-CK";

    /// <summary>调拨单。</summary>
    internal const string Transfer = "WL-DB";

    /// <summary>盘点计划。</summary>
    internal const string StockTakePlan = "WL-PDJH";

    /// <summary>盘点单。</summary>
    internal const string StockTake = "WL-PD";

    /// <summary>盘盈盘亏单。</summary>
    internal const string InventoryAdjust = "WL-PYK";
}
