/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-22 22:10:00 +08:00 中国标准时间 星期四
 * 描述：调度扫描后台服务——周期比对 job_definitions 下次执行时间，
 *       进程内信号量（tenant:job:dispatch:{code}）+ 存储层乐观锁认领双重防多实例重复触发
 */

using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Qlx.Abstractions.Jobs;
using Qlx.BuildingBlocks.Ids;

namespace Qlx.BuildingBlocks.Jobs;

/// <summary>
/// 调度扫描后台服务。按 <see cref="QlxJobsOptions.DispatchIntervalSeconds"/> 周期扫描到期任务定义，
/// 逐条认领后经 <see cref="IJobScheduler.SubmitAsync"/> 以调度来源提交。
/// 双重防重：① 进程内 SemaphoreSlim（键 <c>tenant:job:dispatch:{code}</c>）防同实例并发 tick；
/// ② 存储层 next_run_at 乐观锁认领防多实例重复触发。
/// 存储抽象未注册（如纯单元测试宿主）时跳过。启动期先做 TaskCode 全局唯一校验，重复即启动失败。
/// </summary>
public sealed partial class JobDispatchHostedService(
    IServiceProvider serviceProvider,
    IOptions<QlxJobsOptions> options,
    ILogger<JobDispatchHostedService> logger) : BackgroundService
{
    /// <summary>LoggerMessage 源生成器要求字段。</summary>
    private readonly ILogger<JobDispatchHostedService> _logger = logger;

    private readonly IServiceProvider _serviceProvider = serviceProvider;
    private readonly QlxJobsOptions _options = options.Value;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> s_dispatchLocks = new();

    /// <summary>
    /// 启动期校验：TaskCode 全局唯一（重复注册视为装配错误，启动失败快速暴露）。
    /// </summary>
    /// <param name="cancellationToken">启动取消令牌。</param>
    /// <returns>启动任务。</returns>
    public override Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        scope.ServiceProvider.GetRequiredService<IJobHandlerFactory>().ValidateUniqueTaskCodes();
        return base.StartAsync(cancellationToken);
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield();

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(_options.DispatchIntervalSeconds));
        do
        {
            try
            {
                await DispatchDueJobsAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                LogDispatchError(ex);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
    }

    private async Task DispatchDueJobsAsync(CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        await using var scope = _serviceProvider.CreateAsyncScope();
        var store = scope.ServiceProvider.GetService<IJobExecutionStore>();
        var scheduler = scope.ServiceProvider.GetService<IJobScheduler>();
        if (store is null || scheduler is null)
        {
            return;
        }

        var candidates = await store.GetDueDefinitionsAsync(now, _options.DispatchBatchSize, cancellationToken)
            .ConfigureAwait(false);
        foreach (var candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // 进程内调度锁：tenant:job:dispatch:{code}，同实例并发 tick 串行化
            var lockKey = $"tenant:job:dispatch:{candidate.TaskCode}:{candidate.TenantId}";
            var dispatchLock = s_dispatchLocks.GetOrAdd(lockKey, static _ => new SemaphoreSlim(1, 1));
            if (!await dispatchLock.WaitAsync(0, cancellationToken).ConfigureAwait(false))
            {
                continue;
            }

            try
            {
                await DispatchSingleAsync(store, scheduler, candidate, now, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogCandidateFailed(candidate.TaskCode, candidate.TenantId, ex);
            }
            finally
            {
                dispatchLock.Release();
            }
        }
    }

    private async Task DispatchSingleAsync(
        IJobExecutionStore store,
        IJobScheduler scheduler,
        JobDispatchCandidate candidate,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var cron = new SixFieldCronSchedule(candidate.EffectiveCron);
        var nextRunAt = cron.NextOccurrence(now);
        // 乐观锁认领：next_run_at 未被其他实例推进时才提交（分布式防重）
        if (!await store.TryClaimDueJobAsync(
                    candidate.TenantId, candidate.DefinitionId, candidate.PreviousNextRunAt, nextRunAt, cancellationToken)
                .ConfigureAwait(false))
        {
            return;
        }

        LogDispatchTrigger(candidate.TaskCode, candidate.TenantId, nextRunAt);
        _ = await scheduler.SubmitAsync(
            new TaskSubmitRequest(
                candidate.TaskCode,
                GuidIdMapper.ToGuid(candidate.TenantId),
                candidate.DefaultMode,
                InitiatorUserId: null,
                Parameters: new Dictionary<string, object?>
                {
                    [JobSubmitMetadata.TriggerSourceParameterKey] = JobSubmitMetadata.TriggerSchedule,
                },
                TimeoutSeconds: candidate.TimeoutSeconds),
            cancellationToken).ConfigureAwait(false);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "调度触发任务：{TaskCode}（租户 {TenantId}），下次执行 {NextRunAt:O}")]
    private partial void LogDispatchTrigger(string taskCode, long tenantId, DateTimeOffset nextRunAt);

    [LoggerMessage(Level = LogLevel.Warning, Message = "到期任务提交失败（下一轮扫描重试）")]
    private partial void LogDispatchError(Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "到期任务提交失败（下一轮扫描重试）：{TaskCode} / 租户 {TenantId}")]
    private partial void LogCandidateFailed(string taskCode, long tenantId, Exception exception);
}
