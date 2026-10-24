/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-20 23:00:00 +08:00 中国标准时间 星期二
 * 修改时间：2026-10-20 23:00:00 +08:00 中国标准时间 星期二
 * 描述：开发期内存版租户数据库路由解析器，生产由 Portal 模块的门户库实现覆盖
 */

using System.Collections.Concurrent;

namespace Qlx.BuildingBlocks.MultiTenancy;

/// <summary>
/// 开发期内存版租户数据库路由解析器。仅用于无门户库环境下的本地开发，
/// 通过 <see cref="Register"/> 手动注册租户连接信息。
/// 生产环境由 Portal 模块实现从门户库解密并缓存的正式版本覆盖，
/// 只需在 DI 中 <c>AddSingleton&lt;ITenantDatabaseResolver, PortalTenantDatabaseResolver&gt;()</c>
/// 覆盖本占位注册即可。
/// </summary>
public sealed class InMemoryTenantDatabaseResolver : ITenantDatabaseResolver
{
    private readonly ConcurrentDictionary<long, TenantConnectionInfo> _cache = new();

    /// <summary>
    /// 注册租户连接信息（Host 开发启动期调用）。
    /// </summary>
    public void Register(TenantConnectionInfo connectionInfo)
    {
        ArgumentNullException.ThrowIfNull(connectionInfo);
        _cache[connectionInfo.TenantId] = connectionInfo;
    }

    /// <inheritdoc />
    public TenantConnectionInfo Resolve(long tenantId)
    {
        if (_cache.TryGetValue(tenantId, out var info))
        {
            return info;
        }

        throw new InvalidOperationException($"租户 {tenantId} 连接信息未注册，请在 Host 启动期调用 {nameof(Register)}");
    }

    /// <inheritdoc />
    public Task PreloadAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <inheritdoc />
    public void Invalidate(long tenantId) => _cache.TryRemove(tenantId, out _);
}
