/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-23 09:30:00 中国标准时间 星期五
 * 描述：消息发送状态机测试——合法链路（Queued→Sent→Read→Archived）、失败重试回路
 *       （Failed→Queued）、非法跳转拒绝、终态判定、EnsureTransition 异常
 */

using Qlx.Abstractions.Messages;
using Qlx.Modules.Support.Services.Messages;

namespace Qlx.BuildingBlocks.Tests.Messages;

/// <summary>
/// <see cref="MessageStateMachine"/> 单元测试（纯转移表）。
/// </summary>
public sealed class MessageStateMachineTests
{
    [Fact]
    public void QueuedTransitionsOnlyToSentOrFailed()
    {
        Assert.True(MessageStateMachine.CanTransition(MessageStatus.Queued, MessageStatus.Sent));
        Assert.True(MessageStateMachine.CanTransition(MessageStatus.Queued, MessageStatus.Failed));
        Assert.False(MessageStateMachine.CanTransition(MessageStatus.Queued, MessageStatus.Read));
        Assert.False(MessageStateMachine.CanTransition(MessageStatus.Queued, MessageStatus.Archived));
    }

    [Fact]
    public void SentToDeliveredToReadChainIsAllowed()
    {
        Assert.True(MessageStateMachine.CanTransition(MessageStatus.Sent, MessageStatus.Delivered));
        Assert.True(MessageStateMachine.CanTransition(MessageStatus.Sent, MessageStatus.Read));
        Assert.True(MessageStateMachine.CanTransition(MessageStatus.Delivered, MessageStatus.Read));
        Assert.False(MessageStateMachine.CanTransition(MessageStatus.Delivered, MessageStatus.Sent));
    }

    [Fact]
    public void ReadOnlyArchivesAndArchivedIsTerminal()
    {
        Assert.True(MessageStateMachine.CanTransition(MessageStatus.Read, MessageStatus.Archived));
        Assert.False(MessageStateMachine.CanTransition(MessageStatus.Archived, MessageStatus.Read));
        Assert.True(MessageStateMachine.IsTerminal(MessageStatus.Archived));
        Assert.False(MessageStateMachine.IsTerminal(MessageStatus.Read));
        Assert.Empty(MessageStateMachine.AllowedTargets(MessageStatus.Archived));
    }

    [Fact]
    public void FailedReturnsToQueuedForRetry()
    {
        Assert.True(MessageStateMachine.CanTransition(MessageStatus.Failed, MessageStatus.Queued));
        Assert.False(MessageStateMachine.CanTransition(MessageStatus.Failed, MessageStatus.Sent));
        Assert.False(MessageStateMachine.CanTransition(MessageStatus.Failed, MessageStatus.Read));
    }

    [Fact]
    public void SameStateTransitionIsRejected()
    {
        Assert.False(MessageStateMachine.CanTransition(MessageStatus.Sent, MessageStatus.Sent));
        Assert.False(MessageStateMachine.CanTransition(MessageStatus.Read, MessageStatus.Read));
    }

    [Fact]
    public void EnsureTransitionThrowsOnIllegalMove()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            MessageStateMachine.EnsureTransition(MessageStatus.Queued, MessageStatus.Read));

        Assert.Contains("非法的消息状态转移", exception.Message, StringComparison.Ordinal);
    }
}
