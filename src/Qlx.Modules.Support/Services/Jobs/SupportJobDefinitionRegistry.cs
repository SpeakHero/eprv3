/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-22 22:45:00 +08:00 中国标准时间 星期四
 * 描述：任务定义注册表——模块 Manifest.Jobs + DI 处理器清单 → job_definitions 非破坏性种子 / 刷新，
 *       许可启停联动任务定义 Enabled；新租户首次调度扫描时懒种子
 */

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Qlx.Abstractions.Jobs;
using Qlx.Abstractions.Modules;
using Qlx.BuildingBlocks.Jobs;
using Qlx.BuildingBlocks.MultiTenancy;
using Qlx.Modules.Support.Domain;
using Qlx.Modules.Support.Options;

namespace Qlx.Modules.Support.Services.Jobs;

/// <summary>
/// 任务定义注册表。种子来源 = 全部 <see cref="ModuleManifest.Jobs"/>（许可过滤）∩
/// 当前部署已注册的 <see cref="IJobHandler"/>（处理器未注册的任务不种子，避免幽灵调度）。
/// 刷新为非破坏性：仅覆盖 Manifest 来源字段与许可 Enabled，保留 Paused / NextRunAt 运行态。
/// </summary>
public sealed partial class SupportJobDefinitionRegistry(
    IServiceProvider serviceProvider,
    ITenantDatabaseResolver resolver,
    IOptions<SupportJobsOptions> jobsOptions,
    ILogger<SupportJobDefinitionRegistry> logger)
{
    private readonly IServiceProvider _serviceProvider = serviceProvider;
    private readonly ITenantDatabaseResolver _resolver = resolver;
    private readonly SupportJobsOptions _jobsOptions = jobsOptions.Value;
    private readonly ILogger<SupportJobDefinitionRegistry> _logger = logger;

    /// <summary>
    /// 确保指定租户的任务定义已种子化并按当前 Manifest / 许可刷新。
    /// 新租户（零定义）全量插入；存量租户非破坏性刷新 + 许可启停联动。
    /// </summary>
    /// <param name="tenantId">租户 Id。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>本次新插入的定义条数。</returns>
    public async Task<int> EnsureTenantSeededAsync(long tenantId, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(tenantId);

        var manifests = _serviceProvider.GetRequiredService<IReadOnlyList<ModuleManifest>>();
        var handlerCodes = _serviceProvider.GetServices<IJobHandler>()
            .Select(static h => h.TaskCode)
            .ToHashSet(StringComparer.Ordinal);
        var licenseService = _serviceProvider.GetService<IModuleLicenseService>();
        var now = DateTimeOffset.UtcNow;

        await using var db = CreateContext(tenantId);
        var existing = await db.JobDefinitions.ToListAsync(cancellationToken).ConfigureAwait(false);
        var existingByCode = existing.ToDictionary(static d => d.TaskCode, StringComparer.Ordinal);
        var inserted = 0;

        foreach (var manifest in manifests)
        {
            if (manifest.Jobs.Count == 0 && manifest.ServiceJobs.Count == 0)
            {
                continue;
            }

            // 模块许可联动：无许可服务（开发 / 单测环境）按已授权处理；许可查询失败降级为跳过该模块（不阻断种子）
            var licensed = true;
            if (licenseService is not null)
            {
                try
                {
                    licensed = await licenseService.HasLicenseAsync(tenantId, manifest.ModuleCode, cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    LogLicenseCheckFailed(manifest.ModuleCode, tenantId, ex);
                    continue;
                }
            }

            foreach (var job in manifest.Jobs)
            {
                // 处理器未随部署注册的任务不种子（分部署裁剪；处理器是任务可执行的前提）
                if (!handlerCodes.Contains(job.JobCode))
                {
                    continue;
                }

                if (existingByCode.TryGetValue(job.JobCode, out var row))
                {
                    // 非破坏性刷新：Manifest 来源字段 + 许可 Enabled；Paused / NextRunAt 运行态保留
                    row.RefreshFromManifest(
                        job.Name, manifest.ModuleCode, job.HandlerTypeName, job.DefaultCron,
                        ExecutionMode.Async, _jobsOptions.DefaultTimeoutSeconds, now);
                    row.Enabled = licensed;
                    row.UpdatedAt = now;
                }
                else
                {
                    // Manifest 未声明执行模式，调度触发一律 Async（手动触发按请求指定）；默认 CRON 解析失败在启动期快速失败
                    var nextRunAt = new SixFieldCronSchedule(job.DefaultCron).NextOccurrence(now);
                    db.JobDefinitions.Add(new JobDefinitionEntity(
                        tenantId, job.JobCode, job.Name, manifest.ModuleCode, job.HandlerTypeName,
                        job.DefaultCron, ExecutionMode.Async, _jobsOptions.DefaultTimeoutSeconds,
                        nextRunAt, now)
                    {
                        Enabled = licensed,
                    });
                    existingByCode[job.JobCode] = null!;
                    inserted++;
                }
            }

            // 服务任务（无调度按需提交）：CRON 恒空、NextRunAt 恒 null（调度扫描永不命中），
            // 默认执行模式按声明（SYNC 直执行 / ASYNC 入队）
            foreach (var serviceJob in manifest.ServiceJobs)
            {
                if (!handlerCodes.Contains(serviceJob.JobCode))
                {
                    continue;
                }

                if (existingByCode.TryGetValue(serviceJob.JobCode, out var row))
                {
                    row.RefreshFromManifest(
                        serviceJob.Name, manifest.ModuleCode, serviceJob.HandlerTypeName,
                        string.Empty, serviceJob.Mode, _jobsOptions.DefaultTimeoutSeconds, now);
                    row.Enabled = licensed;
                    row.UpdatedAt = now;
                }
                else
                {
                    db.JobDefinitions.Add(new JobDefinitionEntity(
                        tenantId, serviceJob.JobCode, serviceJob.Name, manifest.ModuleCode,
                        serviceJob.HandlerTypeName, string.Empty, serviceJob.Mode,
                        _jobsOptions.DefaultTimeoutSeconds, nextRunAt: null, now)
                    {
                        Enabled = licensed,
                    });
                    existingByCode[serviceJob.JobCode] = null!;
                    inserted++;
                }
            }
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        if (inserted > 0)
        {
            LogSeeded(tenantId, inserted);
        }

        return inserted;
    }

    /// <summary>按显式租户 Id 构建 SupportTenantDbContext（调度 / Worker 无 HTTP 租户上下文场景复用）。</summary>
    /// <param name="tenantId">租户 Id。</param>
    /// <returns>租户上下文实例。</returns>
    internal SupportTenantDbContext CreateContext(long tenantId)
    {
        var builder = new DbContextOptionsBuilder<SupportTenantDbContext>();
        var interceptors = _serviceProvider.GetServices<Microsoft.EntityFrameworkCore.Diagnostics.IInterceptor>();
        builder.AddInterceptors(interceptors);
        return new SupportTenantDbContext(_resolver, tenantId, builder.Options);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "租户 {TenantId} 任务定义种子完成：新插入 {Inserted} 条")]
    private partial void LogSeeded(long tenantId, int inserted);

    [LoggerMessage(Level = LogLevel.Warning, Message = "租户 {TenantId} 模块 {ModuleCode} 许可查询失败，本轮跳过该模块任务定义刷新")]
    private partial void LogLicenseCheckFailed(string moduleCode, long tenantId, Exception exception);
}
