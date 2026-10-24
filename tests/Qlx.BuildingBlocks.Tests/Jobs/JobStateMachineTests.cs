/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-23 04:40:00 +08:00 中国标准时间 星期五
 * 描述：任务执行状态机测试——合法转移 / 非法转移 / 终态空集 / 自转移禁止 / 校验异常
 */

using Qlx.Abstractions.Jobs;
using Qlx.BuildingBlocks.Jobs;

namespace Qlx.BuildingBlocks.Tests.Jobs;

/// <summary>
/// <see cref="JobStateMachine"/> 单元测试。方案 D19：状态不可跳过、终态不可再转移。
/// </summary>
public sealed class JobStateMachineTests
{
    [Theory]
    [InlineData(JobExecutionStatus.Pending, JobExecutionStatus.Queued)]
    [InlineData(JobExecutionStatus.Pending, JobExecutionStatus.Running)]
    [InlineData(JobExecutionStatus.Pending, JobExecutionStatus.Canceled)]
    [InlineData(JobExecutionStatus.Queued, JobExecutionStatus.Running)]
    [InlineData(JobExecutionStatus.Queued, JobExecutionStatus.Canceled)]
    [InlineData(JobExecutionStatus.Running, JobExecutionStatus.Success)]
    [InlineData(JobExecutionStatus.Running, JobExecutionStatus.Failed)]
    [InlineData(JobExecutionStatus.Running, JobExecutionStatus.Timeout)]
    [InlineData(JobExecutionStatus.Running, JobExecutionStatus.Canceled)]
    public void CanTransitionLegalPathsReturnTrue(JobExecutionStatus from, JobExecutionStatus to)
    {
        Assert.True(JobStateMachine.CanTransition(from, to));
    }

    [Theory]
    [InlineData(JobExecutionStatus.Pending, JobExecutionStatus.Success)]
    [InlineData(JobExecutionStatus.Pending, JobExecutionStatus.Failed)]
    [InlineData(JobExecutionStatus.Pending, JobExecutionStatus.Timeout)]
    [InlineData(JobExecutionStatus.Queued, JobExecutionStatus.Success)]
    [InlineData(JobExecutionStatus.Queued, JobExecutionStatus.Pending)]
    [InlineData(JobExecutionStatus.Running, JobExecutionStatus.Pending)]
    [InlineData(JobExecutionStatus.Running, JobExecutionStatus.Queued)]
    [InlineData(JobExecutionStatus.Success, JobExecutionStatus.Running)]
    [InlineData(JobExecutionStatus.Failed, JobExecutionStatus.Running)]
    [InlineData(JobExecutionStatus.Timeout, JobExecutionStatus.Running)]
    [InlineData(JobExecutionStatus.Canceled, JobExecutionStatus.Running)]
    public void CanTransitionIllegalPathsReturnFalse(JobExecutionStatus from, JobExecutionStatus to)
    {
        Assert.False(JobStateMachine.CanTransition(from, to));
    }

    [Theory]
    [InlineData(JobExecutionStatus.Pending)]
    [InlineData(JobExecutionStatus.Queued)]
    [InlineData(JobExecutionStatus.Running)]
    [InlineData(JobExecutionStatus.Success)]
    [InlineData(JobExecutionStatus.Failed)]
    [InlineData(JobExecutionStatus.Timeout)]
    [InlineData(JobExecutionStatus.Canceled)]
    public void CanTransitionSelfTransitionAlwaysFalse(JobExecutionStatus status)
    {
        Assert.False(JobStateMachine.CanTransition(status, status));
    }

    [Fact]
    public void AllowedTargetsTerminalStatesReturnEmptySet()
    {
        Assert.Empty(JobStateMachine.AllowedTargets(JobExecutionStatus.Success));
        Assert.Empty(JobStateMachine.AllowedTargets(JobExecutionStatus.Failed));
        Assert.Empty(JobStateMachine.AllowedTargets(JobExecutionStatus.Timeout));
        Assert.Empty(JobStateMachine.AllowedTargets(JobExecutionStatus.Canceled));
    }

    [Fact]
    public void AllowedTargetsPendingContainsThreeTargets()
    {
        var targets = JobStateMachine.AllowedTargets(JobExecutionStatus.Pending);

        Assert.Equal(3, targets.Count);
        Assert.Contains(JobExecutionStatus.Queued, targets);
        Assert.Contains(JobExecutionStatus.Running, targets);
        Assert.Contains(JobExecutionStatus.Canceled, targets);
    }

    [Fact]
    public void EnsureTransitionLegalPathDoesNotThrow()
    {
        JobStateMachine.EnsureTransition(JobExecutionStatus.Queued, JobExecutionStatus.Running);
    }

    [Fact]
    public void EnsureTransitionIllegalPathThrowsInvalidOperationException()
    {
        Assert.Throws<InvalidOperationException>(
            () => JobStateMachine.EnsureTransition(JobExecutionStatus.Pending, JobExecutionStatus.Success));
    }

    [Theory]
    [InlineData(JobExecutionStatus.Success, true)]
    [InlineData(JobExecutionStatus.Failed, true)]
    [InlineData(JobExecutionStatus.Timeout, true)]
    [InlineData(JobExecutionStatus.Canceled, true)]
    [InlineData(JobExecutionStatus.Pending, false)]
    [InlineData(JobExecutionStatus.Queued, false)]
    [InlineData(JobExecutionStatus.Running, false)]
    public void IsTerminalDistinguishesTerminalFromTransient(JobExecutionStatus status, bool expected)
    {
        Assert.Equal(expected, JobStateMachine.IsTerminal(status));
    }
}
