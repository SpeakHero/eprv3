/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-20 22:00:00 +08:00 中国标准时间 星期二
 * 修改时间：2026-10-20 22:00:00 +08:00 中国标准时间 星期二
 * 描述：内存 Outbox 存储实现，供开发环境与单元测试使用
 */

using System.Collections.Concurrent;

namespace Qlx.BuildingBlocks.EventBus.Outbox;

/// <summary>
/// 内存 Outbox 存储。进程重启消息即丢失，仅供开发与单元测试；
/// 生产环境必须替换为 EF Core 持久化实现。
/// </summary>
public sealed class InMemoryOutboxStore : IOutboxStore
{
    private readonly ConcurrentDictionary<long, OutboxMessage> _messages = new();
    private readonly Lock _leaseSync = new();

    /// <inheritdoc />
    public Task EnqueueAsync(OutboxMessage message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        _messages[message.Id] = message;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<OutboxMessage>> LeaseBatchAsync(
        string owner,
        int maxCount,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(owner);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxCount);

        var now = DateTimeOffset.UtcNow;
        List<OutboxMessage> leased = [];

        lock (_leaseSync)
        {
            foreach (var message in _messages.Values.OrderBy(static m => m.AvailableAt))
            {
                if (leased.Count >= maxCount)
                {
                    break;
                }

                if (message.ProcessedAt is not null || message.AvailableAt > now)
                {
                    continue;
                }

                if (message.LeaseUntil is { } leaseUntil && leaseUntil > now)
                {
                    continue;
                }

                message.LeaseOwner = owner;
                message.LeaseUntil = now.Add(leaseDuration);
                message.AttemptCount++;
                leased.Add(message);
            }
        }

        return Task.FromResult<IReadOnlyList<OutboxMessage>>(leased);
    }

    /// <inheritdoc />
    public Task CompleteAsync(long messageId, string owner, CancellationToken cancellationToken)
    {
        if (_messages.TryGetValue(messageId, out var message) && string.Equals(message.LeaseOwner, owner, StringComparison.Ordinal))
        {
            message.ProcessedAt = DateTimeOffset.UtcNow;
            message.LeaseOwner = null;
            message.LeaseUntil = null;
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task FailAsync(long messageId, string owner, string failureReason, TimeSpan retryDelay, CancellationToken cancellationToken)
    {
        if (_messages.TryGetValue(messageId, out var message) && string.Equals(message.LeaseOwner, owner, StringComparison.Ordinal))
        {
            message.LastError = failureReason;
            message.AvailableAt = DateTimeOffset.UtcNow.Add(retryDelay);
            message.LeaseOwner = null;
            message.LeaseUntil = null;
        }

        return Task.CompletedTask;
    }
}
