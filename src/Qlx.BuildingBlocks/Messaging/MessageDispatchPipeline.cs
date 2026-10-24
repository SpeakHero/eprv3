/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-23 05:50:00 +08:00 中国标准时间 星期五
 * 描述：消息渠道分发管线——Hangfire 异步执行器（复刻 JobBackgroundInvoker 模式）：
 *       IBackgroundJobClient 只进 BuildingBlocks；业务模块经 IMessageDispatchEnqueuer 入队，
 *       Worker 作用域内按租户上下文解析 IMessageDeliveryExecutor（实现在 Support 模块）执行渠道分发。
 *       刻意不走 job_executions 任务管线：避免 JobExecutionRunner 终态通知递归产生新消息。
 */

using Hangfire;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Qlx.Abstractions.Tenancy;
using Qlx.BuildingBlocks.MultiTenancy;

namespace Qlx.BuildingBlocks.Messaging;

/// <summary>
/// 消息渠道分发执行器契约（接口在 BuildingBlocks，实现在 Support 模块）。
/// Hangfire Worker 作用域内执行：按 messageId 加载租户库消息行，执行邮件/短信渠道投递
/// 并推进消息状态、落 send_logs。站内信已在 write-through 阶段持久化，本执行器只补外部渠道。
/// </summary>
public interface IMessageDeliveryExecutor
{
    /// <summary>
    /// 执行单条消息的渠道分发（Worker 作用域；租户上下文已由 Invoker 填充）。
    /// </summary>
    /// <param name="messageId">消息 Id。</param>
    /// <param name="tenantId">租户 Id。</param>
    /// <param name="forceRetry">人工重试标记：true 时绕过自动重试上限门槛强制尝试一轮（已送达渠道仍跳过）。</param>
    /// <param name="cancellationToken">Worker 中止令牌。</param>
    Task ExecuteDeliveryAsync(long messageId, long tenantId, bool forceRetry, CancellationToken cancellationToken);
}

/// <summary>
/// 消息分发入队抽象。业务模块（Support 消息服务）经本接口将渠道投递提交 Hangfire，
/// 禁止直引 <c>BackgroundJob</c> / <see cref="IBackgroundJobClient"/>（架构测试守护）。
/// </summary>
public interface IMessageDispatchEnqueuer
{
    /// <summary>立即入队渠道分发。</summary>
    /// <param name="messageId">消息 Id。</param>
    /// <param name="tenantId">租户 Id（提交期捕获，Worker 内据此路由租户库）。</param>
    /// <returns>Hangfire 作业 Id。</returns>
    string Enqueue(long messageId, long tenantId);

    /// <summary>延迟入队渠道分发（邮件失败重试按退避策略等待）。</summary>
    /// <param name="delay">延迟时长。</param>
    /// <param name="messageId">消息 Id。</param>
    /// <param name="tenantId">租户 Id。</param>
    /// <returns>Hangfire 作业 Id。</returns>
    string EnqueueIn(TimeSpan delay, long messageId, long tenantId);

    /// <summary>立即入队渠道分发（人工重试：绕过自动重试上限强制尝试一轮）。</summary>
    /// <param name="messageId">消息 Id。</param>
    /// <param name="tenantId">租户 Id。</param>
    /// <returns>Hangfire 作业 Id。</returns>
    string EnqueueManualRetry(long messageId, long tenantId);
}

/// <summary>基于 Hangfire <see cref="IBackgroundJobClient"/> 的消息分发入队实现。</summary>
/// <param name="client">Hangfire 作业客户端。</param>
public sealed class HangfireMessageDispatchEnqueuer(IBackgroundJobClient client) : IMessageDispatchEnqueuer
{
    private readonly IBackgroundJobClient _client = client;

    /// <inheritdoc />
    public string Enqueue(long messageId, long tenantId)
        => _client.Enqueue<MessageDeliveryInvoker>(invoker =>
            invoker.RunAsync(messageId, tenantId, forceRetry: false,
                // 【豁免点】Hangfire 在序列化调用表达式时自动将 CancellationToken 参数绑定
                // 为 Worker 中止令牌，此处的 None 仅为表达式占位、不会作为真实令牌执行（规范 §6.13.2）
                CancellationToken.None));

    /// <inheritdoc />
    public string EnqueueIn(TimeSpan delay, long messageId, long tenantId)
        => _client.Schedule<MessageDeliveryInvoker>(invoker =>
            invoker.RunAsync(messageId, tenantId, forceRetry: false,
                // 【豁免点】同上：表达式占位，Hangfire 入队时自动绑定 Worker 中止令牌
                CancellationToken.None),
            delay);

    /// <inheritdoc />
    public string EnqueueManualRetry(long messageId, long tenantId)
        => _client.Enqueue<MessageDeliveryInvoker>(invoker =>
            invoker.RunAsync(messageId, tenantId, forceRetry: true,
                // 【豁免点】同上：表达式占位，Hangfire 入队时自动绑定 Worker 中止令牌
                CancellationToken.None));
}

/// <summary>
/// Hangfire Worker 入口（消息分发）：填充作业作用域租户上下文 → 作用域内解析 <see cref="IMessageDeliveryExecutor"/> 执行。
/// 每次执行独立 DI 作用域（Hangfire JobActivator）；执行器必须在 SetTenantId 之后解析，
/// 否则其依赖的租户 DbContext 会以 tenantId=0 构造（对齐 JobExecutionRunner 的惰性解析模式）。
/// </summary>
/// <param name="tenantContext">作业作用域内的租户上下文（执行前按租户填充）。</param>
/// <param name="serviceProvider">作业作用域服务提供者（执行器惰性解析入口）。</param>
/// <param name="logger">日志器。</param>
public sealed partial class MessageDeliveryInvoker(
    ITenantContext tenantContext,
    IServiceProvider serviceProvider,
    ILogger<MessageDeliveryInvoker> logger)
{
    private readonly ITenantContext _tenantContext = tenantContext;
    private readonly IServiceProvider _serviceProvider = serviceProvider;
    private readonly ILogger<MessageDeliveryInvoker> _logger = logger;

    /// <summary>
    /// Hangfire 分发入口：填充租户上下文 → 解析执行器完成渠道投递。
    /// </summary>
    /// <param name="messageId">消息 Id。</param>
    /// <param name="tenantId">租户 Id。</param>
    /// <param name="forceRetry">人工重试标记（透传执行器，绕过自动重试上限）。</param>
    /// <param name="cancellationToken">Worker 中止令牌（Hangfire 自动绑定）。</param>
    public async Task RunAsync(
        long messageId, long tenantId, bool forceRetry, CancellationToken cancellationToken)
    {
        if (_tenantContext is RequestTenantContext requestContext)
        {
            requestContext.SetTenantId(tenantId);
        }

        var executor = _serviceProvider.GetRequiredService<IMessageDeliveryExecutor>();
        LogDispatch(messageId, tenantId);
        await executor.ExecuteDeliveryAsync(messageId, tenantId, forceRetry, cancellationToken).ConfigureAwait(false);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "消息渠道分发开始：{MessageId}（租户 {TenantId}）")]
    private partial void LogDispatch(long messageId, long tenantId);
}
