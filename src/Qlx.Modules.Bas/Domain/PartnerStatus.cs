/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-25 18:00:00 +08:00 中国标准时间 星期日
 * 描述：往来单位（供应商 / 客户）共享状态枚举与状态流转守卫——经公共层 StateMachine 校验非法迁移
 */

using Qlx.Abstractions.Exceptions;
using Qlx.BuildingBlocks.StateMachines;

namespace Qlx.Modules.Bas.Domain;

/// <summary>
/// 往来单位状态（供应商 / 客户共用）。数值枚举，中文文案由前端公共层映射（B-2-5）。
/// </summary>
public enum PartnerStatus
{
    /// <summary>待准入（准入审批通过前不可被业务单据引用）。</summary>
    Pending = 1,

    /// <summary>合作中。</summary>
    Active = 2,

    /// <summary>冻结（资质到期 / 黑名单等触发，解冻需重新准入审批）。</summary>
    Frozen = 3,

    /// <summary>终止（终态，不可再流转）。</summary>
    Closed = 4,
}

/// <summary>往来单位状态流转触发器。</summary>
public enum PartnerTrigger
{
    /// <summary>提交（重新）准入审批。</summary>
    SubmitAdmission = 1,

    /// <summary>准入审批通过。</summary>
    Approve = 2,

    /// <summary>冻结。</summary>
    Freeze = 3,

    /// <summary>终止合作。</summary>
    Close = 4,
}

/// <summary>
/// 往来单位状态流转守卫。基于公共层 <see cref="StateMachine{TState,TTrigger}"/> 的单次迁移校验：
/// 允许 PENDING/FROZEN 提交（重新）准入（状态不变，审批通过后置 ACTIVE）、PENDING/FROZEN→ACTIVE、
/// ACTIVE→FROZEN、PENDING/ACTIVE/FROZEN→CLOSED；CLOSED 为终态（无出边），其余迁移一律抛 <see cref="ConflictException"/>。
/// </summary>
internal static class PartnerStatusGuard
{
    /// <summary>执行一次状态迁移，非法迁移抛冲突异常。</summary>
    /// <param name="current">当前状态。</param>
    /// <param name="trigger">触发动作。</param>
    /// <param name="errorCode">冲突业务码。</param>
    /// <returns>迁移后的状态。</returns>
    public static PartnerStatus Fire(PartnerStatus current, PartnerTrigger trigger, string errorCode)
    {
        StateMachine<PartnerStatus, PartnerTrigger> machine = Configure(current);
        StateTransitionResult<PartnerStatus> result = machine.Fire(trigger);
        return result.IsSuccess
            ? result.NewState!
            : throw new ConflictException($"当前状态不允许该操作：{result.ErrorMessage}", errorCode);
    }

    private static StateMachine<PartnerStatus, PartnerTrigger> Configure(PartnerStatus current)
    {
        var machine = new StateMachine<PartnerStatus, PartnerTrigger>(current);

        _ = machine.Configure(PartnerStatus.Pending)
            .Permit(PartnerTrigger.SubmitAdmission, PartnerStatus.Pending)
            .Permit(PartnerTrigger.Approve, PartnerStatus.Active)
            .Permit(PartnerTrigger.Close, PartnerStatus.Closed);

        _ = machine.Configure(PartnerStatus.Active)
            .Permit(PartnerTrigger.Freeze, PartnerStatus.Frozen)
            .Permit(PartnerTrigger.Close, PartnerStatus.Closed);

        _ = machine.Configure(PartnerStatus.Frozen)
            .Permit(PartnerTrigger.SubmitAdmission, PartnerStatus.Frozen)
            .Permit(PartnerTrigger.Approve, PartnerStatus.Active)
            .Permit(PartnerTrigger.Close, PartnerStatus.Closed);

        _ = machine.Configure(PartnerStatus.Closed);

        return machine;
    }
}
