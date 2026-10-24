/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-23 05:45:00 +08:00 中国标准时间 星期五
 * 描述：SSE 基座 DI 装配扩展（AddQlxSse）：强类型 Options + 连接管理器单例（双接口同实例）
 */

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Qlx.Abstractions.Sse;

namespace Qlx.BuildingBlocks.Sse;

/// <summary>SSE 基座装配扩展。</summary>
public static class SseServiceCollectionExtensions
{
    /// <summary>
    /// 注册 SSE 基座：<see cref="QlxSseOptions"/>（Qlx:Sse 节，ValidateOnStart）+
    /// <see cref="SseConnectionManager"/> 单例（同时承载 <see cref="ISseConnectionManager"/> 与
    /// <see cref="ISseClientChannel"/>，业务侧与端点侧解析到同一实例）。
    /// </summary>
    /// <param name="services">服务集合。</param>
    /// <returns>服务集合（链式调用）。</returns>
    public static IServiceCollection AddQlxSse(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddOptions<QlxSseOptions>()
            .BindConfiguration(QlxSseOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.TryAddSingleton<SseConnectionManager>();
        services.TryAddSingleton<ISseConnectionManager>(static sp => sp.GetRequiredService<SseConnectionManager>());
        services.TryAddSingleton<ISseClientChannel>(static sp => sp.GetRequiredService<SseConnectionManager>());
        return services;
    }
}
