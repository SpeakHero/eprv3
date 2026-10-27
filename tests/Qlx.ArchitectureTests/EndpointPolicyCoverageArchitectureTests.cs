/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-24
 * 描述：集中式授权映射架构守护四断言——
 *       ① 零漏配：三模块全部 Controller 的每个公开 Action 必须被 MapProvider 规则覆盖、
 *          或带 [AllowAnonymous]、或自带手写授权标注（逃生舱）；
 *       ② 防回退：Controller/Action 上禁止再出现 Policy = "Permission:..." 硬编码标注
 *          （权限点绑定唯一入口 = 模块 EndpointPolicyMap + authorization.json 覆盖）；
 *       ③ 权限码存在性：模块 Map 声明的全部 PermissionCode 必须在本模块 Manifest.Permissions 中声明；
 *       ④ 反向存在性：Map 声明的 Controller/Action 必须真实存在于对应程序集（防幽灵规则）。
 */

using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Qlx.Abstractions.Authorization;
using Qlx.Abstractions.Modules;
using Qlx.Modules.Bas;
using Qlx.Modules.Portal;
using Qlx.Modules.Support;
using Qlx.Modules.Stk;
using Qlx.Modules.Wms;

namespace Qlx.ArchitectureTests;

/// <summary>端点授权覆盖率与纪律守护（新增端点忘配规则即测试失败，根治遗漏引发的安全漏洞）。</summary>
public sealed class EndpointPolicyCoverageArchitectureTests
{
    private static readonly (string AssemblyName, IEndpointPolicyMapProvider MapProvider)[] s_modules =
    [
        ("Qlx.Modules.Bas", new BasEndpointPolicyMap()),
        ("Qlx.Modules.Support", new SupportEndpointPolicyMap()),
        ("Qlx.Modules.Portal", new PortalEndpointPolicyMap()),
        ("Qlx.Modules.Wms", new WmsEndpointPolicyMap()),
        ("Qlx.Modules.Stk", new StkEndpointPolicyMap()),
    ];

    /// <summary>断言①：零漏配——每个公开 Action 必须被规则覆盖或显式豁免。</summary>
    [Fact]
    public void EveryPublicActionMustBeCoveredByRuleOrExemption()
    {
        var failures = new List<string>();

        foreach (var (assemblyName, mapProvider) in s_modules)
        {
            var rules = mapProvider.GetRules();
            var controllerKeys = rules.Where(static r => r.Action is null)
                .Select(static r => r.Controller)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var actionKeys = rules.Where(static r => r.Action is not null)
                .Select(static r => $"{r.Controller}.{r.Action}")
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var assembly = Assembly.Load(assemblyName);
            foreach (var controllerType in GetSafelyLoadableTypes(assembly).Where(IsController))
            {
                var controllerName = controllerType.Name.EndsWith("Controller", StringComparison.Ordinal)
                    ? controllerType.Name[..^"Controller".Length]
                    : controllerType.Name;

                foreach (var method in GetActions(controllerType))
                {
                    // 豁免路径 1：方法/类带 [AllowAnonymous]
                    if (method.GetCustomAttribute<AllowAnonymousAttribute>() is not null
                        || controllerType.GetCustomAttribute<AllowAnonymousAttribute>() is not null)
                    {
                        continue;
                    }

                    // 豁免路径 2：自带手写授权标注（逃生舱，须评审登记）
                    if (method.GetCustomAttributes().Any(static a => a is IAuthorizeData)
                        || controllerType.GetCustomAttributes().Any(static a => a is IAuthorizeData))
                    {
                        continue;
                    }

                    var covered = controllerKeys.Contains(controllerName)
                        || actionKeys.Contains($"{controllerName}.{method.Name}");
                    if (!covered)
                    {
                        failures.Add($"{assemblyName}:{controllerType.FullName}.{method.Name}");
                    }
                }
            }
        }

        Assert.True(
            failures.Count == 0,
            "以下端点未被任何 EndpointPolicyMap 规则覆盖，也无匿名/手写豁免：" + Environment.NewLine
            + string.Join(Environment.NewLine, failures));
    }

    /// <summary>断言②：防回退——禁止在 Controller/Action 上回写 Permission: 硬编码策略标注。</summary>
    [Fact]
    public void NoPermissionPolicyLiteralOnControllers()
    {
        var failures = new List<string>();

        foreach (var (assemblyName, _) in s_modules)
        {
            var assembly = Assembly.Load(assemblyName);
            foreach (var controllerType in GetSafelyLoadableTypes(assembly).Where(IsController))
            {
                var suspects = controllerType.GetCustomAttributes(inherit: true)
                    .Concat(GetActions(controllerType).SelectMany(static m => m.GetCustomAttributes(inherit: true)))
                    .OfType<AuthorizeAttribute>()
                    .Where(static a => a.Policy?.StartsWith("Permission:", StringComparison.Ordinal) == true)
                    .ToList();

                if (suspects.Count > 0)
                {
                    failures.Add($"{assemblyName}:{controllerType.FullName}（{suspects.Count} 处）");
                }
            }
        }

        Assert.True(
            failures.Count == 0,
            "权限点硬编码标注回退检测到（必须改回模块 EndpointPolicyMap 声明）：" + Environment.NewLine
            + string.Join(Environment.NewLine, failures));
    }

    /// <summary>断言③：权限码存在性——Map 声明的 PermissionCode 必须在本模块 Manifest 中注册。</summary>
    [Fact]
    public void EveryPermissionCodeInMapMustExistInModuleManifest()
    {
        (IModule Module, IEndpointPolicyMapProvider MapProvider)[] pairs =
        [
            (new BasModule(), new BasEndpointPolicyMap()),
            (new SupportModule(), new SupportEndpointPolicyMap()),
            (new PortalModule(), new PortalEndpointPolicyMap()),
            (new WmsModule(), new WmsEndpointPolicyMap()),
            (new StkModule(), new StkEndpointPolicyMap()),
        ];

        var failures = new List<string>();
        foreach (var (module, mapProvider) in pairs)
        {
            var declared = module.Manifest.Permissions
                .Select(static p => p.Code)
                .ToHashSet(StringComparer.Ordinal);

            foreach (var rule in mapProvider.GetRules())
            {
                if (!string.IsNullOrWhiteSpace(rule.PermissionCode) && !declared.Contains(rule.PermissionCode))
                {
                    failures.Add($"{module.Manifest.ModuleCode}: {rule.Controller}.{rule.Action ?? "*"} → {rule.PermissionCode}");
                }
            }
        }

        Assert.True(
            failures.Count == 0,
            "以下权限码未在本模块 Manifest.Permissions 中声明（策略未物化，运行时将 403）：" + Environment.NewLine
            + string.Join(Environment.NewLine, failures));
    }

    /// <summary>断言④：反向存在性——规则声明的 Controller/Action 必须真实存在（防幽灵规则）。</summary>
    [Fact]
    public void EveryRuleMustPointToExistingControllerAndAction()
    {
        var failures = new List<string>();

        foreach (var (assemblyName, mapProvider) in s_modules)
        {
            var controllers = GetSafelyLoadableTypes(Assembly.Load(assemblyName))
                .Where(IsController)
                .ToDictionary(
                    static t => t.Name.EndsWith("Controller", StringComparison.Ordinal)
                        ? t.Name[..^"Controller".Length]
                        : t.Name,
                    static t => t,
                    StringComparer.OrdinalIgnoreCase);

            foreach (var rule in mapProvider.GetRules())
            {
                if (!controllers.TryGetValue(rule.Controller, out Type? controllerType))
                {
                    failures.Add($"{assemblyName}:{rule.Controller}.* → 控制器不存在");
                    continue;
                }

                if (rule.Action is not null
                    && !GetActions(controllerType).Any(
                        m => string.Equals(m.Name, rule.Action, StringComparison.Ordinal)))
                {
                    failures.Add($"{assemblyName}:{rule.Controller}.{rule.Action} → 动作不存在");
                }
            }
        }

        Assert.True(
            failures.Count == 0,
            "以下授权规则指向不存在的控制器/动作（幽灵规则）：" + Environment.NewLine
            + string.Join(Environment.NewLine, failures));
    }

    private static bool IsController(Type type)
        => type is { IsAbstract: false, IsPublic: true }
           && type.Name.EndsWith("Controller", StringComparison.Ordinal)
           && typeof(ControllerBase).IsAssignableFrom(type);

    private static IEnumerable<MethodInfo> GetActions(Type controllerType)
        => controllerType
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(static m => !m.IsSpecialName && m.GetCustomAttribute<NonActionAttribute>() is null);

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
}
