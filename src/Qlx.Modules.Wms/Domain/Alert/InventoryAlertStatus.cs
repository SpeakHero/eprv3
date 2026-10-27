/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-26
 * 描述：库存预警状态数值枚举
 */

namespace Qlx.Modules.Wms.Domain.Alert;

/// <summary>库存预警状态（数值枚举，中文映射由前端维护，B-2-5）。</summary>
public enum InventoryAlertStatus
{
    /// <summary>待处理。</summary>
    Pending = 1,

    /// <summary>已处理。</summary>
    Handled = 2,

    /// <summary>已忽略。</summary>
    Ignored = 3,
}
