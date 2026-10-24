/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-20 22:00:00 +08:00 中国标准时间 星期二
 * 修改时间：2026-10-20 22:00:00 +08:00 中国标准时间 星期二
 * 描述：缓存体系 DI 装配扩展，按配置 provider 节整体替换提供方
 */

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using StackExchange.Redis;

namespace Qlx.BuildingBlocks.Caching;

/// <summary>
/// 缓存体系装配扩展。
/// </summary>
public static class CachingServiceCollectionExtensions
{
    /// <summary>
    /// 注册统一缓存服务。配置节 <c>Qlx:Cache</c>；
    /// <c>Provider</c> 为 <c>Redis</c> 时同时注册 <see cref="IConnectionMultiplexer"/> 单例。
    /// </summary>
    /// <param name="services">服务集合。</param>
    /// <param name="configuration">配置根。</param>
    /// <returns>服务集合。</returns>
    /// <exception cref="InvalidOperationException">Redis 提供方缺少连接串时抛出。</exception>
    public static IServiceCollection AddQlxCaching(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        _ = services.AddOptions<CacheOptions>()
            .Bind(configuration.GetSection(CacheOptions.SectionName))
            .ValidateDataAnnotations()
            .Validate(
                static options => !string.Equals(options.Provider, CacheOptions.Providers.Redis, StringComparison.OrdinalIgnoreCase)
                    || !string.IsNullOrWhiteSpace(options.RedisConnectionString),
                "Provider 为 Redis 时必须提供 RedisConnectionString")
            .ValidateOnStart();

        // 强类型绑定后读取提供方，禁止裸读配置字符串键
        string provider = configuration.GetSection(CacheOptions.SectionName).Get<CacheOptions>()?.Provider
            ?? CacheOptions.Providers.Redis;

        if (string.Equals(provider, CacheOptions.Providers.Redis, StringComparison.OrdinalIgnoreCase))
        {
            _ = services.AddSingleton<IConnectionMultiplexer>(static serviceProvider =>
            {
                var options = serviceProvider.GetRequiredService<IOptions<CacheOptions>>().Value;
                return ConnectionMultiplexer.Connect(options.RedisConnectionString!);
            });
            _ = services.AddSingleton<ICacheService, RedisCacheService>();
        }
        else
        {
            _ = services.AddSingleton<ICacheService, MemoryCacheService>();
        }

        return services;
    }
}
