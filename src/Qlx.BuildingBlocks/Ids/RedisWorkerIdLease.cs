/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-20 22:00:00 +08:00 中国标准时间 星期二
 * 修改时间：2026-10-20 22:00:00 +08:00 中国标准时间 星期二
 * 描述：基于 Redis 的 workerId 租约实现，SET NX 抢占槽位并后台自动续期
 */

using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Qlx.BuildingBlocks.Ids;

/// <summary>
/// Redis workerId 租约：按槽位顺序用 <c>SET key value NX PX</c> 抢占第一个空闲槽位，
/// 后台周期续期；释放时经 Lua 校验持有者后删除，避免误删他实例租约。
/// </summary>
/// <remarks>
/// 创建 Redis workerId 租约。
/// </remarks>
/// <param name="multiplexer">Redis 连接复用器。</param>
/// <param name="options">雪花配置。</param>
public sealed class RedisWorkerIdLease(IConnectionMultiplexer multiplexer, IOptions<SnowflakeOptions> options) : IWorkerIdLease
{
    private const string ReleaseScript = """
        if redis.call('get', KEYS[1]) == ARGV[1] then
            return redis.call('del', KEYS[1])
        else
            return 0
        end
        """;
    private readonly SnowflakeOptions _options = options.Value;
    private readonly string _leaseToken = Guid.NewGuid().ToString("N");
    private readonly CancellationTokenSource _renewalCancellation = new();

    private Task? _renewalTask;
    private string? _leaseKey;

    /// <inheritdoc />
    public long? WorkerId { get; private set; }

    /// <inheritdoc />
    public async Task<long> AcquireAsync(CancellationToken cancellationToken)
    {
        if (WorkerId is not null)
        {
            return WorkerId.Value;
        }

        var database = multiplexer.GetDatabase();
        var expiry = TimeSpan.FromSeconds(_options.LeaseSeconds);

        for (long candidate = 0; candidate <= SnowflakeIdGenerator.MaxWorkerId; candidate++)
        {
            var key = $"{_options.RedisKeyPrefix}:{candidate}";
            var acquired = await database.StringSetAsync(key, _leaseToken, expiry, When.NotExists).ConfigureAwait(false);
            if (acquired)
            {
                WorkerId = candidate;
                _leaseKey = key;
                _renewalTask = RenewLoopAsync(_renewalCancellation.Token);
                return candidate;
            }
        }

        throw new InvalidOperationException($"Redis 租约槽位已耗尽（0-{SnowflakeIdGenerator.MaxWorkerId}），无可用 workerId");
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await _renewalCancellation.CancelAsync().ConfigureAwait(false);
        if (_renewalTask is not null)
        {
            try
            {
                await _renewalTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // 续期循环正常取消
            }
        }

        if (_leaseKey is not null)
        {
            var database = multiplexer.GetDatabase();
            await database.ScriptEvaluateAsync(ReleaseScript, [_leaseKey], [_leaseToken]).ConfigureAwait(false);
        }

        WorkerId = null;
        _renewalCancellation.Dispose();
    }

    private async Task RenewLoopAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(_options.RenewIntervalSeconds));
        var database = multiplexer.GetDatabase();
        var expiry = TimeSpan.FromSeconds(_options.LeaseSeconds);

        while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
        {
            if (_leaseKey is null)
            {
                return;
            }

            // 仅当租约仍属于本实例时才续期，避免租约被抢占后误续
            var current = await database.StringGetAsync(_leaseKey).ConfigureAwait(false);
            if (current != _leaseToken)
            {
                return;
            }

            await database.KeyExpireAsync(_leaseKey, expiry).ConfigureAwait(false);
        }
    }
}
