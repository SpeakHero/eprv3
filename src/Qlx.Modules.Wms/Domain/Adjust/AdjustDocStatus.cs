/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-26
 * 描述：盘盈盘亏单状态数值枚举（1 草稿 / 2 待审批 / 3 已通过；驳回回 1 草稿）
 */

namespace Qlx.Modules.Wms.Domain.Adjust;

/// <summary>
/// 盘盈盘亏单状态。数值一经发布不可变更（前端按数值映射中文，B-2-5）。
/// </summary>
public enum AdjustDocStatus
{
    /// <summary>草稿（可编辑、可提交审批；驳回后回到本状态）。</summary>
    Draft = 1,

    /// <summary>待审批。</summary>
    PendingApproval = 2,

    /// <summary>已通过（库存命令已入队）。</summary>
    Approved = 3,
}
