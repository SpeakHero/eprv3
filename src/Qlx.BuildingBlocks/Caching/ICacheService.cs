/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-20 22:00:00 +08:00 中国标准时间 星期二
 * 修改时间：2026-10-20 22:00:00 +08:00 中国标准时间 星期二
 * 描述：统一缓存服务契约，业务代码唯一依赖，提供方经配置整体替换
 */

namespace Qlx.BuildingBlocks.Caching;

/// <summary>
/// 统一缓存服务（Cache-Aside 封装）。业务代码只依赖本接口；
/// 提供方（Redis / 内存 / 其它）经 DI 与配置整体替换，业务零改动。
/// 缓存键必须遵循 <see cref="QlxCacheKey"/> 规范。
/// </summary>
public interface ICacheService
{
    /// <summary>
    /// 读取缓存。
    /// </summary>
    /// <typeparam name="T">值类型。</typeparam>
    /// <param name="key">规范缓存键。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>命中时返回值，未命中返回默认值。</returns>
    Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken);

    /// <summary>
    /// 写入缓存。
    /// </summary>
    /// <typeparam name="T">值类型。</typeparam>
    /// <param name="key">规范缓存键。</param>
    /// <param name="value">缓存值。</param>
    /// <param name="options">过期选项；为 <see langword="null"/> 时使用提供方默认。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task SetAsync<T>(string key, T value, CancellationToken cancellationToken, CacheEntryOptions? options = null);

    /// <summary>
    /// 移除指定缓存键。
    /// </summary>
    /// <param name="key">规范缓存键。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>是否实际移除。</returns>
    Task<bool> RemoveAsync(string key, CancellationToken cancellationToken);

    /// <summary>
    /// 按前缀批量移除（如移除某租户某模块全部缓存）。
    /// </summary>
    /// <param name="prefix">键前缀（含冒号边界，如 <c>tenantA:pur:</c>）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>实际移除条数。</returns>
    Task<long> RemoveByPrefixAsync(string prefix, CancellationToken cancellationToken);

    /// <summary>
    /// 读穿缓存：未命中时调用工厂加载并回写。对同一键并发调用进行合并防击穿。
    /// </summary>
    /// <typeparam name="T">值类型。</typeparam>
    /// <param name="key">规范缓存键。</param>
    /// <param name="factory">未命中时的数据加载工厂。</param>
    /// <param name="options">过期选项。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>缓存值或工厂加载值。</returns>
    Task<T> GetOrCreateAsync<T>(
        string key,
        Func<CancellationToken, Task<T>> factory,
        CancellationToken cancellationToken,
        CacheEntryOptions? options = null);
}
