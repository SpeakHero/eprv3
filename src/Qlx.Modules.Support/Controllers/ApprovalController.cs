/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-23 10:00:00 +08:00 中国标准时间 星期五
 * 描述：审批中心 API Controller（/api/support/approvals）——Controller 层仅参数提取 + Service 调用 + 响应封装，
 *       操作人身份一律取自当前登录上下文（服务端可信，禁止前端传入用户 Id 冒充）
 */

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Qlx.Abstractions.Approvals;
using Qlx.Abstractions.Domain;
using Qlx.Abstractions.Exceptions;
using Qlx.Abstractions.Constants;
using Qlx.BuildingBlocks.Api;
using Qlx.BuildingBlocks.Audit;
using Qlx.Modules.Support.Services.Approvals;

namespace Qlx.Modules.Support.Controllers;

/// <summary>
/// 审批中心控制器。所有端点需租户级 Token（OpenIddict Validation Scheme），
/// 路由经模块许可过滤（support.approval.* 权限点）。
/// 实例 / 任务 / 委托 Id 为雪花 long（响应 JSON 属性名以 Id 结尾自动 Hashids 混淆，请求侧自动解码）。
/// </summary>
/// <param name="approvalService">审批服务（2A 冻结契约能力）。</param>
/// <param name="adminService">审批模块门面服务（流程定义 / 已办 / 委托）。</param>
/// <param name="currentUser">当前用户上下文。</param>
[ApiController]
[Route("api/support/approvals")]
public sealed class ApprovalController(
    IApprovalService approvalService,
    ISupportApprovalAdminService adminService,
    ICurrentUserContext currentUser) : ControllerBase
{
    /// <summary>当前登录用户 Id（未认证时抛 401）。</summary>
    private string CurrentUserId => currentUser.UserId
        ?? throw new UnauthorizedException("缺少用户身份，无法执行审批操作。");

    // ============ 动作 ============

    /// <summary>发起审批（单据提交时由业务模块或前端触发；BizData 含分支条件快照字段）。</summary>
    /// <param name="request">发起请求（InitiatorUserId 由服务端覆盖为当前登录人）。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>审批实例 Id。</returns>
    [HttpPost("start")]
    [AuditOperation(QlxModuleCodes.Support, "APPROVAL_START", "发起了审批")]
    [ProducesResponseType(typeof(ApiResponse<string>), StatusCodes.Status200OK)]
    public async Task<ApiResponse<string>> Start([FromBody] StartApprovalRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        var initiated = request with { InitiatorUserId = CurrentUserId };
        var instanceId = await approvalService.StartApprovalAsync(initiated, ct).ConfigureAwait(false);
        return ApiResponse<string>.Ok(instanceId);
    }

    /// <summary>审批通过（或签首人决 / 会签全员通过后流转，R4）。</summary>
    /// <param name="instanceId">实例 Id。</param>
    /// <param name="request">通过请求（Comment 可空意见）。</param>
    /// <param name="ct">取消令牌。</param>
    [HttpPost("{instanceId}/approve")]
    [AuditOperation(QlxModuleCodes.Support, "APPROVAL_APPROVE", "审批通过了任务", TargetType = "ApprovalInstance", TargetIdParam = "instanceId")]
    public async Task<ApiResponse> Approve(string instanceId, [FromBody] ApprovePayload? request, CancellationToken ct)
        => await InvokeAsync(() => approvalService.ApproveAsync(new ApproveRequest(
            instanceId, CurrentUserId, request?.Comment), ct));

    /// <summary>审批驳回（R6：按节点策略逐级回退或直达发起人）。</summary>
    /// <param name="instanceId">实例 Id。</param>
    /// <param name="request">驳回请求（Reason 必填）。</param>
    /// <param name="ct">取消令牌。</param>
    [HttpPost("{instanceId}/reject")]
    [AuditOperation(QlxModuleCodes.Support, "APPROVAL_REJECT", "驳回了审批任务", TargetType = "ApprovalInstance", TargetIdParam = "instanceId")]
    public async Task<ApiResponse> Reject(string instanceId, [FromBody] RejectPayload request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        return await InvokeAsync(() => approvalService.RejectAsync(new RejectRequest(
            instanceId, CurrentUserId, request.Reason), ct));
    }

    /// <summary>发起人撤回（R7：当前节点无任何处理动作时方可撤回）。</summary>
    /// <param name="instanceId">实例 Id。</param>
    /// <param name="request">撤回请求（Reason 可空说明）。</param>
    /// <param name="ct">取消令牌。</param>
    [HttpPost("{instanceId}/withdraw")]
    [AuditOperation(QlxModuleCodes.Support, "APPROVAL_WITHDRAW", "撤回了审批实例", TargetType = "ApprovalInstance", TargetIdParam = "instanceId")]
    public async Task<ApiResponse> Withdraw(string instanceId, [FromBody] WithdrawPayload? request, CancellationToken ct)
        => await InvokeAsync(() => approvalService.WithdrawAsync(new WithdrawRequest(
            instanceId, CurrentUserId, request?.Reason), ct));

    /// <summary>催办当前节点待办人（站内高优先级提醒，R8 人工入口）。</summary>
    /// <param name="instanceId">实例 Id。</param>
    /// <param name="request">催办请求（Message 可空附言）。</param>
    /// <param name="ct">取消令牌。</param>
    [HttpPost("{instanceId}/urge")]
    public async Task<ApiResponse> Urge(string instanceId, [FromBody] UrgePayload? request, CancellationToken ct)
        => await InvokeAsync(() => approvalService.UrgeAsync(new UrgeRequest(
            instanceId, CurrentUserId, request?.Message), ct));

    /// <summary>加签（R5：加签人先审，原审批人后审；节点须允许加签）。</summary>
    /// <param name="instanceId">实例 Id。</param>
    /// <param name="taskId">任务 Id。</param>
    /// <param name="request">加签请求（AddSignerUserId 目标人；Reason 可空）。</param>
    /// <param name="ct">取消令牌。</param>
    [HttpPost("{instanceId}/tasks/{taskId}/add-sign")]
    public async Task<ApiResponse> AddSign(
        string instanceId, string taskId, [FromBody] AddSignPayload request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        return await InvokeAsync(() => approvalService.AddSignerAsync(new AddSignerRequest(
            instanceId, taskId, CurrentUserId, request.AddSignerUserId, request.Reason, request.IsBeforeCurrent), ct));
    }

    /// <summary>转办（原任务转出留痕，新任务给目标人）。</summary>
    /// <param name="instanceId">实例 Id。</param>
    /// <param name="taskId">任务 Id。</param>
    /// <param name="request">转办请求（ToUserId 目标人；Reason 可空）。</param>
    /// <param name="ct">取消令牌。</param>
    [HttpPost("{instanceId}/tasks/{taskId}/transfer")]
    public async Task<ApiResponse> Transfer(
        string instanceId, string taskId, [FromBody] TransferPayload request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        return await InvokeAsync(() => approvalService.TransferAsync(new TransferRequest(
            instanceId, taskId, CurrentUserId, request.ToUserId, request.Reason), ct));
    }

    // ============ 查询 ============

    /// <summary>当前用户待办列表（Pending，最近 200 条）。</summary>
    /// <param name="ct">取消令牌。</param>
    [HttpGet("my-todos")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<ApprovalTaskInfo>>), StatusCodes.Status200OK)]
    public async Task<ApiResponse<IReadOnlyList<ApprovalTaskInfo>>> MyTodos(CancellationToken ct)
        => ApiResponse<IReadOnlyList<ApprovalTaskInfo>>.Ok(
            await approvalService.GetTodoListAsync(CurrentUserId, ct).ConfigureAwait(false));

    /// <summary>当前用户已办列表（最近 200 条）。</summary>
    /// <param name="ct">取消令牌。</param>
    [HttpGet("my-done")]
    public async Task<ApiResponse<IReadOnlyList<ApprovalTaskInfo>>> MyDone(CancellationToken ct)
        => ApiResponse<IReadOnlyList<ApprovalTaskInfo>>.Ok(
            await adminService.ListMyDoneTasksAsync(CurrentUserId, ct).ConfigureAwait(false));

    /// <summary>当前用户发起的审批列表（最近 100 条，含当前节点待办任务）。</summary>
    /// <param name="ct">取消令牌。</param>
    [HttpGet("my-initiated")]
    public async Task<ApiResponse<IReadOnlyList<ApprovalInstanceInfo>>> MyInitiated(CancellationToken ct)
        => ApiResponse<IReadOnlyList<ApprovalInstanceInfo>>.Ok(
            await approvalService.GetMyInitiatedListAsync(CurrentUserId, ct).ConfigureAwait(false));

    /// <summary>审批实例详情（全量任务 + 状态）。</summary>
    /// <param name="instanceId">实例 Id。</param>
    /// <param name="ct">取消令牌。</param>
    [HttpGet("instance/{instanceId}")]
    [ProducesResponseType(typeof(ApiResponse<ApprovalInstanceInfo>), StatusCodes.Status200OK)]
    public async Task<ApiResponse<ApprovalInstanceInfo>> Instance(string instanceId, CancellationToken ct)
    {
        var instance = await approvalService.GetInstanceAsync(instanceId, ct).ConfigureAwait(false)
            ?? throw new NotFoundException("审批实例", instanceId);
        return ApiResponse<ApprovalInstanceInfo>.Ok(instance);
    }

    /// <summary>流程定义摘要列表（全部版本，含节点/分支统计）。</summary>
    /// <param name="ct">取消令牌。</param>
    [HttpGet("flows")]
    public async Task<ApiResponse<IReadOnlyList<SupportFlowSummary>>> Flows(CancellationToken ct)
        => ApiResponse<IReadOnlyList<SupportFlowSummary>>.Ok(
            await adminService.ListFlowsAsync(ct).ConfigureAwait(false));

    // ============ 委托 ============

    /// <summary>我的委托列表（作为委托人）。</summary>
    /// <param name="ct">取消令牌。</param>
    [HttpGet("delegates")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<ApprovalDelegateInfo>>), StatusCodes.Status200OK)]
    public async Task<ApiResponse<IReadOnlyList<ApprovalDelegateInfo>>> Delegates(CancellationToken ct)
        => ApiResponse<IReadOnlyList<ApprovalDelegateInfo>>.Ok(
            await adminService.ListMyDelegatesAsync(CurrentUserId, ct).ConfigureAwait(false));

    /// <summary>创建委托（R9：有效期内新任务自动转代理人并留痕）。</summary>
    /// <param name="request">创建请求（BizTypes 空=全部单据类型）。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>委托 Id。</returns>
    [HttpPost("delegates")]
    public async Task<ApiResponse<string>> CreateDelegate([FromBody] SupportCreateDelegateRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        var delegateId = await adminService.CreateDelegateAsync(CurrentUserId, request, ct).ConfigureAwait(false);
        return ApiResponse<string>.Ok(delegateId);
    }

    /// <summary>撤销委托（仅委托人本人，幂等）。</summary>
    /// <param name="delegateId">委托 Id。</param>
    /// <param name="ct">取消令牌。</param>
    [HttpPost("delegates/{delegateId}/revoke")]
    [AuditOperation(QlxModuleCodes.Support, "APPROVAL_DELEGATE_REVOKE", "撤销了审批委托", TargetType = "ApprovalDelegate", TargetIdParam = "delegateId")]
    public async Task<ApiResponse> RevokeDelegate(string delegateId, CancellationToken ct)
    {
        await adminService.RevokeDelegateAsync(CurrentUserId, delegateId, ct).ConfigureAwait(false);
        return ApiResponse.Ok();
    }

    // ============ 工具 ============

    /// <summary>统一执行无返回值动作（异常由全局中间件包装）。</summary>
    private static async Task<ApiResponse> InvokeAsync(Func<Task> action)
    {
        await action().ConfigureAwait(false);
        return ApiResponse.Ok();
    }

    /// <summary>通过请求体（意见可空）。</summary>
    /// <param name="Comment">审批意见（可空）。</param>
    public sealed record ApprovePayload(string? Comment);

    /// <summary>驳回请求体（原因必填）。</summary>
    /// <param name="Reason">驳回原因。</param>
    public sealed record RejectPayload(string Reason);

    /// <summary>撤回请求体（说明可空）。</summary>
    /// <param name="Reason">撤回说明（可空）。</param>
    public sealed record WithdrawPayload(string? Reason);

    /// <summary>催办请求体（附言可空）。</summary>
    /// <param name="Message">催办附言（可空）。</param>
    public sealed record UrgePayload(string? Message);

    /// <summary>加签请求体。</summary>
    /// <param name="AddSignerUserId">加签人用户 Id。</param>
    /// <param name="Reason">加签原因（可空）。</param>
    /// <param name="IsBeforeCurrent">前加签（默认 true：加签人先审）。</param>
    public sealed record AddSignPayload(string AddSignerUserId, string? Reason = null, bool IsBeforeCurrent = true);

    /// <summary>转办请求体。</summary>
    /// <param name="ToUserId">转办目标人用户 Id。</param>
    /// <param name="Reason">转办原因（可空）。</param>
    public sealed record TransferPayload(string ToUserId, string? Reason = null);
}
