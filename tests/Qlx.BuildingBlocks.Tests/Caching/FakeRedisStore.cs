/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-23 00:00:00 +08:00 中国标准时间 星期五
 * 修改时间：2026-10-23 00:00:00 +08:00 中国标准时间 星期五
 * 描述：假 Redis 存储测试桩，模拟 StringGet/StringSet（带过期）、SET NX EX 分布式锁与 SCAN，供缓存提供方确定性单测复用
 */

using System.Collections.Concurrent;
using Qlx.BuildingBlocks.Caching;
using StackExchange.Redis;

namespace Qlx.BuildingBlocks.Tests.Caching;

/// <summary>
/// 假 Redis 存储：进程内字典模拟值与锁，支持过期判定、TTL 探测与批量删除计数。
/// </summary>
internal sealed class FakeRedisStore : IRedisCacheStore
{
    private sealed record Entry(string Value, DateTimeOffset ExpiresAt);

    private readonly ConcurrentDictionary<string, Entry> _values = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, Entry> _locks = new(StringComparer.Ordinal);

    /// <summary>键是否存在（未过期）。</summary>
    public bool HasKey(string physicalKey)
        => _values.TryGetValue(physicalKey, out var entry) && entry.ExpiresAt > DateTimeOffset.UtcNow;

    /// <summary>读取原始存储值（不做 JSON 处理，供哨兵断言）。</summary>
    public string? GetRawValue(string physicalKey)
        => HasKey(physicalKey) ? _values[physicalKey].Value : null;

    /// <summary>读取键剩余 TTL；键不存在返回 null。</summary>
    public TimeSpan? GetRemainingTtl(string physicalKey)
        => HasKey(physicalKey) ? _values[physicalKey].ExpiresAt - DateTimeOffset.UtcNow : null;

    /// <summary>模拟他实例持锁。</summary>
    public void HoldLock(string physicalLockKey, string token, TimeSpan duration)
        => _locks[physicalLockKey] = new Entry(token, DateTimeOffset.UtcNow + duration);

    /// <summary>锁持有者主动释放（模拟他实例正常解锁）。</summary>
    public void ReleaseLockByOwner(string physicalLockKey, string token)
    {
        if (_locks.TryGetValue(physicalLockKey, out var existing) && existing.Value == token)
        {
            _locks.TryRemove(physicalLockKey, out _);
        }
    }

    /// <inheritdoc />
    public Task<RedisValue> StringGetAsync(string physicalKey)
    {
        if (HasKey(physicalKey))
        {
            return Task.FromResult<RedisValue>(_values[physicalKey].Value);
        }

        return Task.FromResult(RedisValue.Null);
    }

    /// <inheritdoc />
    public Task StringSetAsync(string physicalKey, string payload, TimeSpan expiry)
    {
        _values[physicalKey] = new Entry(payload, DateTimeOffset.UtcNow + expiry);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<bool> TryAcquireLockAsync(string physicalLockKey, string token, TimeSpan expiry)
    {
        var now = DateTimeOffset.UtcNow;
        var result = _locks.AddOrUpdate(
            physicalLockKey,
            static (_, state) => new Entry(state.Token, state.Now + state.Expiry),
            static (_, existing, state) => existing.ExpiresAt <= state.Now
                ? new Entry(state.Token, state.Now + state.Expiry)
                : existing,
            (Token: token, Now: now, Expiry: expiry));
        return Task.FromResult(result.Value == token);
    }

    /// <inheritdoc />
    public Task ReleaseLockAsync(string physicalLockKey, string token)
    {
        ReleaseLockByOwner(physicalLockKey, token);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<bool> KeyDeleteAsync(string physicalKey)
        => Task.FromResult(_values.TryRemove(physicalKey, out _));

    /// <inheritdoc />
    public async IAsyncEnumerable<RedisKey> ScanKeysAsync(string pattern)
    {
        var prefix = pattern.TrimEnd('*');
        foreach (var key in _values.Keys.ToArray())
        {
            if (key.StartsWith(prefix, StringComparison.Ordinal))
            {
                yield return (RedisKey)key;
            }
        }

        await Task.CompletedTask;
    }

    /// <summary>记录每次批量删除的批大小（供分批断言）。</summary>
    public List<int> DeleteBatchSizes { get; } = [];

    /// <inheritdoc />
    public Task<long> KeyDeleteBatchAsync(RedisKey[] keys)
    {
        DeleteBatchSizes.Add(keys.Length);
        long removed = 0;
        foreach (var key in keys)
        {
            if (_values.TryRemove((string)key!, out _))
            {
                removed++;
            }
        }

        return Task.FromResult(removed);
    }
}
