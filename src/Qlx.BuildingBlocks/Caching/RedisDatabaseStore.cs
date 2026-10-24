/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-23 00:00:00 +08:00 中国标准时间 星期五
 * 修改时间：2026-10-23 00:00:00 +08:00 中国标准时间 星期五
 * 描述：IRedisCacheStore 的 StackExchange.Redis 默认实现
 */

using StackExchange.Redis;

namespace Qlx.BuildingBlocks.Caching;

/// <summary>
/// 基于 <see cref="IConnectionMultiplexer"/> 的 <see cref="IRedisCacheStore"/> 默认实现。
/// </summary>
internal sealed class RedisDatabaseStore(IConnectionMultiplexer multiplexer) : IRedisCacheStore
{
    /// <summary>Lua 释放锁脚本：仅当锁持有者 token 匹配时才删除，防误删他者锁。</summary>
    private const string ReleaseLockScript =
        "if redis.call('get', KEYS[1]) == ARGV[1] then return redis.call('del', KEYS[1]) else return 0 end";

    /// <inheritdoc />
    public Task<RedisValue> StringGetAsync(string physicalKey)
        => multiplexer.GetDatabase().StringGetAsync(physicalKey);

    /// <inheritdoc />
    public Task StringSetAsync(string physicalKey, string payload, TimeSpan expiry)
        => multiplexer.GetDatabase().StringSetAsync(physicalKey, payload, expiry);

    /// <inheritdoc />
    public Task<bool> TryAcquireLockAsync(string physicalLockKey, string token, TimeSpan expiry)
        => multiplexer.GetDatabase().StringSetAsync(physicalLockKey, token, expiry, When.NotExists);

    /// <inheritdoc />
    public Task ReleaseLockAsync(string physicalLockKey, string token)
        => multiplexer.GetDatabase().ScriptEvaluateAsync(
            ReleaseLockScript,
            [(RedisKey)physicalLockKey],
            [(RedisValue)token]);

    /// <inheritdoc />
    public Task<bool> KeyDeleteAsync(string physicalKey)
        => multiplexer.GetDatabase().KeyDeleteAsync(physicalKey);

    /// <inheritdoc />
    public async IAsyncEnumerable<RedisKey> ScanKeysAsync(string pattern)
    {
        foreach (var endpoint in multiplexer.GetEndPoints())
        {
            var server = multiplexer.GetServer(endpoint);
            await foreach (var key in server.KeysAsync(pattern: pattern).ConfigureAwait(false))
            {
                yield return key;
            }
        }
    }

    /// <inheritdoc />
    public Task<long> KeyDeleteBatchAsync(RedisKey[] keys)
        => multiplexer.GetDatabase().KeyDeleteAsync(keys);
}
