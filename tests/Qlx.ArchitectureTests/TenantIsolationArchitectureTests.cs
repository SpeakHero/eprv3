/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-22 00:00:00 +08:00 中国标准时间 星期四
 * 描述：多租户隔离红线架构测试（实体 TenantId / 唯一索引 / 历史表命名 / 表归属 / 禁重写 OnConfiguring）
 */

using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Qlx.BuildingBlocks.MultiTenancy;
using Qlx.Modules.Portal;

namespace Qlx.ArchitectureTests;

/// <summary>
/// 多租户隔离红线守护测试。按设计文档 §9 的 S-06 要求，
/// 用反射与 EF 模型元数据在 CI 层拦截共享库串数据风险：
/// ① 租户库实体必须含 TenantId；
/// ② 租户库唯一索引必须含 TenantId；
/// ③ 模块历史表命名必须遵循 <c>__EFMigrationsHistory_{module}</c>；
/// ④ <see cref="TenantDbContext"/> 子类禁止重写 OnConfiguring；
/// ⑤ 门户库 / 租户库表名必须归属各自前缀，禁止跨库侵占。
/// </summary>
public sealed partial class TenantIsolationArchitectureTests
{
    private static readonly Assembly[] s_moduleAssemblies = LoadQlxModuleAssemblies();

    /// <summary>模块编码格式（源生成预编译，200ms 匹配超时）。</summary>
    [GeneratedRegex(@"^[a-z0-9_]+$", RegexOptions.Compiled | RegexOptions.CultureInvariant, 200)]
    private static partial Regex ModuleCodeRegex();

    private static readonly string[] s_portalAllowedPrefixes = ["por_", "plog_"];
    private static readonly string[] s_portalForbiddenPrefixes = ["bas_", "glb_", "log_"];
    private static readonly string[] s_tenantForbiddenPrefixes = ["por_", "plog_", "glb_", "log_"];

    [Fact]
    public void TenantDbContextSubclassesMustNotOverrideOnConfiguring()
    {
        var tenantContextType = typeof(TenantDbContext);

        foreach (var assembly in s_moduleAssemblies)
        {
            foreach (var type in GetSafelyLoadableTypes(assembly))
            {
                if (type.IsAbstract || !tenantContextType.IsAssignableFrom(type) || type == tenantContextType)
                {
                    continue;
                }

                var overrideMethod = type.GetMethod(
                    "OnConfiguring",
                    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);

                Assert.True(
                    overrideMethod is null,
                    $"{type.FullName} 禁止重写 OnConfiguring；连接串必须由基类按租户路由解析。");
            }
        }
    }

    [Fact]
    public void TenantDbContextModelEntitiesMustHaveTenantIdProperty()
    {
        foreach (var context in CreateTenantDbContexts())
        {
            using (context)
            {
                var model = context.Model;
                Assert.NotNull(model);

                foreach (var entityType in model.GetEntityTypes())
                {
                    var tenantIdProperty = entityType.FindProperty("TenantId");
                    Assert.True(
                        tenantIdProperty is not null,
                        $"{context.GetType().Name} 的实体 {entityType.ClrType.Name} 缺少 TenantId 属性，共享库下会串数据。");
                    Assert.True(
                        tenantIdProperty!.ClrType == typeof(long),
                        $"{context.GetType().Name} 的实体 {entityType.ClrType.Name} 的 TenantId 必须是 long。");
                }
            }
        }
    }

    [Fact]
    public void TenantDbContextModelUniqueIndexesMustContainTenantId()
    {
        foreach (var context in CreateTenantDbContexts())
        {
            using (context)
            {
                var model = context.Model;
                Assert.NotNull(model);

                foreach (var entityType in model.GetEntityTypes())
                {
                    foreach (var index in entityType.GetIndexes())
                    {
                        if (!index.IsUnique)
                        {
                            continue;
                        }

                        var containsTenantId = index.Properties.Any(static p => p.Name == "TenantId");
                        Assert.True(
                            containsTenantId,
                            $"{context.GetType().Name} 的实体 {entityType.ClrType.Name} 唯一索引缺少 TenantId，共享库下会跨租户冲突。");
                    }
                }
            }
        }
    }

    [Fact]
    public void TenantMigrationPlanMustFollowModuleCodeAndHistoryTableNaming()
    {
        var planInterfaceType = typeof(ITenantMigrationPlan);
        var discoveredPlans = 0;

        foreach (var assembly in s_moduleAssemblies)
        {
            foreach (var type in GetSafelyLoadableTypes(assembly))
            {
                if (!planInterfaceType.IsAssignableFrom(type) || type.IsInterface || type.IsAbstract)
                {
                    continue;
                }

                discoveredPlans++;
                var plan = (ITenantMigrationPlan)Activator.CreateInstance(type, nonPublic: true)!;

                Assert.Matches(ModuleCodeRegex(), plan.ModuleCode);
                Assert.Equal($"__EFMigrationsHistory_{plan.ModuleCode}", TenantMigrationRunner.BuildHistoryTableName(plan.ModuleCode));
            }
        }

        Assert.True(discoveredPlans > 0, "未发现任何 ITenantMigrationPlan 实现，模块迁移计划可能漏注册。");
    }

    [Fact]
    public void DatabaseSchemaTableNamesMustBelongToCorrectNamespace()
    {
        // PortalDbContext：只允许门户/平台日志前缀，禁止租户/全局/业务日志前缀
        var portalOptions = new DbContextOptionsBuilder<PortalDbContext>()
            .UseSqlServer("Server=stub;Database=stub;Integrated Security=true;Encrypt=false")
            .Options;

        using (var portalContext = new PortalDbContext(portalOptions))
        {
            var portalModel = portalContext.Model;
            Assert.NotNull(portalModel);

            foreach (var entityType in portalModel.GetEntityTypes())
            {
                var tableName = entityType.GetTableName();
                Assert.True(!string.IsNullOrWhiteSpace(tableName), $"PortalDbContext 实体 {entityType.ClrType.Name} 未映射表名。");
                Assert.True(
                    s_portalAllowedPrefixes.Any(p => tableName!.StartsWith(p, StringComparison.Ordinal)),
                    $"PortalDbContext 表 {tableName} 必须属于门户/平台日志命名空间（{string.Join(" / ", s_portalAllowedPrefixes)}）。");
                Assert.True(
                    !s_portalForbiddenPrefixes.Any(p => tableName!.StartsWith(p, StringComparison.Ordinal)),
                    $"PortalDbContext 表 {tableName} 禁止侵占租户/全局/业务日志前缀。");
            }
        }

        // 租户 DbContext：只允许模块前缀，禁止门户/全局/日志前缀
        foreach (var context in CreateTenantDbContexts())
        {
            using (context)
            {
                var model = context.Model;
                Assert.NotNull(model);

                var modulePrefix = GetModuleTablePrefix(context);
                Assert.False(string.IsNullOrWhiteSpace(modulePrefix), $"{context.GetType().Name} 未声明模块表前缀。");

                foreach (var entityType in model.GetEntityTypes())
                {
                    var tableName = entityType.GetTableName();
                    Assert.True(!string.IsNullOrWhiteSpace(tableName), $"{context.GetType().Name} 实体 {entityType.ClrType.Name} 未映射表名。");

                    // 实体级前缀覆盖（[TenantTablePrefix]，如 Support 模块 job_/msg_）：显式声明的前缀同样属于本模块命名空间
                    var declaredPrefix = entityType.ClrType.GetCustomAttribute<TenantTablePrefixAttribute>()?.Prefix;
                    var allowedPrefixes = declaredPrefix is null
                        ? [modulePrefix]
                        : new[] { modulePrefix, declaredPrefix };

                    Assert.True(
                        allowedPrefixes.Any(p => tableName!.StartsWith(p, StringComparison.Ordinal)),
                        $"{context.GetType().Name} 表 {tableName} 必须以模块前缀 {modulePrefix} 或实体声明的覆盖前缀「{declaredPrefix}」开头。");
                    Assert.True(
                        !s_tenantForbiddenPrefixes.Any(p => tableName!.StartsWith(p, StringComparison.Ordinal)),
                        $"{context.GetType().Name} 表 {tableName} 禁止侵占门户/全局/日志前缀。");
                }
            }
        }
    }

    private static Assembly[] LoadQlxModuleAssemblies()
    {
        var names = Directory
            .EnumerateFiles(AppContext.BaseDirectory, "Qlx.Modules.*.dll", SearchOption.TopDirectoryOnly)
            .Select(static file => Path.GetFileNameWithoutExtension(file))
            .OrderBy(static name => name, StringComparer.Ordinal)
            .ToArray();

        var assemblies = new List<Assembly>(names.Length);
        foreach (var name in names)
        {
            assemblies.Add(Assembly.Load(new AssemblyName(name)));
        }

        return [.. assemblies];
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

    private static List<TenantDbContext> CreateTenantDbContexts()
    {
        var tenantContextType = typeof(TenantDbContext);
        var result = new List<TenantDbContext>();
        var stubResolver = new ArchitectureStubTenantDatabaseResolver();

        foreach (var assembly in s_moduleAssemblies)
        {
            foreach (var type in GetSafelyLoadableTypes(assembly))
            {
                if (type.IsAbstract || !tenantContextType.IsAssignableFrom(type) || type == tenantContextType)
                {
                    continue;
                }

                var options = new DbContextOptionsBuilder().Options;
                var context = (TenantDbContext)Activator.CreateInstance(type, stubResolver, 1L, options)!;
                result.Add(context);
            }
        }

        Assert.True(result.Count > 0, "未发现任何 TenantDbContext 子类，租户库架构测试失效。");
        return result;
    }

    private static string GetModuleTablePrefix(TenantDbContext context)
    {
        // 前缀以 GetModuleTablePrefix 反射为准（Support 模块单 DbContext 多前缀 fil_/job_... 时
        // 类名推导 {name.ToLower()}_ 不再成立）；未覆写返回基类 null → 回落类名推导
        var method = context.GetType().GetMethod(
            "GetModuleTablePrefix",
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            Type.EmptyTypes,
            modifiers: null);

        if (method?.Invoke(context, null) is string prefix && !string.IsNullOrWhiteSpace(prefix))
        {
            return prefix;
        }

        // 按约定 {Module}TenantDbContext 且前缀为 {module}_
        var name = context.GetType().Name;
        const string suffix = "TenantDbContext";
        if (name.EndsWith(suffix, StringComparison.Ordinal))
        {
            var modulePart = name[..^suffix.Length];
            return $"{modulePart.ToLowerInvariant()}_";
        }

        return string.Empty;
    }

    /// <summary>架构测试专用桩解析器，仅用于构建 EF 模型，不建立真实连接。</summary>
    private sealed class ArchitectureStubTenantDatabaseResolver : ITenantDatabaseResolver
    {
        public TenantConnectionInfo Resolve(long tenantId)
            => new(tenantId, "stub", 0, "stub", string.Empty, string.Empty);

        public Task PreloadAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public void Invalidate(long tenantId)
        {
        }
    }
}
