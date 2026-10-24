/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-23 11:20:00 +08:00 中国标准时间 星期五
 * 描述：审批实例 / 任务状态机测试——合法转移、非法转换拒绝、终态判定、允许目标集合
 */

using Qlx.Abstractions.Approvals;
using Qlx.Modules.Support.Services.Approvals;
using Xunit;

namespace Qlx.BuildingBlocks.Tests.Approvals;

/// <summary>
/// <see cref="ApprovalInstanceStateMachine"/> 与 <see cref="ApprovalTaskStateMachine"/> 单元测试
/// （非法转换抛 InvalidOperationException，终态无出边）。
/// </summary>
public sealed class ApprovalInstanceStateMachineTests
{
    // ============ 实例状态机：合法转移 ============

    [Fact]
    public void InstanceCanTransitionFromDraftToPending()
        => Assert.True(ApprovalInstanceStateMachine.CanTransition(ApprovalStatus.Draft, ApprovalStatus.Pending));

    [Theory]
    [InlineData(ApprovalStatus.Approved)]
    [InlineData(ApprovalStatus.Rejected)]
    [InlineData(ApprovalStatus.Withdrawn)]
    [InlineData(ApprovalStatus.Terminated)]
    [InlineData(ApprovalStatus.Canceled)]
    public void InstanceCanTransitionFromPendingToAnyTerminal(ApprovalStatus terminal)
        => Assert.True(ApprovalInstanceStateMachine.CanTransition(ApprovalStatus.Pending, terminal));

    // ============ 实例状态机：非法转换 ============

    [Theory]
    [InlineData(ApprovalStatus.Draft, ApprovalStatus.Approved)]
    [InlineData(ApprovalStatus.Draft, ApprovalStatus.Rejected)]
    [InlineData(ApprovalStatus.Draft, ApprovalStatus.Draft)]
    [InlineData(ApprovalStatus.Pending, ApprovalStatus.Draft)]
    [InlineData(ApprovalStatus.Pending, ApprovalStatus.Pending)]
    [InlineData(ApprovalStatus.Approved, ApprovalStatus.Rejected)]
    [InlineData(ApprovalStatus.Approved, ApprovalStatus.Pending)]
    [InlineData(ApprovalStatus.Rejected, ApprovalStatus.Approved)]
    [InlineData(ApprovalStatus.Withdrawn, ApprovalStatus.Pending)]
    [InlineData(ApprovalStatus.Terminated, ApprovalStatus.Pending)]
    [InlineData(ApprovalStatus.Canceled, ApprovalStatus.Approved)]
    public void InstanceIllegalTransitionsAreRejected(ApprovalStatus from, ApprovalStatus to)
        => Assert.False(ApprovalInstanceStateMachine.CanTransition(from, to));

    [Fact]
    public void InstanceEnsureTransitionFromTerminalThrows()
        => Assert.Throws<InvalidOperationException>(
            () => ApprovalInstanceStateMachine.EnsureTransition(ApprovalStatus.Approved, ApprovalStatus.Pending));

    [Fact]
    public void InstanceAllowedTargetsOfTerminalIsEmpty()
        => Assert.Empty(ApprovalInstanceStateMachine.AllowedTargets(ApprovalStatus.Terminated));

    [Theory]
    [InlineData(ApprovalStatus.Approved, true)]
    [InlineData(ApprovalStatus.Rejected, true)]
    [InlineData(ApprovalStatus.Withdrawn, true)]
    [InlineData(ApprovalStatus.Terminated, true)]
    [InlineData(ApprovalStatus.Canceled, true)]
    [InlineData(ApprovalStatus.Draft, false)]
    [InlineData(ApprovalStatus.Pending, false)]
    public void InstanceIsTerminalReflectsLifecycle(ApprovalStatus status, bool expected)
        => Assert.Equal(expected, ApprovalInstanceStateMachine.IsTerminal(status));

    // ============ 任务状态机 ============

    [Theory]
    [InlineData(ApprovalTaskStatus.Approved)]
    [InlineData(ApprovalTaskStatus.Rejected)]
    [InlineData(ApprovalTaskStatus.Transferred)]
    [InlineData(ApprovalTaskStatus.AddSigned)]
    [InlineData(ApprovalTaskStatus.Canceled)]
    public void TaskCanTransitionFromPendingToAnyTerminal(ApprovalTaskStatus terminal)
        => Assert.True(ApprovalTaskStateMachine.CanTransition(ApprovalTaskStatus.Pending, terminal));

    [Fact]
    public void TaskEnsureTransitionFromApprovedThrows()
        => Assert.Throws<InvalidOperationException>(
            () => ApprovalTaskStateMachine.EnsureTransition(ApprovalTaskStatus.Approved, ApprovalTaskStatus.Rejected));

    [Theory]
    [InlineData(ApprovalTaskStatus.Pending, false)]
    [InlineData(ApprovalTaskStatus.Approved, true)]
    [InlineData(ApprovalTaskStatus.Canceled, true)]
    public void TaskIsTerminalReflectsStatus(ApprovalTaskStatus status, bool expected)
        => Assert.Equal(expected, ApprovalTaskStateMachine.IsTerminal(status));
}
