/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-23 11:20:00 +08:00 中国标准时间 星期五
 * 描述：审批中心服务集成测试——真实种子流程 + 真实条件引擎 + 组合审批人来源（测试桩）+
 *       记录型消息服务 / 事件总线 / 审计桩。覆盖：三级金额条件链（R3）、或签首人决 / 会签全通过（R4）、
 *       驳回 / 撤回 / 加签 / 转办 / 催办、委托留痕（R9）、终态事件发布（R11）、消息送达断言
 */

using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Qlx.Abstractions.Approvals;
using Qlx.Abstractions.Audit;
using Qlx.Abstractions.Events;
using Qlx.Abstractions.Exceptions;
using Qlx.Abstractions.Messages;
using Qlx.BuildingBlocks.EventBus;
using Qlx.BuildingBlocks.Expressions;
using Qlx.BuildingBlocks.Tests.Files;
using Qlx.BuildingBlocks.Tests.Jobs;
using Qlx.BuildingBlocks.Tests.Portal;
using Qlx.Modules.Support;
using Qlx.Modules.Support.Domain;
using Qlx.Modules.Support.Options;
using Qlx.Modules.Support.Services.Approvals;
using Qlx.Modules.Support.Services.Files;
using Xunit;

namespace Qlx.BuildingBlocks.Tests.Approvals;

/// <summary>
/// <see cref="SupportApprovalService"/> 集成测试（InMemory 提供程序 + 真实 SupportTenantDbContext）。
/// 审批人解析：DEPT_LEADER→9001，ROLE admin→[9002,9003]，FIELD_USER 走快照字段；
/// Support 兜底来源（INITIATOR/USER/FIELD_USER）为真实实现。
/// </summary>
public sealed class SupportApprovalServiceTests
{
    private const long TenantId = 11L;
    private const string Initiator = "8001";
    private const string DeptLeader = "9001";
    private const string Admin1 = "9002";
    private const string Admin2 = "9003";
    private const string Agent = "9100";
    private const string GeneralManager = "9101";

    // ============ 构建与替身 ============

    /// <summary>构建服务与全部替身：真实种子流程 + 真实条件引擎。</summary>
    private static (
        SupportApprovalService Service,
        SupportTenantDbContext Db,
        RecordingMessageService Messages,
        RecordingEventBus Events) Build()
    {
        var db = new SupportTenantDbContext(
            new JobStubTenantDatabaseResolver(),
            TenantId,
            new DbContextOptionsBuilder<SupportTenantDbContext>()
                .UseInMemoryDatabase($"apr-svc-{Guid.NewGuid():N}")
                .ConfigureWarnings(static w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                .Options);
        var seed = new SupportSeedService(
            db, ConditionExpressionEngineFactory.Default, NullLogger<SupportSeedService>.Instance);
        seed.EnsureApprovalFlowsSeededAsync(TenantId, TestContext.Current.CancellationToken)
            .GetAwaiter().GetResult();

        var messages = new RecordingMessageService();
        var events = new RecordingEventBus();
        var service = new SupportApprovalService(
            db,
            ConditionExpressionEngineFactory.Default,
            new ApprovalFlowRegistry(db, seed),
            [new SupportApprovalApproverSource(), new TestApproverSource()],
            messages,
            new StubAuditService(),
            events,
            Options.Create(new SupportApprovalsOptions()),
            NullLogger<SupportApprovalService>.Instance);
        return (service, db, messages, events);
    }

    /// <summary>组织侧审批人来源桩：DEPT_LEADER→部门负责人，ROLE admin→双管理员（支撑会签/或签断言）。</summary>
    private sealed class TestApproverSource : IApprovalApproverSource
    {
        public bool CanResolve(string approverType)
            => approverType is ApprovalApproverTypes.DeptLeader or ApprovalApproverTypes.Role;

        public Task<IReadOnlyList<string>> ResolveAsync(
            ApprovalApproverResolveRequest request, CancellationToken cancellationToken)
        {
            IReadOnlyList<string> resolved = request.ApproverType switch
            {
                ApprovalApproverTypes.DeptLeader => [DeptLeader],
                ApprovalApproverTypes.Role when request.ApproverRef == "admin" => [Admin1, Admin2],
                _ => [],
            };
            return Task.FromResult(resolved);
        }
    }

    /// <summary>记录型消息服务：捕获模板发送请求（消息送达断言）。</summary>
    private sealed class RecordingMessageService : IMessageService
    {
        public List<SendTemplateRequest> Templates { get; } = [];

        public Task SendTemplateAsync(SendTemplateRequest request, CancellationToken cancellationToken)
        {
            Templates.Add(request);
            return Task.CompletedTask;
        }

        public Task PushAsync(PushRequest request, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task MarkReadAsync(string messageInboxId, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task MarkAllReadAsync(string userId, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<int> GetUnreadCountAsync(string userId, CancellationToken cancellationToken) => Task.FromResult(0);

        public Task<IReadOnlyList<MessageInfo>> GetInboxAsync(MessageQuery request, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<MessageInfo>>([]);

        public Task<IReadOnlyList<MessageInfo>> GetSentListAsync(MessageQuery request, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<MessageInfo>>([]);
    }

    /// <summary>记录型事件总线：捕获 Outbox 持久发布（R11 终态事件断言）。</summary>
    private sealed class RecordingEventBus : IEventBus
    {
        public List<IntegrationEvent> DurableEvents { get; } = [];

        public Task PublishAsync<TEvent>(TEvent eventData, CancellationToken cancellationToken)
            where TEvent : IntegrationEvent => Task.CompletedTask;

        public Task PublishDurableAsync<TEvent>(TEvent eventData, CancellationToken cancellationToken)
            where TEvent : IntegrationEvent
        {
            DurableEvents.Add(eventData);
            return Task.CompletedTask;
        }
    }

    private static Dictionary<string, object?> BizData(decimal totalAmount, long orgId = 100)
        => new()
        {
            ["orgId"] = orgId.ToString(CultureInfo.InvariantCulture),
            ["total_amount"] = totalAmount,
            ["general_manager"] = GeneralManager,
            ["legal_officer"] = "9102",
        };

    private static long ParseId(string instanceId)
        => long.Parse(instanceId, CultureInfo.InvariantCulture);

    private static async Task<string> StartPurchaseAsync(
        SupportApprovalService service, decimal totalAmount, string bizId = "PR-1")
        => await service.StartApprovalAsync(new StartApprovalRequest(
            ApprovalDefinitionCode: "flow.purchase.request", BizType: "pur_request", BizId: bizId,
            BizNo: $"CG-{bizId}", InitiatorUserId: Initiator, Title: $"采购申请 {bizId}",
            Description: null, BizData: BizData(totalAmount)), TestContext.Current.CancellationToken);

    private static async Task<ApprovalTask> RequireTaskAsync(
        SupportTenantDbContext db, long instanceId, string nodeKey, string approverId)
    {
        var task = await db.Tasks.AsNoTracking()
            .SingleOrDefaultAsync(t => t.InstId == instanceId
                && t.NodeKey == nodeKey && t.ApproverId == approverId);
        Assert.NotNull(task);
        return task;
    }

    // ============ 发起 ============

    [Fact]
    public async Task StartApprovalCreatesInstanceAndFirstTaskWithTodoNotification()
    {
        var (service, db, messages, _) = Build();
        var instanceId = await StartPurchaseAsync(service, 60000);

        var instance = await db.Instances.AsNoTracking().SingleAsync(
            i => i.Id == ParseId(instanceId), TestContext.Current.CancellationToken);
        Assert.Equal(ApprovalStatus.Pending, instance.Status);
        Assert.StartsWith("APR", instance.InstNo, StringComparison.Ordinal);
        Assert.Equal(1, instance.DefVersion);

        var task = await RequireTaskAsync(db, instance.Id, "dept_leader", DeptLeader);
        Assert.Equal(ApprovalTaskStatus.Pending, task.Status);
        Assert.Equal(ApprovalTaskSourceTypes.Normal, task.SourceType);
        Assert.NotNull(task.DueAt); // 节点配置了 24h 超时

        // 消息送达断言：待办人收到 msg.todo.generated
        var todo = Assert.Single(messages.Templates, m => m.TemplateCode == "msg.todo.generated");
        Assert.Equal(DeptLeader, todo.RecipientUserId);
    }

    [Fact]
    public async Task StartApprovalUnknownBizTypeThrowsNotFound()
    {
        var (service, _, _, _) = Build();
        await Assert.ThrowsAsync<NotFoundException>(() => service.StartApprovalAsync(
            new StartApprovalRequest("flow.none", "biz.none", "B-1", "N-1", Initiator, "标题", null, BizData(1)),
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ApproveUnresolvableFieldUserNodeThrowsConflict()
    {
        var (service, _, _, _) = Build();
        // 大额链第三级 FIELD_USER：快照缺 general_manager 字段 → n2 通过后解析不到人 → 拒绝（引擎收口策略）
        var data = new Dictionary<string, object?>
        {
            ["orgId"] = "100", ["total_amount"] = 999999m,
        };
        var instanceId = await service.StartApprovalAsync(
            new StartApprovalRequest("flow.purchase.request", "pur_request", "PR-X", "CG-X", Initiator, "缺字段申请", null, data),
            TestContext.Current.CancellationToken);
        await service.ApproveAsync(
            new ApproveRequest(instanceId, DeptLeader, "同意"), TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<ConflictException>(() => service.ApproveAsync(
            new ApproveRequest(instanceId, Admin1, "同意"), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ApproveWithMalformedInstanceIdThrowsValidation()
    {
        var (service, _, _, _) = Build();
        await Assert.ThrowsAsync<ValidationException>(() => service.ApproveAsync(
            new ApproveRequest("not-a-number", DeptLeader, "ok"), TestContext.Current.CancellationToken));
    }

    // ============ 金额条件链（R3） ============

    [Fact]
    public async Task SmallAmountFallsBackToTerminalAndPublishesEvent()
    {
        var (service, db, messages, events) = Build();
        var instanceId = await StartPurchaseAsync(service, 1000);

        await service.ApproveAsync(
            new ApproveRequest(instanceId, DeptLeader, "同意"), TestContext.Current.CancellationToken);

        var instance = await db.Instances.AsNoTracking().SingleAsync(
            i => i.Id == ParseId(instanceId), TestContext.Current.CancellationToken);
        Assert.Equal(ApprovalStatus.Approved, instance.Status);
        Assert.Equal(string.Empty, instance.CurrentNodeKey);

        // 终态事件（R11）：一条 APPROVED 持久事件，负载字段齐全
        var evt = Assert.IsType<ApprovalCompletedEvent>(Assert.Single(events.DurableEvents));
        Assert.Equal(ApprovalStatus.Approved, evt.FinalStatus);
        Assert.Equal(instanceId, evt.ApprovalInstanceId);
        Assert.Equal("pur_request", evt.BizType);
        Assert.Equal(Initiator, evt.InitiatorUserId);

        // 结果通知送达发起人
        var notice = Assert.Single(messages.Templates, m => m.TemplateCode == "msg.result.notice");
        Assert.Equal(Initiator, notice.RecipientUserId);
    }

    [Fact]
    public async Task MidAmountRoutesToRoleAdminNode()
    {
        var (service, db, _, _) = Build();
        var instanceId = await StartPurchaseAsync(service, 60000);
        await service.ApproveAsync(
            new ApproveRequest(instanceId, DeptLeader, "同意"), TestContext.Current.CancellationToken);

        var instance = await db.Instances.AsNoTracking().SingleAsync(
            i => i.Id == ParseId(instanceId), TestContext.Current.CancellationToken);
        Assert.Equal(ApprovalStatus.Pending, instance.Status);
        Assert.Equal("role_admin", instance.CurrentNodeKey);

        // 或签：双管理员同时生成任务
        await RequireTaskAsync(db, instance.Id, "role_admin", Admin1);
        await RequireTaskAsync(db, instance.Id, "role_admin", Admin2);
    }

    [Fact]
    public async Task LargeAmountCompletesThreeLevelChain()
    {
        var (service, db, _, events) = Build();
        var instanceId = await StartPurchaseAsync(service, 300000);

        await service.ApproveAsync(
            new ApproveRequest(instanceId, DeptLeader, "同意"), TestContext.Current.CancellationToken);
        await service.ApproveAsync(
            new ApproveRequest(instanceId, Admin1, "同意"), TestContext.Current.CancellationToken);

        // 第三级 FIELD_USER：general_manager 快照字段解析
        var instance = await db.Instances.AsNoTracking().SingleAsync(
            i => i.Id == ParseId(instanceId), TestContext.Current.CancellationToken);
        Assert.Equal("general_manager", instance.CurrentNodeKey);
        await RequireTaskAsync(db, instance.Id, "general_manager", GeneralManager);

        await service.ApproveAsync(
            new ApproveRequest(instanceId, GeneralManager, "总经理同意"), TestContext.Current.CancellationToken);

        var final = await db.Instances.AsNoTracking().SingleAsync(
            i => i.Id == ParseId(instanceId), TestContext.Current.CancellationToken);
        Assert.Equal(ApprovalStatus.Approved, final.Status);
        Assert.Equal(ApprovalStatus.Approved,
            Assert.IsType<ApprovalCompletedEvent>(events.DurableEvents.Single()).FinalStatus);
    }

    [Fact]
    public async Task OrSignFirstDecisionCancelsSiblingTasks()
    {
        var (service, db, _, _) = Build();
        // 大额 300000：role_admin 通过后命中 ≥200000 分支推进第三级 general_manager
        var instanceId = await StartPurchaseAsync(service, 300000);
        await service.ApproveAsync(
            new ApproveRequest(instanceId, DeptLeader, "同意"), TestContext.Current.CancellationToken);

        await service.ApproveAsync(
            new ApproveRequest(instanceId, Admin1, "先到先审"), TestContext.Current.CancellationToken);

        var sibling = await RequireTaskAsync(db, ParseId(instanceId), "role_admin", Admin2);
        Assert.Equal(ApprovalTaskStatus.Canceled, sibling.Status);
        var instance = await db.Instances.AsNoTracking().SingleAsync(
            i => i.Id == ParseId(instanceId), TestContext.Current.CancellationToken);
        Assert.Equal("general_manager", instance.CurrentNodeKey);
    }

    [Fact]
    public async Task CountersignNodeWaitsUntilAllApproversApprove()
    {
        var (service, db, _, _) = Build();
        var instanceId = await service.StartApprovalAsync(new StartApprovalRequest(
            "flow.contract.review", "contract_review", "HT-1", "HT-0001", Initiator, "合同评审",
            null, new Dictionary<string, object?> { ["orgId"] = "100", ["legal_officer"] = "9102" }),
            TestContext.Current.CancellationToken);

        await service.ApproveAsync(
            new ApproveRequest(instanceId, DeptLeader, "同意"), TestContext.Current.CancellationToken);

        // 会签：双管理员须全部通过（R4），首个通过不推进
        await service.ApproveAsync(
            new ApproveRequest(instanceId, Admin1, "会签同意"), TestContext.Current.CancellationToken);
        var instance = await db.Instances.AsNoTracking().SingleAsync(
            i => i.Id == ParseId(instanceId), TestContext.Current.CancellationToken);
        Assert.Equal("role_admin", instance.CurrentNodeKey);

        await service.ApproveAsync(
            new ApproveRequest(instanceId, Admin2, "会签同意"), TestContext.Current.CancellationToken);
        instance = await db.Instances.AsNoTracking().SingleAsync(
            i => i.Id == ParseId(instanceId), TestContext.Current.CancellationToken);
        Assert.Equal("legal_officer", instance.CurrentNodeKey);
    }

    // ============ 审批操作边界 ============

    [Fact]
    public async Task ApproveByNonAssigneeThrowsForbidden()
    {
        var (service, _, _, _) = Build();
        var instanceId = await StartPurchaseAsync(service, 1000);
        await Assert.ThrowsAsync<ForbiddenException>(() => service.ApproveAsync(
            new ApproveRequest(instanceId, Admin1, "越权"), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ApproveUnknownInstanceThrowsNotFound()
    {
        var (service, _, _, _) = Build();
        await Assert.ThrowsAsync<NotFoundException>(() => service.ApproveAsync(
            new ApproveRequest("12345", DeptLeader, "ok"), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task RejectFirstNodeRejectsInstanceAndPublishesEvent()
    {
        var (service, db, _, events) = Build();
        var instanceId = await StartPurchaseAsync(service, 1000);

        await service.RejectAsync(
            new RejectRequest(instanceId, DeptLeader, "预算不足"), TestContext.Current.CancellationToken);

        var instance = await db.Instances.AsNoTracking().SingleAsync(
            i => i.Id == ParseId(instanceId), TestContext.Current.CancellationToken);
        Assert.Equal(ApprovalStatus.Rejected, instance.Status);
        var evt = Assert.IsType<ApprovalCompletedEvent>(events.DurableEvents.Single());
        Assert.Equal(ApprovalStatus.Rejected, evt.FinalStatus);
    }

    [Fact]
    public async Task WithdrawByNonInitiatorThrowsForbidden()
    {
        var (service, _, _, _) = Build();
        var instanceId = await StartPurchaseAsync(service, 1000);
        await Assert.ThrowsAsync<ForbiddenException>(() => service.WithdrawAsync(
            new WithdrawRequest(instanceId, Admin1, "非本人"), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task WithdrawBeforeAnyActionIsTerminal()
    {
        var (service, db, _, events) = Build();
        var instanceId = await StartPurchaseAsync(service, 1000);

        await service.WithdrawAsync(
            new WithdrawRequest(instanceId, Initiator, "填错单据"), TestContext.Current.CancellationToken);

        var instance = await db.Instances.AsNoTracking().SingleAsync(
            i => i.Id == ParseId(instanceId), TestContext.Current.CancellationToken);
        Assert.Equal(ApprovalStatus.Withdrawn, instance.Status);
        Assert.Equal(ApprovalStatus.Withdrawn,
            Assert.IsType<ApprovalCompletedEvent>(events.DurableEvents.Single()).FinalStatus);
    }

    [Fact]
    public async Task WithdrawAfterNodeHandledThrowsConflict()
    {
        var (service, _, _, _) = Build();
        var instanceId = await StartPurchaseAsync(service, 1000);
        await service.ApproveAsync(
            new ApproveRequest(instanceId, DeptLeader, "同意"), TestContext.Current.CancellationToken);

        // 实例已终态（小额链一级即通过），撤回被拒绝
        await Assert.ThrowsAsync<ConflictException>(() => service.WithdrawAsync(
            new WithdrawRequest(instanceId, Initiator, "撤回"), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AddSignerCreatesPendingAddSignTaskWithTrail()
    {
        var (service, db, messages, _) = Build();
        var instanceId = await StartPurchaseAsync(service, 1000);
        var instance = await db.Instances.AsNoTracking().SingleAsync(
            i => i.Id == ParseId(instanceId), TestContext.Current.CancellationToken);
        var task = await RequireTaskAsync(db, instance.Id, "dept_leader", DeptLeader);

        await service.AddSignerAsync(new AddSignerRequest(
            instanceId, task.Id.ToString(CultureInfo.InvariantCulture), DeptLeader, Agent,
            Reason: "补充合规意见"), TestContext.Current.CancellationToken);

        var addSign = await db.Tasks.AsNoTracking()
            .SingleAsync(t => t.InstId == instance.Id && t.SourceType == ApprovalTaskSourceTypes.AddSign,
                TestContext.Current.CancellationToken);
        Assert.Equal(Agent, addSign.ApproverId);
        Assert.Equal(DeptLeader, addSign.SourceFrom);
        Assert.Equal(ApprovalTaskStatus.Pending, addSign.Status);

        var todo = Assert.Single(messages.Templates,
            m => m.TemplateCode == "msg.todo.generated" && m.RecipientUserId == Agent);
        Assert.NotNull(todo);
    }

    [Fact]
    public async Task TransferMovesTaskToNewAssignee()
    {
        var (service, db, _, _) = Build();
        var instanceId = await StartPurchaseAsync(service, 1000);
        var instance = await db.Instances.AsNoTracking().SingleAsync(
            i => i.Id == ParseId(instanceId), TestContext.Current.CancellationToken);
        var task = await RequireTaskAsync(db, instance.Id, "dept_leader", DeptLeader);

        await service.TransferAsync(new TransferRequest(
            instanceId, task.Id.ToString(CultureInfo.InvariantCulture), DeptLeader, Agent,
            Reason: "休假转办"), TestContext.Current.CancellationToken);

        var transferredFrom = await db.Tasks.AsNoTracking().SingleAsync(
            t => t.Id == task.Id, TestContext.Current.CancellationToken);
        Assert.Equal(ApprovalTaskStatus.Transferred, transferredFrom.Status);
        var transferred = await db.Tasks.AsNoTracking()
            .SingleAsync(t => t.InstId == instance.Id && t.SourceType == ApprovalTaskSourceTypes.Transfer,
                TestContext.Current.CancellationToken);
        Assert.Equal(Agent, transferred.ApproverId);
        Assert.Equal(DeptLeader, transferred.SourceFrom);
    }

    [Fact]
    public async Task UrgeIncrementsUrgeCountAndSendsReminder()
    {
        var (service, db, messages, _) = Build();
        var instanceId = await StartPurchaseAsync(service, 1000);

        await service.UrgeAsync(
            new UrgeRequest(instanceId, Initiator, "请尽快处理"), TestContext.Current.CancellationToken);

        var instance = await db.Instances.AsNoTracking().SingleAsync(
            i => i.Id == ParseId(instanceId), TestContext.Current.CancellationToken);
        var task = await db.Tasks.AsNoTracking().SingleAsync(
            t => t.InstId == instance.Id, TestContext.Current.CancellationToken);
        Assert.Equal(1, task.UrgeCount);
        Assert.NotNull(task.UrgeLastAt);
        var urge = Assert.Single(messages.Templates, m => m.TemplateCode == "msg.urge.reminder");
        Assert.Equal(DeptLeader, urge.RecipientUserId);
    }

    // ============ 委托留痕（R9） ============

    [Fact]
    public async Task ActiveDelegationRoutesTaskToAgentWithTrail()
    {
        var (service, db, _, _) = Build();
        var now = DateTimeOffset.UtcNow;
        db.Delegates.Add(new ApprovalDelegation(
            TenantId, DeptLeader, Agent, "[]", now.AddMinutes(-1), now.AddDays(1), "出差委托", now));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var instanceId = await StartPurchaseAsync(service, 1000);
        var instance = await db.Instances.AsNoTracking().SingleAsync(
            i => i.Id == ParseId(instanceId), TestContext.Current.CancellationToken);

        var task = await db.Tasks.AsNoTracking().SingleAsync(
            t => t.InstId == instance.Id, TestContext.Current.CancellationToken);
        Assert.Equal(Agent, task.ApproverId);
        Assert.Equal(ApprovalTaskSourceTypes.Delegate, task.SourceType);
        Assert.Equal(DeptLeader, task.SourceFrom);

        // 委托留痕日志
        var logs = await db.OpLogs.AsNoTracking()
            .Where(l => l.InstId == instance.Id).ToListAsync(TestContext.Current.CancellationToken);
        Assert.Contains(logs, l => l.Action == ApprovalLogActions.Delegate);
    }

    // ============ 查询 ============

    [Fact]
    public async Task TodoListReturnsOnlyPendingTasksForAssignee()
    {
        var (service, _, _, _) = Build();
        var instanceId = await StartPurchaseAsync(service, 60000);
        await service.ApproveAsync(
            new ApproveRequest(instanceId, DeptLeader, "同意"), TestContext.Current.CancellationToken);

        var leaderTodos = await service.GetTodoListAsync(DeptLeader, TestContext.Current.CancellationToken);
        Assert.Empty(leaderTodos);
        var adminTodos = await service.GetTodoListAsync(Admin1, TestContext.Current.CancellationToken);
        Assert.Contains(adminTodos, t => t.NodeCode == "role_admin" && t.Status == ApprovalTaskStatus.Pending);
    }

    [Fact]
    public async Task GetInstanceReturnsDetailAndUnknownReturnsNull()
    {
        var (service, _, _, _) = Build();
        var instanceId = await StartPurchaseAsync(service, 1000);

        var detail = await service.GetInstanceAsync(instanceId, TestContext.Current.CancellationToken);
        Assert.NotNull(detail);
        Assert.Equal(Initiator, detail.InitiatorUserId);
        Assert.NotEmpty(detail.Tasks);

        Assert.Null(await service.GetInstanceAsync("12345", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task InitiatedListReturnsOwnInstances()
    {
        var (service, _, _, _) = Build();
        await StartPurchaseAsync(service, 1000, bizId: "PR-MINE");
        await service.StartApprovalAsync(new StartApprovalRequest(
            "flow.purchase.request", "pur_request", "PR-OTHER", "CG-OTHER", Agent, "他人申请", null, BizData(1000)),
            TestContext.Current.CancellationToken);

        var list = await service.GetMyInitiatedListAsync(Initiator, TestContext.Current.CancellationToken);
        Assert.Single(list);
        Assert.Equal("采购申请 PR-MINE", list[0].Title);
    }
}
