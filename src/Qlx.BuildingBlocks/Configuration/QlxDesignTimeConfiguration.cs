/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-22 20:00:00 +08:00 中国标准时间 星期四
 * 修改时间：2026-10-22 20:00:00 +08:00 中国标准时间 星期四
 * 描述：EF Core 设计时（dotnet ef）配置装配：统一 config 目录回溯与连接串解析，禁止各工厂硬编码
 */

using Microsoft.Extensions.Configuration;
using Qlx.BuildingBlocks.MultiTenancy;

namespace Qlx.BuildingBlocks.Configuration;

/// <summary>
/// EF Core 设计时配置工具。供各 <c>IDesignTimeDbContextFactory</c> 复用：
/// 统一向上回溯 <c>config</c> 目录、统一加载分层配置、统一解析连接串，避免各工厂重复实现与硬编码。
/// <para>仅在设计时（<c>dotnet ef migrations</c>）调用，运行期请使用强类型 Options。</para>
/// </summary>
public static class QlxDesignTimeConfiguration
{
    /// <summary>设计时建模使用的占位租户 Id（不连接数据库，仅满足上下文构造）。</summary>
    public const long DesignTimeTenantId = 1;

    private const string ConfigDirectoryName = "config";
    private const string CommonDirectory = "common";
    private const string PortalDirectory = "portal";
    private const string TenantDirectory = "tenant";
    private const string ModulesDirectory = "modules";
    private const string JsonSearchPattern = "*.json";
    private const int MaxParentLevels = 5;

    /// <summary>
    /// 构建设计时配置根：自动向上回溯包含 <c>config</c> 目录的工作目录，
    /// 并分层加载 common / portal / tenant / modules 下全部 JSON。
    /// </summary>
    /// <param name="startDirectory">起始目录；默认为当前工作目录。</param>
    /// <returns>配置根。</returns>
    /// <exception cref="InvalidOperationException">未找到 config 目录时抛出。</exception>
    public static IConfigurationRoot Build(string? startDirectory = null)
    {
        var configDirectory = FindConfigDirectory(startDirectory ?? Directory.GetCurrentDirectory())
            ?? throw new InvalidOperationException(
                "未能找到 config 目录。请确保 dotnet ef 命令在包含 config 目录的工作目录或其子目录下执行。");

        var builder = new ConfigurationBuilder().SetBasePath(configDirectory);

        AddJsonFiles(builder, Path.Combine(configDirectory, CommonDirectory));
        AddJsonFiles(builder, Path.Combine(configDirectory, PortalDirectory));
        AddJsonFiles(builder, Path.Combine(configDirectory, TenantDirectory));
        AddJsonFiles(builder, Path.Combine(configDirectory, ModulesDirectory), searchSubdirectories: true);

        return builder.Build();
    }

    /// <summary>
    /// 从配置中解析设计时建模用 SQL Server 连接串（取租户共享库配置，仅建模不连接）。
    /// </summary>
    /// <param name="configuration">配置根。</param>
    /// <returns>SQL Server 连接串。</returns>
    /// <exception cref="InvalidOperationException">缺少共享库配置时抛出。</exception>
    public static string ResolveModelingConnectionString(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var shared = configuration.GetSection(SharedDatabaseOptions.SectionName).Get<SharedDatabaseOptions>()
            ?? throw new InvalidOperationException(
                $"缺少配置节 {SharedDatabaseOptions.SectionName}，无法构建设计时建模连接串。");

        return shared.ToConnectionInfo(0).ToConnectionString();
    }

    /// <summary>向上逐级查找包含 <c>config</c> 目录的父目录，返回其 config 绝对路径。</summary>
    private static string? FindConfigDirectory(string startDirectory)
    {
        var candidate = Path.GetFullPath(startDirectory);

        for (var level = 0; level <= MaxParentLevels; level++)
        {
            var configDirectory = Path.Combine(candidate, ConfigDirectoryName);
            if (Directory.Exists(configDirectory))
            {
                return configDirectory;
            }

            var parent = Path.GetDirectoryName(candidate);
            if (string.IsNullOrEmpty(parent))
            {
                break;
            }

            candidate = parent;
        }

        return null;
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
            builder.AddJsonFile(file, optional: true, reloadOnChange: false);
        }
    }
}
