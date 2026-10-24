/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-22 16:30:00 +08:00 中国标准时间 星期四
 * 修改时间：2026-10-22 16:30:00 +08:00 中国标准时间 星期四
 * 描述：租户升级任务调度后台服务：周期扫描 Pending 任务并串行驱动升级流水线
 */

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Qlx.Abstractions.Tenancy;

namespace Qlx.BuildingBlocks.MultiTenancy.Upgrade;

/// <summary>
/// 租户升级任务调度后台服务。启动即扫一轮，随后按 <see cref="TenantUpgradeOptions.ScanIntervalSeconds"/>
/// 周期扫描 Pending 任务并**串行**执行 <see cref="TenantUpgradePipeline"/>
/// （升级涉及大批量数据复制，单 Host 单任务执行，避免并发复制压垮共享库）。
/// 门户模块未注册 <see cref="ITenantUpgradeJobRepository"/>（如纯开发期）时跳过。
/// </summary>
public sealed partial class TenantUpgradeJobDispatcher(
    IServiceProvider serviceProvider,
    IOptions<TenantUpgradeOptions> options,
    ILogger<TenantUpgradeJobDispatcher> logger) : BackgroundService
{
    /// <summary>LoggerMessage 源生成器要求字段。</summary>
    private readonly ILogger<TenantUpgradeJobDispatcher> _logger = logger;

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled)
        {
            LogDisabled();
            return;
        }

        await Task.Yield();

        await ScanAndDispatchAsync(stoppingToken).ConfigureAwait(false);

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(options.Value.ScanIntervalSeconds));
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            await ScanAndDispatchAsync(stoppingToken).ConfigureAwait(false);
        }
    }

    private async Task ScanAndDispatchAsync(CancellationToken ct)
    {
        try
        {
            await using var scope = serviceProvider.CreateAsyncScope();
            var repo = scope.ServiceProvider.GetService<ITenantUpgradeJobRepository>();
            if (repo is null)
            {
                return;
            }

            var jobIds = await repo.GetPendingJobIdsAsync(ct).ConfigureAwait(false);
            if (jobIds.Count == 0)
            {
                return;
            }

            LogScanFound(jobIds.Count);

            var pipeline = scope.ServiceProvider.GetRequiredService<TenantUpgradePipeline>();
            foreach (var jobId in jobIds)
            {
                ct.ThrowIfCancellationRequested();
                await pipeline.ExecuteAsync(jobId, scope.ServiceProvider, ct).ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogScanError(ex);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "租户升级调度已禁用（Qlx:Tenancy:Upgrade:Enabled=false）")]
    private partial void LogDisabled();

    [LoggerMessage(Level = LogLevel.Information, Message = "租户升级扫描发现 {Count} 个待处理任务")]
    private partial void LogScanFound(int count);

    [LoggerMessage(Level = LogLevel.Warning, Message = "租户升级扫描异常（详细异常见日志）")]
    private partial void LogScanError(Exception ex);
}
