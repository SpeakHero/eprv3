/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-22 23:15:00 +08:00 中国标准时间 星期四
 * 描述：后台任务中心门面服务——触发 / 取消 / 状态 / 历史分页 / 日志查询 / 定义列表与启停。
 *       提交链路统一走 IJobScheduler（D20）；查询链路走注入的请求作用域 SupportTenantDbContext（EF 投影禁 N+1）
 */

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Qlx.Abstractions.Domain;
using Qlx.Abstractions.Exceptions;
using Qlx.Abstractions.Jobs;
using Qlx.Abstractions.Tenancy;
using Qlx.BuildingBlocks.Ids;
using Qlx.BuildingBlocks.MultiTenancy;
using Qlx.Modules.Support.Domain;

namespace Qlx.Modules.Support.Services.Jobs;

/// <summary>后台任务中心门面服务契约。</summary>
public interface ISupportJobService
{
    /// <summary>触发任务（SYNC 阻塞等待完成并返回结果；ASYNC 立即返回 QUEUED）。</summary>
    /// <param name="payload">触发入参。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>提交结果。</returns>
    Task<JobSubmitResult> TriggerAsync(JobTriggerPayload payload, CancellationToken cancellationToken);

    /// <summary>取消任务（PENDING / QUEUED 删队列；RUNNING 发运行期取消信号）。</summary>
    /// <param name="executionId">执行 Id。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task CancelAsync(Guid executionId, CancellationToken cancellationToken);

    /// <summary>查询任务执行状态；不存在抛 NotFound。</summary>
    /// <param name="executionId">执行 Id。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>提交结果快照。</returns>
    Task<JobSubmitResult> GetStatusAsync(Guid executionId, CancellationToken cancellationToken);

    /// <summary>分页查询执行历史（当前租户）。</summary>
    /// <param name="taskCode">任务编码过滤（可空）。</param>
    /// <param name="status">状态过滤（可空，不区分大小写）。</param>
    /// <param name="from">登记时间起点（UTC，可空）。</param>
    /// <param name="toTime">登记时间终点（UTC，可空）。</param>
    /// <param name="pageIndex">页码（1 起）。</param>
    /// <param name="pageSize">页大小（1~200）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>分页结果。</returns>
    Task<JobExecutionPage> QueryExecutionsAsync(
        string? taskCode, string? status, DateTimeOffset? from, DateTimeOffset? toTime,
        int pageIndex, int pageSize, CancellationToken cancellationToken);

    /// <summary>按执行 Id 查询执行日志（时间升序）。</summary>
    /// <param name="executionId">执行 Id。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>日志条目列表。</returns>
    Task<IReadOnlyList<JobExecutionLogEntry>> GetLogsAsync(Guid executionId, CancellationToken cancellationToken);

    /// <summary>查询当前租户任务定义清单（含租户级配置覆盖生效值）。</summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>定义清单。</returns>
    Task<IReadOnlyList<JobDefinitionItem>> ListDefinitionsAsync(CancellationToken cancellationToken);

    /// <summary>启用 / 停用任务定义（租户级；启用时同时解除熔断暂停）。</summary>
    /// <param name="taskCode">任务编码。</param>
    /// <param name="enabled">是否启用。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task ToggleDefinitionAsync(string taskCode, bool enabled, CancellationToken cancellationToken);
}

/// <summary>后台任务中心门面服务实现。</summary>
/// <param name="scheduler">任务调度器。</param>
/// <param name="logQuery">执行日志查询。</param>
/// <param name="db">请求作用域租户上下文（租户过滤由全局查询过滤器兜底）。</param>
/// <param name="tenantContext">租户上下文。</param>
/// <param name="userContext">当前用户上下文。</param>
public sealed class SupportJobService(
    IJobScheduler scheduler,
    IJobExecutionLogQuery logQuery,
    SupportTenantDbContext db,
    ITenantContext tenantContext,
    ICurrentUserContext userContext) : ISupportJobService
{
    private readonly IJobScheduler _scheduler = scheduler;
    private readonly IJobExecutionLogQuery _logQuery = logQuery;
    private readonly SupportTenantDbContext _db = db;
    private readonly ITenantContext _tenantContext = tenantContext;
    private readonly ICurrentUserContext _userContext = userContext;

    /// <inheritdoc />
    public async Task<JobSubmitResult> TriggerAsync(JobTriggerPayload payload, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(payload);
        if (_tenantContext.TenantId is not > 0)
        {
            throw new UnauthorizedException("租户上下文未初始化，禁止触发后台任务。");
        }

        if (string.IsNullOrWhiteSpace(_userContext.UserId))
        {
            throw new UnauthorizedException("请先登录后再触发后台任务。");
        }

        var mode = ParseMode(payload.Mode);
        var response = await _scheduler.SubmitAsync(
            new TaskSubmitRequest(
                payload.TaskCode,
                GuidIdMapper.ToGuid(_tenantContext.TenantId.Value),
                mode,
                InitiatorUserId: _userContext.UserId,
                Parameters: payload.Parameters,
                TimeoutSeconds: payload.TimeoutSeconds,
                IdempotencyKey: payload.IdempotencyKey),
            cancellationToken).ConfigureAwait(false);
        return MapResult(response);
    }

    /// <inheritdoc />
    public Task CancelAsync(Guid executionId, CancellationToken cancellationToken)
        => _scheduler.CancelAsync(executionId, cancellationToken);

    /// <inheritdoc />
    public async Task<JobSubmitResult> GetStatusAsync(Guid executionId, CancellationToken cancellationToken)
    {
        var response = await _scheduler.GetStatusAsync(executionId, cancellationToken).ConfigureAwait(false)
            ?? throw new NotFoundException("任务执行", executionId.ToString("N"));
        return MapResult(response);
    }

    /// <inheritdoc />
    public async Task<JobExecutionPage> QueryExecutionsAsync(
        string? taskCode, string? status, DateTimeOffset? from, DateTimeOffset? toTime,
        int pageIndex, int pageSize, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pageIndex);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pageSize);
        if (pageSize > 200)
        {
            throw new ValidationException([new FieldError("pageSize", "页大小不能超过 200。")]);
        }

        var statusFilter = ParseStatus(status);
        var query = _db.JobExecutions.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(taskCode))
        {
            query = query.Where(e => e.TaskCode == taskCode);
        }

        if (statusFilter is not null)
        {
            query = query.Where(e => e.Status == statusFilter);
        }

        if (from is not null)
        {
            query = query.Where(e => e.QueuedAt >= from);
        }

        if (toTime is not null)
        {
            query = query.Where(e => e.QueuedAt <= toTime);
        }

        var totalCount = await query.CountAsync(cancellationToken).ConfigureAwait(false);
        var items = await query
            .OrderByDescending(e => e.QueuedAt)
            .Skip((pageIndex - 1) * pageSize)
            .Take(pageSize)
            .Select(e => new JobExecutionItem(
                e.ExecutionId, e.TaskCode, e.Mode.ToString(), e.Status.ToString(), e.RetryCount,
                e.TriggerSource, e.InitiatorUserId, e.QueuedAt, e.StartedAt, e.FinishedAt,
                e.DurationMs, e.Error))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return new JobExecutionPage(items, totalCount, pageIndex, pageSize);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<JobExecutionLogEntry>> GetLogsAsync(Guid executionId, CancellationToken cancellationToken)
        => _logQuery.GetLogsByExecutionIdAsync(executionId, cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<JobDefinitionItem>> ListDefinitionsAsync(CancellationToken cancellationToken)
    {
        // 定义 + 配置各一次查询，内存合并生效值（2 条 SQL，禁 N+1）
        var definitions = await _db.JobDefinitions
            .AsNoTracking()
            .OrderBy(d => d.TaskCode)
            .Select(d => new { d.TaskCode, d.Name, d.ModuleCode, d.DefaultCron, d.DefaultMode, d.DefaultTimeoutSeconds, d.Enabled, d.Paused, d.NextRunAt })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var configs = await _db.JobConfigs
            .AsNoTracking()
            .Select(c => new { c.TaskCode, c.CronOverride, c.TimeoutSecondsOverride, c.Enabled })
            .ToDictionaryAsync(static c => c.TaskCode, cancellationToken)
            .ConfigureAwait(false);

        return [.. definitions
            .Select(d => configs.TryGetValue(d.TaskCode, out var config)
                ? new JobDefinitionItem(
                    d.TaskCode, d.Name, d.ModuleCode, d.DefaultCron, config.CronOverride,
                    config.CronOverride ?? d.DefaultCron, d.DefaultMode.ToString(),
                    d.Enabled && config.Enabled, d.Paused,
                    config.TimeoutSecondsOverride ?? d.DefaultTimeoutSeconds, d.NextRunAt)
                : new JobDefinitionItem(
                    d.TaskCode, d.Name, d.ModuleCode, d.DefaultCron, null,
                    d.DefaultCron, d.DefaultMode.ToString(), d.Enabled, d.Paused,
                    d.DefaultTimeoutSeconds, d.NextRunAt))];
    }

    /// <inheritdoc />
    public async Task ToggleDefinitionAsync(string taskCode, bool enabled, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(taskCode);

        var definitionExists = await _db.JobDefinitions
            .AnyAsync(d => d.TaskCode == taskCode, cancellationToken)
            .ConfigureAwait(false);
        if (!definitionExists)
        {
            throw new NotFoundException("任务定义", taskCode);
        }

        var config = await _db.JobConfigs
            .FirstOrDefaultAsync(c => c.TaskCode == taskCode, cancellationToken)
            .ConfigureAwait(false);
        if (config is null)
        {
            _db.JobConfigs.Add(new JobConfigEntity(
                _db.CurrentTenantId, taskCode, null, null, enabled, DateTimeOffset.UtcNow));
        }
        else
        {
            config.Enabled = enabled;
            config.UpdatedAt = DateTimeOffset.UtcNow;
        }

        // 启用时同时解除熔断暂停（管理员显式恢复）；ExecuteUpdate 仅推进运行态字段、无审计诉求（绕过审计拦截器属有意为之）
        if (enabled)
        {
            await _db.JobDefinitions
                .Where(d => d.TaskCode == taskCode)
                .ExecuteUpdateAsync(setter => setter
                        .SetProperty(d => d.Paused, false)
                        .SetProperty(d => d.UpdatedAt, DateTimeOffset.UtcNow),
                    cancellationToken)
                .ConfigureAwait(false);
        }

        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>解析执行模式（缺省 sync）。</summary>
    /// <param name="mode">模式文本（可空）。</param>
    /// <returns>执行模式。</returns>
    private static ExecutionMode ParseMode(string? mode) => mode?.Trim().ToLowerInvariant() switch
    {
        null or "" or "sync" => ExecutionMode.Sync,
        "async" => ExecutionMode.Async,
        _ => throw new ValidationException([new FieldError("mode", $"执行模式非法：{mode}（仅支持 sync / async）。")]),
    };

    /// <summary>解析状态过滤文本。</summary>
    /// <param name="status">状态文本（可空）。</param>
    /// <returns>状态枚举（可空）。</returns>
    private static JobExecutionStatus? ParseStatus(string? status)
    {
        if (string.IsNullOrWhiteSpace(status))
        {
            return null;
        }

        if (Enum.TryParse<JobExecutionStatus>(status.Trim(), ignoreCase: true, out var parsed)
            && Enum.IsDefined(parsed))
        {
            return parsed;
        }

        throw new ValidationException([new FieldError("status", $"任务状态非法：{status}。")]);
    }

    /// <summary>提交响应 → 模块提交结果。</summary>
    /// <param name="response">提交响应。</param>
    /// <returns>模块提交结果。</returns>
    private static JobSubmitResult MapResult(TaskSubmitResponse response)
        => new(response.ExecutionId, response.Mode.ToString(), response.InitialStatus.ToString(),
            response.CreatedAt, response.CompletedResult);
}
