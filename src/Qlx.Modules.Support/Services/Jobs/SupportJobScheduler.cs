/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-22 23:05:00 +08:00 中国标准时间 星期四
 * 描述：IJobScheduler 的 Support 模块实现（方案 D20 统一提交五步）：
 *       ① 校验注册与定义 ② 幂等键防重 ③ 登记 PENDING ④ SYNC 直执行 / ASYNC 入队 ⑤ 推进 QUEUED 并返回。
 *       SYNC 模式 BusinessException 仍向上透传（HTTP 语义化状态码），同时已由运行器落 FAILED + 错误日志。
 */

using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Qlx.Abstractions.Exceptions;
using Qlx.Abstractions.Jobs;
using Qlx.Abstractions.Serialization;
using Qlx.Abstractions.Tenancy;
using Qlx.BuildingBlocks.Ids;
using Qlx.BuildingBlocks.Jobs;
using Qlx.Modules.Support.Options;

namespace Qlx.Modules.Support.Services.Jobs;

/// <summary>
/// 后台任务调度器（Support 模块实现）。租户 Id 经 <see cref="GuidIdMapper"/> 在契约 Guid 与实体 long 间映射；
/// 执行记录 / 状态推进 / 日志全部经 <see cref="IJobExecutionStore"/> 落租户库 job_ 表。
/// </summary>
public sealed partial class SupportJobScheduler(
    IJobHandlerFactory handlerFactory,
    IJobExecutionStore store,
    IJobBackgroundEnqueuer enqueuer,
    JobExecutionRunner runner,
    ITenantContext tenantContext,
    IOptions<SupportJobsOptions> jobsOptions,
    ILogger<SupportJobScheduler> logger) : IJobScheduler
{
    private readonly IJobHandlerFactory _handlerFactory = handlerFactory;
    private readonly IJobExecutionStore _store = store;
    private readonly IJobBackgroundEnqueuer _enqueuer = enqueuer;
    private readonly JobExecutionRunner _runner = runner;
    private readonly ITenantContext _tenantContext = tenantContext;
    private readonly SupportJobsOptions _jobsOptions = jobsOptions.Value;
    private readonly ILogger<SupportJobScheduler> _logger = logger;

    /// <summary>
    /// 提交任务（D20 五步）：校验 → 幂等 → 登记 PENDING → SYNC 直执行 / ASYNC 入队 → 返回响应。
    /// </summary>
    /// <param name="request">提交请求。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>提交响应（SYNC 模式 CompletedResult 已填充）。</returns>
    /// <exception cref="ValidationException">TenantId 无法映射或请求非法。</exception>
    /// <exception cref="NotFoundException">任务处理器或任务定义未注册。</exception>
    /// <exception cref="ForbiddenException">任务定义已停用（许可 / 管理员停用）。</exception>
    /// <exception cref="ConflictException">任务定义因连续失败已熔断暂停。</exception>
    /// <exception cref="BusinessException">SYNC 模式下处理器抛出的业务异常（已落 FAILED 后透传）。</exception>
    public async Task<TaskSubmitResponse> SubmitAsync(TaskSubmitRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.TaskCode);

        // ① 校验：TenantId 映射 + 处理器注册 + 定义存在与可用
        if (!GuidIdMapper.TryToLong(request.TenantId, out var tenantId))
        {
            throw new ValidationException(
                [new FieldError("tenantId", "TenantId 非法（无法映射为租户标识）。")]);
        }

        var handler = _handlerFactory.Resolve(request.TaskCode);
        var definition = await _store.GetDefinitionAsync(tenantId, request.TaskCode, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new NotFoundException("任务定义", request.TaskCode);
        if (!definition.Enabled)
        {
            throw new ForbiddenException($"任务 {request.TaskCode} 已停用（许可或管理员停用），禁止提交。");
        }

        if (definition.Paused)
        {
            throw new ConflictException($"任务 {request.TaskCode} 因连续失败已自动暂停，请先恢复调度后重试。", "JOB_PAUSED");
        }

        // ② 幂等防重：窗口内同 TaskCode + IdempotencyKey 直接返回既有执行
        if (!string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            var windowStart = DateTimeOffset.UtcNow - TimeSpan.FromSeconds(_jobsOptions.IdempotencyWindowSeconds);
            var existing = await _store.GetByIdempotencyKeyAsync(
                    tenantId, request.TaskCode, request.IdempotencyKey, windowStart, cancellationToken)
                .ConfigureAwait(false);
            if (existing is not null)
            {
                LogIdempotentHit(request.TaskCode, existing.ExecutionId);
                return MapResponse(existing);
            }
        }

        var timeoutSeconds = request.TimeoutSeconds ?? definition.TimeoutSeconds;

        // 运行期参数仅内存传递：ASYNC 入队（Hangfire 序列化轮次）无法承载，直接拒绝
        if (request.Mode == ExecutionMode.Async && request.RuntimeParameters is { Count: > 0 })
        {
            throw new ValidationException(
                [new FieldError("runtimeParameters", "ASYNC 任务禁止携带运行期参数（仅内存传递，无法跨入队序列化）。")]);
        }

        var parametersJson = request.Parameters is null
            ? null
            : JsonSerializer.Serialize(request.Parameters, QlxJsonDefaults.Default);
        var retryPolicyJson = request.RetryPolicyOverride is null
            ? null
            : JsonSerializer.Serialize(request.RetryPolicyOverride, QlxJsonDefaults.Default);
        var triggerSource = request.Parameters is not null
            && request.Parameters.TryGetValue(JobSubmitMetadata.TriggerSourceParameterKey, out var triggerValue)
            && triggerValue is string triggerText
                ? triggerText
                : JobSubmitMetadata.TriggerManual;

        // ③ 登记 PENDING
        var executionId = Guid.NewGuid();
        await _store.CreateAsync(new JobExecutionCreation(
            executionId, tenantId, request.TaskCode, request.Mode,
            JobExecutionStatus.Pending, RetryCount: 0, triggerSource, request.InitiatorUserId,
            request.IdempotencyKey, parametersJson, timeoutSeconds, retryPolicyJson),
            cancellationToken).ConfigureAwait(false);

        // ④⑤ 分模式执行
        if (request.Mode == ExecutionMode.Sync)
        {
            var execution = await _store.GetAsync(tenantId, executionId, cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidOperationException($"任务执行记录登记后不可见：{executionId}");
            var result = await _runner.ExecuteDirectAsync(
                execution, handler, cancellationToken, request.RuntimeParameters).ConfigureAwait(false);
            return new TaskSubmitResponse(
                executionId, ExecutionMode.Sync, result.Status, execution.QueuedAt, result.Result);
        }

        var queuedAt = DateTimeOffset.UtcNow;
        var hangfireJobId = _enqueuer.Enqueue(executionId, tenantId, request.TaskCode);
        if (!await _store.TryTransitionAsync(tenantId, executionId, JobExecutionStatus.Pending,
                JobExecutionStatus.Queued, queuedAt, cancellationToken).ConfigureAwait(false))
        {
            // 理论不可达（刚登记）；防御：入队后转移失败按已取消处理
            _ = _enqueuer.Delete(hangfireJobId);
            throw new ConflictException($"任务执行登记已被并发取消：{executionId}", "JOB_ALREADY_CANCELED");
        }

        await _store.SaveHangfireJobIdAsync(tenantId, executionId, hangfireJobId, cancellationToken)
            .ConfigureAwait(false);
        return new TaskSubmitResponse(executionId, ExecutionMode.Async, JobExecutionStatus.Queued, queuedAt, null);
    }

    /// <summary>
    /// 取消任务：PENDING / QUEUED 删队列 + 落 CANCELED；RUNNING 发运行期取消信号（落 CANCELED）；
    /// 终态任务拒绝取消。
    /// </summary>
    /// <param name="executionId">执行 Id。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public async Task CancelAsync(Guid executionId, CancellationToken cancellationToken)
    {
        var tenantId = RequireTenantId();
        var execution = await _store.GetAsync(tenantId, executionId, cancellationToken).ConfigureAwait(false)
            ?? throw new NotFoundException("任务执行", executionId.ToString("N"));

        switch (execution.Status)
        {
            case JobExecutionStatus.Pending:
            case JobExecutionStatus.Queued:
                if (execution.HangfireJobId is not null)
                {
                    // 队列删除尽力而为：作业已被 Worker 取走时删除失败，由状态转移原子性兜底
                    _ = _enqueuer.Delete(execution.HangfireJobId);
                }

                if (!await _store.TryTransitionAsync(tenantId, executionId, execution.Status,
                        JobExecutionStatus.Canceled, DateTimeOffset.UtcNow, cancellationToken).ConfigureAwait(false))
                {
                    throw new ConflictException($"任务状态已变更（当前 {execution.Status}），取消失败。", "JOB_STATE_CHANGED");
                }

                break;

            case JobExecutionStatus.Running:
                if (!JobExecutionRunner.TryRequestCancel(executionId))
                {
                    throw new ConflictException(
                        "取消信号未送达：任务可能已结束，或在其他节点执行（多实例取消联动为后续增强）。", "JOB_CANCEL_UNDELIVERED");
                }

                break;

            default:
                throw new ConflictException($"任务已终态（{execution.Status}），无法取消。", "JOB_TERMINAL");
        }
    }

    /// <summary>
    /// 查询任务执行状态；不存在返回 null。
    /// </summary>
    /// <param name="executionId">执行 Id。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public async Task<TaskSubmitResponse?> GetStatusAsync(Guid executionId, CancellationToken cancellationToken)
    {
        var tenantId = RequireTenantId();
        var execution = await _store.GetAsync(tenantId, executionId, cancellationToken).ConfigureAwait(false);
        return execution is null ? null : MapResponse(execution);
    }

    /// <summary>获取当前租户 Id；租户上下文未解析抛未授权。</summary>
    /// <returns>租户 Id。</returns>
    private long RequireTenantId()
        => _tenantContext.TenantId is not null && _tenantContext.TenantId.Value > 0
            ? _tenantContext.TenantId.Value
            : throw new UnauthorizedException("租户上下文未初始化，禁止操作后台任务。");

    /// <summary>执行快照 → 提交响应（终态填充 CompletedResult）。</summary>
    /// <param name="execution">执行快照。</param>
    /// <returns>提交响应。</returns>
    private static TaskSubmitResponse MapResponse(JobExecutionSnapshot execution)
    {
        CompletedResult? completed = null;
        if (JobStateMachine.IsTerminal(execution.Status))
        {
            var resultJson = execution.ResultJson is null
                ? null
                : (JsonElement?)JsonSerializer.Deserialize<JsonElement>(execution.ResultJson, QlxJsonDefaults.Default);
            completed = new CompletedResult(
                resultJson, null, null, execution.DurationMs ?? 0,
                execution.Status == JobExecutionStatus.Success, execution.Error);
        }

        return new TaskSubmitResponse(execution.ExecutionId, execution.Mode, execution.Status, execution.QueuedAt, completed);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "幂等键命中，返回既有执行：{TaskCode} / {ExecutionId}")]
    private partial void LogIdempotentHit(string taskCode, Guid executionId);
}
