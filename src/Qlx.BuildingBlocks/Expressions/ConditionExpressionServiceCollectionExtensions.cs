/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-22 10:00:00 +08:00 中国标准时间 星期四
 * 修改时间：2026-10-22 10:00:00 +08:00 中国标准时间 星期四
 * 描述：条件表达式引擎 DI 注册扩展（单例装配，组合期固化扩展配置）
 */

using Microsoft.Extensions.DependencyInjection;

using Qlx.Abstractions.Expressions;

namespace Qlx.BuildingBlocks.Expressions;

/// <summary>
/// 条件表达式引擎依赖注入注册扩展。
/// </summary>
public static class ConditionExpressionServiceCollectionExtensions
{
    /// <summary>
    /// 注册条件表达式引擎（单例）。
    /// </summary>
    /// <remarks>
    /// 引擎实例在注册期完成注册表快照构建，运行期完全不可变、无任何租户态，
    /// 因此单例是安全的（区别于承载业务状态、需 Scoped 的业务 Service）。
    /// 宿主服务经 <see cref="ConditionEngineOptions.Services"/> 注入，
    /// 自定义函数 / 运算符经求值上下文访问。
    /// </remarks>
    /// <param name="services">服务集合。</param>
    /// <param name="configure">组合期配置回调（回调第二参数为宿主服务提供程序，可在组合期解析服务）。</param>
    /// <returns>服务集合（链式调用）。</returns>
    public static IServiceCollection AddQlxConditionExpressions(
        this IServiceCollection services,
        Action<ConditionEngineOptions, IServiceProvider> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        services.AddSingleton<IConditionExpressionEngine>(serviceProvider =>
        {
            var options = new ConditionEngineOptions { Services = serviceProvider };
            configure(options, serviceProvider);
            return new ConditionExpressionEngine(options);
        });

        return services;
    }

    /// <summary>
    /// 注册条件表达式引擎（单例，无需访问宿主服务提供程序的简化重载）。
    /// </summary>
    /// <param name="services">服务集合。</param>
    /// <param name="configure">组合期配置回调（可选，用于注册业务扩展）。</param>
    /// <returns>服务集合（链式调用）。</returns>
    public static IServiceCollection AddQlxConditionExpressions(
        this IServiceCollection services,
        Action<ConditionEngineOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        return services.AddQlxConditionExpressions((options, _) => configure?.Invoke(options));
    }
}
