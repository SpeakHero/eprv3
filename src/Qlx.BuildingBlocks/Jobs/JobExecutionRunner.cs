/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-22 21:55:00 +08:00 中国标准时间 星期四
 * 描述：任务执行运行器——SYNC 当前 HTTP 作用域直执行 / ASYNC Hangfire Worker 内执行，
 *       状态机推进 + 超时终止 + 日志落库 + 终态通知 + 连续失败熔断
 */

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Qlx.Abstractions.Exceptions;
using Qlx.Abstractions.Jobs;
using Qlx.Abstractions.Messages;
using Qlx.Abstractions.Serialization;
using Qlx.BuildingBlocks.Ids;

namespace Qlx.BuildingBlocks.Jobs;

/// <summary>
/// 任务执行运行器。SYNC 模式在当前 HTTP 作用域内直接执行处理器并返回
/// <see cref="CompletedResult"/>；ASYNC 模式在 Hangfire Worker 作用域内执行
/// （QUEUED→RUNNING→终态），超时经 <c>CancellationTokenSource.CancelAfter</c> 强制终止。
/// 业务异常（<see cref="BusinessException"/>）在 SYNC 模式向上透传保持 HTTP 语义化状态码，
/// 落库 FAILED 后再抛；非业务异常捕获落库后返回 Failed 结果。
/// </summary>
public sealed partial class JobExecutionRunner(
    IServiceProvider serviceProvider,
    IJobExecutionStore store,
    IJobBackgroundEnqueuer enqueuer,
    IMessageService messageService,
    IOptions<QlxJobsOptions> options,
    ILogger<JobExecutionRunner> logger)
{
    /// <summary>同租户同 TaskCode 串行锁字典（键 tenant:job:{code}，进程内熔断并发）。</summary>
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> s_serialLocks = new();

    /// <summary>用户取消登记表（执行中任务的取消信号来源，运行器为该生命周期所有者）。</summary>
    private static readonly ConcurrentDictionary<Guid, CancellationTokenSource> s_cancelRegistry = new();

    private readonly IServiceProvider _serviceProvider = serviceProvider;
    private readonly IJobExecutionStore _store = store;
    private readonly IJobBackgroundEnqueuer _enqueuer = enqueuer;
    private readonly IMessageService _messageService = messageService;
    private readonly QlxJobsOptions _options = options.Value;
    private readonly ILogger<JobExecutionRunner> _logger = logger;

    /// <summary>
    /// 取消执行中的任务（ASYNC 运行期用户取消入口）：触发登记的取消源，运行器捕获后落 CANCELED。
    /// </summary>
    /// <param name="executionId">执行 Id。</param>
    /// <returns>任务处于执行中并已发出取消信号返回 true。</returns>
    public static bool TryRequestCancel(Guid executionId)
    {
        if (s_cancelRegistry.TryGetValue(executionId, out var cts))
        {
            cts.Cancel();
            return true;
        }

        return false;
    }

    /// <summary>
    /// SYNC 直执行：在当前调用方作用域内执行处理器（含租户串行锁、超时终止、状态机与日志落库）。
    /// </summary>
    /// <param name="execution">执行快照（PENDING 态）。</param>
    /// <param name="handler">已解析的处理器。</param>
    /// <param name="cancellationToken">调用方取消令牌（HTTP RequestAborted）。</param>
    /// <param name="runtimeParameters">运行期参数（仅内存传递，与持久化参数合并后注入上下文，键冲突时优先）。</param>
    /// <returns>终态与完成结果。</returns>
    /// <exception cref="BusinessException">业务异常透传（先落库 FAILED + 日志再抛）。</exception>
    public async Task<JobDirectExecutionResult> ExecuteDirectAsync(
        JobExecutionSnapshot execution, IJobHandler handler, CancellationToken cancellationToken,
        IReadOnlyDictionary<string, object?>? runtimeParameters = null)
    {
        ArgumentNullException.ThrowIfNull(execution);
        ArgumentNullException.ThrowIfNull(handler);

        var now = DateTimeOffset.UtcNow;
        // SYNC 捷径：PENDING→RUNNING；转移失败说明并发期被取消，按取消处理
        if (!await _store.TryTransitionAsync(execution.TenantId, execution.ExecutionId,
                JobExecutionStatus.Pending, JobExecutionStatus.Running, now, cancellationToken).ConfigureAwait(false))
        {
            return new JobDirectExecutionResult(
                JobExecutionStatus.Canceled,
                new CompletedResult(null, null, null, 0, IsSuccess: false, "任务在执行前已被取消。"));
        }

        var lockKey = BuildSerialLockKey(execution.TenantId, execution.TaskCode);
        var serialLock = s_serialLocks.GetOrAdd(lockKey, static _ => new SemaphoreSlim(1, 1));

        // 【豁免点】超时控制：运行器是本次执行生命周期的所有者，超时取消源由其创建并最终释放（规范 §6.13.2.4）
        using var timeoutCts = CreateTimeoutCts(execution.TimeoutSeconds, cancellationToken);
        var stopwatch = Stopwatch.StartNew();
        await serialLock.WaitAsync(timeoutCts.Token).ConfigureAwait(false);
        try
        {
            var context = BuildContext(execution, timeoutCts.Token, runtimeParameters);
            s_cancelRegistry[execution.ExecutionId] = timeoutCts;
            try
            {
                var result = await handler.ExecuteAsync(context, timeoutCts.Token).ConfigureAwait(false);
                stopwatch.Stop();
                var duration = stopwatch.ElapsedMilliseconds;

                await AppendLogAsync(execution, JobLogLevel.Info,
                    result.IsSuccess ? "SYNC 执行成功" : "SYNC 执行完成（处理器返回失败）",
                    null, null, cancellationToken).ConfigureAwait(false);

                var status = result.IsSuccess ? JobExecutionStatus.Success : JobExecutionStatus.Failed;
                await CompleteAsync(execution, status, result, duration, cancellationToken).ConfigureAwait(false);
                return new JobDirectExecutionResult(status, WrapResult(result, duration, status));
            }
            catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                // 超时终止（非调用方取消）：落 TIMEOUT 终态
                stopwatch.Stop();
                var timeoutStatus = await HandleTimeoutAsync(execution, stopwatch.ElapsedMilliseconds, cancellationToken)
                    .ConfigureAwait(false);
                return new JobDirectExecutionResult(
                    timeoutStatus,
                    new CompletedResult(null, null, null, stopwatch.ElapsedMilliseconds, IsSuccess: false,
                        $"任务超时（{execution.TimeoutSeconds}s）强制终止"));
            }
            catch (OperationCanceledException)
            {
                // 调用方令牌取消：落 CANCELED 后向上传播（规范 §6.13.2 禁止吞取消）
                stopwatch.Stop();
                await _store.CompleteAsync(execution.TenantId, execution.ExecutionId,
                    JobExecutionStatus.Running, JobExecutionStatus.Canceled, null, "调用方取消",
                    stopwatch.ElapsedMilliseconds, DateTimeOffset.UtcNow, cancellationToken).ConfigureAwait(false);
                throw;
            }
            catch (BusinessException ex)
            {
                // SYNC 业务异常语义：先落库 FAILED + 错误日志，再向上透传（HTTP 404/403/409/422）
                stopwatch.Stop();
                await FailExecutionAsync(execution, ex.Message, ex.StackTrace, stopwatch.ElapsedMilliseconds,
                    cancellationToken).ConfigureAwait(false);
                throw;
            }
            catch (Exception ex)
            {
                // 非业务异常：捕获落库，返回 Failed 响应不抛
                stopwatch.Stop();
                LogSyncUnexpected(execution.TaskCode, execution.ExecutionId, ex);
                await FailExecutionAsync(execution, ex.Message, ex.StackTrace, stopwatch.ElapsedMilliseconds,
                    cancellationToken).ConfigureAwait(false);
                return new JobDirectExecutionResult(
                    JobExecutionStatus.Failed,
                    new CompletedResult(null, null, null, stopwatch.ElapsedMilliseconds, IsSuccess: false, ex.Message));
            }
            finally
            {
                _ = s_cancelRegistry.TryRemove(execution.ExecutionId, out _);
            }
        }
        finally
        {
            serialLock.Release();
        }
    }

    /// <summary>
    /// ASYNC Worker 执行：Hangfire Worker 作用域内推进 QUEUED→RUNNING→终态。
    /// </summary>
    /// <param name="executionId">执行 Id。</param>
    /// <param name="tenantId">租户 Id（提交期捕获，Worker 无 HTTP 租户上下文）。</param>
    /// <param name="taskCode">任务编码。</param>
    /// <param name="workerToken">Worker 令牌（宿主停机时由 Hangfire 注入）。</param>
    public async Task ExecuteAsync(Guid executionId, long tenantId, string taskCode, CancellationToken workerToken)
    {
        var execution = await _store.GetAsync(tenantId, executionId, workerToken).ConfigureAwait(false);
        if (execution is null)
        {
            LogExecutionMissing(taskCode, executionId);
            return;
        }

        var now = DateTimeOffset.UtcNow;
        if (!await _store.TryTransitionAsync(tenantId, executionId,
                execution.Status, JobExecutionStatus.Running, now, workerToken).ConfigureAwait(false))
        {
            // 已被取消 / 终态：跳过执行（幂等保护）
            LogExecutionSkipped(taskCode, executionId, execution.Status);
            return;
        }

        var lockKey = BuildSerialLockKey(tenantId, taskCode);
        var serialLock = s_serialLocks.GetOrAdd(lockKey, static _ => new SemaphoreSlim(1, 1));

        // 【豁免点】超时控制：运行器是本次执行生命周期的所有者（规范 §6.13.2.4）
        using var timeoutCts = CreateTimeoutCts(execution.TimeoutSeconds, workerToken);
        var stopwatch = Stopwatch.StartNew();
        await serialLock.WaitAsync(timeoutCts.Token).ConfigureAwait(false);
        try
        {
            var handler = ResolveHandler(taskCode);
            var context = BuildContext(execution, timeoutCts.Token);
            s_cancelRegistry[executionId] = timeoutCts;
            try
            {
                await PushProgressAsync(execution, 5, "任务开始执行", workerToken).ConfigureAwait(false);
                var result = await handler.ExecuteAsync(context, timeoutCts.Token).ConfigureAwait(false);
                stopwatch.Stop();
                var duration = stopwatch.ElapsedMilliseconds;

                var status = result.IsSuccess ? JobExecutionStatus.Success : JobExecutionStatus.Failed;
                await AppendLogAsync(execution, JobLogLevel.Info,
                    status == JobExecutionStatus.Success ? "ASYNC 执行成功" : "ASYNC 执行完成（处理器返回失败）",
                    null, null, workerToken).ConfigureAwait(false);
                await CompleteAsync(execution, status, result, duration, workerToken).ConfigureAwait(false);
                await PushTerminalEventAsync(execution, status, workerToken).ConfigureAwait(false);
                if (status == JobExecutionStatus.Failed)
                {
                    await CheckCircuitBreakerAsync(execution, workerToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !workerToken.IsCancellationRequested)
            {
                stopwatch.Stop();
                var outcome = await HandleTimeoutAsync(execution, stopwatch.ElapsedMilliseconds, workerToken)
                    .ConfigureAwait(false);
                if (outcome == JobExecutionStatus.Timeout)
                {
                    await MaybeRetryOnTimeoutAsync(execution, workerToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (!workerToken.IsCancellationRequested)
            {
                // 用户取消（登记表触发）：落 CANCELED + 通知
                stopwatch.Stop();
                await _store.CompleteAsync(tenantId, executionId, JobExecutionStatus.Running,
                    JobExecutionStatus.Canceled, null, "用户取消", stopwatch.ElapsedMilliseconds,
                    DateTimeOffset.UtcNow, workerToken).ConfigureAwait(false);
                await AppendLogAsync(execution, JobLogLevel.Warn, "任务被用户取消", null, null, workerToken)
                    .ConfigureAwait(false);
                await PushTerminalEventAsync(execution, JobExecutionStatus.Canceled, workerToken).ConfigureAwait(false);
            }
            catch (BusinessException ex)
            {
                stopwatch.Stop();
                await FailExecutionAsync(execution, ex.Message, ex.StackTrace, stopwatch.ElapsedMilliseconds,
                    workerToken).ConfigureAwait(false);
                await MaybeRetryOrNotifyFailureAsync(execution, workerToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                LogAsyncUnexpected(taskCode, executionId, ex);
                await FailExecutionAsync(execution, ex.Message, ex.StackTrace, stopwatch.ElapsedMilliseconds,
                    workerToken).ConfigureAwait(false);
                await MaybeRetryOrNotifyFailureAsync(execution, workerToken).ConfigureAwait(false);
            }
            finally
            {
                _ = s_cancelRegistry.TryRemove(executionId, out _);
            }
        }
        finally
        {
            serialLock.Release();
        }
    }

    private IJobHandler ResolveHandler(string taskCode)
    {
        // 当前作用域（HTTP 请求 / Worker 独立作用域）内解析处理器，处理器可注入租户级依赖
        return _serviceProvider.GetServices<IJobHandler>()
                .FirstOrDefault(h => h.TaskCode == taskCode)
            ?? throw new NotFoundException("任务处理器", taskCode);
    }

    private static CancellationTokenSource CreateTimeoutCts(int timeoutSeconds, CancellationToken linked)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(linked);
        if (timeoutSeconds > 0)
        {
            cts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
        }

        return cts;
    }

    private static JobContext BuildContext(
        JobExecutionSnapshot execution, CancellationToken cancellationToken,
        IReadOnlyDictionary<string, object?>? runtimeParameters = null)
    {
        var parameters = execution.ParametersJson is null
            ? new Dictionary<string, object?>()
            : JsonSerializer.Deserialize<Dictionary<string, object?>>(execution.ParametersJson, QlxJsonDefaults.Default)
                ?? new Dictionary<string, object?>();

        // JSON 往返后值一律为 JsonElement：还原为基本类型（string / long / double / bool / null），
        // 处理器按强类型读取（is string / is long）；Object/Array 保持 JsonElement 由处理器自行反序列化
        foreach (var key in parameters.Keys.ToArray())
        {
            parameters[key] = UnwrapJsonElement(parameters[key]);
        }

        // 运行期参数仅内存传递：与持久化参数合并（键冲突时运行期优先）
        if (runtimeParameters is { Count: > 0 })
        {
            foreach (var pair in runtimeParameters)
            {
                parameters[pair.Key] = pair.Value;
            }
        }

        return new JobContext
        {
            ExecutionId = execution.ExecutionId,
            TaskCode = execution.TaskCode,
            TenantId = Ids.GuidIdMapper.ToGuid(execution.TenantId),
            Mode = execution.Mode,
            Parameters = parameters,
            CancellationToken = cancellationToken,
        };
    }

    /// <summary>
    /// JSON 往返后的值还原：JsonElement → 基本类型（string / long / double / bool / null）。
    /// Object / Array 保持 JsonElement 原样，由处理器按需要自行反序列化；非 JsonElement 值原样返回。
    /// </summary>
    private static object? UnwrapJsonElement(object? value)
        => value is JsonElement element
            ? element.ValueKind switch
            {
                JsonValueKind.String => element.GetString(),
                // 显式 (object) 装箱：避免三元公共类型推断为 double 丢失整数精确类型（处理器按 is long 读取）
                JsonValueKind.Number => element.TryGetInt64(out var l) ? (object)l : element.GetDouble(),
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Null or JsonValueKind.Undefined => null,
                _ => element,
            }
            : value;

    private static string BuildSerialLockKey(long tenantId, string taskCode)
        => $"tenant:job:{taskCode}:{tenantId}";

    private static CompletedResult WrapResult(CompletedResult result, long durationMs, JobExecutionStatus status)
        => result with { ExecutionDurationMs = durationMs, IsSuccess = status == JobExecutionStatus.Success };

    /// <summary>解析推送接收人：手动触发推发起人；系统调度推系统操作人。</summary>
    private static string ResolveRecipient(JobExecutionSnapshot execution)
        => string.IsNullOrWhiteSpace(execution.InitiatorUserId) ? JobSubmitMetadata.SystemUserId : execution.InitiatorUserId;

    private async Task<JobExecutionStatus> HandleTimeoutAsync(
        JobExecutionSnapshot execution, long durationMs, CancellationToken cancellationToken)
    {
        LogTimeout(execution.TaskCode, execution.ExecutionId, execution.TimeoutSeconds);
        await _store.CompleteAsync(execution.TenantId, execution.ExecutionId, JobExecutionStatus.Running,
            JobExecutionStatus.Timeout, null, $"任务超时（{execution.TimeoutSeconds}s）强制终止", durationMs,
            DateTimeOffset.UtcNow, cancellationToken).ConfigureAwait(false);
        await AppendLogAsync(execution, JobLogLevel.Error,
            $"任务超时（{execution.TimeoutSeconds}s）被强制终止", null, null, cancellationToken).ConfigureAwait(false);
        await PushTerminalEventAsync(execution, JobExecutionStatus.Timeout, cancellationToken).ConfigureAwait(false);
        return JobExecutionStatus.Timeout;
    }

    private async Task MaybeRetryOnTimeoutAsync(JobExecutionSnapshot execution, CancellationToken cancellationToken)
    {
        if (!_options.RetryOnTimeout || execution.RetryCount >= 1)
        {
            return;
        }

        // 超时重试一次：新登记同参数执行（RetryCount+1）并重新入队
        var retryId = Guid.NewGuid();
        await _store.CreateAsync(new JobExecutionCreation(
            retryId, execution.TenantId, execution.TaskCode, execution.Mode,
            JobExecutionStatus.Pending, execution.RetryCount + 1,
            JobSubmitMetadata.TriggerSchedule, null, null, execution.ParametersJson,
            execution.TimeoutSeconds, execution.RetryPolicyJson),
            cancellationToken).ConfigureAwait(false);
        var hangfireJobId = _enqueuer.Enqueue(retryId, execution.TenantId, execution.TaskCode);
        await _store.TryTransitionAsync(execution.TenantId, retryId, JobExecutionStatus.Pending,
            JobExecutionStatus.Queued, DateTimeOffset.UtcNow, cancellationToken).ConfigureAwait(false);
        await _store.SaveHangfireJobIdAsync(execution.TenantId, retryId, hangfireJobId, cancellationToken)
            .ConfigureAwait(false);
        LogTimeoutRetried(execution.TaskCode, execution.ExecutionId, retryId);
    }

    /// <summary>
    /// ASYNC 失败后的重试或终态通知（方案 D19 默认重试策略：MaxRetries 内按退避策略延迟重新入队；
    /// 重试额度耗尽才推 TASK_FAILED 终态事件）。重试不适用于 SYNC（调用方同步等待，失败即时返回）。
    /// </summary>
    /// <param name="execution">执行快照（已落 FAILED 终态）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    private async Task MaybeRetryOrNotifyFailureAsync(JobExecutionSnapshot execution, CancellationToken cancellationToken)
    {
        var policy = ResolveRetryPolicy(execution);
        if (execution.RetryCount < policy.MaxRetries)
        {
            var retryId = Guid.NewGuid();
            var delay = RetryDelayCalculator.ComputeDelay(policy, execution.RetryCount + 1);
            await _store.CreateAsync(new JobExecutionCreation(
                retryId, execution.TenantId, execution.TaskCode, execution.Mode,
                JobExecutionStatus.Pending, execution.RetryCount + 1,
                JobSubmitMetadata.TriggerRetry, execution.InitiatorUserId, null,
                execution.ParametersJson, execution.TimeoutSeconds, execution.RetryPolicyJson),
                cancellationToken).ConfigureAwait(false);
            var hangfireJobId = _enqueuer.EnqueueIn(delay, retryId, execution.TenantId, execution.TaskCode);
            await _store.TryTransitionAsync(execution.TenantId, retryId, JobExecutionStatus.Pending,
                JobExecutionStatus.Queued, DateTimeOffset.UtcNow, cancellationToken).ConfigureAwait(false);
            await _store.SaveHangfireJobIdAsync(execution.TenantId, retryId, hangfireJobId, cancellationToken)
                .ConfigureAwait(false);
            await AppendLogAsync(execution, JobLogLevel.Warn,
                $"第 {execution.RetryCount + 1}/{policy.MaxRetries} 次重试已排程（{delay.TotalSeconds:F0}s 后执行）",
                null, null, cancellationToken).ConfigureAwait(false);
            await PushSafeAsync(execution, new PushRequest(
                ResolveRecipient(execution),
                "TASK_RETRYING",
                $"任务 {execution.TaskCode} 执行失败，第 {execution.RetryCount + 1}/{policy.MaxRetries} 次重试已排程（executionId={retryId:N}）",
                MessageChannel.InApp,
                MessagePriority.Normal,
                new MessageBizRef("job_execution", retryId.ToString("N"))),
                cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await PushTerminalEventAsync(execution, JobExecutionStatus.Failed, cancellationToken).ConfigureAwait(false);
        }

        // 无论是否重试，连续失败都参与熔断统计（重试期间的失败同样计入）
        await CheckCircuitBreakerAsync(execution, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>解析执行的重试策略：优先执行记录的覆盖策略（JSON），缺省用契约默认值。</summary>
    /// <param name="execution">执行快照。</param>
    /// <returns>重试策略。</returns>
    private static RetryPolicy ResolveRetryPolicy(JobExecutionSnapshot execution)
        => string.IsNullOrWhiteSpace(execution.RetryPolicyJson)
            ? new RetryPolicy()
            : JsonSerializer.Deserialize<RetryPolicy>(execution.RetryPolicyJson, QlxJsonDefaults.Default)
                ?? new RetryPolicy();

    private async Task FailExecutionAsync(
        JobExecutionSnapshot execution, string error, string? stackTrace, long durationMs,
        CancellationToken cancellationToken)
    {
        await _store.CompleteAsync(execution.TenantId, execution.ExecutionId, JobExecutionStatus.Running,
            JobExecutionStatus.Failed, null, error, durationMs, DateTimeOffset.UtcNow, cancellationToken)
            .ConfigureAwait(false);
        await AppendLogAsync(execution, JobLogLevel.Error, $"任务执行失败：{error}", error, stackTrace, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task CompleteAsync(
        JobExecutionSnapshot execution, JobExecutionStatus status, CompletedResult result, long durationMs,
        CancellationToken cancellationToken)
    {
        var resultJson = result.ResultJson is null
            ? null
            : JsonSerializer.Serialize(result.ResultJson, QlxJsonDefaults.Default);
        await _store.CompleteAsync(execution.TenantId, execution.ExecutionId, JobExecutionStatus.Running, status,
            resultJson, result.ErrorMessage, durationMs, DateTimeOffset.UtcNow, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task AppendLogAsync(
        JobExecutionSnapshot execution, JobLogLevel level, string message, string? detail, string? stackTrace,
        CancellationToken cancellationToken)
    {
        await _store.AppendLogsAsync(
            [
                new JobLogDraft(execution.ExecutionId, execution.TenantId, execution.TaskCode, level, message,
                    detail, stackTrace, DateTimeOffset.UtcNow),
            ],
            cancellationToken).ConfigureAwait(false);
    }

    private async Task PushProgressAsync(
        JobExecutionSnapshot execution, int percent, string message, CancellationToken cancellationToken)
    {
        await PushSafeAsync(execution, new PushRequest(
            ResolveRecipient(execution),
            "task.progress",
            $"{message}（{percent}%）",
            MessageChannel.InApp,
            MessagePriority.Normal,
            new MessageBizRef("job_execution", execution.ExecutionId.ToString("N"))),
            cancellationToken).ConfigureAwait(false);
    }

    private async Task PushTerminalEventAsync(
        JobExecutionSnapshot execution, JobExecutionStatus status, CancellationToken cancellationToken)
    {
        var (subject, priority) = status switch
        {
            JobExecutionStatus.Success => ("TASK_COMPLETED", MessagePriority.Normal),
            JobExecutionStatus.Failed => ("TASK_FAILED", MessagePriority.High),
            JobExecutionStatus.Timeout => ("TASK_TIMEOUT", MessagePriority.High),
            _ => ("TASK_CANCELED", MessagePriority.Normal),
        };

        await PushSafeAsync(execution, new PushRequest(
            ResolveRecipient(execution),
            subject,
            $"任务 {execution.TaskCode} 终态：{status}（executionId={execution.ExecutionId:N}）",
            MessageChannel.InApp,
            priority,
            new MessageBizRef("job_execution", execution.ExecutionId.ToString("N"))),
            cancellationToken).ConfigureAwait(false);
    }

    private async Task PushSafeAsync(JobExecutionSnapshot execution, PushRequest request, CancellationToken cancellationToken)
    {
        try
        {
            await _messageService.PushAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // 通知链路故障不阻断任务执行链路（NoOp 实现阶段恒走本分支降级）
            LogPushFailed(execution.TaskCode, execution.ExecutionId, ex);
        }
    }

    private async Task CheckCircuitBreakerAsync(JobExecutionSnapshot execution, CancellationToken cancellationToken)
    {
        var failures = await _store.CountLeadingFailuresAsync(
            execution.TenantId, execution.TaskCode, _options.FailureThreshold, cancellationToken).ConfigureAwait(false);
        if (failures < _options.FailureThreshold)
        {
            return;
        }

        await _store.PauseDefinitionAsync(execution.TenantId, execution.TaskCode, cancellationToken)
            .ConfigureAwait(false);
        LogCircuitBreakerTripped(execution.TaskCode, failures);
        await PushSafeAsync(execution, new PushRequest(
            JobSubmitMetadata.SystemUserId,
            "SYSTEM_ALERT",
            $"任务 {execution.TaskCode} 连续失败 {failures} 次，已自动暂停调度（阈值 {_options.FailureThreshold}），请运维介入。",
            MessageChannel.InApp,
            MessagePriority.Urgent,
            new MessageBizRef("job_definition", execution.TaskCode)), cancellationToken).ConfigureAwait(false);
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "SYNC 任务执行出现非业务异常：{TaskCode} / {ExecutionId}")]
    private partial void LogSyncUnexpected(string taskCode, Guid executionId, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "ASYNC 任务执行出现非业务异常：{TaskCode} / {ExecutionId}")]
    private partial void LogAsyncUnexpected(string taskCode, Guid executionId, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "任务执行记录缺失，跳过：{TaskCode} / {ExecutionId}")]
    private partial void LogExecutionMissing(string taskCode, Guid executionId);

    [LoggerMessage(Level = LogLevel.Information, Message = "任务状态已变更（{From}），跳过重复执行：{TaskCode} / {ExecutionId}")]
    private partial void LogExecutionSkipped(string taskCode, Guid executionId, JobExecutionStatus from);

    [LoggerMessage(Level = LogLevel.Warning, Message = "任务超时强制终止：{TaskCode} / {ExecutionId}（{TimeoutSeconds}s）")]
    private partial void LogTimeout(string taskCode, Guid executionId, int timeoutSeconds);

    [LoggerMessage(Level = LogLevel.Information, Message = "任务超时已按 RetryOnTimeout 重新入队：{TaskCode} / {ExecutionId} → {RetryExecutionId}")]
    private partial void LogTimeoutRetried(string taskCode, Guid executionId, Guid retryExecutionId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "任务终态通知推送失败（不阻断执行链路）：{TaskCode} / {ExecutionId}")]
    private partial void LogPushFailed(string taskCode, Guid executionId, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "任务连续失败触发熔断，已自动暂停：{TaskCode}（失败 {Failures} 次）")]
    private partial void LogCircuitBreakerTripped(string taskCode, int failures);
}
