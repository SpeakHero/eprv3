/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-22 23:20:00 +08:00 中国标准时间 星期四
 * 描述：后台任务中心 API Controller（/api/support/jobs）——Controller 层仅参数提取 + Service 调用 + 响应封装
 */

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Qlx.Abstractions.Jobs;
using Qlx.Abstractions.Constants;
using Qlx.BuildingBlocks.Api;
using Qlx.BuildingBlocks.Audit;
using Qlx.Modules.Support.Services.Jobs;

namespace Qlx.Modules.Support.Controllers;

/// <summary>
/// 后台任务中心控制器。所有端点需租户级 Token（OpenIddict Validation Scheme），
/// 路由经模块许可过滤（support.job.* 权限点）。
/// </summary>
/// <param name="jobService">任务中心门面服务。</param>
[ApiController]
[Route("api/support/jobs")]
public sealed class JobController(ISupportJobService jobService) : ControllerBase
{
    // ============ 触发 / 取消 / 状态 ============

    /// <summary>触发任务（mode=sync 阻塞等待完成；mode=async 立即返回 QUEUED）。</summary>
    /// <param name="payload">触发入参。</param>
    /// <param name="ct">取消令牌。</param>
    [HttpPost("trigger")]
    [AuditOperation(QlxModuleCodes.Support, "JOB_TRIGGER", "触发了后台任务")]
    [ProducesResponseType(typeof(ApiResponse<JobSubmitResult>), StatusCodes.Status200OK)]
    public async Task<ApiResponse<JobSubmitResult>> Trigger(
        [FromBody] JobTriggerPayload payload, CancellationToken ct)
        => ApiResponse<JobSubmitResult>.Ok(await jobService.TriggerAsync(payload, ct).ConfigureAwait(false));

    /// <summary>取消任务（PENDING / QUEUED 删队列；RUNNING 发运行期取消信号）。</summary>
    /// <param name="executionId">执行 Id。</param>
    /// <param name="ct">取消令牌。</param>
    [HttpPost("{executionId:guid}/cancel")]
    [AuditOperation(QlxModuleCodes.Support, "JOB_CANCEL", "取消了任务执行")]
    public async Task<ApiResponse> Cancel(Guid executionId, CancellationToken ct)
    {
        await jobService.CancelAsync(executionId, ct).ConfigureAwait(false);
        return ApiResponse.Ok();
    }

    /// <summary>查询任务执行状态（终态填充完成结果）。</summary>
    /// <param name="executionId">执行 Id。</param>
    /// <param name="ct">取消令牌。</param>
    [HttpGet("{executionId:guid}")]
    public async Task<ApiResponse<JobSubmitResult>> GetStatus(Guid executionId, CancellationToken ct)
        => ApiResponse<JobSubmitResult>.Ok(await jobService.GetStatusAsync(executionId, ct).ConfigureAwait(false));

    // ============ 历史 / 日志 ============

    /// <summary>分页查询执行历史（当前租户；taskCode / status / 时间窗口过滤）。</summary>
    /// <param name="taskCode">任务编码（可空）。</param>
    /// <param name="status">状态（可空）。</param>
    /// <param name="from">登记时间起点（UTC，可空）。</param>
    /// <param name="to">登记时间终点（UTC，可空）。</param>
    /// <param name="pageIndex">页码（默认 1）。</param>
    /// <param name="pageSize">页大小（默认 20，上限 200）。</param>
    /// <param name="ct">取消令牌。</param>
    [HttpGet("history")]
    public async Task<ApiResponse<JobExecutionPage>> History(
        [FromQuery] string? taskCode, [FromQuery] string? status, [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to, [FromQuery] int pageIndex = 1, [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
        => ApiResponse<JobExecutionPage>.Ok(await jobService
            .QueryExecutionsAsync(taskCode, status, from, to, pageIndex, pageSize, ct)
            .ConfigureAwait(false));

    /// <summary>按执行 Id 查询执行日志（时间升序）。</summary>
    /// <param name="executionId">执行 Id。</param>
    /// <param name="ct">取消令牌。</param>
    [HttpGet("{executionId:guid}/logs")]
    public async Task<ApiResponse<IReadOnlyList<JobExecutionLogEntry>>> Logs(
        Guid executionId, CancellationToken ct)
        => ApiResponse<IReadOnlyList<JobExecutionLogEntry>>.Ok(
            await jobService.GetLogsAsync(executionId, ct).ConfigureAwait(false));

    // ============ 定义 / 启停 ============

    /// <summary>查询当前租户任务定义清单（含租户级配置覆盖生效值）。</summary>
    /// <param name="ct">取消令牌。</param>
    [HttpGet("definitions")]
    public async Task<ApiResponse<IReadOnlyList<JobDefinitionItem>>> Definitions(CancellationToken ct)
        => ApiResponse<IReadOnlyList<JobDefinitionItem>>.Ok(
            await jobService.ListDefinitionsAsync(ct).ConfigureAwait(false));

    /// <summary>启用 / 停用任务定义（租户级；启用时同时解除熔断暂停）。</summary>
    /// <param name="taskCode">任务编码。</param>
    /// <param name="payload">启停入参。</param>
    /// <param name="ct">取消令牌。</param>
    [HttpPost("definitions/{taskCode}/toggle")]
    [AuditOperation(QlxModuleCodes.Support, "JOB_DEFINITION_TOGGLE", "变更了任务定义启停状态")]
    public async Task<ApiResponse> ToggleDefinition(
        string taskCode, [FromBody] JobTogglePayload payload, CancellationToken ct)
    {
        await jobService.ToggleDefinitionAsync(taskCode, payload.Enabled, ct).ConfigureAwait(false);
        return ApiResponse.Ok();
    }
}
