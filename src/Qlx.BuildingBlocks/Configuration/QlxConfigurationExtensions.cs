/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.1.0 beta
 * 创建时间：2026-10-20 22:30:00 +08:00 中国标准时间 星期二
 * 修改时间：2026-10-25 10:00:00 +08:00 中国标准时间 星期日
 * 描述：四级配置目录分层加载扩展（common/portal|tenant/modules/environments）
 */

using Microsoft.Extensions.Configuration;

namespace Qlx.BuildingBlocks.Configuration;

/// <summary>
/// 四级配置目录装配扩展。配置根解析优先级：环境变量 <c>QLX_CONFIG_ROOT</c>
/// → 内容根目录下 <c>config</c> → 内容根目录上两级下 <c>config</c>（仓库根约定）。
/// 配置优先级（低→高）：默认源 → <c>config/common/*.json</c>
/// → <c>config/portal|tenant/*.json</c>（按宿主角色）
/// → <c>config/modules/**/*.json</c> → <c>config/environments/{环境名}/**/*.json</c>
/// → 环境变量（双下划线映射 <c>__</c> → <c>:</c>，如 <c>Qlx__Ai__Providers__zhipu__ApiKey</c>）。
/// 全部文件可选且热重载；业务代码禁止裸读字符串键，须经强类型 Options 消费。
/// </summary>
public static class QlxConfigurationExtensions
{
    private const string ConfigRootEnvironmentVariable = "QLX_CONFIG_ROOT";
    private const string ConfigDirectoryName = "config";
    private const string CommonDirectory = "common";
    private const string PortalDirectory = "portal";
    private const string TenantDirectory = "tenant";
    private const string ModulesDirectory = "modules";
    private const string EnvironmentsDirectory = "environments";
    private const string JsonSearchPattern = "*.json";

    /// <summary>
    /// 按四级目录约定追加 JSON 配置源。
    /// </summary>
    /// <param name="builder">配置构建器。</param>
    /// <param name="contentRootPath">宿主内容根目录。</param>
    /// <param name="environmentName">当前环境名（Development / Production 等）。</param>
    /// <param name="role">宿主角色，决定门户级或租户级目录是否参与加载。</param>
    /// <returns>配置构建器。</returns>
    public static IConfigurationBuilder AddQlxConfiguration(
        this IConfigurationBuilder builder,
        string contentRootPath,
        string environmentName,
        QlxHostRole role)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentRootPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(environmentName);

        var configRoot = ResolveConfigRoot(contentRootPath);
        if (configRoot is null)
        {
            return builder;
        }

        AddJsonFiles(builder, Path.Combine(configRoot, CommonDirectory));

        if (role is QlxHostRole.AllInOne or QlxHostRole.Portal)
        {
            AddJsonFiles(builder, Path.Combine(configRoot, PortalDirectory));
        }

        if (role is QlxHostRole.AllInOne or QlxHostRole.Tenant)
        {
            AddJsonFiles(builder, Path.Combine(configRoot, TenantDirectory));
        }

        AddJsonFiles(builder, Path.Combine(configRoot, ModulesDirectory), searchSubdirectories: true);
        AddJsonFiles(builder, Path.Combine(configRoot, EnvironmentsDirectory, environmentName), searchSubdirectories: true);

        // 环境变量最后加载，优先级最高：Qlx__Ai__Strategy、Qlx__Ai__Providers__zhipu__ApiKey
        // （WebApplicationBuilder 默认环境变量源在 JSON 之前，会被此处的 JSON 源反向覆盖）
        builder.AddEnvironmentVariables();

        return builder;
    }

    /// <summary>
    /// 解析配置根目录（与 <see cref="AddQlxConfiguration"/> 同一套定位逻辑）。
    /// 供需要向配置目录落盘的组件复用（如授权规则 JSON 防删自动生成），禁止业务代码自行拼路径。
    /// </summary>
    /// <param name="contentRootPath">宿主内容根目录。</param>
    /// <returns>配置根目录全路径；三处候选均不存在时返回 <c>null</c>。</returns>
    public static string? ResolveQlxConfigRoot(string contentRootPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contentRootPath);
        return ResolveConfigRoot(contentRootPath);
    }

    private static string? ResolveConfigRoot(string contentRootPath)
    {
        var fromEnvironment = Environment.GetEnvironmentVariable(ConfigRootEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(fromEnvironment) && Directory.Exists(fromEnvironment))
        {
            return Path.GetFullPath(fromEnvironment);
        }

        var underContentRoot = Path.Combine(contentRootPath, ConfigDirectoryName);
        if (Directory.Exists(underContentRoot))
        {
            return underContentRoot;
        }

        // 仓库布局约定：宿主位于 src/Qlx.Host，config 位于仓库根，即内容根上两级
        var repositoryRoot = Path.GetFullPath(Path.Combine(contentRootPath, "..", ".."));
        var underRepositoryRoot = Path.Combine(repositoryRoot, ConfigDirectoryName);
        return Directory.Exists(underRepositoryRoot) ? underRepositoryRoot : null;
    }

    private static void AddJsonFiles(IConfigurationBuilder builder, string directory, bool searchSubdirectories = false)
    {
        if (!Directory.Exists(directory))
        {
            return;
        }

        var option = searchSubdirectories ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        foreach (var file in Directory.EnumerateFiles(directory, JsonSearchPattern, option).Order(StringComparer.OrdinalIgnoreCase))
        {
            builder.AddJsonFile(file, optional: true, reloadOnChange: true);
        }
    }
}
