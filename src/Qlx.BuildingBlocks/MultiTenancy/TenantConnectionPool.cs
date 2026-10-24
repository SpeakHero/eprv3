/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-21 05:30:00 +08:00 中国标准时间 星期三
 * 修改时间：2026-10-21 05:30:00 +08:00 中国标准时间 星期三
 * 描述：基于 SemaphoreSlim 的租户连接池闸门实现
 */

using System.Collections.Concurrent;
using Microsoft.Extensions.Options;

namespace Qlx.BuildingBlocks.MultiTenancy;

/// <summary>
/// 基于 <see cref="ConcurrentDictionary{TKey,TValue}"/> + <see cref="SemaphoreSlim"/>
/// 的租户连接池闸门。每个租户 ID 一个独立信号量，
/// 允许的最大并发数由 <see cref="TenantMigrationOptions.MaxConcurrentConnectionsPerTenant"/> 控制。
/// </summary>
/// <remarks>
/// 构造函数。
/// </remarks>
public sealed class TenantConnectionPool(IOptions<TenantMigrationOptions> options) : ITenantConnectionPool
{
    private readonly ConcurrentDictionary<long, SemaphoreSlim> _pool = new();
    private readonly int _maxConcurrent = options.Value.MaxConcurrentConnectionsPerTenant;


    /// <inheritdoc />
    public async Task<IDisposable> AcquireAsync(long tenantId, CancellationToken cancellationToken)
    {
        var semaphore = _pool.GetOrAdd(tenantId, static (_, max) => new SemaphoreSlim(max, max), _maxConcurrent);
        await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        return new Lease(semaphore);
    }

    /// <inheritdoc />
    public void Reset(long tenantId)
    {
        if (_pool.TryRemove(tenantId, out var semaphore))
        {
            semaphore.Dispose();
        }
    }

    /// <summary>
    /// 闸门令牌：Dispose 即释放信号量。
    /// </summary>
    private readonly struct Lease(SemaphoreSlim semaphore) : IDisposable
    {
        public void Dispose() => semaphore.Release();
    }
}
