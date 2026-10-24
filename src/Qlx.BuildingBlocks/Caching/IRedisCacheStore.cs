/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-23 00:00:00 +08:00 中国标准时间 星期五
 * 修改时间：2026-10-23 00:00:00 +08:00 中国标准时间 星期五
 * 描述：Redis 存储接缝（内部），隔离 StackExchange.Redis 依赖，便于缓存提供方确定性单测
 */

using StackExchange.Redis;

namespace Qlx.BuildingBlocks.Caching;

/// <summary>
/// Redis 存储接缝（仅 <see cref="RedisCacheService"/> 使用）。
/// 隔离 <see cref="StackExchange.Redis.IDatabase"/> 具体依赖，使分布式锁、
/// 空值缓存、分批删除等逻辑可在无真实 Redis 环境下确定性单测。
/// </summary>
internal interface IRedisCacheStore
{
    /// <summary>读取物理键对应的字符串值；不存在返回 <see cref="RedisValue.Null"/>。</summary>
    Task<RedisValue> StringGetAsync(string physicalKey);

    /// <summary>写入字符串值并设置绝对过期。</summary>
    Task StringSetAsync(string physicalKey, string payload, TimeSpan expiry);

    /// <summary>尝试获取分布式锁（SET key token NX EX）。</summary>
    /// <returns>是否成功获锁。</returns>
    Task<bool> TryAcquireLockAsync(string physicalLockKey, string token, TimeSpan expiry);

    /// <summary>释放分布式锁（Lua 校验 token 后删除，防误删他者锁）。</summary>
    Task ReleaseLockAsync(string physicalLockKey, string token);

    /// <summary>删除单个物理键。</summary>
    Task<bool> KeyDeleteAsync(string physicalKey);

    /// <summary>SCAN 流式枚举匹配模式的物理键（禁止 KEYS）。</summary>
    IAsyncEnumerable<RedisKey> ScanKeysAsync(string pattern);

    /// <summary>批量删除物理键。</summary>
    Task<long> KeyDeleteBatchAsync(RedisKey[] keys);
}
