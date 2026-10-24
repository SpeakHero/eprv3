/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-23 11:20:00 +08:00 中国标准时间 星期五
 * 描述：审批流程注册表测试——代码定义内存注册、R1 唯一启用解析（组织范围 / 版本 / 多命中冲突）、
 *       R2 按锁定定义 Id 装载、R3 兜底分支完整性校验
 */

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Qlx.Abstractions.Approvals;
using Qlx.Abstractions.Exceptions;
using Qlx.BuildingBlocks.Expressions;
using Qlx.BuildingBlocks.Tests.Files;
using Qlx.BuildingBlocks.Tests.Jobs;
using Qlx.Modules.Support;
using Qlx.Modules.Support.Domain;
using Qlx.Modules.Support.Services.Approvals;
using Qlx.Modules.Support.Services.Files;
using Xunit;

namespace Qlx.BuildingBlocks.Tests.Approvals;

/// <summary>
/// <see cref="ApprovalFlowRegistry"/> 单元测试（InMemory 提供程序 + 真实 SupportTenantDbContext +
/// 真实种子服务种入 4 个默认流程）。
/// </summary>
public sealed class ApprovalFlowRegistryTests
{
    private const long TenantId = 9L;

    /// <summary>构建（注册表, 上下文）对：真实种子服务种入 4 个默认流程。</summary>
    private static (ApprovalFlowRegistry Registry, SupportTenantDbContext Db) Build()
    {
        var db = new SupportTenantDbContext(
            new JobStubTenantDatabaseResolver(),
            TenantId,
            new DbContextOptionsBuilder<SupportTenantDbContext>()
                .UseInMemoryDatabase($"apr-reg-{Guid.NewGuid():N}")
                .ConfigureWarnings(static w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                .Options);
        var seed = new SupportSeedService(
            db, ConditionExpressionEngineFactory.Default, NullLogger<SupportSeedService>.Instance);
        seed.EnsureApprovalFlowsSeededAsync(TenantId, TestContext.Current.CancellationToken)
            .GetAwaiter().GetResult();
        return (new ApprovalFlowRegistry(db, seed), db);
    }

    /// <summary>构建未预种子的（注册表, 上下文）对：验证租户级零数据时懒种子路径。</summary>
    private static (ApprovalFlowRegistry Registry, SupportTenantDbContext Db) BuildUnseeded()
    {
        var db = new SupportTenantDbContext(
            new JobStubTenantDatabaseResolver(),
            TenantId,
            new DbContextOptionsBuilder<SupportTenantDbContext>()
                .UseInMemoryDatabase($"apr-reg-lazy-{Guid.NewGuid():N}")
                .ConfigureWarnings(static w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                .Options);
        var seed = new SupportSeedService(
            db, ConditionExpressionEngineFactory.Default, NullLogger<SupportSeedService>.Instance);
        return (new ApprovalFlowRegistry(db, seed), db);
    }

    // ============ 代码定义注册（2A 契约） ============

    [Fact]
    public void RegisteredDefinitionRoundtripsJson()
    {
        var (registry, _) = Build();
        registry.Register("flow.code.custom", """{"nodes":[]}""");
        Assert.Equal("""{"nodes":[]}""", registry.GetDefinition("flow.code.custom"));
    }

    [Fact]
    public void UnknownCodeReturnsNull()
    {
        var (registry, _) = Build();
        Assert.Null(registry.GetDefinition("flow.unknown.code"));
    }

    [Fact]
    public void DuplicateCodeOverwritesPrevious()
    {
        var (registry, _) = Build();
        registry.Register("flow.dup", "v1");
        registry.Register("flow.dup", "v2");
        Assert.Equal("v2", registry.GetDefinition("flow.dup"));
    }

    // ============ R1 唯一启用解析 ============

    [Fact]
    public async Task SeededFlowMatchesWholeTenantOrgScope()
    {
        var (registry, _) = Build();
        var flow = await registry.ResolveEnabledAsync("pur_request", orgId: 0, TestContext.Current.CancellationToken);
        Assert.NotNull(flow);
        Assert.Equal("flow.purchase.request", flow!.Definition.DefCode);
    }

    [Fact]
    public async Task SeededFlowNodesAndBranchesBatchLoaded()
    {
        var (registry, _) = Build();
        var flow = await registry.ResolveEnabledAsync("pur_request", orgId: 100, TestContext.Current.CancellationToken);
        Assert.NotNull(flow);
        Assert.Equal(3, flow!.Nodes.Count);
        Assert.True(flow.Nodes[0].SortNo <= flow.Nodes[1].SortNo);
        Assert.Equal(5, flow.Branches.Count);
    }

    [Fact]
    public async Task UnknownBizTypeReturnsNull()
    {
        var (registry, _) = Build();
        var flow = await registry.ResolveEnabledAsync(
            "biz.not.seeded", orgId: 0, TestContext.Current.CancellationToken);
        Assert.Null(flow);
    }

    // ============ 懒种子（租户级零定义时补种后重查一次） ============

    [Fact]
    public async Task EmptyTenantLazySeedsDefaultFlowsOnResolve()
    {
        var (registry, db) = BuildUnseeded();

        var flow = await registry.ResolveEnabledAsync("pur_request", orgId: 0, TestContext.Current.CancellationToken);

        Assert.NotNull(flow);
        Assert.Equal("flow.purchase.request", flow!.Definition.DefCode);
        Assert.Equal(4, await db.FlowDefinitions.CountAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task EmptyTenantLazySeedMissForUnseededBizTypeStillReturnsNull()
    {
        var (registry, db) = BuildUnseeded();

        var flow = await registry.ResolveEnabledAsync(
            "biz.not.seeded", orgId: 0, TestContext.Current.CancellationToken);

        Assert.Null(flow);
        Assert.Equal(4, await db.FlowDefinitions.CountAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task TwoEnabledVersionsThrowConflict()
    {
        var (registry, db) = Build();
        // 脏数据模拟：同 biz_type 两个 Enabled 版本（启用动作未互斥破坏 R1）
        var now = DateTimeOffset.UtcNow;
        var v2 = new ApprovalFlowDefinition(TenantId, "flow.purchase.request.v2", "采购申请审批 v2", "pur_request",
            "[]", version: 2, "{}", now);
        v2.SetStatus(ApprovalFlowStatus.Enabled, now);
        db.FlowDefinitions.Add(v2);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<ConflictException>(() => registry.ResolveEnabledAsync(
            "pur_request", orgId: 0, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task NarrowedOrgScopeOnlyMatchesListedOrg()
    {
        var (registry, db) = Build();
        var now = DateTimeOffset.UtcNow;
        var scoped = new ApprovalFlowDefinition(TenantId, "flow.sales.scoped", "销售审批（限定组织）", "sal_order",
            "[300]", version: 1, "{}", now);
        scoped.SetStatus(ApprovalFlowStatus.Enabled, now);
        db.FlowDefinitions.Add(scoped);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        Assert.Null(await registry.ResolveEnabledAsync("sal_order", orgId: 400, TestContext.Current.CancellationToken));
        Assert.NotNull(await registry.ResolveEnabledAsync("sal_order", orgId: 300, TestContext.Current.CancellationToken));
    }

    // ============ R2 按锁定定义 Id 装载 ============

    [Fact]
    public async Task LoadByLockedDefinitionIdReturnsSameAggregate()
    {
        var (registry, _) = Build();
        var flow = await registry.ResolveEnabledAsync("pur_request", orgId: 0, TestContext.Current.CancellationToken);

        var reloaded = await registry.LoadByDefinitionIdAsync(flow!.Definition.Id, TestContext.Current.CancellationToken);
        Assert.Equal(flow.Definition.Id, reloaded.Definition.Id);
        Assert.Equal(flow.Nodes.Count, reloaded.Nodes.Count);
    }

    [Fact]
    public async Task LoadMissingDefinitionThrowsNotFound()
    {
        var (registry, _) = Build();
        await Assert.ThrowsAsync<NotFoundException>(() => registry.LoadByDefinitionIdAsync(
            19_900_000L, TestContext.Current.CancellationToken));
    }

    // ============ R3 兜底分支完整性 ============

    [Fact]
    public async Task SeededFlowPassesBranchDefaultValidation()
    {
        var (registry, _) = Build();
        var flow = await registry.ResolveEnabledAsync("expense_claim", orgId: 0, TestContext.Current.CancellationToken);
        ApprovalFlowRegistry.EnsureBranchDefaults(flow!);
    }

    [Fact]
    public async Task LineWithoutDefaultBranchThrowsConflict()
    {
        var (registry, db) = Build();
        var now = DateTimeOffset.UtcNow;
        var definition = new ApprovalFlowDefinition(TenantId, "flow.no.default", "缺兜底流程", "no_default_biz",
            "[]", version: 1, "{}", now);
        definition.SetStatus(ApprovalFlowStatus.Enabled, now);
        // 定义先落库拿雪花 Id（雪花主键在 SaveChanges 时由拦截器填充），再构造节点与分支
        db.FlowDefinitions.Add(definition);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var node = new ApprovalFlowNode(TenantId, definition.Id, "n1", ApprovalNodeType.Approval, "一级审批",
            ApprovalApproverTypes.DeptLeader, null, ApprovalSignMode.OrSign, 24, ApprovalTimeoutAction.Urge,
            allowAddSign: true, ApprovalRejectMode.StepBack, sortNo: 10);
        var branch = new ApprovalFlowBranch(TenantId, definition.Id, "n1", "n2",
            ConditionExpressionEngineFactory.Default.Parse("total_amount >= 100").ToJson(), 10, isDefault: false);
        db.FlowNodes.Add(node);
        db.FlowBranches.Add(branch);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var flow = new EnabledApprovalFlow(definition, [node], [branch]);
        Assert.Throws<ConflictException>(() => ApprovalFlowRegistry.EnsureBranchDefaults(flow));
    }

    [Fact]
    public async Task TailNodeWithoutAnyBranchRequiresDefault()
    {
        var (registry, db) = Build();
        var now = DateTimeOffset.UtcNow;
        var definition = new ApprovalFlowDefinition(TenantId, "flow.no.branch", "无出边流程", "no_branch_biz",
            "[]", version: 1, "{}", now);
        definition.SetStatus(ApprovalFlowStatus.Enabled, now);
        // 定义先落库拿雪花 Id（雪花主键在 SaveChanges 时由拦截器填充），再构造节点
        db.FlowDefinitions.Add(definition);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var node = new ApprovalFlowNode(TenantId, definition.Id, "tail", ApprovalNodeType.Approval, "末级审批",
            ApprovalApproverTypes.User, "9001", ApprovalSignMode.OrSign, 0, ApprovalTimeoutAction.Urge,
            allowAddSign: true, ApprovalRejectMode.StepBack, sortNo: 10);
        db.FlowNodes.Add(node);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        // 链尾节点无任何出边 = 分支线缺兜底（运行期会误入"未命中终止"），启用前必须拦截
        var flow = new EnabledApprovalFlow(definition, [node], []);
        Assert.Throws<ConflictException>(() => ApprovalFlowRegistry.EnsureBranchDefaults(flow));
    }

    [Fact]
    public async Task BranchesOrderedByPriorityDescending()
    {
        var (registry, _) = Build();
        var flow = await registry.ResolveEnabledAsync("pur_request", orgId: 0, TestContext.Current.CancellationToken);

        var fromLeader = flow!.FindBranches("dept_leader");
        Assert.Equal(2, fromLeader.Count);
        Assert.Equal(20, fromLeader[0].Priority);
        Assert.True(fromLeader[0].Priority >= fromLeader[1].Priority);
        // ExprJson 为条件引擎 Parse 产出的 AST JSON（以 {"$type": 开头），断言变量名包含其中而非表达式原文前缀
        Assert.Contains("total_amount", fromLeader[0].ExprJson, StringComparison.Ordinal);
    }
}
