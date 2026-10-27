/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-23 00:00:00 +08:00 中国标准时间 星期五
 * 修改时间：2026-10-23 00:00:00 +08:00 中国标准时间 星期五
 * 描述：缓存唯一入口（C-1-1）架构守护：业务模块禁止直引缓存提供方 SDK、禁止声明提供方类型依赖
 */

using System.Reflection;

namespace Qlx.ArchitectureTests;

/// <summary>
/// 缓存唯一入口守护（规则文件 v1.2 C-1-1 / E-2-1）：
/// Qlx.Modules.* 程序集不得引用缓存提供方 SDK，模块内类型不得声明
/// IConnectionMultiplexer / IMemoryCache / IDistributedCache 依赖（构造参数 / 字段扫描）。
/// Qlx.BuildingBlocks 基础设施（RedisCacheService / RedisWorkerIdLease 等）为白名单，不在本断言范围。
/// </summary>
public sealed class CacheGuardArchitectureTests
{
    /// <summary>模块程序集禁止引用的缓存提供方 SDK 程序集名。</summary>
    private static readonly string[] s_bannedAssemblyNames =
    [
        "StackExchange.Redis",
        "Microsoft.Extensions.Caching.Memory",
        "Microsoft.Extensions.Caching.Abstractions",
    ];

    /// <summary>模块类型禁止声明依赖的提供方类型全名。</summary>
    private static readonly string[] s_bannedTypeNames =
    [
        "StackExchange.Redis.IConnectionMultiplexer",
        "Microsoft.Extensions.Caching.Memory.IMemoryCache",
        "Microsoft.Extensions.Caching.Abstractions.IDistributedCache",
    ];

    [Fact]
    public void ModuleAssembliesMustNotReferenceCacheProviderSdks()
    {
        foreach (var moduleName in EnumerateModuleNames())
        {
            var assembly = Assembly.Load(new AssemblyName(moduleName));
            var referenced = assembly.GetReferencedAssemblies()
                .Select(static name => name.Name)
                .Where(static name => name is not null)
                .Select(static name => name!)
                .ToArray();

            Assert.DoesNotContain(referenced, name =>
                s_bannedAssemblyNames.Any(banned => string.Equals(name, banned, StringComparison.Ordinal)));
        }
    }

    [Fact]
    public void ModuleTypesMustNotDeclareCacheProviderDependencies()
    {
        foreach (var moduleName in EnumerateModuleNames())
        {
            var assembly = Assembly.Load(new AssemblyName(moduleName));
            foreach (var type in assembly.GetTypes())
            {
                var dependencies = type.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                    .SelectMany(static ctor => ctor.GetParameters())
                    .Select(static parameter => parameter.ParameterType)
                    .Concat(type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
                        .Select(static field => field.FieldType));

                Assert.DoesNotContain(dependencies, dependency =>
                    dependency.FullName is not null
                    && s_bannedTypeNames.Any(banned => string.Equals(dependency.FullName, banned, StringComparison.Ordinal)));
            }
        }
    }

    private static string[] EnumerateModuleNames()
        => [.. Directory
            .EnumerateFiles(AppContext.BaseDirectory, "Qlx.Modules.*.dll", SearchOption.TopDirectoryOnly)
            .Select(static file => Path.GetFileNameWithoutExtension(file))];
}
