/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-26 23:10:00 +08:00 中国标准时间 星期一
 * 修改时间：2026-10-26 23:10:00 +08:00 中国标准时间 星期一
 * 描述：服务校验代理 DI 注册扩展——一行注册完成「实现 + ValidationDispatchProxy 接口包装」，方法入口自动校验 DTO
 */

using Microsoft.Extensions.DependencyInjection;

namespace Qlx.BuildingBlocks.Validation;

/// <summary>
/// 服务校验代理 DI 注册扩展：以 <see cref="ValidationDispatchProxy{T}"/> 包装服务接口，
/// 调用方解析接口即得代理，方法入口自动完成 DTO 校验，无需在服务实现内手写 ValidateOrThrow。
/// </summary>
public static class ValidationServiceCollectionExtensions
{
    /// <summary>
    /// 以 Scoped 生命周期注册 <typeparamref name="TImplementation"/>，并将 <typeparamref name="TService"/>
    /// 注册为包裹实现的校验代理（代理持有当前作用域的 <see cref="IServiceProvider"/>，供 DTO 校验解析 DI 服务）。
    /// </summary>
    /// <typeparam name="TService">服务接口（DispatchProxy 仅支持接口代理）。</typeparam>
    /// <typeparam name="TImplementation">服务实现类型。</typeparam>
    /// <param name="services">服务集合。</param>
    /// <returns>服务集合（链式）。</returns>
    /// <remarks>实现类型仍以自身注册，直接解析 <typeparamref name="TImplementation"/> 不经过校验代理（仅内部场景使用）。</remarks>
    public static IServiceCollection AddValidatedScoped<TService, TImplementation>(this IServiceCollection services)
        where TService : class
        where TImplementation : class, TService
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddScoped<TImplementation>();
        services.AddScoped<TService>(sp =>
            ValidationDispatchProxy<TService>.Create(sp.GetRequiredService<TImplementation>(), sp));
        return services;
    }
}
