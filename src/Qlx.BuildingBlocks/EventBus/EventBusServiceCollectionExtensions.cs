/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-20 22:00:00 +08:00 中国标准时间 星期二
 * 修改时间：2026-10-20 22:00:00 +08:00 中国标准时间 星期二
 * 描述：事件总线 DI 装配扩展，含消费者程序集扫描与 Outbox 调度器注册
 */

using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Qlx.Abstractions.Events;
using Qlx.BuildingBlocks.EventBus.Outbox;

namespace Qlx.BuildingBlocks.EventBus;

/// <summary>
/// 事件总线装配扩展。
/// </summary>
public static class EventBusServiceCollectionExtensions
{
    /// <summary>
    /// 注册事件总线、Outbox 存储与后台调度器。配置节 <c>Qlx:EventBus:Outbox</c>。
    /// 默认使用内存 Outbox 存储；生产环境经 EF Core 持久化实现替换。
    /// </summary>
    /// <param name="services">服务集合。</param>
    /// <param name="configuration">配置根。</param>
    /// <returns>服务集合。</returns>
    public static IServiceCollection AddQlxEventBus(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<OutboxOptions>()
            .Bind(configuration.GetSection(OutboxOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddSingleton<IOutboxStore, InMemoryOutboxStore>();
        services.AddSingleton<IEventBus, InProcessEventBus>();

        // 强类型绑定后读取开关，禁止裸读配置字符串键
        if (configuration.GetSection(OutboxOptions.SectionName).Get<OutboxOptions>()?.Enabled ?? true)
        {
            services.AddHostedService<OutboxDispatcher>();
        }

        return services;
    }

    /// <summary>
    /// 扫描程序集中全部 <see cref="IIntegrationEventConsumer{TEvent}"/> 实现并以 Scoped 注册。
    /// 模块装配器装载模块程序集时调用。
    /// </summary>
    /// <param name="services">服务集合。</param>
    /// <param name="assembly">待扫描的模块程序集。</param>
    /// <returns>服务集合。</returns>
    public static IServiceCollection AddIntegrationEventConsumers(this IServiceCollection services, Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        var consumerDefinitions = assembly.GetTypes()
            .Where(static type => type is { IsAbstract: false, IsInterface: false, IsGenericTypeDefinition: false })
            .SelectMany(static type => type.GetInterfaces()
                .Where(static i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IIntegrationEventConsumer<>))
                .Select(i => (ServiceType: i, ImplementationType: type)));

        foreach (var (serviceType, implementationType) in consumerDefinitions)
        {
            services.AddScoped(serviceType, implementationType);
        }

        return services;
    }
}
