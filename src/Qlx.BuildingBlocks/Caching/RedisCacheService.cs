/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-20 22:00:00 +08:00 中国标准时间 星期二
 * 修改时间：2026-10-23 00:00:00 +08:00 中国标准时间 星期五
 * 描述：基于 StackExchange.Redis 的 Redis 缓存提供方，生产多实例默认提供方
 */

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Qlx.Abstractions.Serialization;
using StackExchange.Redis;

namespace Qlx.BuildingBlocks.Caching;

/// <summary>
/// Redis 缓存提供方。值经 System.Text.Json 序列化存储；
/// 物理键统一加实例名前缀；前缀移除使用 SCAN 避免 KEYS 阻塞；
/// 并发回源采用「进程内信号量 + Redis 分布式锁」双道互斥，跨实例防击穿（C-2-2）。
/// </summary>
public sealed partial class RedisCacheService : ICacheService
{
    private static readonly JsonSerializerOptions s_serializerOptions = QlxJsonDefaults.Default;

    /// <summary>分布式锁键后缀。</summary>
    private const string LockKeySuffix = ":lock";

    /// <summary>前缀批量删除的单批大小（C-6-2：SCAN 分批 ≤500/批）。</summary>
    private const int RemoveBatchSize = 500;

    /// <summary>
    /// 空值缓存哨兵（C-3）：原始字符串存储、绕过 JSON 序列化，与任何 JSON 载荷天然不相撞。
    /// </summary>
    private const string NullSentinel = "\u0001qlx:null";

    /// <summary>空值缓存默认过期时长（30 秒，显著短于正常值 TTL）。</summary>
    private static readonly TimeSpan s_defaultNullValueExpiration = TimeSpan.FromSeconds(30);

    /// <summary>分布式锁过期时长（C-2-2：SET key:lock token NX EX 5）。</summary>
    private static readonly TimeSpan s_lockExpiry = TimeSpan.FromSeconds(5);

    /// <summary>未获锁者重读缓存的总等待预算，超时后回源但不写缓存（限流兜底）。</summary>
    private static readonly TimeSpan s_lockWaitBudget = TimeSpan.FromSeconds(2);

    private readonly IRedisCacheStore _store;
    private readonly CacheOptions _options;
    private readonly ILogger<RedisCacheService> _logger;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _keyLocks = new(StringComparer.Ordinal);

    /// <summary>
    /// 创建 Redis 缓存提供方。
    /// </summary>
    /// <param name="multiplexer">Redis 连接复用器。</param>
    /// <param name="options">缓存配置。</param>
    /// <param name="logger">日志记录器。</param>
    public RedisCacheService(IConnectionMultiplexer multiplexer, IOptions<CacheOptions> options, ILogger<RedisCacheService> logger)
        : this(new RedisDatabaseStore(multiplexer), options, logger)
    {
    }

    /// <summary>
    /// 创建 Redis 缓存提供方（内部接缝入口，供单元测试注入假存储）。
    /// </summary>
    /// <param name="store">Redis 存储接缝。</param>
    /// <param name="options">缓存配置。</param>
    /// <param name="logger">日志记录器；为 <see langword="null"/> 时使用空日志。</param>
    internal RedisCacheService(IRedisCacheStore store, IOptions<CacheOptions> options, ILogger<RedisCacheService>? logger = null)
    {
        _store = store;
        _options = options.Value;
        _logger = logger ?? NullLogger<RedisCacheService>.Instance;
    }

    /// <inheritdoc />
    public async Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);

        var (kind, value) = await ReadInternalAsync<T>(key).ConfigureAwait(false);
        return kind == CacheHitKind.HitValue ? value : default;
    }

    /// <inheritdoc />
    /// <remarks>显式写入 <see langword="null"/> 语义为缓存「不存在」结论（C-3），落空值缓存。</remarks>
    public async Task SetAsync<T>(string key, T value, CancellationToken cancellationToken, CacheEntryOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);

        if (value is null)
        {
            await WriteNullMarkerAsync(key, options).ConfigureAwait(false);
            return;
        }

        var payload = JsonSerializer.Serialize(value, s_serializerOptions);
        var expiry = CacheTtlJitter.Apply(
            options?.AbsoluteExpirationRelativeToNow ?? TimeSpan.FromSeconds(_options.DefaultExpirationSeconds),
            options?.DisableJitter == true);
        await _store.StringSetAsync(BuildPhysicalKey(key), payload, expiry).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task<bool> RemoveAsync(string key, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        return _store.KeyDeleteAsync(BuildPhysicalKey(key));
    }

    /// <inheritdoc />
    /// <remarks>
    /// SCAN 游标流式收集，每满 500 个键即批量删除并 <see cref="Task.Yield"/> 让出（C-6-2），
    /// 避免大前缀一次性删除阻塞。
    /// </remarks>
    public async Task<long> RemoveByPrefixAsync(string prefix, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(prefix);

        var pattern = $"{BuildPhysicalKey(prefix)}*";
        var batch = new List<RedisKey>(RemoveBatchSize);
        long removed = 0;

        await foreach (var key in _store.ScanKeysAsync(pattern).WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            batch.Add(key);
            if (batch.Count >= RemoveBatchSize)
            {
                removed += await _store.KeyDeleteBatchAsync([.. batch]).ConfigureAwait(false);
                batch.Clear();
                await Task.Yield();
            }
        }

        if (batch.Count > 0)
        {
            removed += await _store.KeyDeleteBatchAsync([.. batch]).ConfigureAwait(false);
        }

        return removed;
    }

    /// <inheritdoc />
    /// <remarks>
    /// 双道互斥（C-2-2）：第一道进程内键级信号量合并本实例并发；第二道 Redis 分布式锁
    /// （SET key:lock token NX EX 5）跨实例互斥。未获锁者 50~150ms 随机退避重读，
    /// 总等待上限 2s；超时回源但不写缓存（写缓存权只属于持锁者，防无锁并发写入竞态）。
    /// factory 抛异常时释放锁、异常透传、不写缓存。锁 TTL 5s 不续期，factory 耗时应小于锁 TTL。
    /// </remarks>
    public async Task<T> GetOrCreateAsync<T>(
        string key,
        Func<CancellationToken, Task<T>> factory,
        CancellationToken cancellationToken,
        CacheEntryOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        ArgumentNullException.ThrowIfNull(factory);

        var (kind, hit) = await ReadInternalAsync<T>(key).ConfigureAwait(false);
        if (kind == CacheHitKind.HitValue)
        {
            return hit!;
        }

        if (kind == CacheHitKind.HitNull)
        {
            return default!;
        }

        var keyLock = _keyLocks.GetOrAdd(key, static _ => new SemaphoreSlim(1, 1));
        await keyLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            (kind, hit) = await ReadInternalAsync<T>(key).ConfigureAwait(false);
            if (kind == CacheHitKind.HitValue)
            {
                return hit!;
            }

            if (kind == CacheHitKind.HitNull)
            {
                return default!;
            }

            var physicalLockKey = BuildPhysicalKey(key) + LockKeySuffix;
            var token = Guid.NewGuid().ToString("N");
            if (await _store.TryAcquireLockAsync(physicalLockKey, token, s_lockExpiry).ConfigureAwait(false))
            {
                try
                {
                    // 获锁后再次读取：等待信号量期间他实例可能已回填
                    (kind, hit) = await ReadInternalAsync<T>(key).ConfigureAwait(false);
                    if (kind == CacheHitKind.HitValue)
                    {
                        return hit!;
                    }

                    if (kind == CacheHitKind.HitNull)
                    {
                        return default!;
                    }

                    var created = await factory(cancellationToken).ConfigureAwait(false);
                    if (created is null)
                    {
                        // 回源结论为「不存在」：写空值缓存防穿透（C-3），TTL 显著短于正常值
                        await WriteNullMarkerAsync(key, options).ConfigureAwait(false);
                        return default!;
                    }

                    await SetAsync(key, created, cancellationToken, options).ConfigureAwait(false);
                    return created;
                }
                finally
                {
                    await _store.ReleaseLockAsync(physicalLockKey, token).ConfigureAwait(false);
                }
            }

            // 未获锁：随机退避重读，总等待上限 2s
            var deadline = Stopwatch.GetTimestamp() + (long)(s_lockWaitBudget.TotalSeconds * Stopwatch.Frequency);
            while (Stopwatch.GetTimestamp() < deadline)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(50 + Random.Shared.Next(101)), cancellationToken).ConfigureAwait(false);
                (kind, hit) = await ReadInternalAsync<T>(key).ConfigureAwait(false);
                if (kind == CacheHitKind.HitValue)
                {
                    return hit!;
                }

                if (kind == CacheHitKind.HitNull)
                {
                    return default!;
                }
            }

            // 超时：直接回源但不写缓存（限流兜底：宁可少量回源，不可雪崩）
            return await factory(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            keyLock.Release();
            _keyLocks.TryRemove(key, out _);
        }
    }

    /// <summary>
    /// 三态读取：区分命中值 / 命中空值缓存 / 未命中（空值缓存对调用方透明，仅读穿路径区分）。
    /// </summary>
    private async Task<(CacheHitKind Kind, T? Value)> ReadInternalAsync<T>(string key)
    {
        var raw = await _store.StringGetAsync(BuildPhysicalKey(key)).ConfigureAwait(false);
        if (!raw.HasValue)
        {
            return (CacheHitKind.Miss, default);
        }

        if ((string)raw! == NullSentinel)
        {
            return (CacheHitKind.HitNull, default);
        }

        try
        {
            return (CacheHitKind.HitValue, JsonSerializer.Deserialize<T>((string)raw!, s_serializerOptions));
        }
        catch (JsonException ex)
        {
            // 坏值自愈（C-5-3）：反序列化失败视为未命中，清除坏键走正常回源
            LogCorruptedValue(ex, key);
            await _store.KeyDeleteAsync(BuildPhysicalKey(key)).ConfigureAwait(false);
            return (CacheHitKind.Miss, default);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "缓存键 {Key} 反序列化失败，已清除坏值")]
    private partial void LogCorruptedValue(JsonException exception, string key);

    /// <summary>
    /// 写入空值缓存哨兵（原始字符串，绕过 JSON），TTL 取 <see cref="CacheEntryOptions.NullValueExpiration"/> 或默认 30 秒。
    /// </summary>
    private Task WriteNullMarkerAsync(string key, CacheEntryOptions? options)
    {
        var expiry = CacheTtlJitter.Apply(
            options?.NullValueExpiration ?? s_defaultNullValueExpiration,
            options?.DisableJitter == true);
        return _store.StringSetAsync(BuildPhysicalKey(key), NullSentinel, expiry);
    }

    private string BuildPhysicalKey(string key) => $"{_options.InstanceName}:{key}";
}
