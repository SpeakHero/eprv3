/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-20 23:00:00 +08:00 中国标准时间 星期二
 * 修改时间：2026-10-20 23:00:00 +08:00 中国标准时间 星期二
 * 描述：租户数据库路由预热后台服务，Host 启动时将全部活跃租户连接信息拉入缓存
 */

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Qlx.BuildingBlocks.MultiTenancy;

/// <summary>
/// 租户连接信息预热后台服务。Host 启动时同步调用 <see cref="ITenantDatabaseResolver.PreloadAsync"/>，
/// 将全部活跃租户的连接信息拉入内存缓存，确保运行时 Resolve 调用零 DB 查询。
/// </summary>
public sealed partial class TenantConnectionPreloadService(
    ITenantDatabaseResolver resolver,
    ILogger<TenantConnectionPreloadService> logger) : BackgroundService
{
    private readonly ILogger<TenantConnectionPreloadService> _logger = logger;

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield(); // 不阻塞 Host 启动

        try
        {
            await resolver.PreloadAsync(stoppingToken).ConfigureAwait(false);
            LogPreloadCompleted();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogPreloadFailed(exception);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "租户数据库路由预热完成")]
    private partial void LogPreloadCompleted();

    [LoggerMessage(Level = LogLevel.Warning, Message = "租户数据库路由预热失败（可能首次运行无租户），将在请求到达时按需解析")]
    private partial void LogPreloadFailed(Exception exception);
}
