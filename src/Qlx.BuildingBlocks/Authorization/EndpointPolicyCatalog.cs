/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-24
 * 描述：端点授权规则目录（Singleton，启动期一次构建定稿）——
 *       合并各模块 IEndpointPolicyMapProvider 基线与 authorization.json 覆盖层，
 *       构建 (controller / controller.action) 双级索引；JSON 缺失时自动序列化基线
 *       写出默认文件（防删）。合并语义与原「类级 + 方法级 [Authorize] 叠加」严格等价。
 */

using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Qlx.Abstractions.Authorization;
using Qlx.Abstractions.Constants;
using Qlx.BuildingBlocks.Configuration;

namespace Qlx.BuildingBlocks.Authorization;

/// <summary>合并后的端点有效授权策略（控制器级 ∪ 动作级）。</summary>
/// <param name="RequireTenantContext">是否叠加租户上下文前置策略。</param>
/// <param name="PolicyNames">策略名集合（控制器级在前、动作级在后，去重；AND 叠加）。</param>
/// <param name="AllowAnonymous">显式匿名豁免（任一级为真即真）。</param>
public sealed record EffectiveEndpointPolicy(
    bool RequireTenantContext,
    IReadOnlyList<string> PolicyNames,
    bool AllowAnonymous);

/// <summary>
/// 端点授权规则目录。构建顺序：模块基线（同键并集合并）→ JSON 同键覆盖 → disabledKeys 删除。
/// 规则键序数不区分大小写；启动时单次构建（D-9），配置变更重启生效。
/// </summary>
public sealed partial class EndpointPolicyCatalog
{
    private const string AuthorizationFileName = "authorization.json";
    private const string CommonDirectoryName = "common";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true,
    };

    private readonly Dictionary<string, EndpointPolicyRule> _controllerRules = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, EndpointPolicyRule> _actionRules = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _disabledKeys = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 构建端点授权规则目录（启动期单次执行）：模块基线并集合并 → JSON 防删默认文件 →
    /// JSON 同键覆盖 → 禁用键删除。
    /// </summary>
    /// <param name="providers">全部模块端点策略映射提供方。</param>
    /// <param name="options"><c>endpointPolicies</c> 配置节绑定结果。</param>
    /// <param name="environment">宿主环境（用于解析配置根目录）。</param>
    /// <param name="logger">日志记录器。</param>
    public EndpointPolicyCatalog(
        IEnumerable<IEndpointPolicyMapProvider> providers,
        IOptions<EndpointPolicyOptions> options,
        IHostEnvironment environment,
        ILogger<EndpointPolicyCatalog> logger)
    {
        ArgumentNullException.ThrowIfNull(providers);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(logger);

        // 1. 模块基线（同键并集合并，防多 provider 重复声明丢策略）
        var baseline = providers.SelectMany(p => p.GetRules()).ToList();
        foreach (var rule in baseline)
        {
            MergeBaseline(rule);
        }

        // 2. JSON 防删：文件缺失/为空 → 用基线写出默认文件（写失败仅告警，基线兜底仍在）
        EnsureDefaultFileExists(baseline, environment, logger);

        // 3. JSON 覆盖（同键替换基线；基线新增键不受存量 JSON 掩盖）
        foreach (var rule in options.Value.Rules)
        {
            if (string.IsNullOrWhiteSpace(rule.Controller))
            {
                LogEmptyControllerRuleSkipped(logger);
                continue;
            }

            if (rule.Action is null)
            {
                _controllerRules[rule.Controller] = rule;
            }
            else
            {
                _actionRules[ActionKey(rule.Controller, rule.Action)] = rule;
            }
        }

        // 4. 禁用键删除（仅删对应键的规则；剩余规则仍生效）
        foreach (var key in options.Value.DisabledKeys)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                continue;
            }

            _disabledKeys.Add(key.Trim());
        }
    }

    /// <summary>
    /// 查找端点有效规则：先取控制器级（未禁用）与动作级（未禁用），按并集语义合并。
    /// </summary>
    /// <param name="controller">控制器名（不含 Controller 后缀）。</param>
    /// <param name="action">动作方法名。</param>
    /// <param name="policy">命中时输出合并后的有效策略。</param>
    /// <returns>任一级命中返回 <c>true</c>；全 miss 返回 <c>false</c>（调用方走仅认证兜底）。</returns>
    public bool TryFind(string controller, string action, out EffectiveEndpointPolicy? policy)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(controller);
        ArgumentException.ThrowIfNullOrWhiteSpace(action);

        EndpointPolicyRule? controllerRule = null;
        EndpointPolicyRule? actionRule = null;
        var hasController = !_disabledKeys.Contains(controller)
            && _controllerRules.TryGetValue(controller, out controllerRule);
        var hasAction = !_disabledKeys.Contains(ActionKey(controller, action))
            && _actionRules.TryGetValue(ActionKey(controller, action), out actionRule);

        if (!hasController && !hasAction)
        {
            policy = null;
            return false;
        }

        var policies = new List<string>();
        AppendPolicies(controllerRule, policies);
        AppendPolicies(actionRule, policies);

        policy = new EffectiveEndpointPolicy(
            RequireTenantContext: (controllerRule?.RequireTenantContext ?? false) || (actionRule?.RequireTenantContext ?? false),
            PolicyNames: [.. policies.Distinct(StringComparer.Ordinal)],
            AllowAnonymous: (controllerRule?.AllowAnonymous ?? false) || (actionRule?.AllowAnonymous ?? false));
        return true;
    }

    private static void AppendPolicies(EndpointPolicyRule? rule, List<string> policies)
    {
        if (rule is null)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(rule.Policy))
        {
            policies.Add(rule.Policy);
        }

        if (!string.IsNullOrWhiteSpace(rule.PermissionCode))
        {
            policies.Add(QlxPolicyNames.Permission(rule.PermissionCode));
        }
    }

    private void MergeBaseline(EndpointPolicyRule rule)
    {
        var key = rule.Action is null ? rule.Controller : ActionKey(rule.Controller, rule.Action);
        var table = rule.Action is null ? _controllerRules : _actionRules;

        if (table.TryGetValue(key, out var existing))
        {
            // 并集合并：策略并存、TenantContext/AllowAnonymous 取或
            table[key] = new EndpointPolicyRule
            {
                Controller = existing.Controller,
                Action = existing.Action,
                Policy = existing.Policy ?? rule.Policy,
                PermissionCode = existing.PermissionCode ?? rule.PermissionCode,
                RequireTenantContext = existing.RequireTenantContext || rule.RequireTenantContext,
                AllowAnonymous = existing.AllowAnonymous || rule.AllowAnonymous,
            };
        }
        else
        {
            table[key] = rule;
        }
    }

    private static void EnsureDefaultFileExists(
        IReadOnlyList<EndpointPolicyRule> baseline,
        IHostEnvironment environment,
        ILogger<EndpointPolicyCatalog> logger)
    {
        var configRoot = QlxConfigurationExtensions.ResolveQlxConfigRoot(environment.ContentRootPath);
        if (configRoot is null)
        {
            LogConfigRootUnresolved(logger);
            return;
        }

        var path = Path.Combine(configRoot, CommonDirectoryName, AuthorizationFileName);
        if (File.Exists(path) && new FileInfo(path).Length > 0)
        {
            return;
        }

        try
        {
            var payload = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                [EndpointPolicyOptions.SectionName] = new
                {
                    rules = baseline.OrderBy(r => r.Controller, StringComparer.OrdinalIgnoreCase)
                        .ThenBy(r => r.Action, StringComparer.OrdinalIgnoreCase)
                        .ToList(),
                    disabledKeys = Array.Empty<string>(),
                },
            };
            File.WriteAllText(path, JsonSerializer.Serialize(payload, SerializerOptions));
            LogDefaultFileGenerated(logger, path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LogDefaultFileWriteFailed(logger, ex, path);
        }
    }

    private static string ActionKey(string controller, string action) => $"{controller}.{action}";

    [LoggerMessage(Level = LogLevel.Warning, Message = "authorization.json 存在 Controller 为空的规则条目，已跳过")]
    private static partial void LogEmptyControllerRuleSkipped(ILogger<EndpointPolicyCatalog> logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "配置根目录未解析，authorization.json 默认文件生成跳过（基线兜底仍生效）")]
    private static partial void LogConfigRootUnresolved(ILogger<EndpointPolicyCatalog> logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "authorization.json 不存在或为空，已按模块基线生成默认授权规则文件：{Path}")]
    private static partial void LogDefaultFileGenerated(ILogger<EndpointPolicyCatalog> logger, string path);

    [LoggerMessage(Level = LogLevel.Warning, Message = "authorization.json 默认文件写出失败（{Path}），基线兜底仍生效")]
    private static partial void LogDefaultFileWriteFailed(ILogger<EndpointPolicyCatalog> logger, Exception exception, string path);
}
