/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-22 20:00:00 +08:00 中国标准时间 星期四
 * 修改时间：2026-10-26 22:00:00 +08:00 中国标准时间 星期一
 * 描述：审批中心服务（2A 冻结契约 IApprovalService 实现）——发起/通过/驳回/撤回/加签/转办/催办，
 *       会签全通过（R4）、或签首人决、条件分支求值（R3）、委托留痕（R9）、终态事件驱动（R11）
 */

using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Qlx.Abstractions.Approvals;
using Qlx.Abstractions.Audit;
using Qlx.Abstractions.Constants;
using Qlx.Abstractions.Exceptions;
using Qlx.Abstractions.Expressions;
using Qlx.Abstractions.Messages;
using Qlx.Abstractions.Serialization;
using Qlx.BuildingBlocks.EventBus;
using Qlx.Modules.Support.Domain;
using Qlx.Modules.Support.Options;

namespace Qlx.Modules.Support.Services.Approvals;

/// <summary>
/// 审批中心服务实现。同一动作内的实例 / 任务 / 日志变更单次 <c>SaveChangesAsync</c> 提交；
/// 终态事件在持久化成功后经事件总线 <c>PublishDurableAsync</c> 发布（Outbox at-least-once，消费者幂等，R11）。
/// </summary>
/// <param name="db">租户上下文（apr_ / msg_ 同库）。</param>
/// <param name="expressionEngine">条件表达式引擎（分支求值）。</param>
/// <param name="flowRegistry">流程注册表（R1 唯一启用解析 + 代码默认定义注册）。</param>
/// <param name="approverSources">审批人来源集合（Support 兜底 + Bas 组织侧，宿主聚合）。</param>
/// <param name="messageService">信息中心消息服务（待办 / 催办 / 结果通知）。</param>
/// <param name="auditService">审计服务（业务级操作日志）。</param>
/// <param name="eventBus">事件总线（终态事件 Outbox 发布，R11）。</param>
/// <param name="options">审批中心配置（默认超时小时）。</param>
/// <param name="logger">日志器。</param>
public sealed partial class SupportApprovalService(
    SupportTenantDbContext db,
    IConditionExpressionEngine expressionEngine,
    ApprovalFlowRegistry flowRegistry,
    IEnumerable<IApprovalApproverSource> approverSources,
    IMessageService messageService,
    IAuditService auditService,
    IEventBus eventBus,
    IOptions<SupportApprovalsOptions> options,
    ILogger<SupportApprovalService> logger) : IApprovalService
{
    private readonly SupportTenantDbContext _db = db;
    private readonly IConditionExpressionEngine _expressionEngine = expressionEngine;
    private readonly ApprovalFlowRegistry _flowRegistry = flowRegistry;
    private readonly IReadOnlyList<IApprovalApproverSource> _approverSources = [.. approverSources];
    private readonly IMessageService _messageService = messageService;
    private readonly IAuditService _auditService = auditService;
    private readonly IEventBus _eventBus = eventBus;
    private readonly SupportApprovalsOptions _options = options.Value;
    private readonly ILogger<SupportApprovalService> _logger = logger;

    private static readonly CultureInfo s_culture = CultureInfo.InvariantCulture;

    // ============ 发起 ============

    /// <inheritdoc />
    public async Task<string> StartApprovalAsync(StartApprovalRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.BizType);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.BizId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.InitiatorUserId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Title);

        var bizData = request.BizData ?? new Dictionary<string, object?>();
        var orgId = bizData.TryGetValue(ApprovalCompositeApproverResolver.BizDataKeyOrgId, out var orgValue)
            && long.TryParse(orgValue?.ToString(), out var orgParsed) ? orgParsed : 0;

        var flow = await ResolveStartFlowAsync(request, orgId, cancellationToken).ConfigureAwait(false)
            ?? throw new NotFoundException("审批流程定义", $"{request.BizType}（组织 {orgId}）");
        ApprovalFlowRegistry.EnsureBranchDefaults(flow);

        var firstNode = flow.Nodes
            .Where(n => n.NodeType == ApprovalNodeType.Approval)
            .OrderBy(n => n.SortNo)
            .FirstOrDefault()
            ?? throw new ConflictException($"流程 {flow.Definition.DefCode} 未配置任何审批节点。");

        var now = DateTimeOffset.UtcNow;
        var snapshotJson = JsonSerializer.Serialize(bizData, QlxJsonDefaults.PreserveNull);
        var instance = new ApprovalInstance(
            _db.CurrentTenantId, instNo: "pending", flow.Definition.Id, flow.Definition.Version, flow.Definition.DefCode,
            request.BizType, request.BizId, request.BizNo ?? string.Empty, request.Title,
            request.InitiatorUserId, orgId, snapshotJson, now);

        // 先落实例拿雪花 Id（拦截器填充），再按 Id 派生唯一 InstNo，最后生成任务与日志
        await _db.Instances.AddAsync(instance, cancellationToken).ConfigureAwait(false);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        instance.AssignInstNo($"APR{now:yyyyMMdd}{(instance.Id % 100_000_000).ToString("D8", s_culture)}");
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        var logs = new List<ApprovalLog>
        {
            new(_db.CurrentTenantId, instance.Id, ApprovalLogActions.Submit, request.InitiatorUserId,
                $"提交审批：{request.Title}（流程 {flow.Definition.DefCode} v{flow.Definition.Version}）", ip: null, now),
        };
        await CreateTasksForNodeAsync(flow, firstNode, instance, request.InitiatorUserId, now, logs, cancellationToken)
            .ConfigureAwait(false);
        await _db.OpLogs.AddRangeAsync(logs, cancellationToken).ConfigureAwait(false);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        LogInstanceAction(instance.InstNo, ApprovalLogActions.Submit, request.InitiatorUserId);
        await CaptureOperationAsync(instance, ApprovalLogActions.Submit, request.InitiatorUserId, now, cancellationToken)
            .ConfigureAwait(false);
        return instance.Id.ToString(s_culture);
    }

    /// <summary>
    /// 发起时解析流程：显式 <see cref="StartApprovalRequest.ApprovalDefinitionCode"/> 优先
    /// （同单据类型可挂多条流程，如调拨「同公司 / 跨公司」、成本调整「普通 / 大额」，
    /// 此时 biz_type 无法唯一确定流程）；未指定或未启用时回退 biz_type + 组织唯一启用解析（R1）。
    /// </summary>
    /// <param name="request">发起请求。</param>
    /// <param name="orgId">发起组织 Id。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>流程聚合（未命中返回 null）。</returns>
    /// <exception cref="ConflictException">显式流程与请求单据类型不一致时抛出。</exception>
    private async Task<EnabledApprovalFlow?> ResolveStartFlowAsync(
        StartApprovalRequest request, long orgId, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(request.ApprovalDefinitionCode))
        {
            var pinned = await _flowRegistry
                .ResolveByCodeAsync(request.ApprovalDefinitionCode, cancellationToken).ConfigureAwait(false);
            if (pinned is not null)
            {
                if (!string.Equals(pinned.Definition.BizType, request.BizType, StringComparison.Ordinal))
                {
                    throw new ConflictException(
                        $"流程定义 {request.ApprovalDefinitionCode} 的单据类型为 {pinned.Definition.BizType}，与请求单据类型 {request.BizType} 不一致。");
                }

                return pinned;
            }
        }

        return await _flowRegistry.ResolveEnabledAsync(request.BizType, orgId, cancellationToken)
            .ConfigureAwait(false);
    }

    // ============ 通过 ============

    /// <inheritdoc />
    public async Task ApproveAsync(ApproveRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.InstanceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ApproverUserId);

        var instanceId = ParseId(request.InstanceId, nameof(request.InstanceId));
        var now = DateTimeOffset.UtcNow;

        var (instance, flow) = await LoadRunningInstanceAsync(instanceId, request.InstanceId, cancellationToken).ConfigureAwait(false);
        var task = await RequirePendingTaskAsync(instanceId, instance.CurrentNodeKey, request.ApproverUserId, cancellationToken)
            .ConfigureAwait(false);
        var node = flow.FindNode(task.NodeKey)
            ?? throw new NotFoundException("流程节点", task.NodeKey);

        ApprovalTaskStateMachine.EnsureTransition(task.Status, ApprovalTaskStatus.Approved);
        task.SetStatus(ApprovalTaskStatus.Approved, request.Comment, ip: null, now);

        var logs = new List<ApprovalLog>
        {
            new(_db.CurrentTenantId, instanceId, ApprovalLogActions.Approve, request.ApproverUserId,
                $"通过节点 {node.NodeName}（{task.NodeKey}）：{request.Comment ?? "无意见"}", ip: null, now),
        };

        if (await DecideNodeCompletionAsync(instanceId, node, cancellationToken).ConfigureAwait(false))
        {
            await AdvanceFromNodeAsync(flow, node, instance, request.ApproverUserId, now, logs, cancellationToken)
                .ConfigureAwait(false);
        }

        await _db.OpLogs.AddRangeAsync(logs, cancellationToken).ConfigureAwait(false);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        LogInstanceAction(instance.InstNo, ApprovalLogActions.Approve, request.ApproverUserId);
        await CaptureOperationAsync(instance, ApprovalLogActions.Approve, request.ApproverUserId, now, cancellationToken)
            .ConfigureAwait(false);

        // R11：终态（通过 / 分支未命中终止）在持久化成功后通知 + 事件发布
        if (ApprovalInstanceStateMachine.IsTerminal(instance.Status))
        {
            if (instance.Status == ApprovalStatus.Approved)
            {
                await NotifyResultAsync(instance, "通过", cancellationToken).ConfigureAwait(false);
            }

            await PublishCompletedEventAsync(instance, cancellationToken).ConfigureAwait(false);
        }
    }

    // ============ 驳回 ============

    /// <inheritdoc />
    public async Task RejectAsync(RejectRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.InstanceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ApproverUserId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Reason);

        var instanceId = ParseId(request.InstanceId, nameof(request.InstanceId));
        var now = DateTimeOffset.UtcNow;

        var (instance, flow) = await LoadRunningInstanceAsync(instanceId, request.InstanceId, cancellationToken).ConfigureAwait(false);
        var task = await RequirePendingTaskAsync(instanceId, instance.CurrentNodeKey, request.ApproverUserId, cancellationToken)
            .ConfigureAwait(false);
        var node = flow.FindNode(task.NodeKey)
            ?? throw new NotFoundException("流程节点", task.NodeKey);

        ApprovalTaskStateMachine.EnsureTransition(task.Status, ApprovalTaskStatus.Rejected);
        task.SetStatus(ApprovalTaskStatus.Rejected, request.Reason, ip: null, now);

        // R4：会签任一驳回即整体驳回；取消本节点全部在途任务（含加签）。
        // 查询在 SaveChanges 之前执行，刚决任务在数据库中仍为 Pending（EF 追踪态滞后），
        // 物化后须按内存态二次过滤，避免对已决任务触发非法状态转移。
        var pendingSiblings = await _db.Tasks
            .Where(t => t.InstId == instanceId && t.NodeKey == task.NodeKey && t.Status == ApprovalTaskStatus.Pending)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        foreach (var sibling in pendingSiblings.Where(t => t.Status == ApprovalTaskStatus.Pending))
        {
            ApprovalTaskStateMachine.EnsureTransition(sibling.Status, ApprovalTaskStatus.Canceled);
            sibling.SetStatus(ApprovalTaskStatus.Canceled, "节点驳回后取消在途任务", ip: null, now);
        }

        var logs = new List<ApprovalLog>
        {
            new(_db.CurrentTenantId, instanceId, ApprovalLogActions.Reject, request.ApproverUserId,
                $"驳回节点 {node.NodeName}（{node.RejectMode}）：{request.Reason}", ip: null, now),
        };

        if (node.RejectMode == ApprovalRejectMode.ToInitiator)
        {
            // R6 直达发起人：实例终态 REJECTED，单据回草稿由订阅方按终态事件驱动（R11）
            ApprovalInstanceStateMachine.EnsureTransition(instance.Status, ApprovalStatus.Rejected);
            instance.SetStatus(ApprovalStatus.Rejected, string.Empty, now);
            logs.Add(new ApprovalLog(_db.CurrentTenantId, instanceId, ApprovalLogActions.Complete, "system",
                $"实例终态 REJECTED（节点 {node.NodeKey} 驳回直达发起人）", ip: null, now));
        }
        else
        {
            // R6 逐级驳回：回上一审批节点重做；首节点被驳回无上级可回 → 直达发起人
            var previousNode = flow.Nodes
                .Where(n => n.NodeType == ApprovalNodeType.Approval && n.SortNo < node.SortNo)
                .OrderByDescending(n => n.SortNo)
                .FirstOrDefault();
            if (previousNode is null)
            {
                ApprovalInstanceStateMachine.EnsureTransition(instance.Status, ApprovalStatus.Rejected);
                instance.SetStatus(ApprovalStatus.Rejected, string.Empty, now);
                logs.Add(new ApprovalLog(_db.CurrentTenantId, instanceId, ApprovalLogActions.Complete, "system",
                    "首节点驳回无上级节点可回退，实例终态 REJECTED", ip: null, now));
            }
            else
            {
                instance.SetStatus(ApprovalStatus.Pending, previousNode.NodeKey, now);
                await CreateTasksForNodeAsync(flow, previousNode, instance, request.ApproverUserId, now, logs, cancellationToken)
                    .ConfigureAwait(false);
                logs.Add(new ApprovalLog(_db.CurrentTenantId, instanceId, ApprovalLogActions.Reject, "system",
                    $"逐级驳回回退至节点 {previousNode.NodeName}（{previousNode.NodeKey}）", ip: null, now));
            }
        }

        await _db.OpLogs.AddRangeAsync(logs, cancellationToken).ConfigureAwait(false);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        LogInstanceAction(instance.InstNo, ApprovalLogActions.Reject, request.ApproverUserId);
        await CaptureOperationAsync(instance, ApprovalLogActions.Reject, request.ApproverUserId, now, cancellationToken)
            .ConfigureAwait(false);

        // R11：驳回终态通知发起人 + 事件发布（持久化成功之后）
        if (ApprovalInstanceStateMachine.IsTerminal(instance.Status))
        {
            await NotifyResultAsync(instance, $"驳回（{request.Reason}）", cancellationToken).ConfigureAwait(false);
            await PublishCompletedEventAsync(instance, cancellationToken).ConfigureAwait(false);
        }
    }

    // ============ 撤回 ============

    /// <inheritdoc />
    public async Task WithdrawAsync(WithdrawRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.InstanceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.InitiatorUserId);

        var instanceId = ParseId(request.InstanceId, nameof(request.InstanceId));
        var now = DateTimeOffset.UtcNow;

        var instance = await _db.Instances
            .FirstOrDefaultAsync(i => i.Id == instanceId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new NotFoundException("审批实例", request.InstanceId);
        if (!string.Equals(instance.InitiatorId, request.InitiatorUserId, StringComparison.Ordinal))
        {
            throw new ForbiddenException("仅发起人本人可撤回审批。");
        }

        // 实例已终态（如小额链一级即通过）直接冲突拒绝，避免落入状态机通用异常
        EnsureRunning(instance);

        // R7：下一节点无任何处理动作时方可撤回（当前节点存在已处理任务即拒绝）
        var currentTasks = await _db.Tasks
            .Where(t => t.InstId == instanceId && t.NodeKey == instance.CurrentNodeKey)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        if (currentTasks.Any(t => ApprovalTaskStateMachine.IsTerminal(t.Status)))
        {
            throw new ConflictException("当前节点已有处理动作，无法撤回。");
        }

        foreach (var task in currentTasks.Where(t => t.Status == ApprovalTaskStatus.Pending))
        {
            ApprovalTaskStateMachine.EnsureTransition(task.Status, ApprovalTaskStatus.Canceled);
            task.SetStatus(ApprovalTaskStatus.Canceled, "发起人撤回", ip: null, now);
        }

        ApprovalInstanceStateMachine.EnsureTransition(instance.Status, ApprovalStatus.Withdrawn);
        instance.SetStatus(ApprovalStatus.Withdrawn, string.Empty, now);

        var logs = new List<ApprovalLog>
        {
            new(_db.CurrentTenantId, instanceId, ApprovalLogActions.Withdraw, request.InitiatorUserId,
                $"发起人撤回：{request.Reason ?? "无说明"}", ip: null, now),
            new(_db.CurrentTenantId, instanceId, ApprovalLogActions.Complete, "system",
                "实例终态 WITHDRAWN（发起人撤回）", ip: null, now),
        };
        await _db.OpLogs.AddRangeAsync(logs, cancellationToken).ConfigureAwait(false);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        LogInstanceAction(instance.InstNo, ApprovalLogActions.Withdraw, request.InitiatorUserId);
        await CaptureOperationAsync(instance, ApprovalLogActions.Withdraw, request.InitiatorUserId, now, cancellationToken)
            .ConfigureAwait(false);
        await PublishCompletedEventAsync(instance, cancellationToken).ConfigureAwait(false);
    }

    // ============ 加签 ============

    /// <inheritdoc />
    public async Task AddSignerAsync(AddSignerRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.InstanceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.TaskId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ApproverUserId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.AddSignerUserId);

        var instanceId = ParseId(request.InstanceId, nameof(request.InstanceId));
        var taskId = ParseId(request.TaskId, nameof(request.TaskId));
        var now = DateTimeOffset.UtcNow;

        var instance = await _db.Instances
            .FirstOrDefaultAsync(i => i.Id == instanceId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new NotFoundException("审批实例", request.InstanceId);
        EnsureRunning(instance);

        var task = await _db.Tasks
            .FirstOrDefaultAsync(t => t.Id == taskId && t.InstId == instanceId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new NotFoundException("审批任务", request.TaskId);
        if (!string.Equals(task.ApproverId, request.ApproverUserId, StringComparison.Ordinal))
        {
            throw new ForbiddenException("仅任务当前审批人可发起加签。");
        }

        var node = await RequireFlowNodeAsync(instance, task.NodeKey, cancellationToken).ConfigureAwait(false);
        if (!node.AllowAddSign)
        {
            throw new ConflictException($"节点 {node.NodeName} 不允许加签。");
        }
        if (string.Equals(request.AddSignerUserId, request.ApproverUserId, StringComparison.Ordinal))
        {
            throw new ConflictException("加签人不能是当前审批人本人。");
        }

        // R5：加签任务计入节点完成判定——加签人须先审批，原审批人才能继续（原任务保持 Pending）
        var addSignTask = new ApprovalTask(
            _db.CurrentTenantId, instanceId, task.NodeKey, node.NodeName, request.AddSignerUserId,
            ApprovalTaskSourceTypes.AddSign, request.ApproverUserId, task.DueAt, now);

        var log = new ApprovalLog(_db.CurrentTenantId, instanceId, ApprovalLogActions.AddSign, request.ApproverUserId,
            $"{(request.IsBeforeCurrent ? "前加签" : "后加签")}：{request.AddSignerUserId} 加入节点 {node.NodeName} 审批（{request.Reason ?? "无说明"}）",
            ip: null, now);
        await _db.Tasks.AddAsync(addSignTask, cancellationToken).ConfigureAwait(false);
        await _db.OpLogs.AddAsync(log, cancellationToken).ConfigureAwait(false);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        LogInstanceAction(instance.InstNo, ApprovalLogActions.AddSign, request.ApproverUserId);
        await CaptureOperationAsync(instance, ApprovalLogActions.AddSign, request.ApproverUserId, now, cancellationToken)
            .ConfigureAwait(false);
        await NotifyTodoAsync(
            request.AddSignerUserId, instance, node.NodeName, cancellationToken).ConfigureAwait(false);
    }

    // ============ 转办 ============

    /// <inheritdoc />
    public async Task TransferAsync(TransferRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.InstanceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.TaskId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.FromUserId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ToUserId);

        var instanceId = ParseId(request.InstanceId, nameof(request.InstanceId));
        var taskId = ParseId(request.TaskId, nameof(request.TaskId));
        var now = DateTimeOffset.UtcNow;

        var instance = await _db.Instances
            .FirstOrDefaultAsync(i => i.Id == instanceId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new NotFoundException("审批实例", request.InstanceId);
        EnsureRunning(instance);

        var task = await _db.Tasks
            .FirstOrDefaultAsync(t => t.Id == taskId && t.InstId == instanceId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new NotFoundException("审批任务", request.TaskId);
        if (!string.Equals(task.ApproverId, request.FromUserId, StringComparison.Ordinal))
        {
            throw new ForbiddenException("仅任务当前审批人可转办。");
        }
        if (string.Equals(request.ToUserId, request.FromUserId, StringComparison.Ordinal))
        {
            throw new ConflictException("转办目标不能是自己。");
        }

        ApprovalTaskStateMachine.EnsureTransition(task.Status, ApprovalTaskStatus.Transferred);
        task.SetStatus(ApprovalTaskStatus.Transferred, $"转办给 {request.ToUserId}：{request.Reason ?? "无说明"}", ip: null, now);

        var transferTask = new ApprovalTask(
            _db.CurrentTenantId, instanceId, task.NodeKey, task.NodeName, request.ToUserId,
            ApprovalTaskSourceTypes.Transfer, request.FromUserId, task.DueAt, now);

        var log = new ApprovalLog(_db.CurrentTenantId, instanceId, ApprovalLogActions.Transfer, request.FromUserId,
            $"转办给 {request.ToUserId}：{request.Reason ?? "无说明"}", ip: null, now);
        await _db.Tasks.AddAsync(transferTask, cancellationToken).ConfigureAwait(false);
        await _db.OpLogs.AddAsync(log, cancellationToken).ConfigureAwait(false);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        LogInstanceAction(instance.InstNo, ApprovalLogActions.Transfer, request.FromUserId);
        await CaptureOperationAsync(instance, ApprovalLogActions.Transfer, request.FromUserId, now, cancellationToken)
            .ConfigureAwait(false);
        await NotifyTodoAsync(
            request.ToUserId, instance, task.NodeName, cancellationToken).ConfigureAwait(false);
    }

    // ============ 催办 ============

    /// <inheritdoc />
    public async Task UrgeAsync(UrgeRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.InstanceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.RequestorUserId);

        var instanceId = ParseId(request.InstanceId, nameof(request.InstanceId));
        var now = DateTimeOffset.UtcNow;

        var instance = await _db.Instances
            .FirstOrDefaultAsync(i => i.Id == instanceId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new NotFoundException("审批实例", request.InstanceId);
        EnsureRunning(instance);

        var pendingTasks = await _db.Tasks
            .Where(t => t.InstId == instanceId && t.Status == ApprovalTaskStatus.Pending)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        if (pendingTasks.Count == 0)
        {
            throw new ConflictException("当前无待办任务可催办。");
        }

        foreach (var task in pendingTasks)
        {
            task.RecordUrge(now);
        }

        var log = new ApprovalLog(_db.CurrentTenantId, instanceId, ApprovalLogActions.Urge, request.RequestorUserId,
            $"催办节点 {instance.CurrentNodeKey} 的 {pendingTasks.Count} 个待办任务：{request.Message ?? "请尽快处理"}",
            ip: null, now);
        await _db.OpLogs.AddAsync(log, cancellationToken).ConfigureAwait(false);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        LogInstanceAction(instance.InstNo, ApprovalLogActions.Urge, request.RequestorUserId);
        await CaptureOperationAsync(instance, ApprovalLogActions.Urge, request.RequestorUserId, now, cancellationToken)
            .ConfigureAwait(false);

        foreach (var task in pendingTasks)
        {
            await SendUrgeAsync(task.ApproverId, instance, task.NodeName, cancellationToken).ConfigureAwait(false);
        }
    }

    // ============ 查询（批量投影，禁止 N+1） ============

    /// <inheritdoc />
    public async Task<IReadOnlyList<ApprovalTaskInfo>> GetTodoListAsync(string userId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        var tasks = await _db.Tasks
            .AsNoTracking()
            .Where(t => t.ApproverId == userId && t.Status == ApprovalTaskStatus.Pending)
            .OrderByDescending(t => t.CreatedAt)
            .Take(200)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return await ProjectTaskInfosAsync(tasks, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ApprovalInstanceInfo>> GetMyInitiatedListAsync(string userId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        var instances = await _db.Instances
            .AsNoTracking()
            .Where(i => i.InitiatorId == userId)
            .OrderByDescending(i => i.StartedAt)
            .Take(100)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        if (instances.Count == 0)
        {
            return [];
        }

        var instanceIds = instances.Select(static i => i.Id).ToList();
        var tasks = await _db.Tasks
            .AsNoTracking()
            .Where(t => instanceIds.Contains(t.InstId) && t.Status == ApprovalTaskStatus.Pending)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var tasksByInstance = tasks.GroupBy(static t => t.InstId).ToDictionary(static g => g.Key, static g => g.ToList());
        return [.. instances.Select(i => new ApprovalInstanceInfo(
            i.Id.ToString(s_culture),
            i.DefCode,
            i.BizType,
            i.BizId,
            i.BizNo,
            i.Title,
            i.InitiatorId,
            i.Status,
            i.StartedAt,
            i.EndedAt,
            tasksByInstance.TryGetValue(i.Id, out var list)
                ? [.. list.Select(ToTaskInfo)]
                : []))];
    }

    /// <inheritdoc />
    public async Task<ApprovalInstanceInfo?> GetInstanceAsync(string instanceId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceId);

        var id = ParseId(instanceId, nameof(instanceId));
        var instance = await _db.Instances
            .AsNoTracking()
            .FirstOrDefaultAsync(i => i.Id == id, cancellationToken)
            .ConfigureAwait(false);
        if (instance is null)
        {
            return null;
        }

        var tasks = await _db.Tasks
            .AsNoTracking()
            .Where(t => t.InstId == id)
            .OrderBy(t => t.CreatedAt)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return new ApprovalInstanceInfo(
            instance.Id.ToString(s_culture),
            instance.DefCode,
            instance.BizType,
            instance.BizId,
            instance.BizNo,
            instance.Title,
            instance.InitiatorId,
            instance.Status,
            instance.StartedAt,
            instance.EndedAt,
            [.. tasks.Select(ToTaskInfo)]);
    }

    // ============ 内部：加载与校验 ============

    /// <summary>加载运行中的实例（含锁定版本流程聚合，一次批量装配）；终态实例抛冲突。</summary>
    private async Task<(ApprovalInstance Instance, EnabledApprovalFlow Flow)> LoadRunningInstanceAsync(
        long instanceId, string rawInstanceId, CancellationToken cancellationToken)
    {
        var instance = await _db.Instances
            .FirstOrDefaultAsync(i => i.Id == instanceId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new NotFoundException("审批实例", rawInstanceId);
        EnsureRunning(instance);

        var flow = await _flowRegistry.LoadByDefinitionIdAsync(instance.DefId, cancellationToken).ConfigureAwait(false);
        return (instance, flow);
    }

    /// <summary>实例当前必须处于审批中。</summary>
    /// <param name="instance">实例实体。</param>
    /// <exception cref="ConflictException">实例非 Pending 状态时抛出。</exception>
    private static void EnsureRunning(ApprovalInstance instance)
    {
        if (instance.Status != ApprovalStatus.Pending)
        {
            throw new ConflictException($"审批实例 {instance.InstNo} 已处于终态 {instance.Status}，禁止操作。");
        }
    }

    /// <summary>
    /// 定位实例当前节点的指定审批人待办任务。节点无任何在途任务抛 404（无效引用）；
    /// 存在他人待办而操作者无份抛 403（越权边界，不泄露任务归属明细）。
    /// </summary>
    private async Task<ApprovalTask> RequirePendingTaskAsync(
        long instanceId, string nodeKey, string approverId, CancellationToken cancellationToken)
    {
        var nodeTasks = await _db.Tasks
            .Where(t => t.InstId == instanceId && t.NodeKey == nodeKey)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var mine = nodeTasks.FirstOrDefault(t => t.Status == ApprovalTaskStatus.Pending
            && string.Equals(t.ApproverId, approverId, StringComparison.Ordinal));
        if (mine is not null)
        {
            return mine;
        }

        if (nodeTasks.Any(t => t.Status == ApprovalTaskStatus.Pending))
        {
            throw new ForbiddenException("仅任务当前审批人可执行该操作。");
        }

        throw new NotFoundException("审批待办任务", $"实例 {instanceId} 节点 {nodeKey} 待办人 {approverId}");
    }

    /// <summary>按实例锁定定义取节点实体（DefId 唯一对应锁定版本，R2）。</summary>
    private async Task<ApprovalFlowNode> RequireFlowNodeAsync(
        ApprovalInstance instance, string nodeKey, CancellationToken cancellationToken)
    {
        var node = await _db.FlowNodes
            .AsNoTracking()
            .FirstOrDefaultAsync(n => n.DefId == instance.DefId && n.NodeKey == nodeKey, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new NotFoundException("流程节点", $"实例 {instance.InstNo} 节点 {nodeKey}");
        return node;
    }

    // ============ 内部：任务生成 ============

    /// <summary>
    /// 为节点生成任务：审批人解析（轮询来源）→ 发起人避让（R13）→ 委托转派（R9）。
    /// 避让命中时沿分支线继续路由（嵌套深度受流程定义校验约束）。
    /// </summary>
    private async Task CreateTasksForNodeAsync(
        EnabledApprovalFlow flow,
        ApprovalFlowNode node,
        ApprovalInstance instance,
        string operatorId,
        DateTimeOffset now,
        List<ApprovalLog> logs,
        CancellationToken cancellationToken)
    {
        var snapshot = JsonSerializer.Deserialize<Dictionary<string, object?>>(instance.SnapshotJson, QlxJsonDefaults.Default)
            ?? new Dictionary<string, object?>();
        var request = new ApprovalApproverResolveRequest(
            node.ApproverType, node.ApproverRef, _db.CurrentTenantId, instance.InitiatorId, instance.OrgId, snapshot);

        var approvers = await ResolveApproversAsync(request, cancellationToken).ConfigureAwait(false);

        // R13 发起人避让：节点审批人仅为发起人本人且开启避让时自动通过、沿分支线继续
        if (node.SkipIfInitiator
            && approvers.Count == 1
            && string.Equals(approvers[0], instance.InitiatorId, StringComparison.Ordinal))
        {
            logs.Add(new ApprovalLog(_db.CurrentTenantId, instance.Id, ApprovalLogActions.Approve, "system",
                $"发起人避让：节点 {node.NodeName}（{node.NodeKey}）审批人为发起人本人，自动通过", ip: null, now));
            var outcome = await EvaluateBranchAsync(flow, instance, node.NodeKey, snapshot, logs, cancellationToken)
                .ConfigureAwait(false);
            await RouteAfterBranchAsync(flow, instance, outcome, operatorId, now, logs, cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        if (approvers.Count == 0)
        {
            throw new ConflictException(
                $"节点 {node.NodeName}（{node.NodeKey}）解析不到任何审批人（类型 {node.ApproverType}），提交被拒绝。");
        }

        instance.SetStatus(ApprovalStatus.Pending, node.NodeKey, now);

        // R9 委托转派：有效期内且覆盖本单据类型的委托自动转代理，留痕 source_type=DELEGATE
        var delegates = await FindActiveDelegatesAsync(approvers, instance.BizType, now, cancellationToken)
            .ConfigureAwait(false);
        var dueAt = node.TimeoutHours > 0 ? now.AddHours(node.TimeoutHours) : (DateTimeOffset?)null;
        var tasks = approvers.Distinct().Select(approverId =>
        {
            var hit = delegates.GetValueOrDefault(approverId);
            return hit is null
                ? new ApprovalTask(_db.CurrentTenantId, instance.Id, node.NodeKey, node.NodeName, approverId,
                    ApprovalTaskSourceTypes.Normal, null, dueAt, now)
                : new ApprovalTask(_db.CurrentTenantId, instance.Id, node.NodeKey, node.NodeName, hit.AgentId,
                    ApprovalTaskSourceTypes.Delegate, approverId, dueAt, now);
        }).ToList();

        await _db.Tasks.AddRangeAsync(tasks, cancellationToken).ConfigureAwait(false);

        // 待办通知（write-through 之外尽力推送；失败不阻断主流程）
        foreach (var task in tasks)
        {
            await NotifyTodoAsync(task.ApproverId, instance, node.NodeName, cancellationToken).ConfigureAwait(false);
        }

        foreach (var delegated in tasks.Where(t => t.SourceType == ApprovalTaskSourceTypes.Delegate))
        {
            logs.Add(new ApprovalLog(_db.CurrentTenantId, instance.Id, ApprovalLogActions.Delegate, delegated.SourceFrom!,
                $"委托生效：{delegated.SourceFrom} 委托 {delegated.ApproverId} 处理节点 {node.NodeName}", ip: null, now));
        }
    }

    /// <summary>轮询审批人来源解析节点审批人（来源命中即消费；全部来源解析不到返回空列表）。</summary>
    private async Task<IReadOnlyList<string>> ResolveApproversAsync(
        ApprovalApproverResolveRequest request, CancellationToken cancellationToken)
    {
        foreach (var source in _approverSources)
        {
            if (!source.CanResolve(request.ApproverType))
            {
                continue;
            }

            var resolved = await source.ResolveAsync(request, cancellationToken).ConfigureAwait(false);
            if (resolved.Count > 0)
            {
                return resolved;
            }
        }

        return [];
    }

    /// <summary>批量查询委托映射（一次 SQL，键=委托人）；BizTypesJson 空数组表示全部单据类型（R9）。</summary>
    private async Task<Dictionary<string, ApprovalDelegation>> FindActiveDelegatesAsync(
        IReadOnlyList<string> approverIds, string bizType, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var rows = await _db.Delegates
            .AsNoTracking()
            .Where(d => d.Status == ApprovalDelegationStatus.Active
                && d.StartTime <= now && now <= d.EndTime
                && approverIds.Contains(d.DelegatorId))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var map = new Dictionary<string, ApprovalDelegation>(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            if (!MatchesBizTypes(row.BizTypesJson, bizType))
            {
                continue;
            }

            // 同一委托人多条有效委托时取最早开始的一条（确定性）
            if (!map.TryGetValue(row.DelegatorId, out var existing) || row.StartTime < existing.StartTime)
            {
                map[row.DelegatorId] = row;
            }
        }

        return map;
    }

    // ============ 内部：节点完成判定与分支路由 ============

    /// <summary>
    /// 节点完成判定（R4）：或签首个通过即决（true）；会签须全部通过——按内存态判定无任何 Pending 任务。
    /// 判定发生在 SaveChanges 之前，数据库态滞后（刚决任务仍为 Pending），必须物化后取跟踪实体内存态。
    /// </summary>
    private async Task<bool> DecideNodeCompletionAsync(
        long instanceId, ApprovalFlowNode node, CancellationToken cancellationToken)
    {
        if (node.SignMode == ApprovalSignMode.OrSign)
        {
            return true;
        }

        var nodeTasks = await _db.Tasks
            .Where(t => t.InstId == instanceId && t.NodeKey == node.NodeKey)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return nodeTasks.All(t => t.Status != ApprovalTaskStatus.Pending);
    }

    /// <summary>节点已决后：或签他决取消 → 分支求值 → 路由到下一节点或终态（R3/R4）。</summary>
    private async Task AdvanceFromNodeAsync(
        EnabledApprovalFlow flow,
        ApprovalFlowNode node,
        ApprovalInstance instance,
        string operatorId,
        DateTimeOffset now,
        List<ApprovalLog> logs,
        CancellationToken cancellationToken)
    {
        var snapshot = JsonSerializer.Deserialize<Dictionary<string, object?>>(instance.SnapshotJson, QlxJsonDefaults.Default)
            ?? new Dictionary<string, object?>();

        // R4 或签首个通过后其余任务他决取消（会签场景 pending 为空，调用无害）
        await CancelOrSignPendingTasksAsync(instance.Id, node.NodeKey, now, cancellationToken).ConfigureAwait(false);

        var outcome = await EvaluateBranchAsync(flow, instance, node.NodeKey, snapshot, logs, cancellationToken)
            .ConfigureAwait(false);
        await RouteAfterBranchAsync(flow, instance, outcome, operatorId, now, logs, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>分支求值：priority 降序逐条经条件引擎求值，未命中走兜底；无兜底置实例 Terminated 并告警（R3）。</summary>
    private async Task<string?> EvaluateBranchAsync(
        EnabledApprovalFlow flow,
        ApprovalInstance instance,
        string fromNodeKey,
        Dictionary<string, object?> snapshot,
        List<ApprovalLog> logs,
        CancellationToken cancellationToken)
    {
        var branches = flow.FindBranches(fromNodeKey);
        var resolver = new ApprovalSnapshotResolver(instance.SnapshotJson);

        foreach (var branch in branches.Where(b => !b.IsDefault))
        {
            var expression = ConditionExpression.FromJson(branch.ExprJson);
            if (_expressionEngine.Evaluate(expression, resolver))
            {
                logs.Add(new ApprovalLog(_db.CurrentTenantId, instance.Id, ApprovalLogActions.Approve, "system",
                    $"分支命中（priority={branch.Priority}）：{fromNodeKey} → {branch.ToNodeKey}", ip: null, DateTimeOffset.UtcNow));
                return branch.ToNodeKey;
            }
        }

        var fallback = branches.FirstOrDefault(b => b.IsDefault);
        if (fallback is not null)
        {
            logs.Add(new ApprovalLog(_db.CurrentTenantId, instance.Id, ApprovalLogActions.Approve, "system",
                $"条件分支全部未命中，走兜底分支：{fromNodeKey} → {fallback.ToNodeKey}", ip: null, DateTimeOffset.UtcNow));
            return fallback.ToNodeKey;
        }

        // R3 无兜底且未命中：实例置异常终态（映射 Terminated，人工干预），系统预警消息 + 错误日志
        var now = DateTimeOffset.UtcNow;
        ApprovalInstanceStateMachine.EnsureTransition(instance.Status, ApprovalStatus.Terminated);
        instance.SetStatus(ApprovalStatus.Terminated, string.Empty, now);
        logs.Add(new ApprovalLog(_db.CurrentTenantId, instance.Id, ApprovalLogActions.BranchMissAlert, "system",
            $"节点 {fromNodeKey} 条件分支全部未命中且无兜底分支，实例终止等待人工干预", ip: null, now));
        LogBranchMiss(instance.InstNo, fromNodeKey);
        await SendSystemAlertAsync(
            instance.InitiatorId, instance,
            $"审批实例 {instance.InstNo} 在节点 {fromNodeKey} 条件分支未命中且无兜底，已终止等待人工处理",
            cancellationToken).ConfigureAwait(false);
        return null;
    }

    /// <summary>按分支结果路由：终态收口、无兜底终止收口或生成下一节点任务（非审批节点沿分支线继续）。</summary>
    private async Task RouteAfterBranchAsync(
        EnabledApprovalFlow flow,
        ApprovalInstance instance,
        string? nextNodeKey,
        string operatorId,
        DateTimeOffset now,
        List<ApprovalLog> logs,
        CancellationToken cancellationToken)
    {
        if (nextNodeKey is null)
        {
            // 无兜底未命中：实例已置 Terminated，等本次动作统一 SaveChanges 收口
            return;
        }

        if (nextNodeKey.Length == 0)
        {
            // 终态分支：实例 Approved + 终态留痕
            ApprovalInstanceStateMachine.EnsureTransition(instance.Status, ApprovalStatus.Approved);
            instance.SetStatus(ApprovalStatus.Approved, string.Empty, now);
            logs.Add(new ApprovalLog(_db.CurrentTenantId, instance.Id, ApprovalLogActions.Complete, "system",
                "实例终态 APPROVED（到达结束分支）", ip: null, now));
            return;
        }

        var nextNode = flow.FindNode(nextNodeKey)
            ?? throw new ConflictException($"分支目标节点 {nextNodeKey} 不在流程 {flow.Definition.DefCode} 定义中。");
        if (nextNode.NodeType == ApprovalNodeType.Approval)
        {
            await CreateTasksForNodeAsync(flow, nextNode, instance, operatorId, now, logs, cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        // 分支 / 抄送节点不生成任务：沿其分支线继续路由
        var nested = await EvaluateBranchAsync(flow, instance, nextNode.NodeKey, JsonSerializer
            .Deserialize<Dictionary<string, object?>>(instance.SnapshotJson, QlxJsonDefaults.Default)
            ?? new Dictionary<string, object?>(), logs, cancellationToken).ConfigureAwait(false);
        await RouteAfterBranchAsync(flow, instance, nested, operatorId, now, logs, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>或签首个通过后取消其余在途任务（他决留痕，R4）。</summary>
    private async Task CancelOrSignPendingTasksAsync(
        long instanceId, string nodeKey, DateTimeOffset now, CancellationToken cancellationToken)
    {
        // 查询在 SaveChanges 之前执行，刚决任务在数据库中仍为 Pending（EF 追踪态滞后），
        // 物化后须按内存态二次过滤，避免对已决任务触发非法状态转移。
        var pending = await _db.Tasks
            .Where(t => t.InstId == instanceId && t.NodeKey == nodeKey && t.Status == ApprovalTaskStatus.Pending)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        foreach (var task in pending.Where(t => t.Status == ApprovalTaskStatus.Pending))
        {
            ApprovalTaskStateMachine.EnsureTransition(task.Status, ApprovalTaskStatus.Canceled);
            task.SetStatus(ApprovalTaskStatus.Canceled, "或签他决自动取消", ip: null, now);
        }
    }

    // ============ 内部：通知与留痕 ============

    /// <summary>发送待办通知（msg.todo.generated 模板，2D write-through）。</summary>
    private async Task NotifyTodoAsync(
        string approverId, ApprovalInstance instance, string nodeName, CancellationToken cancellationToken)
    {
        try
        {
            await _messageService.SendTemplateAsync(new SendTemplateRequest(
                "msg.todo.generated", approverId,
                new Dictionary<string, object?>
                {
                    ["title"] = $"{instance.Title}（{nodeName}）",
                    ["bizNo"] = instance.BizNo,
                },
                BizRef: new MessageBizRef(instance.BizType, instance.BizId, instance.BizNo)), cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // 通知失败不阻断审批主流转（write-through 持久化由信息中心保证；此处仅告警）
            LogNotifyFailed(instance.InstNo, approverId, ex.Message);
        }
    }

    /// <summary>发送催办通知（msg.urge.reminder 模板，高优先级）。</summary>
    private async Task SendUrgeAsync(
        string approverId, ApprovalInstance instance, string nodeName, CancellationToken cancellationToken)
    {
        try
        {
            await _messageService.SendTemplateAsync(new SendTemplateRequest(
                "msg.urge.reminder", approverId,
                new Dictionary<string, object?>
                {
                    ["title"] = $"{instance.Title}（{nodeName}）",
                    ["bizNo"] = instance.BizNo,
                },
                Priority: MessagePriority.High,
                BizRef: new MessageBizRef(instance.BizType, instance.BizId, instance.BizNo)), cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogNotifyFailed(instance.InstNo, approverId, ex.Message);
        }
    }

    /// <summary>审批结果通知发起人（msg.result.notice 模板，R11 验收：驳回结果必达）。</summary>
    private async Task NotifyResultAsync(ApprovalInstance instance, string result, CancellationToken cancellationToken)
    {
        try
        {
            await _messageService.SendTemplateAsync(new SendTemplateRequest(
                "msg.result.notice", instance.InitiatorId,
                new Dictionary<string, object?>
                {
                    ["title"] = instance.Title,
                    ["bizNo"] = instance.BizNo,
                    ["result"] = result,
                },
                BizRef: new MessageBizRef(instance.BizType, instance.BizId, instance.BizNo)), cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogNotifyFailed(instance.InstNo, instance.InitiatorId, ex.Message);
        }
    }

    /// <summary>系统预警（msg.alert.warning 模板：分支未命中无兜底 / 超时上报解析不到上级）。</summary>
    private async Task SendSystemAlertAsync(
        string recipientId, ApprovalInstance instance, string content, CancellationToken cancellationToken)
    {
        try
        {
            await _messageService.SendTemplateAsync(new SendTemplateRequest(
                "msg.alert.warning", recipientId,
                new Dictionary<string, object?>
                {
                    ["title"] = $"审批异常：{instance.Title}",
                    ["content"] = content,
                },
                Priority: MessagePriority.Urgent,
                BizRef: new MessageBizRef(instance.BizType, instance.BizId, instance.BizNo)), cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogNotifyFailed(instance.InstNo, recipientId, ex.Message);
        }
    }

    /// <summary>终态事件发布（R11）：持久化成功后经事件总线 Outbox 通道发布，业务模块据此驱动单据状态机。</summary>
    private async Task PublishCompletedEventAsync(ApprovalInstance instance, CancellationToken cancellationToken)
    {
        var completed = new ApprovalCompletedEvent(
            instance.Id.ToString(s_culture),
            instance.DefCode,
            instance.BizType,
            instance.BizId,
            instance.Status,
            instance.InitiatorId,
            instance.EndedAt ?? DateTimeOffset.UtcNow)
        {
            TenantCode = _db.CurrentTenantId.ToString(s_culture),
        };
        await _eventBus.PublishDurableAsync(completed, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>记录业务级操作日志（IAuditService，审计失败不阻断业务）。</summary>
    private async Task CaptureOperationAsync(
        ApprovalInstance instance, string operation, string operatorId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        try
        {
            await _auditService.CaptureOperationAsync(new OperationLogEntry(
                EventId: 0,
                ModuleCode: QlxModuleCodes.Support,
                Operation: operation,
                Description: $"审批实例 {instance.InstNo}（{instance.Title}）{operation}",
                OperatedBy: operatorId,
                OperatedAt: now,
                TenantId: _db.CurrentTenantId,
                TargetType: instance.BizType,
                TargetId: long.TryParse(instance.BizId, out var bizId) ? bizId : null,
                Metadata: null), cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogAuditFailed(instance.InstNo, operation, ex.Message);
        }
    }

    // ============ 内部：工具 ============

    /// <summary>混淆串 Id 解析（契约侧为 string；失败抛校验异常）。</summary>
    /// <param name="raw">原始字符串。</param>
    /// <param name="paramName">参数名（nameof 传入）。</param>
    /// <returns>long Id。</returns>
    private static long ParseId(string raw, string paramName)
        => long.TryParse(raw, NumberStyles.Integer, s_culture, out var id) && id > 0
            ? id
            : throw new ValidationException(paramName, $"无效的审批资源 Id：{raw}。");

    /// <summary>判定委托范围 JSON 是否覆盖单据类型（空数组 = 全部）。</summary>
    private static bool MatchesBizTypes(string bizTypesJson, string bizType)
    {
        if (string.IsNullOrWhiteSpace(bizTypesJson) || bizTypesJson == "[]")
        {
            return true;
        }

        var scope = JsonSerializer.Deserialize<string[]>(bizTypesJson, QlxJsonDefaults.Default);
        return scope is null || scope.Length == 0 || scope.Contains(bizType, StringComparer.Ordinal);
    }

    /// <summary>任务实体 → 2A 契约 DTO 投影。</summary>
    private static ApprovalTaskInfo ToTaskInfo(ApprovalTask task)
        => new(
            task.Id.ToString(s_culture),
            task.InstId.ToString(s_culture),
            task.NodeKey,
            task.NodeName,
            task.ApproverId,
            task.Status,
            task.CreatedAt,
            task.HandledAt,
            task.Opinion);

    /// <summary>批量把任务列表投影为 DTO（实例标题一次批量补充，禁止 N+1）。</summary>
    private async Task<IReadOnlyList<ApprovalTaskInfo>> ProjectTaskInfosAsync(
        List<ApprovalTask> tasks, CancellationToken cancellationToken)
    {
        if (tasks.Count == 0)
        {
            return [];
        }

        var instIds = tasks.Select(static t => t.InstId).Distinct().ToList();
        var instances = await _db.Instances
            .AsNoTracking()
            .Where(i => instIds.Contains(i.Id))
            .ToDictionaryAsync(static i => i.Id, cancellationToken)
            .ConfigureAwait(false);

        return [.. tasks.Select(t => new ApprovalTaskInfo(
            t.Id.ToString(s_culture),
            t.InstId.ToString(s_culture),
            t.NodeKey,
            instances.TryGetValue(t.InstId, out var inst) ? $"{inst.Title}（{t.NodeName}）" : t.NodeName,
            t.ApproverId,
            t.Status,
            t.CreatedAt,
            t.HandledAt,
            t.Opinion))];
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "审批实例 {InstNo} 完成 {Action}（操作人 {OperatorId}）")]
    private partial void LogInstanceAction(string instNo, string action, string operatorId);

    [LoggerMessage(Level = LogLevel.Error, Message = "审批实例 {InstNo} 节点 {NodeKey} 条件分支全部未命中且无兜底，实例已终止（R3）")]
    private partial void LogBranchMiss(string instNo, string nodeKey);

    [LoggerMessage(Level = LogLevel.Warning, Message = "审批实例 {InstNo} 通知 {RecipientId} 失败：{Reason}")]
    private partial void LogNotifyFailed(string instNo, string recipientId, string reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "审批实例 {InstNo} 操作 {Action} 审计记录失败：{Reason}")]
    private partial void LogAuditFailed(string instNo, string action, string reason);
}
