/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-20 22:00:00 +08:00 中国标准时间 星期二
 * 修改时间：2026-10-23 00:00:00 +08:00 中国标准时间 星期五
 * 描述：基于 IMemoryCache 的内存缓存提供方，供开发环境与单实例部署使用
 */

using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

using System.Collections.Concurrent;

namespace Qlx.BuildingBlocks.Caching;

/// <summary>
/// 内存缓存提供方。进程内有效；多实例部署请改用 Redis 提供方，避免缓存不一致。
/// 前缀移除依赖写入期登记的键集合与驱逐回调维护。
/// </summary>
/// <remarks>
/// 创建内存缓存提供方。
/// </remarks>
/// <param name="options">缓存配置。</param>
public sealed class MemoryCacheService(IOptions<CacheOptions> options) : ICacheService, IDisposable
{
    /// <summary>
    /// 空值缓存哨兵对象（C-3）：与业务值同键存储，以引用类型判别，不与任何业务载荷碰撞。
    /// </summary>
    private sealed class NullMarker
    {
        internal static readonly NullMarker Instance = new();
    }

    /// <summary>空值缓存默认过期时长（30 秒，显著短于正常值 TTL）。</summary>
    private static readonly TimeSpan s_defaultNullValueExpiration = TimeSpan.FromSeconds(30);

    private readonly MemoryCache _cache = new(new MemoryCacheOptions());
    private readonly CacheOptions _options = options.Value;
    private readonly ConcurrentDictionary<string, byte> _knownKeys = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _keyLocks = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        var (kind, value) = ReadInternal<T>(key);
        return Task.FromResult(kind == CacheHitKind.HitValue ? value : default);
    }

    /// <inheritdoc />
    /// <remarks>显式写入 <see langword="null"/> 语义为缓存「不存在」结论（C-3），落空值缓存。</remarks>
    public Task SetAsync<T>(string key, T value, CancellationToken cancellationToken, CacheEntryOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);

        if (value is null)
        {
            WriteNullMarker(key, options);
            return Task.CompletedTask;
        }

        SetEntry(key, value, CacheTtlJitter.Apply(
            options?.AbsoluteExpirationRelativeToNow ?? TimeSpan.FromSeconds(_options.DefaultExpirationSeconds),
            options?.DisableJitter == true), options?.SlidingExpiration);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<bool> RemoveAsync(string key, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        var existed = _cache.TryGetValue(key, out _);
        _cache.Remove(key);
        _ = _knownKeys.TryRemove(key, out _);
        return Task.FromResult(existed);
    }

    /// <inheritdoc />
    public Task<long> RemoveByPrefixAsync(string prefix, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(prefix);

        long removed = 0;
        foreach (var key in _knownKeys.Keys)
        {
            if (key.StartsWith(prefix, StringComparison.Ordinal))
            {
                _cache.Remove(key);
                if (_knownKeys.TryRemove(key, out _))
                {
                    removed++;
                }
            }
        }

        return Task.FromResult(removed);
    }

    /// <inheritdoc />
    public async Task<T> GetOrCreateAsync<T>(
        string key,
        Func<CancellationToken, Task<T>> factory,
        CancellationToken cancellationToken,
        CacheEntryOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        ArgumentNullException.ThrowIfNull(factory);

        var (kind, hit) = ReadInternal<T>(key);
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
            (kind, hit) = ReadInternal<T>(key);
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
                WriteNullMarker(key, options);
                return default!;
            }

            await SetAsync(key, created, cancellationToken, options).ConfigureAwait(false);
            return created;
        }
        finally
        {
            _ = keyLock.Release();
            _ = _keyLocks.TryRemove(key, out _);
        }
    }

    /// <summary>
    /// 三态读取：区分命中值 / 命中空值缓存 / 未命中（空值缓存对调用方透明，仅读穿路径区分）。
    /// </summary>
    private (CacheHitKind Kind, T? Value) ReadInternal<T>(string key)
    {
        if (!_cache.TryGetValue(key, out object? raw))
        {
            return (CacheHitKind.Miss, default);
        }

        if (raw is NullMarker)
        {
            return (CacheHitKind.HitNull, default);
        }

        return (CacheHitKind.HitValue, (T?)raw);
    }

    /// <summary>
    /// 写入空值缓存哨兵，TTL 取 <see cref="CacheEntryOptions.NullValueExpiration"/> 或默认 30 秒。
    /// </summary>
    private void WriteNullMarker(string key, CacheEntryOptions? options)
        => SetEntry(key, NullMarker.Instance, CacheTtlJitter.Apply(
            options?.NullValueExpiration ?? s_defaultNullValueExpiration,
            options?.DisableJitter == true), slidingExpiration: null);

    private void SetEntry<TValue>(string key, TValue value, TimeSpan absoluteExpiration, TimeSpan? slidingExpiration)
    {
        MemoryCacheEntryOptions entryOptions = new()
        {
            AbsoluteExpirationRelativeToNow = absoluteExpiration,
            SlidingExpiration = slidingExpiration,
        };

        entryOptions.PostEvictionCallbacks.Add(new PostEvictionCallbackRegistration
        {
            EvictionCallback = static (evictedKey, _, _, state) =>
                ((ConcurrentDictionary<string, byte>)state!).TryRemove((string)evictedKey, out _),
            State = _knownKeys,
        });

        _knownKeys[key] = 0;
        _ = _cache.Set(key, value, entryOptions);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _cache.Dispose();
        GC.SuppressFinalize(this);
    }
}
