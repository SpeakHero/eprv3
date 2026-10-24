/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-23 09:30:00 +08:00 中国标准时间 星期五
 * 描述：审批超时扫描任务（approval.timeout.scan，R8）——扫描到期 Pending 任务：
 *       节流催办（msg.urge.reminder）→ 催办达上限且节点为上报策略时转上级（ESCALATE 任务留痕）；
 *       上级解析不到时发系统预警并继续催办。次数 / 间隔 / 批量参数化走 SupportApprovalsOptions。
 */

using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Qlx.Abstractions.Approvals;
using Qlx.Abstractions.Jobs;
using Qlx.Abstractions.Messages;
using Qlx.Modules.Support.Domain;
using Qlx.Modules.Support.Options;
using Qlx.Modules.Support.Services.Approvals;

namespace Qlx.Modules.Support.Jobs.SystemJobs;

/// <summary>
/// 审批超时扫描任务处理器（R8）。
/// 单轮流程：到期任务批量扫描（Take 批量上限）→ 实例 / 节点批量装配（禁止 N+1）→
/// 节流判定（UrgeIntervalMinutes）→ 催办通知 → 达上限且节点 Escalate 策略时上报上级（DEPT_LEADER 经审批人来源解析）。
/// </summary>
/// <param name="db">租户上下文。</param>
/// <param name="approverSources">审批人来源集合（上级解析经 Bas 来源，宿主聚合）。</param>
/// <param name="messageService">信息中心消息服务（催办 / 预警）。</param>
/// <param name="options">审批中心配置（催办上限 / 节流间隔 / 批量）。</param>
/// <param name="logger">日志器。</param>
public sealed partial class ApprovalTimeoutScanHandler(
    SupportTenantDbContext db,
    IEnumerable<IApprovalApproverSource> approverSources,
    IMessageService messageService,
    IOptions<SupportApprovalsOptions> options,
    ILogger<ApprovalTimeoutScanHandler> logger) : IJobHandler
{
    private readonly SupportTenantDbContext _db = db;
    private readonly IReadOnlyList<IApprovalApproverSource> _approverSources = [.. approverSources];
    private readonly IMessageService _messageService = messageService;
    private readonly SupportApprovalsOptions _options = options.Value;
    private readonly ILogger<ApprovalTimeoutScanHandler> _logger = logger;

    /// <inheritdoc />
    public string TaskCode => JobTaskCodes.ApprovalTimeoutScan;

    /// <inheritdoc />
    public async Task<CompletedResult> ExecuteAsync(JobContext context, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;

        // 1. 到期任务批量扫描（DueAt 非空即节点配置了超时；Take 批量上限分批防抖）
        var dueTasks = await _db.Tasks
            .Where(t => t.Status == ApprovalTaskStatus.Pending
                && t.DueAt != null && t.DueAt <= now)
            .OrderBy(t => t.DueAt)
            .Take(_options.TimeoutScanBatchSize)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        if (dueTasks.Count == 0)
        {
            LogScanCompleted(_db.CurrentTenantId, 0, 0, 0);
            return BuildResult(0, 0, 0);
        }

        // 2. 实例与节点批量装配（两次批量 SQL，禁止逐任务联查）
        var instIds = dueTasks.Select(static t => t.InstId).Distinct().ToList();
        var instances = await _db.Instances
            .Where(i => instIds.Contains(i.Id) && i.Status == ApprovalStatus.Pending)
            .ToDictionaryAsync(static i => i.Id, cancellationToken)
            .ConfigureAwait(false);
        var defIds = instances.Values.Select(static i => i.DefId).Distinct().ToList();
        var nodes = defIds.Count == 0
            ? []
            : await _db.FlowNodes
                .AsNoTracking()
                .Where(n => defIds.Contains(n.DefId))
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
        var nodesByDef = nodes.ToDictionary(static n => (n.DefId, n.NodeKey), static n => n);

        var urged = 0;
        var escalated = 0;
        var alerts = 0;
        var logs = new List<ApprovalLog>();

        foreach (var task in dueTasks)
        {
            if (!instances.TryGetValue(task.InstId, out var instance))
            {
                continue; // 实例已终态：任务随后由动作收口或人工清理，本轮跳过
            }

            var node = nodesByDef.GetValueOrDefault((instance.DefId, task.NodeKey));
            var urgeInterval = TimeSpan.FromMinutes(_options.UrgeIntervalMinutes);

            // 节流：距最近一次催办不足间隔时跳过（避免每轮扫描重复轰炸）
            if (task.UrgeLastAt is { } lastUrge && now - lastUrge < urgeInterval)
            {
                continue;
            }

            // 达到催办上限且节点为上报策略 → 转上级（R8）；否则仅催办
            if (node is not null
                && node.TimeoutAction == ApprovalTimeoutAction.Escalate
                && task.UrgeCount >= _options.MaxUrgeCount)
            {
                var handled = await EscalateToLeaderAsync(
                    task, instance, node, now, logs, cancellationToken).ConfigureAwait(false);
                if (handled)
                {
                    escalated++;
                    continue;
                }

                // 上级解析不到：系统预警 + 继续催办（R8 收口策略）
                alerts++;
                await SendSystemAlertAsync(
                    instance.InitiatorId, instance, task,
                    $"审批任务（节点 {task.NodeName}）已催办 {task.UrgeCount} 次仍超时，且解析不到上级审批人，请人工跟进。",
                    cancellationToken).ConfigureAwait(false);
            }

            task.RecordUrge(now);
            logs.Add(new ApprovalLog(_db.CurrentTenantId, instance.Id, ApprovalLogActions.Urge, "system",
                $"超时自动催办（第 {task.UrgeCount} 次）：节点 {task.NodeName}，待办人 {task.ApproverId}", ip: null, now));
            await SendUrgeAsync(task.ApproverId, instance, task.NodeName, cancellationToken).ConfigureAwait(false);
            urged++;
        }

        if (logs.Count > 0)
        {
            _db.OpLogs.AddRange(logs);
            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        LogScanCompleted(_db.CurrentTenantId, urged, escalated, alerts);
        return BuildResult(urged, escalated, alerts);
    }

    /// <summary>组装任务执行结果载荷。</summary>
    private CompletedResult BuildResult(int urged, int escalated, int alerts)
        => new(
            new { Urged = urged, Escalated = escalated, Alerts = alerts, BatchSize = _options.TimeoutScanBatchSize },
            null, null, ExecutionDurationMs: 0, IsSuccess: true);

    /// <summary>
    /// 超时上报上级（R8）：取消原任务（留痕）→ 经审批人来源解析上级（DEPT_LEADER，Bas 来源按组织管理员承担）→
    /// 生成 ESCALATE 任务（SourceFrom=原待办人留痕）。解析不到任何人返回 false（调用方发预警继续催办）。
    /// </summary>
    private async Task<bool> EscalateToLeaderAsync(
        ApprovalTask task,
        ApprovalInstance instance,
        ApprovalFlowNode node,
        DateTimeOffset now,
        List<ApprovalLog> logs,
        CancellationToken cancellationToken)
    {
        var request = new ApprovalApproverResolveRequest(
            ApprovalApproverTypes.DeptLeader,
            ApproverRef: instance.OrgId > 0 ? instance.OrgId.ToString(CultureInfo.InvariantCulture) : null,
            _db.CurrentTenantId,
            instance.InitiatorId,
            instance.OrgId,
            new Dictionary<string, object?>());

        var leaders = new List<string>();
        foreach (var source in _approverSources)
        {
            if (!source.CanResolve(ApprovalApproverTypes.DeptLeader))
            {
                continue;
            }

            var resolved = await source.ResolveAsync(request, cancellationToken).ConfigureAwait(false);
            if (resolved.Count > 0)
            {
                leaders.AddRange(resolved);
                break;
            }
        }

        leaders = [.. leaders.Where(l => !string.Equals(l, task.ApproverId, StringComparison.Ordinal)).Distinct()];
        if (leaders.Count == 0)
        {
            return false;
        }

        // 原任务取消留痕 + 上报任务生成（DueAt 按节点超时周期顺延，进入下一轮催办循环）
        ApprovalTaskStateMachine.EnsureTransition(task.Status, ApprovalTaskStatus.Canceled);
        task.SetStatus(ApprovalTaskStatus.Canceled, $"超时催办 {task.UrgeCount} 次未处理，上报上级", ip: null, now);

        var dueAt = node.TimeoutHours > 0 ? now.AddHours(node.TimeoutHours) : (DateTimeOffset?)null;
        var escalateTasks = leaders.Select(leaderId => new ApprovalTask(
            _db.CurrentTenantId, task.InstId, task.NodeKey, task.NodeName, leaderId,
            ApprovalTaskSourceTypes.Escalate, task.ApproverId, dueAt, now)).ToList();
        await _db.Tasks.AddRangeAsync(escalateTasks, cancellationToken).ConfigureAwait(false);

        logs.Add(new ApprovalLog(_db.CurrentTenantId, instance.Id, ApprovalLogActions.Escalate, "system",
            $"超时上报：节点 {task.NodeName} 待办人 {task.ApproverId} 催办 {task.UrgeCount} 次未处理，转上级 {string.Join("、", leaders)}",
            ip: null, now));

        foreach (var leaderId in leaders)
        {
            await NotifyTodoAsync(leaderId, instance, task.NodeName, cancellationToken).ConfigureAwait(false);
        }

        return true;
    }

    /// <summary>发送催办通知（msg.urge.reminder 模板，高优先级；失败不阻断扫描主流程）。</summary>
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

    /// <summary>发送上报任务待办通知（msg.todo.generated 模板）。</summary>
    private async Task NotifyTodoAsync(
        string approverId, ApprovalInstance instance, string nodeName, CancellationToken cancellationToken)
    {
        try
        {
            await _messageService.SendTemplateAsync(new SendTemplateRequest(
                "msg.todo.generated", approverId,
                new Dictionary<string, object?>
                {
                    ["title"] = $"{instance.Title}（{nodeName}·超时上报）",
                    ["bizNo"] = instance.BizNo,
                },
                BizRef: new MessageBizRef(instance.BizType, instance.BizId, instance.BizNo)), cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogNotifyFailed(instance.InstNo, approverId, ex.Message);
        }
    }

    /// <summary>发送系统预警（msg.alert.warning 模板，紧急）。</summary>
    private async Task SendSystemAlertAsync(
        string recipientId,
        ApprovalInstance instance,
        ApprovalTask task,
        string content,
        CancellationToken cancellationToken)
    {
        try
        {
            await _messageService.SendTemplateAsync(new SendTemplateRequest(
                "msg.alert.warning", recipientId,
                new Dictionary<string, object?>
                {
                    ["title"] = $"审批超时升级失败：{instance.Title}",
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

    [LoggerMessage(Level = LogLevel.Information, Message = "租户 {TenantId} 审批超时扫描完成：催办 {Urged} / 上报 {Escalated} / 预警 {Alerts}")]
    private partial void LogScanCompleted(long tenantId, int urged, int escalated, int alerts);

    [LoggerMessage(Level = LogLevel.Warning, Message = "审批实例 {InstNo} 通知 {RecipientId} 失败：{Reason}")]
    private partial void LogNotifyFailed(string instNo, string recipientId, string reason);
}
