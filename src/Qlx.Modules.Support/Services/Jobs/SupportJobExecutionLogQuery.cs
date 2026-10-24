/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-22 23:25:00 +08:00 中国标准时间 星期四
 * 描述：IJobExecutionLogQuery 的 Support 模块实现——请求作用域租户上下文（HTTP 查询场景），
 *       EF Select 投影单查询（禁 N+1）；租户隔离由 TenantDbContext 全局查询过滤器兜底
 */

using Microsoft.EntityFrameworkCore;
using Qlx.Abstractions.Jobs;
using Qlx.Modules.Support.Domain;

namespace Qlx.Modules.Support.Services.Jobs;

/// <summary>
/// 任务执行日志查询（Support 模块实现）。使用注入的请求作用域
/// <see cref="SupportTenantDbContext"/>（租户 Id 由 HTTP 管线解析），查询条件里的
/// TenantId 仅在可映射时生效、隔离边界始终以上下文租户为准。
/// </summary>
/// <param name="db">请求作用域租户上下文。</param>
public sealed class SupportJobExecutionLogQuery(SupportTenantDbContext db) : IJobExecutionLogQuery
{
    private readonly SupportTenantDbContext _db = db;

    /// <inheritdoc />
    public async Task<IReadOnlyList<JobExecutionLogEntry>> GetLogsByExecutionIdAsync(
        Guid executionId, CancellationToken cancellationToken)
        => await _db.JobLogs
            .AsNoTracking()
            .Where(l => l.ExecutionId == executionId)
            .OrderBy(l => l.LogAt)
            .Select(l => new JobExecutionLogEntry(l.ExecutionId, l.LogAt, l.Level, l.Message, l.Detail, l.StackTrace))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<IReadOnlyList<JobExecutionLogEntry>> QueryAsync(
        JobLogQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(request.From, request.To);

        var query = _db.JobLogs
            .AsNoTracking()
            .Where(l => l.LogAt >= request.From && l.LogAt <= request.To);
        if (!string.IsNullOrWhiteSpace(request.TaskCode))
        {
            query = query.Where(l => l.TaskCode == request.TaskCode);
        }

        if (request.MinLevel is not null)
        {
            query = query.Where(l => l.Level >= request.MinLevel);
        }

        return await query
            .OrderBy(l => l.LogAt)
            .Take(1000)
            .Select(l => new JobExecutionLogEntry(l.ExecutionId, l.LogAt, l.Level, l.Message, l.Detail, l.StackTrace))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }
}
