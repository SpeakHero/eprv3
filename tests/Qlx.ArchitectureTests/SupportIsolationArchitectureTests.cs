/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-22 22:45:00 +08:00 中国标准时间 星期四
 * 描述：B-0 隔离基线架构测试五断言（Support 引用边界 / 业务模块不引用 Support / 门户与租户库表前缀归属 / 跨库 JOIN 为 0）
 */

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

using Qlx.BuildingBlocks.MultiTenancy;
using Qlx.Modules.Portal;

using System.Reflection;

namespace Qlx.ArchitectureTests;

/// <summary>
/// B-0 隔离基线五断言（方案 §4.5）：
/// ① Support 只引用 Abstractions/BuildingBlocks；
/// ② 业务模块（Bas/Portal）不引用 Support；
/// ③ 门户库表无 apr_/msg_/job_/fil_/prt_ 前缀；
/// ④ 租户库无 por_/plog_/glb_ 前缀；
/// ⑤ 跨库 JOIN 为 0。
/// </summary>
public sealed class SupportIsolationArchitectureTests
{
    private static readonly string[] s_portalForbiddenSupportPrefixes = ["apr_", "msg_", "job_", "fil_", "prt_"];

    private static readonly string[] s_tenantForbiddenPortalPrefixes = ["por_", "plog_", "glb_"];

    /// <summary>断言①：Support 程序集只允许引用 Abstractions / BuildingBlocks。</summary>
    [Fact]
    public void SupportAssemblyMustOnlyReferenceAbstractionsAndBuildingBlocks()
    {
        Assembly supportAssembly = LoadSupportAssembly();
        string[] referenced = ReferencedAssemblies(supportAssembly);

        foreach (string reference in referenced)
        {
            bool allowed = string.Equals(reference, "Qlx.Abstractions", StringComparison.Ordinal)
                || string.Equals(reference, "Qlx.BuildingBlocks", StringComparison.Ordinal)
                || reference.StartsWith("System", StringComparison.Ordinal)
                || reference.StartsWith("Microsoft", StringComparison.Ordinal);

            Assert.True(allowed, $"Support 程序集禁止引用 {reference}，只允许 Qlx.Abstractions / Qlx.BuildingBlocks。");
        }
    }

    /// <summary>断言②：业务模块（Bas / Portal）禁止引用 Support。</summary>
    [Fact]
    public void BusinessModulesMustNotReferenceSupport()
    {
        Assembly[] businessAssemblies =
        [
            Assembly.Load("Qlx.Modules.Bas"),
            Assembly.Load("Qlx.Modules.Portal"),
        ];

        foreach (Assembly? assembly in businessAssemblies)
        {
            string[] referenced = ReferencedAssemblies(assembly);
            Assert.DoesNotContain(
                referenced,
                static name => string.Equals(name, "Qlx.Modules.Support", StringComparison.Ordinal));
        }
    }

    /// <summary>断言③：门户库（PortalDbContext）表名禁止侵占支撑域前缀。</summary>
    [Fact]
    public void PortalTablesMustNotUseSupportPrefixes()
    {
        DbContextOptions<PortalDbContext> options = new DbContextOptionsBuilder<PortalDbContext>()
            .UseSqlServer("Server=stub;Database=stub;Integrated Security=true;Encrypt=false")
            .Options;

        using PortalDbContext portalContext = new(options);
        foreach (IEntityType entityType in portalContext.Model.GetEntityTypes())
        {
            string? tableName = entityType.GetTableName();
            Assert.False(s_portalForbiddenSupportPrefixes.Any(p => tableName!.StartsWith(p, StringComparison.Ordinal)), $"门户库表 {tableName} 禁止侵占支撑域前缀（{string.Join(" / ", s_portalForbiddenSupportPrefixes)}）。");
        }
    }

    /// <summary>断言④：租户库全部表禁止侵占门户域前缀（por_/plog_/glb_）。</summary>
    [Fact]
    public void TenantTablesMustNotUsePortalPrefixes()
    {
        foreach (TenantDbContext context in CreateTenantDbContexts())
        {
            using (context)
            {
                foreach (IEntityType entityType in context.Model.GetEntityTypes())
                {
                    string? tableName = entityType.GetTableName();
                    Assert.False(s_tenantForbiddenPortalPrefixes.Any(p => tableName!.StartsWith(p, StringComparison.Ordinal)), $"{context.GetType().Name} 表 {tableName} 禁止侵占门户域前缀（{string.Join(" / ", s_tenantForbiddenPortalPrefixes)}）。");
                }
            }
        }
    }

    /// <summary>
    /// 断言⑤：跨库 JOIN 为 0。
    /// 等价静态断言：每个租户 DbContext 映射的实体类型必须全部声明在该模块自身程序集内，
    /// 门户上下文同理——不同库的实体类型不可能出现在同一模型中，EF 无法生成跨库 JOIN，
    /// 模块间数据访问只能走 Qlx.Abstractions 同步查询接口（方案 §4.5 注明：静态命名约定等价断言）。
    /// 门户模型放行 OpenIddict 第三方实体（OpenIddict 表归属门户库为架构既定，见多租户数据库模式补充设计 §3.1）。
    /// </summary>
    [Fact]
    public void CrossDatabaseJoinsMustBeZero()
    {
        IModel portalModel = CreatePortalModel();
        foreach (IEntityType entityType in portalModel.GetEntityTypes())
        {
            string? assemblyName = entityType.ClrType.Assembly.GetName().Name;
            Assert.True(
                string.Equals(assemblyName, "Qlx.Modules.Portal", StringComparison.Ordinal)
                || assemblyName!.StartsWith("OpenIddict", StringComparison.Ordinal),
                $"门户模型映射了外部程序集实体 {entityType.ClrType.FullName}（{assemblyName}），可能构成跨库 JOIN。");
        }

        foreach (TenantDbContext context in CreateTenantDbContexts())
        {
            using (context)
            {
                string? expectedAssembly = context.GetType().Assembly.GetName().Name;
                foreach (IEntityType entityType in context.Model.GetEntityTypes())
                {
                    Assert.True(
                        string.Equals(entityType.ClrType.Assembly.GetName().Name, expectedAssembly, StringComparison.Ordinal),
                        $"{context.GetType().Name} 映射了外部程序集实体 {entityType.ClrType.FullName}，可能构成跨库 JOIN。");
                }
            }
        }
    }

    private static Assembly LoadSupportAssembly()
    {
        // Support 程序集经输出目录约定加载（与 LayeringTests 同一发现机制）
        return Assembly.Load("Qlx.Modules.Support");
    }

    private static IModel CreatePortalModel()
    {
        DbContextOptions<PortalDbContext> options = new DbContextOptionsBuilder<PortalDbContext>()
            .UseSqlServer("Server=stub;Database=stub;Integrated Security=true;Encrypt=false")
            .Options;

        using PortalDbContext context = new(options);
        return context.Model;
    }

    private static List<TenantDbContext> CreateTenantDbContexts()
    {
        Type tenantContextType = typeof(TenantDbContext);
        List<TenantDbContext> result = [];
        SupportStubTenantDatabaseResolver stubResolver = new();

        string[] assemblyNames = [.. Directory
            .EnumerateFiles(AppContext.BaseDirectory, "Qlx.Modules.*.dll", SearchOption.TopDirectoryOnly)
            .Select(static file => Path.GetFileNameWithoutExtension(file))
            .OrderBy(static name => name, StringComparer.Ordinal)];

        foreach (string? assemblyName in assemblyNames)
        {
            Assembly assembly = Assembly.Load(new AssemblyName(assemblyName));
            foreach (Type type in GetSafelyLoadableTypes(assembly))
            {
                if (type.IsAbstract || !tenantContextType.IsAssignableFrom(type) || type == tenantContextType)
                {
                    continue;
                }

                DbContextOptions options = new DbContextOptionsBuilder().Options;
                TenantDbContext context = (TenantDbContext)Activator.CreateInstance(type, stubResolver, 1L, options)!;
                result.Add(context);
            }
        }

        Assert.True(result.Count > 0, "未发现任何 TenantDbContext 子类，租户库架构测试失效。");
        return result;
    }

    private static Type[] GetSafelyLoadableTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            return [.. ex.Types.Where(static t => t is not null).Cast<Type>()];
        }
    }

    private static string[] ReferencedAssemblies(Assembly assembly)
    {
        return [.. assembly.GetReferencedAssemblies()
            .Select(static name => name.Name)
            .Where(static name => !string.IsNullOrWhiteSpace(name))
            .Select(static name => name!)];
    }

    /// <summary>架构测试专用桩解析器，仅用于构建 EF 模型，不建立真实连接。</summary>
    private sealed class SupportStubTenantDatabaseResolver : ITenantDatabaseResolver
    {
        public TenantConnectionInfo Resolve(long tenantId)
        {
            return new(tenantId, "stub", 0, "stub", string.Empty, string.Empty);
        }

        public Task PreloadAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public void Invalidate(long tenantId)
        {
        }
    }
}
