/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-20 22:30:00 +08:00 中国标准时间 星期二
 * 修改时间：2026-10-20 22:30:00 +08:00 中国标准时间 星期二
 * 描述：分层引用铁律守护测试，基于程序集引用清单精确断言依赖方向
 */

using Qlx.Abstractions.Modules;
using Qlx.BuildingBlocks.Caching;

using System.Reflection;

namespace Qlx.ArchitectureTests;

/// <summary>
/// 分层守护：以程序集引用清单为判据（模块为独立程序集，耦合必然表现为程序集引用），
/// 规避字符串字面量 / XML 注释等非类型耦合造成的误报。
/// </summary>
public sealed class LayeringTests
{
    private static readonly Assembly s_abstractionsAssembly = typeof(IModule).Assembly;
    private static readonly Assembly s_buildingBlocksAssembly = typeof(ICacheService).Assembly;

    [Fact]
    public void AbstractionsMustNotReferenceBuildingBlocksHostOrModules()
    {
        string[] referenced = ReferencedQlxAssemblies(s_abstractionsAssembly);

        Assert.DoesNotContain(referenced, static name =>
            name.StartsWith("Qlx.BuildingBlocks", StringComparison.Ordinal)
            || name.StartsWith("Qlx.Host", StringComparison.Ordinal)
            || name.StartsWith("Qlx.Modules", StringComparison.Ordinal));
    }

    [Fact]
    public void BuildingBlocksMustNotReferenceModulesOrHost()
    {
        string[] referenced = ReferencedQlxAssemblies(s_buildingBlocksAssembly);

        Assert.DoesNotContain(referenced, static name =>
            name.StartsWith("Qlx.Modules", StringComparison.Ordinal)
            || name.StartsWith("Qlx.Host", StringComparison.Ordinal));
    }

    [Fact]
    public void ModuleAssembliesMustNotReferenceEachOtherHostOrProviderSdks()
    {
        // 模块程序集按命名约定 Qlx.Modules.*.dll 发现；当前尚无模块，规则先行落地，
        // 后续任一模块落地即自动纳入守护
        foreach (string moduleName in EnumerateModuleNames())
        {
            var assembly = Assembly.Load(new AssemblyName(moduleName));
            string[] referenced = [.. assembly.GetReferencedAssemblies()
                .Select(static name => name.Name)
                .Where(static name => name is not null)
                .Select(static name => name!)];

            Assert.DoesNotContain(referenced, name =>
                (name.StartsWith("Qlx.Modules", StringComparison.Ordinal) && !string.Equals(name, moduleName, StringComparison.Ordinal))
                || name.StartsWith("Qlx.Host", StringComparison.Ordinal)
                || string.Equals(name, "StackExchange.Redis", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void ModuleAssembliesMustNotReferenceHangfire()
    {
        // 后台任务红线：Hangfire 仅允许出现在 Qlx.BuildingBlocks（IJobBackgroundEnqueuer 唯一封装点），
        // 模块一律经 IJobScheduler / IJobBackgroundEnqueuer 抽象入队，禁止直引 Hangfire.* 程序集
        foreach (string moduleName in EnumerateModuleNames())
        {
            var assembly = Assembly.Load(new AssemblyName(moduleName));
            string[] referenced = [.. assembly.GetReferencedAssemblies()
                .Select(static name => name.Name)
                .Where(static name => name is not null)
                .Select(static name => name!)];

            Assert.DoesNotContain(referenced, static name =>
                name.StartsWith("Hangfire", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void IntegrationEventConsumersMustBePublicForOutboxDynamicDispatch()
    {
        // OutboxDispatcher 以 dynamic 跨程序集调用 ConsumeAsync；非公开实现类会被运行时绑定器
        // 降级为 object（RuntimeBinderException），导致事件派发恒失败。消费者实现类必须 public。
        var consumerOpenType = typeof(Qlx.Abstractions.Events.IIntegrationEventConsumer<>);
        foreach (string moduleName in EnumerateModuleNames())
        {
            var assembly = Assembly.Load(new AssemblyName(moduleName));
            var consumers = assembly.GetTypes()
                .Where(static type => !type.IsAbstract && !type.IsInterface)
                .Where(type => type.GetInterfaces().Any(contract =>
                    contract.IsGenericType
                    && string.Equals(
                        contract.GetGenericTypeDefinition().FullName,
                        consumerOpenType.FullName,
                        StringComparison.Ordinal)))
                .ToArray();

            Assert.DoesNotContain(consumers, static type => !type.IsPublic);
        }
    }

    private static string[] EnumerateModuleNames()
    {
        return [.. Directory
                .EnumerateFiles(AppContext.BaseDirectory, "Qlx.Modules.*.dll", SearchOption.TopDirectoryOnly)
                .Select(static file => Path.GetFileNameWithoutExtension(file))];
    }

    private static string[] ReferencedQlxAssemblies(Assembly assembly)
    {
        return [.. assembly.GetReferencedAssemblies()
            .Select(static name => name.Name)
            .Where(static name => name is not null && name.StartsWith("Qlx.", StringComparison.Ordinal))
            .Select(static name => name!)];
    }
}
