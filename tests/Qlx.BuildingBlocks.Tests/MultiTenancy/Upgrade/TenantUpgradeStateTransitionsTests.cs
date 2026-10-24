/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-22 17:30:00 +08:00 中国标准时间 星期四
 * 修改时间：2026-10-22 17:30:00 +08:00 中国标准时间 星期四
 * 描述：租户升级状态机合法迁移白名单测试（正向路径 / 失败路径 / 重试 / 清理 / 非法迁移拒绝）
 */

using Qlx.Abstractions.Tenancy;

namespace Qlx.BuildingBlocks.Tests.MultiTenancy.Upgrade;

/// <summary>
/// <see cref="TenantUpgradeStateTransitions.CanTransition"/> 白名单测试：
/// 覆盖全部合法迁移、典型非法迁移（跳过 / 回退 / 终态不可动）。
/// </summary>
public sealed class TenantUpgradeStateTransitionsTests
{
    /// <summary>合法迁移用例。</summary>
    public static TheoryData<TenantUpgradeStatus, TenantUpgradeStatus> ValidTransitions => new()
    {
        { TenantUpgradeStatus.Pending, TenantUpgradeStatus.Preparing },
        { TenantUpgradeStatus.Preparing, TenantUpgradeStatus.Copying },
        { TenantUpgradeStatus.Preparing, TenantUpgradeStatus.Failed },
        { TenantUpgradeStatus.Copying, TenantUpgradeStatus.Verifying },
        { TenantUpgradeStatus.Copying, TenantUpgradeStatus.Failed },
        { TenantUpgradeStatus.Verifying, TenantUpgradeStatus.Cutover },
        { TenantUpgradeStatus.Verifying, TenantUpgradeStatus.Failed },
        { TenantUpgradeStatus.Cutover, TenantUpgradeStatus.Done },
        { TenantUpgradeStatus.Cutover, TenantUpgradeStatus.Failed },
        { TenantUpgradeStatus.Failed, TenantUpgradeStatus.Pending },
        { TenantUpgradeStatus.Done, TenantUpgradeStatus.Cleaned },
    };

    [Theory]
    [MemberData(nameof(ValidTransitions))]
    public void ValidTransitionsAreAccepted(TenantUpgradeStatus from, TenantUpgradeStatus to)
    {
        Assert.True(TenantUpgradeStateTransitions.CanTransition(from, to));
    }

    [Fact]
    public void PendingCanOnlyMoveToPreparing()
    {
        foreach (var status in Enum.GetValues<TenantUpgradeStatus>())
        {
            var expected = status == TenantUpgradeStatus.Preparing;
            Assert.Equal(expected, TenantUpgradeStateTransitions.CanTransition(TenantUpgradeStatus.Pending, status));
        }
    }

    [Fact]
    public void FailedCanOnlyRetryToPending()
    {
        foreach (var status in Enum.GetValues<TenantUpgradeStatus>())
        {
            var expected = status == TenantUpgradeStatus.Pending;
            Assert.Equal(expected, TenantUpgradeStateTransitions.CanTransition(TenantUpgradeStatus.Failed, status));
        }
    }

    [Fact]
    public void CleanedIsTerminalAndCannotMoveAnywhere()
    {
        foreach (var status in Enum.GetValues<TenantUpgradeStatus>())
        {
            Assert.False(TenantUpgradeStateTransitions.CanTransition(TenantUpgradeStatus.Cleaned, status));
        }
    }

    [Fact]
    public void DoneCanOnlyMoveToCleaned()
    {
        foreach (var status in Enum.GetValues<TenantUpgradeStatus>())
        {
            var expected = status == TenantUpgradeStatus.Cleaned;
            Assert.Equal(expected, TenantUpgradeStateTransitions.CanTransition(TenantUpgradeStatus.Done, status));
        }
    }

    [Fact]
    public void BackwardTransitionsAreRejected()
    {
        Assert.False(TenantUpgradeStateTransitions.CanTransition(
            TenantUpgradeStatus.Copying, TenantUpgradeStatus.Preparing));
        Assert.False(TenantUpgradeStateTransitions.CanTransition(
            TenantUpgradeStatus.Verifying, TenantUpgradeStatus.Copying));
        Assert.False(TenantUpgradeStateTransitions.CanTransition(
            TenantUpgradeStatus.Cutover, TenantUpgradeStatus.Verifying));
        Assert.False(TenantUpgradeStateTransitions.CanTransition(
            TenantUpgradeStatus.Preparing, TenantUpgradeStatus.Pending));
    }

    [Fact]
    public void SkippingStagesIsRejected()
    {
        Assert.False(TenantUpgradeStateTransitions.CanTransition(
            TenantUpgradeStatus.Pending, TenantUpgradeStatus.Copying));
        Assert.False(TenantUpgradeStateTransitions.CanTransition(
            TenantUpgradeStatus.Preparing, TenantUpgradeStatus.Verifying));
        Assert.False(TenantUpgradeStateTransitions.CanTransition(
            TenantUpgradeStatus.Copying, TenantUpgradeStatus.Done));
    }

    [Fact]
    public void SelfTransitionsAreRejected()
    {
        foreach (var status in Enum.GetValues<TenantUpgradeStatus>())
        {
            Assert.False(TenantUpgradeStateTransitions.CanTransition(status, status));
        }
    }
}
