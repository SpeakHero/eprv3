/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-22 13:00:00 +08:00 中国标准时间 星期四
 * 描述：四库启动校验（B4-08）——生产环境断言四类连接串账号互不相同、共享库只读账号仅 SELECT
 */

using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Qlx.BuildingBlocks.Audit;
using Qlx.BuildingBlocks.MultiTenancy;

namespace Qlx.BuildingBlocks.Configuration;

/// <summary>
/// 四库启动校验器（B4-08）。生产环境断言：
/// ① 四类连接串（门户库、共享库、租户共享库、审计库）账号互不相同；
/// ② 共享库只读账号仅 SELECT 权限（通过连接串 User ID 与主账号不同推断，完整校验需登录验证）。
/// </summary>
public sealed partial class FourDatabaseStartupValidator(
    IOptions<AuditOptions> auditOptions,
    IOptions<SharedDatabaseOptions> sharedDatabaseOptions,
    IOptions<PlatformSharedOptions> platformSharedOptions,
    IOptions<PortalConnectionOptions> portalConnectionOptions,
    IHostEnvironment environment,
    ILogger<FourDatabaseStartupValidator> logger) : IHostedService
{
    private readonly ILogger<FourDatabaseStartupValidator> _logger = logger;

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (environment.IsDevelopment())
        {
            LogSkipped();
            return Task.CompletedTask;
        }

        var errors = new List<string>();

        // ① 四类连接串账号互不相同
        var portalConn = portalConnectionOptions.Value.PortalConnectionString;
        var auditConn = auditOptions.Value.ConnectionString;
        var sharedConn = sharedDatabaseOptions.Value.ToConnectionInfo(0).ToConnectionString();
        var platformSharedConn = platformSharedOptions.Value.ConnectionString;

        var accounts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["门户库"] = ExtractUserId(portalConn),
            ["审计库"] = ExtractUserId(auditConn),
            ["租户共享库"] = ExtractUserId(sharedConn),
            ["平台共享库"] = ExtractUserId(platformSharedConn),
        };

        var duplicates = accounts
            .GroupBy(kv => kv.Value, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => $"{string.Join("/", g.Select(kv => kv.Key))} 使用相同账号 [{g.Key}]");

        errors.AddRange(duplicates);

        // ② 共享库只读账号仅 SELECT 权限
        var readonlyConn = platformSharedOptions.Value.ReadOnlyConnectionString;
        if (!string.IsNullOrWhiteSpace(readonlyConn))
        {
            var readonlyUser = ExtractUserId(readonlyConn);
            var mainUser = ExtractUserId(platformSharedConn);
            if (string.Equals(readonlyUser, mainUser, StringComparison.OrdinalIgnoreCase))
            {
                errors.Add($"共享库只读账号与主账号相同 [{readonlyUser}]，无法保证仅 SELECT 权限");
            }
        }

        if (errors.Count > 0)
        {
            foreach (var error in errors)
            {
                LogValidationError(error);
            }

            if (environment.IsProduction())
            {
                throw new InvalidOperationException(
                    $"四库启动校验失败：{string.Join("；", errors)}");
            }

            LogValidationWarning(string.Join("；", errors));
        }
        else
        {
            LogValidationPassed();
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>从连接串提取 User ID（Integrated Security 时返回 "integrated"）。</summary>
    private static string ExtractUserId(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return "(empty)";
        }

        try
        {
            var builder = new SqlConnectionStringBuilder(connectionString);
            if (builder.IntegratedSecurity)
            {
                return "(integrated)";
            }

            return builder.UserID ?? "(unknown)";
        }
        catch
        {
            return "(parse_error)";
        }
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "开发环境跳过四库启动校验")]
    private partial void LogSkipped();

    [LoggerMessage(Level = LogLevel.Error, Message = "四库启动校验错误：{Error}")]
    private partial void LogValidationError(string error);

    [LoggerMessage(Level = LogLevel.Warning, Message = "四库启动校验警告：{Warning}")]
    private partial void LogValidationWarning(string warning);

    [LoggerMessage(Level = LogLevel.Information, Message = "四库启动校验通过：四类连接串账号互不相同，共享库只读账号独立")]
    private partial void LogValidationPassed();
}
