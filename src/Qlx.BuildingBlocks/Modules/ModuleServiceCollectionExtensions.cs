/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-20 22:30:00 +08:00 中国标准时间 星期二
 * 修改时间：2026-10-20 22:30:00 +08:00 中国标准时间 星期二
 * 描述：模块装配器，启动期扫描 Qlx.Modules.*.dll、实例化 IModule 并完成服务注册
 */

using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Qlx.Abstractions.Modules;
using Qlx.BuildingBlocks.EventBus;

namespace Qlx.BuildingBlocks.Modules;

/// <summary>
/// 模块装配器。在宿主输出目录按 <c>Qlx.Modules.*.dll</c> 约定扫描模块程序集，
/// 实例化其中的 <see cref="IModule"/> 实现：清单注册为单例供许可过滤与菜单下发消费，
/// 随后调用模块服务装配，并扫描程序集内全部集成事件消费者。
/// 模块必须满足引用铁律：只允许引用 Qlx.Abstractions 与 Qlx.BuildingBlocks。
/// </summary>
public static class ModuleServiceCollectionExtensions
{
    private const string ModuleAssemblySearchPattern = "Qlx.Modules.*.dll";

    /// <summary>
    /// 发现并装配全部业务模块。模块清单以 <see cref="ModuleManifest"/> 集合单例暴露，
    /// 重复模块编码直接抛错（启动期失败优于运行时歧义）。
    /// </summary>
    /// <param name="services">服务集合。</param>
    /// <param name="configuration">分层加载后的配置根。</param>
    /// <returns>服务集合。</returns>
    /// <exception cref="InvalidOperationException">模块编码重复或模块程序集加载失败时抛出。</exception>
    public static IServiceCollection AddQlxModules(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var manifests = new List<ModuleManifest>();
        var modules = new List<IModule>();
        foreach (var assembly in LoadModuleAssemblies())
        {
            foreach (var module in CreateModuleInstances(assembly))
            {
                if (manifests.Any(m => string.Equals(m.ModuleCode, module.Manifest.ModuleCode, StringComparison.OrdinalIgnoreCase)))
                {
                    throw new InvalidOperationException($"模块编码重复：{module.Manifest.ModuleCode}");
                }

                manifests.Add(module.Manifest);
                modules.Add(module);
                module.ConfigureServices(services, configuration);
                services.AddIntegrationEventConsumers(assembly);
            }
        }

        services.AddSingleton<IReadOnlyList<ModuleManifest>>(manifests);
        services.AddSingleton<IReadOnlyList<IModule>>(modules);
        return services;
    }

    private static IEnumerable<Assembly> LoadModuleAssemblies()
    {
        var probeDirectory = AppContext.BaseDirectory;
        foreach (var file in Directory.EnumerateFiles(probeDirectory, ModuleAssemblySearchPattern, SearchOption.TopDirectoryOnly).Order(StringComparer.OrdinalIgnoreCase))
        {
            Assembly assembly;
            try
            {
                assembly = Assembly.LoadFrom(file);
            }
            catch (Exception exception) when (exception is BadImageFormatException or FileLoadException)
            {
                throw new InvalidOperationException($"模块程序集加载失败：{file}", exception);
            }

            yield return assembly;
        }
    }

    private static IEnumerable<IModule> CreateModuleInstances(Assembly assembly)
    {
        foreach (var type in assembly.GetTypes().Where(static t => t is { IsAbstract: false, IsInterface: false } && typeof(IModule).IsAssignableFrom(t)))
        {
            if (Activator.CreateInstance(type) is IModule module)
            {
                yield return module;
            }
        }
    }
}
