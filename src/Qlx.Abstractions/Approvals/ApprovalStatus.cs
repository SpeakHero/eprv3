/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-22 15:35:00 +08:00 中国标准时间 星期三
 * 修改时间：2026-10-22 15:35:00 +08:00 中国标准时间 星期三
 * 描述：审批状态枚举
 */

namespace Qlx.Abstractions.Approvals;

/// <summary>
/// 审批实例生命周期状态。
/// </summary>
public enum ApprovalStatus
{
    /// <summary>草稿（未提交）。</summary>
    Draft = 0,

    /// <summary>审批中（至少一个节点未决）。</summary>
    Pending = 1,

    /// <summary>审批通过。</summary>
    Approved = 2,

    /// <summary>审批驳回。</summary>
    Rejected = 3,

    /// <summary>发起人撤回。</summary>
    Withdrawn = 4,

    /// <summary>系统终止（发起人 / 管理员手动终止）。</summary>
    Terminated = 5,

    /// <summary>取消（前置节点失败 / 依赖终止）。</summary>
    Canceled = 6
}
