/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-22 15:45:00 +08:00 中国标准时间 星期三
 * 修改时间：2026-10-22 15:45:00 +08:00 中国标准时间 星期三
 * 描述：后台任务中心公共契约（调度入口 / 处理器 / 状态机 / 重试 / 日志查询）
 */

namespace Qlx.Abstractions.Jobs;

/// <summary>
/// 后台任务执行状态机枚举。
/// </summary>
public enum JobExecutionStatus
{
    /// <summary>已登记，入队前。</summary>
    Pending = 0,

    /// <summary>已入 Hangfire 队列。</summary>
    Queued = 1,

    /// <summary>Worker 开始执行。</summary>
    Running = 2,

    /// <summary>执行成功。</summary>
    Success = 3,

    /// <summary>执行失败（业务异常）。</summary>
    Failed = 4,

    /// <summary>超时强制终止。</summary>
    Timeout = 5,

    /// <summary>用户取消或前置依赖失败。</summary>
    Canceled = 6
}

/// <summary>
/// 执行模式：同步（调用方阻塞等待完成）/ 异步（立即返回 executionId）。
/// </summary>
public enum ExecutionMode
{
    /// <summary>同步：调用方阻塞直到执行完成，响应包含结果。</summary>
    Sync = 0,

    /// <summary>异步：立即返回 executionId，后续通过日志查询获取结果。</summary>
    Async = 1
}

/// <summary>
/// 重试退避策略。
/// </summary>
public enum RetryBackoffStrategy
{
    /// <summary>固定间隔。</summary>
    Fixed,

    /// <summary>线性递增。</summary>
    Linear,

    /// <summary>指数递增。</summary>
    Exponential
}

/// <summary>
/// 重试策略。全部属性 init 不可变。
/// </summary>
public sealed record RetryPolicy
{
    /// <summary>最大重试次数（含首次执行失败后的重试），默认 3。</summary>
    public int MaxRetries { get; init; } = 3;

    /// <summary>首次重试间隔（秒），默认 60。</summary>
    public int InitialIntervalSeconds { get; init; } = 60;

    /// <summary>退避策略。</summary>
    public RetryBackoffStrategy Backoff { get; init; } = RetryBackoffStrategy.Exponential;

    /// <summary>最大允许等待秒数，默认 3600（1h）。</summary>
    public int MaxDelaySeconds { get; init; } = 3600;
}

/// <summary>
/// 任务提交请求（位置参数，字段明确）。
/// </summary>
public sealed record TaskSubmitRequest(
    string TaskCode,
    Guid TenantId,
    ExecutionMode Mode = ExecutionMode.Sync,
    string? InitiatorUserId = null,
    IReadOnlyDictionary<string, object?>? Parameters = null,
    int? TimeoutSeconds = null,
    RetryPolicy? RetryPolicyOverride = null,
    IReadOnlyList<Guid>? DependsOnExecutionIds = null,
    string? IdempotencyKey = null)
{
    /// <summary>
    /// 运行期参数（仅内存传递，永不持久化 / 序列化）。用于承载不可 JSON 序列化的值（如上传内容
    /// <see cref="System.IO.Stream"/>）。仅限 <see cref="ExecutionMode.Sync"/> 提交；
    /// ASYNC 入队（Hangfire 序列化轮次）携带运行期参数将被调度器拒绝（ValidationException）。
    /// 与 <see cref="Parameters"/> 键冲突时以本字典为准。
    /// </summary>
    public IReadOnlyDictionary<string, object?>? RuntimeParameters { get; init; }
}

/// <summary>
/// 任务提交响应。
/// </summary>
public sealed record TaskSubmitResponse(
    Guid ExecutionId,
    ExecutionMode Mode,
    JobExecutionStatus InitialStatus,
    DateTimeOffset CreatedAt,
    CompletedResult? CompletedResult = null);

/// <summary>
/// 同步执行完成结果。
/// </summary>
public sealed record CompletedResult(
    object? ResultJson,
    string? Stdout,
    string? Stderr,
    long ExecutionDurationMs,
    bool IsSuccess,
    string? ErrorMessage = null);

/// <summary>
/// 任务进度信息。
/// </summary>
public sealed record JobProgress(
    Guid ExecutionId,
    int Percent,
    string? Message = null);
