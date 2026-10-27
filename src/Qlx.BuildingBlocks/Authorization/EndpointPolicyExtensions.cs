/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-24
 * 描述：集中式授权映射 DI 装配入口——Host 在 AddQlxAuth 之后调用一次
 *       AddQlxEndpointPolicies 即可完成规则目录构建 + 全局约定注入接线。
 */

using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Qlx.BuildingBlocks.Authorization;

/// <summary>集中式授权映射装配扩展。</summary>
public static class EndpointPolicyExtensions
{
    /// <summary>
    /// 注册端点授权策略目录与全局授权约定。
    /// <list type="number">
    ///   <item><c>endpointPolicies</c> 配置节绑定为 <see cref="EndpointPolicyOptions"/>（JSON 覆盖层）。</item>
    ///   <item><see cref="EndpointPolicyCatalog"/> 以 Singleton 构建（合并模块基线 + JSON 覆盖 + 禁用键）。</item>
    ///   <item><see cref="EndpointPolicyMvcConfigurer"/> 挂入 <see cref="MvcOptions.Conventions"/> 首位。</item>
    /// </list>
    /// 模块基线经 <c>AddSingleton&lt;IEndpointPolicyMapProvider, XxxMap&gt;()</c> 注册，
    /// 目录按 <c>IEnumerable&lt;IEndpointPolicyMapProvider&gt;</c> 集合解析。
    /// </summary>
    /// <param name="services">服务集合。</param>
    /// <param name="configuration">宿主配置根（含四级 JSON 加载结果）。</param>
    /// <returns>服务集合。</returns>
    public static IServiceCollection AddQlxEndpointPolicies(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.Configure<EndpointPolicyOptions>(configuration.GetSection(EndpointPolicyOptions.SectionName));
        services.AddSingleton<EndpointPolicyCatalog>();
        services.AddSingleton<IConfigureOptions<MvcOptions>, EndpointPolicyMvcConfigurer>();
        return services;
    }
}
