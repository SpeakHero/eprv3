/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-22 16:30:00 +08:00 中国标准时间 星期四
 * 修改时间：2026-10-22 16:30:00 +08:00 中国标准时间 星期四
 * 描述：租户隔离周期巡检后台服务：按周期对全部活跃租户执行隔离巡检并将报告落库，严重发现落审计告警
 */

using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Qlx.Abstractions.Audit;
using Qlx.Abstractions.Constants;
using Qlx.Abstractions.Serialization;
using Qlx.Abstractions.Tenancy;
using Qlx.BuildingBlocks.Ids;
using Qlx.BuildingBlocks.MultiTenancy.Isolation;

namespace Qlx.BuildingBlocks.MultiTenancy.Upgrade;

/// <summary>
/// 租户隔离周期巡检后台服务（B5-06）。每 <see cref="TenantIsolationAuditOptions.IntervalHours"/> 小时
/// 对全部活跃租户执行一次 <see cref="TenantIsolationAuditor"/> 巡检，报告经
/// <see cref="ITenantIsolationAuditRepository"/> 落门户库；存在严重发现时落审计操作码
/// <c>ISOLATION_AUDIT_ALERT</c> 告警。门户模块未注册落库契约（如纯开发期）时跳过。
/// </summary>
public sealed partial class TenantIsolationAuditJobService(
    IServiceProvider serviceProvider,
    TenantIsolationAuditor auditor,
    IOptions<TenantIsolationAuditOptions> options,
    ILogger<TenantIsolationAuditJobService> logger) : BackgroundService
{
    /// <summary>LoggerMessage 源生成器要求字段。</summary>
    private readonly ILogger<TenantIsolationAuditJobService> _logger = logger;

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled)
        {
            LogDisabled();
            return;
        }

        await Task.Yield();

        using var timer = new PeriodicTimer(TimeSpan.FromHours(options.Value.IntervalHours));
        do
        {
            await AuditOnceAsync(stoppingToken).ConfigureAwait(false);
        }
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
    }

    private async Task AuditOnceAsync(CancellationToken ct)
    {
        try
        {
            await using var scope = serviceProvider.CreateAsyncScope();
            var repo = scope.ServiceProvider.GetService<ITenantIsolationAuditRepository>();
            if (repo is null)
            {
                return;
            }

            var tenantIds = await repo.GetActiveTenantIdsAsync(ct).ConfigureAwait(false);
            if (tenantIds.Count == 0)
            {
                return;
            }

            LogAuditScanFound(tenantIds.Count);

            foreach (var tenantId in tenantIds)
            {
                ct.ThrowIfCancellationRequested();

                try
                {
                    var report = await auditor.RunAsync(tenantId, ct).ConfigureAwait(false);
                    await repo.SaveReportAsync(
                        tenantId,
                        report.IsHealthy,
                        report.HasCritical,
                        report.Findings.Count,
                        JsonSerializer.Serialize(report.Findings, QlxJsonDefaults.Default),
                        report.ToReadableText(),
                        ct).ConfigureAwait(false);

                    if (report.HasCritical && scope.ServiceProvider.GetService<IAuditService>() is { } audit)
                    {
                        // workerId 租约在宿主启动完成后才就绪，雪花生成器必须在运行期从作用域解析
                        var snowflake = scope.ServiceProvider.GetRequiredService<ISnowflakeIdGenerator>();
                        await audit.CaptureOperationAsync(new OperationLogEntry(
                            snowflake.NextId(), QlxAuditConstants.PortalModuleCode,
                            TenantUpgradeAuditOperations.IsolationAuditAlert,
                            $"租户 {tenantId} 隔离巡检发现 {report.Findings.Count} 项问题（含严重项）",
                            QlxAuditConstants.SystemOperator, DateTimeOffset.UtcNow, tenantId,
                            QlxAuditConstants.TargetTenant, tenantId),
                            ct).ConfigureAwait(false);
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    LogTenantAuditError(tenantId, ex);
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogAuditError(ex);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "租户隔离周期巡检已禁用（Qlx:Tenancy:IsolationAudit:Enabled=false）")]
    private partial void LogDisabled();

    [LoggerMessage(Level = LogLevel.Information, Message = "租户隔离周期巡检开始：{Count} 个活跃租户")]
    private partial void LogAuditScanFound(int count);

    [LoggerMessage(Level = LogLevel.Warning, Message = "租户 {TenantId} 隔离巡检执行异常（详细异常见日志）")]
    private partial void LogTenantAuditError(long tenantId, Exception ex);

    [LoggerMessage(Level = LogLevel.Warning, Message = "租户隔离周期巡检扫描异常（详细异常见日志）")]
    private partial void LogAuditError(Exception ex);
}
