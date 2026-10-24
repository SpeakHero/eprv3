/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-23 11:20:00 +08:00 中国标准时间 星期五
 * 描述：审批超时扫描任务测试（R8）——到期催办 + 节流跳过、催办上限上报上级（ESCALATE 留痕）、
 *       上级解析不到发预警继续催办；R-8 增补参数表阈值覆盖与缺失回落 config 两条路径
 */

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Qlx.Abstractions.Approvals;
using Qlx.Abstractions.Jobs;
using Qlx.Abstractions.Messages;
using Qlx.Abstractions.Modules;
using Qlx.BuildingBlocks.Tests.Files;
using Qlx.BuildingBlocks.Tests.Jobs;
using Qlx.Modules.Support;
using Qlx.Modules.Support.Domain;
using Qlx.Modules.Support.Jobs.SystemJobs;
using Qlx.Modules.Support.Options;
using Qlx.Modules.Support.Services.Approvals;
using Xunit;

namespace Qlx.BuildingBlocks.Tests.Approvals;

/// <summary>
/// <see cref="ApprovalTimeoutScanHandler"/> 单元测试（InMemory 提供程序 + 真实 SupportTenantDbContext +
/// 记录型消息服务 + 脚本化审批人来源）。
/// </summary>
public sealed class ApprovalTimeoutScanHandlerTests
{
    private const long TenantId = 13L;
    private const string Initiator = "8001";
    private const string Assignee = "9001";
    private const string Leader = "9200";

    // ============ 构建与替身 ============

    /// <summary>记录型消息服务：捕获催办 / 待办 / 预警模板发送。</summary>
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

    /// <summary>审批人来源桩：DEPT_LEADER 可配置解析结果（支撑上报与解析不到两条路径）。</summary>
    private sealed class ScriptedLeaderSource(bool resolve) : IApprovalApproverSource
    {
        public bool CanResolve(string approverType) => approverType == ApprovalApproverTypes.DeptLeader;

        public Task<IReadOnlyList<string>> ResolveAsync(
            ApprovalApproverResolveRequest request, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<string>>(resolve ? [Leader] : []);
    }

    /// <summary>系统参数桩：字典内参数覆盖 fallback，其余保持调用方 fallback（模拟 por_parameters 缺失/存在）。</summary>
    private sealed class StubParameterProvider(Dictionary<string, int>? values = null) : ISystemParameterProvider
    {
        private readonly Dictionary<string, int> _values = values ?? [];

        public Task<string> GetStringAsync(string paramKey, string fallback, CancellationToken cancellationToken)
            => Task.FromResult(fallback);

        public Task<int> GetIntAsync(string paramKey, int fallback, CancellationToken cancellationToken)
            => Task.FromResult(_values.TryGetValue(paramKey, out var value) ? value : fallback);
    }

    /// <summary>构建（处理器, 上下文, 消息记录器）：每次独立构建保证测试隔离。</summary>
    private static (ApprovalTimeoutScanHandler Handler, SupportTenantDbContext Db, RecordingMessageService Messages)
        Build(bool resolveLeader, SupportApprovalsOptions? options = null, Dictionary<string, int>? parameters = null)
    {
        var db = new SupportTenantDbContext(
            new JobStubTenantDatabaseResolver(),
            TenantId,
            new DbContextOptionsBuilder<SupportTenantDbContext>()
                .UseInMemoryDatabase($"apr-timeout-{Guid.NewGuid():N}")
                .ConfigureWarnings(static w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                .Options);
        var messages = new RecordingMessageService();
        var handler = new ApprovalTimeoutScanHandler(
            db,
            [new ScriptedLeaderSource(resolveLeader)],
            messages,
            new StubParameterProvider(parameters),
            Options.Create(options ?? new SupportApprovalsOptions { UrgeIntervalMinutes = 0 }),
            NullLogger<ApprovalTimeoutScanHandler>.Instance);
        return (handler, db, messages);
    }

    /// <summary>构造一个到期待办场景（实例 Pending + 节点 + 超时任务），返回任务 Id。</summary>
    private static async Task<long> SeedOverdueTaskAsync(
        SupportTenantDbContext db, ApprovalTimeoutAction timeoutAction, string nodeKey = "n1")
    {
        var ct = TestContext.Current.CancellationToken;
        var now = DateTimeOffset.UtcNow;
        var definition = new ApprovalFlowDefinition(
            TenantId, $"flow.timeout.{Guid.NewGuid():N}", "超时流程", $"timeout_biz_{nodeKey}",
            "[]", version: 1, "{}", now);
        definition.SetStatus(ApprovalFlowStatus.Enabled, now);
        // 定义先落库拿雪花 Id（雪花主键在 SaveChanges 时由拦截器填充），再构造节点，避免 defId=0 校验失败
        db.FlowDefinitions.Add(definition);
        await db.SaveChangesAsync(ct);

        var node = new ApprovalFlowNode(
            TenantId, definition.Id, nodeKey, ApprovalNodeType.Approval, "超时节点",
            ApprovalApproverTypes.DeptLeader, null, ApprovalSignMode.OrSign,
            timeoutHours: 1, timeoutAction, allowAddSign: true, ApprovalRejectMode.StepBack, sortNo: 10);
        db.FlowNodes.Add(node);
        await db.SaveChangesAsync(ct);

        var instance = new ApprovalInstance(
            TenantId, $"APRT{now:yyyyMMdd}{definition.Id % 100_000_000:D8}", definition.Id, 1, definition.DefCode,
            "timeout_biz", $"B-{nodeKey}", "N-1", "超时测试单", Initiator, 100, "{}", now);
        db.Instances.Add(instance);
        await db.SaveChangesAsync(ct);

        var task = new ApprovalTask(
            TenantId, instance.Id, nodeKey, "超时节点", Assignee,
            ApprovalTaskSourceTypes.Normal, null, DateTimeOffset.UtcNow.AddHours(-2), now);
        db.Tasks.Add(task);
        await db.SaveChangesAsync(ct);
        return task.Id;
    }

    // ============ 用例 ============

    [Fact]
    public async Task DueTaskRecordsUrgeAndSendsReminder()
    {
        var (handler, db, messages) = Build(resolveLeader: false);
        var taskId = await SeedOverdueTaskAsync(db, ApprovalTimeoutAction.Urge);

        var result = await handler.ExecuteAsync(new JobContext(), TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        var task = await db.Tasks.AsNoTracking().SingleAsync(
            t => t.Id == taskId, TestContext.Current.CancellationToken);
        Assert.Equal(1, task.UrgeCount);
        Assert.NotNull(task.UrgeLastAt);
        var urge = Assert.Single(messages.Templates, m => m.TemplateCode == "msg.urge.reminder");
        Assert.Equal(Assignee, urge.RecipientUserId);
        Assert.Equal(MessagePriority.High, urge.Priority);

        // 催办留痕（R10 只增）
        var logs = await db.OpLogs.AsNoTracking().ToListAsync(TestContext.Current.CancellationToken);
        Assert.Contains(logs, l => l.InstId == task.InstId && l.Action == ApprovalLogActions.Urge);
    }

    [Fact]
    public async Task TaskWithinThrottleIntervalIsSkipped()
    {
        var (handler, db, messages) = Build(resolveLeader: false,
            new SupportApprovalsOptions { UrgeIntervalMinutes = 60 });
        var taskId = await SeedOverdueTaskAsync(db, ApprovalTimeoutAction.Urge);
        (await db.Tasks.SingleAsync(t => t.Id == taskId, TestContext.Current.CancellationToken))
            .RecordUrge(DateTimeOffset.UtcNow.AddMinutes(-10));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        // 催办间隔 60 分钟：10 分钟前刚催过 → 本轮跳过
        await handler.ExecuteAsync(new JobContext(), TestContext.Current.CancellationToken);

        var task = await db.Tasks.AsNoTracking().SingleAsync(
            t => t.Id == taskId, TestContext.Current.CancellationToken);
        Assert.Equal(1, task.UrgeCount);
        Assert.Empty(messages.Templates);
    }

    [Fact]
    public async Task MaxUrgeCountWithUrgeActionKeepsUrging()
    {
        var (handler, db, messages) = Build(resolveLeader: true,
            new SupportApprovalsOptions { UrgeIntervalMinutes = 0, MaxUrgeCount = 0 });
        var taskId = await SeedOverdueTaskAsync(db, ApprovalTimeoutAction.Urge);

        await handler.ExecuteAsync(new JobContext(), TestContext.Current.CancellationToken);

        // 节点为催办策略：达上限也仅继续催办，不生成上报任务
        var task = await db.Tasks.AsNoTracking().SingleAsync(
            t => t.Id == taskId, TestContext.Current.CancellationToken);
        Assert.Equal(ApprovalTaskStatus.Pending, task.Status);
        Assert.DoesNotContain(messages.Templates, m => m.TemplateCode == "msg.todo.generated");
    }

    [Fact]
    public async Task MaxUrgeCountWithEscalateActionTransfersToLeader()
    {
        var (handler, db, messages) = Build(resolveLeader: true,
            new SupportApprovalsOptions { UrgeIntervalMinutes = 0, MaxUrgeCount = 0 });
        var taskId = await SeedOverdueTaskAsync(db, ApprovalTimeoutAction.Escalate);

        await handler.ExecuteAsync(new JobContext(), TestContext.Current.CancellationToken);

        // 原任务取消留痕 + 上报任务生成（SourceFrom=原待办人）
        var original = await db.Tasks.AsNoTracking().SingleAsync(
            t => t.Id == taskId, TestContext.Current.CancellationToken);
        Assert.Equal(ApprovalTaskStatus.Canceled, original.Status);

        var escalate = await db.Tasks.AsNoTracking()
            .SingleAsync(t => t.InstId == original.InstId && t.SourceType == ApprovalTaskSourceTypes.Escalate,
                TestContext.Current.CancellationToken);
        Assert.Equal(Leader, escalate.ApproverId);
        Assert.Equal(Assignee, escalate.SourceFrom);
        Assert.NotNull(escalate.DueAt); // 按节点超时周期顺延

        // 上报任务待办通知 + 上报留痕日志
        Assert.Contains(messages.Templates,
            m => m.TemplateCode == "msg.todo.generated" && m.RecipientUserId == Leader);
        var logs = await db.OpLogs.AsNoTracking().ToListAsync(TestContext.Current.CancellationToken);
        Assert.Contains(logs, l => l.InstId == original.InstId && l.Action == ApprovalLogActions.Escalate);
    }

    [Fact]
    public async Task UnresolvableLeaderSendsAlertAndKeepsUrging()
    {
        var (handler, db, messages) = Build(resolveLeader: false,
            new SupportApprovalsOptions { UrgeIntervalMinutes = 0, MaxUrgeCount = 0 });
        var taskId = await SeedOverdueTaskAsync(db, ApprovalTimeoutAction.Escalate);

        await handler.ExecuteAsync(new JobContext(), TestContext.Current.CancellationToken);

        // 上级解析不到：系统预警 + 继续催办（原任务保持 Pending）
        var task = await db.Tasks.AsNoTracking().SingleAsync(
            t => t.Id == taskId, TestContext.Current.CancellationToken);
        Assert.Equal(ApprovalTaskStatus.Pending, task.Status);
        Assert.Equal(1, task.UrgeCount);
        Assert.Contains(messages.Templates, m => m.TemplateCode == "msg.alert.warning");
        Assert.Contains(messages.Templates, m => m.TemplateCode == "msg.urge.reminder");
    }

    [Fact]
    public async Task NoDueTasksCompletesWithoutSideEffects()
    {
        var (handler, db, messages) = Build(resolveLeader: false);

        var result = await handler.ExecuteAsync(new JobContext(), TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Empty(messages.Templates);
        Assert.Empty(await db.OpLogs.ToListAsync(TestContext.Current.CancellationToken));
    }

    // ============ R-8 参数表阈值 ============

    [Fact]
    public async Task ParameterTableThresholdOverridesConfigFallback()
    {
        // config 上限 10 次不会上报；参数表改为 1 → 本轮即按节点策略上报
        var (handler, db, messages) = Build(resolveLeader: true,
            new SupportApprovalsOptions { UrgeIntervalMinutes = 0, MaxUrgeCount = 10 },
            new Dictionary<string, int> { [ApprovalParameterKeys.MaxUrgeCount] = 1 });
        var taskId = await SeedOverdueTaskAsync(db, ApprovalTimeoutAction.Escalate);
        // 已催办 1 次：config 上限 10 不会上报，参数表上限 1 则本轮上报
        (await db.Tasks.SingleAsync(t => t.Id == taskId, TestContext.Current.CancellationToken))
            .RecordUrge(DateTimeOffset.UtcNow.AddMinutes(-5));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        await handler.ExecuteAsync(new JobContext(), TestContext.Current.CancellationToken);

        var original = await db.Tasks.AsNoTracking().SingleAsync(
            t => t.Id == taskId, TestContext.Current.CancellationToken);
        Assert.Equal(ApprovalTaskStatus.Canceled, original.Status);
        Assert.Contains(messages.Templates,
            m => m.TemplateCode == "msg.todo.generated" && m.RecipientUserId == Leader);
    }

    [Fact]
    public async Task MissingParameterFallsBackToConfigThreshold()
    {
        // 参数表缺失：config 上限 10 次，本轮仅催办不上报
        var (handler, db, messages) = Build(resolveLeader: true,
            new SupportApprovalsOptions { UrgeIntervalMinutes = 0, MaxUrgeCount = 10 });
        var taskId = await SeedOverdueTaskAsync(db, ApprovalTimeoutAction.Escalate);

        await handler.ExecuteAsync(new JobContext(), TestContext.Current.CancellationToken);

        var task = await db.Tasks.AsNoTracking().SingleAsync(
            t => t.Id == taskId, TestContext.Current.CancellationToken);
        Assert.Equal(ApprovalTaskStatus.Pending, task.Status);
        Assert.Equal(1, task.UrgeCount);
        Assert.DoesNotContain(messages.Templates, m => m.TemplateCode == "msg.todo.generated");
    }
}
