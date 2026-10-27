/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-26
 * 描述：库存预警类型数值枚举（spec §14.5）
 */

namespace Qlx.Modules.Wms.Domain.Alert;

/// <summary>库存预警类型（数值枚举，中文映射由前端维护，B-2-5）。</summary>
public enum InventoryAlertType
{
    /// <summary>安全库存（现存数量低于阈值）。</summary>
    SafetyStock = 1,

    /// <summary>积压（有库存且长期无流水）。</summary>
    Stagnant = 2,

    /// <summary>临期（批次失效日期临近）。</summary>
    NearExpiry = 3,

    /// <summary>在途超期（发货后超过在途天数仍未收货）。</summary>
    InTransitOverdue = 4,
}
