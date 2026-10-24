/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-20 22:00:00 +08:00 中国标准时间 星期二
 * 修改时间：2026-10-20 22:00:00 +08:00 中国标准时间 星期二
 * 描述：ID 体系（雪花 + 混淆）DI 装配扩展，按配置选择 workerId 租约提供方
 */

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Qlx.BuildingBlocks.Ids;

/// <summary>
/// ID 体系装配扩展。
/// </summary>
public static class IdsServiceCollectionExtensions
{
    /// <summary>
    /// 注册雪花 ID 生成器、workerId 租约与 Hashids 混淆器。
    /// 配置节：<c>Qlx:Ids:Snowflake</c>、<c>Qlx:Ids:Obfuscation</c>。
    /// </summary>
    /// <param name="services">服务集合。</param>
    /// <param name="configuration">配置根。</param>
    /// <returns>服务集合。</returns>
    public static IServiceCollection AddQlxIds(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<SnowflakeOptions>()
            .Bind(configuration.GetSection(SnowflakeOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<IdObfuscationOptions>()
            .Bind(configuration.GetSection(IdObfuscationOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddSingleton<IWorkerIdLease>(static provider =>
        {
            var options = provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<SnowflakeOptions>>();
            return string.Equals(options.Value.LeaseProvider, SnowflakeOptions.LeaseProviders.Redis, StringComparison.OrdinalIgnoreCase)
                ? new RedisWorkerIdLease(
                    provider.GetRequiredService<StackExchange.Redis.IConnectionMultiplexer>(),
                    options)
                : new StaticWorkerIdLease(options);
        });

        services.AddSingleton<IWorkerIdLeaseHolder, WorkerIdLeaseHolder>();
        services.AddHostedService<WorkerIdLeaseHostedService>();

        services.AddSingleton<ISnowflakeIdGenerator>(static provider =>
        {
            var holder = provider.GetRequiredService<IWorkerIdLeaseHolder>();
            if (holder.WorkerId is not { } workerId)
            {
                throw new InvalidOperationException("workerId 租约尚未完成初始化，禁止在宿主启动前解析雪花 ID 生成器");
            }

            var options = provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<SnowflakeOptions>>();
            return new SnowflakeIdGenerator(workerId, options.Value.Epoch);
        });

        services.AddSingleton<IIdObfuscator>(static provider =>
        {
            var options = provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<IdObfuscationOptions>>();
            var obfuscator = new HashidsObfuscator(options);
            IIdObfuscatorRegistry.Register(obfuscator);
            return obfuscator;
        });

        return services;
    }
}
