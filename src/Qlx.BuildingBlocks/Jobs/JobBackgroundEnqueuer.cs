/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-22 22:00:00 +08:00 中国标准时间 星期四
 * 描述：后台任务入队抽象（IJobBackgroundEnqueuer）与 Hangfire 实现（Hangfire 依赖只进 BuildingBlocks，模块零引用）
 */

using Hangfire;
using Microsoft.Extensions.Logging;
using Qlx.Abstractions.Tenancy;
using Qlx.BuildingBlocks.MultiTenancy;

namespace Qlx.BuildingBlocks.Jobs;

/// <summary>
/// 后台任务入队抽象。Hangfire 的 <c>BackgroundJob.Enqueue</c> / <see cref="IBackgroundJobClient"/>
/// 只允许出现在 Qlx.BuildingBlocks（架构测试守护），业务模块经本抽象与
/// <see cref="Qlx.Abstractions.Jobs.IJobScheduler.SubmitAsync"/> 提交任务。
/// </summary>
public interface IJobBackgroundEnqueuer
{
    /// <summary>
    /// 将后台执行调用立即入队。
    /// </summary>
    /// <param name="executionId">执行 Id。</param>
    /// <param name="tenantId">租户 Id（提交期捕获，Worker 内据此路由租户库）。</param>
    /// <param name="taskCode">任务编码。</param>
    /// <returns>Hangfire 作业 Id（取消与状态联动用）。</returns>
    string Enqueue(Guid executionId, long tenantId, string taskCode);

    /// <summary>
    /// 将后台执行调用延迟入队（失败重试按退避策略等待后执行）。
    /// </summary>
    /// <param name="delay">延迟时长（重试退避计算结果）。</param>
    /// <param name="executionId">执行 Id。</param>
    /// <param name="tenantId">租户 Id。</param>
    /// <param name="taskCode">任务编码。</param>
    /// <returns>Hangfire 作业 Id。</returns>
    string EnqueueIn(TimeSpan delay, Guid executionId, long tenantId, string taskCode);

    /// <summary>
    /// 删除尚在队列中的 Hangfire 作业（用户取消 QUEUED 任务时调用）。
    /// </summary>
    /// <param name="hangfireJobId">Hangfire 作业 Id。</param>
    /// <returns>删除成功返回 true；作业已开始或不存在返回 false。</returns>
    bool Delete(string hangfireJobId);
}

/// <summary>
/// 基于 Hangfire <see cref="IBackgroundJobClient"/> 的入队实现。
/// </summary>
public sealed class HangfireJobEnqueuer(IBackgroundJobClient client) : IJobBackgroundEnqueuer
{
    private readonly IBackgroundJobClient _client = client;

    /// <inheritdoc />
    public string Enqueue(Guid executionId, long tenantId, string taskCode)
        => _client.Enqueue<JobBackgroundInvoker>(invoker =>
            invoker.RunAsync(executionId, tenantId, taskCode,
                // 【豁免点 3】Hangfire 在序列化调用表达式时自动将 CancellationToken 参数绑定
                // 为 Worker 中止令牌，此处的 None 仅为表达式占位、不会作为真实令牌执行（规范 §6.13.2）
                CancellationToken.None));

    /// <inheritdoc />
    public string EnqueueIn(TimeSpan delay, Guid executionId, long tenantId, string taskCode)
        => _client.Schedule<JobBackgroundInvoker>(invoker =>
            invoker.RunAsync(executionId, tenantId, taskCode,
                // 【豁免点 3】同上：表达式占位，Hangfire 入队时自动绑定 Worker 中止令牌
                CancellationToken.None),
            delay);

    /// <inheritdoc />
    public bool Delete(string hangfireJobId) => _client.Delete(hangfireJobId);
}

/// <summary>
/// Hangfire Worker 入口（Hangfire 按类型 + 方法表达式入队，实例经 DI 作用域解析）。
/// 每次后台执行创建独立 DI 作用域：租户上下文按提交期捕获的 tenantId 设置，
/// 保证 Handler 内解析的租户 DbContext 路由到正确租户库。
/// </summary>
/// <param name="tenantContext">Hangfire 作业作用域内的租户上下文（执行前按租户填充）。</param>
/// <param name="runner">任务执行运行器。</param>
/// <param name="logger">日志器。</param>
public sealed partial class JobBackgroundInvoker(
    ITenantContext tenantContext,
    JobExecutionRunner runner,
    ILogger<JobBackgroundInvoker> logger)
{
    private readonly ITenantContext _tenantContext = tenantContext;
    private readonly JobExecutionRunner _runner = runner;
    private readonly ILogger<JobBackgroundInvoker> _logger = logger;

    /// <summary>
    /// Hangfire 后台执行入口：填充作业作用域租户上下文 → 委托 <see cref="JobExecutionRunner.ExecuteAsync"/>。
    /// Hangfire.AspNetCore 的 JobActivator 为每次作业执行创建独立 DI 作用域，
    /// 运行器与处理器均在同一作用域解析，租户级依赖保持一致。
    /// </summary>
    /// <param name="executionId">执行 Id。</param>
    /// <param name="tenantId">租户 Id。</param>
    /// <param name="taskCode">任务编码。</param>
    /// <param name="cancellationToken">Worker 中止令牌（Hangfire 自动绑定）。</param>
    public Task RunAsync(Guid executionId, long tenantId, string taskCode, CancellationToken cancellationToken)
    {
        if (_tenantContext is RequestTenantContext requestContext)
        {
            requestContext.SetTenantId(tenantId);
        }

        LogDispatch(taskCode, executionId, tenantId);
        return _runner.ExecuteAsync(executionId, tenantId, taskCode, cancellationToken);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "后台任务开始执行：{TaskCode} / {ExecutionId}（租户 {TenantId}）")]
    private partial void LogDispatch(string taskCode, Guid executionId, long tenantId);
}
