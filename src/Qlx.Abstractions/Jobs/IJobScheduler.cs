/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.1.0 beta
 * 创建时间：2026-10-22 15:45:00 +08:00 中国标准时间 星期三
 * 修改时间：2026-10-22 23:50:00 +08:00 中国标准时间 星期四
 * 描述：后台任务调度器 / 处理器 / 执行日志查询接口（1.1.0：IJobHandler 补充 TaskCode 属性）
 */

namespace Qlx.Abstractions.Jobs;

/// <summary>
/// 后台任务调度器：统一入口 SubmitAsync，支持 SYNC / ASYNC 双模式。
/// </summary>
public interface IJobScheduler
{
    /// <summary>
    /// 提交任务。
    /// </summary>
    /// <param name="request">提交请求（含执行模式、参数、重试策略、依赖等）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>提交响应（SYNC 模式下 CompletedResult 会被填充）。</returns>
    Task<TaskSubmitResponse> SubmitAsync(TaskSubmitRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// 取消正在执行或等待执行的任务。
    /// </summary>
    /// <param name="executionId">执行 ID。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task CancelAsync(Guid executionId, CancellationToken cancellationToken);

    /// <summary>
    /// 查询任务执行状态。
    /// </summary>
    /// <param name="executionId">执行 ID。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>执行状态（未找到返回 null）。</returns>
    Task<TaskSubmitResponse?> GetStatusAsync(Guid executionId, CancellationToken cancellationToken);
}

/// <summary>
/// 后台任务处理器接口：业务模块按 TaskCode 实现本接口。
/// </summary>
public interface IJobHandler
{
    /// <summary>
    /// 任务编码（全局唯一；调度器按此从 DI 解析对应处理器，2B 补充属性）。
    /// </summary>
    string TaskCode { get; }

    /// <summary>
    /// 执行任务。
    /// </summary>
    /// <param name="context">执行上下文（含参数、进度上报、取消令牌）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>执行结果。</returns>
    Task<CompletedResult> ExecuteAsync(JobContext context, CancellationToken cancellationToken);
}

/// <summary>
/// 任务执行日志查询接口（只读，供运维 / 前端状态面板使用）。
/// </summary>
public interface IJobExecutionLogQuery
{
    /// <summary>
    /// 按执行 ID 查询日志。
    /// </summary>
    /// <param name="executionId">执行 ID。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>日志条目列表（按时间升序）。</returns>
    Task<IReadOnlyList<JobExecutionLogEntry>> GetLogsByExecutionIdAsync(Guid executionId, CancellationToken cancellationToken);

    /// <summary>
    /// 按条件批量查询日志。
    /// </summary>
    /// <param name="request">查询条件。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>日志条目列表。</returns>
    Task<IReadOnlyList<JobExecutionLogEntry>> QueryAsync(JobLogQuery request, CancellationToken cancellationToken);
}

/// <summary>
/// 任务执行日志条目。
/// </summary>
public sealed record JobExecutionLogEntry(
    Guid ExecutionId,
    DateTimeOffset LogAt,
    JobLogLevel Level,
    string Message,
    string? Detail,
    string? StackTrace);

/// <summary>
/// 任务日志级别。
/// </summary>
public enum JobLogLevel
{
    /// <summary>调试。</summary>
    Debug,

    /// <summary>信息。</summary>
    Info,

    /// <summary>警告。</summary>
    Warn,

    /// <summary>错误。</summary>
    Error
}

/// <summary>
/// 任务日志查询条件。
/// </summary>
public sealed record JobLogQuery(
    DateTimeOffset From,
    DateTimeOffset To,
    string? TaskCode = null,
    Guid? TenantId = null,
    JobLogLevel? MinLevel = null);
