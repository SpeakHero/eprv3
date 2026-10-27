/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-26
 * 描述：盘点单状态数值枚举（1 待盘点 / 2 初盘中 / 3 初盘完成 / 4 复盘中 / 5 已完成 / 6 已取消）
 */

namespace Qlx.Modules.Wms.Domain.StockTake;

/// <summary>
/// 盘点单状态。数值一经发布不可变更（前端按数值映射中文，B-2-5）。
/// </summary>
public enum StockTakeStatus
{
    /// <summary>待盘点（生成盘点单后的初始态）。</summary>
    Pending = 1,

    /// <summary>初盘中。</summary>
    InitialCounting = 2,

    /// <summary>初盘完成（存在差异时自动生成复盘单）。</summary>
    InitialFinished = 3,

    /// <summary>复盘中（复盘单生成即进入本状态）。</summary>
    Rechecking = 4,

    /// <summary>已完成（冻结量已释放、盘盈亏草稿已生成）。</summary>
    Completed = 5,

    /// <summary>已取消（冻结量已释放）。</summary>
    Cancelled = 6,
}
