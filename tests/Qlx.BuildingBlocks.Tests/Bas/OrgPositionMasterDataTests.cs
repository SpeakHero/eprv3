/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-25 15:00:00 +08:00 中国标准时间 星期日
 * 描述：阶段三「组织架构与岗位」测试——统一发号器段解析/序号递增/冲突重试、
 *       组织停用阻断（409 + details 岗位名）、版本快照与按日期回看、审计快照捕获、
 *       审批完成事件应用（通过生效 / 驳回不变更 / 同实例二次事件幂等）、岗位守卫与超编挂起
 */

using System.Globalization;
using System.Text.Json;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

using Qlx.Abstractions.Approvals;
using Qlx.Abstractions.Audit;
using Qlx.Abstractions.Events;
using Qlx.Abstractions.Events.Bas;
using Qlx.Abstractions.Exceptions;
using Qlx.Abstractions.Queries;
using Qlx.Abstractions.Serialization;
using Qlx.BuildingBlocks.Caching;
using Qlx.BuildingBlocks.EventBus;
using Qlx.BuildingBlocks.Ids;
using Qlx.BuildingBlocks.Numbering;
using Qlx.BuildingBlocks.Numbering.Pinyin;
using Qlx.BuildingBlocks.Support.Logs;
using Qlx.BuildingBlocks.Tests.Files;
using Qlx.BuildingBlocks.Tests.Jobs;
using Qlx.BuildingBlocks.Tests.Portal.Auth;
using Qlx.Modules.Bas;
using Qlx.Modules.Bas.Domain;
using Qlx.Modules.Bas.Dtos;
using Qlx.Modules.Bas.Repositories;
using Qlx.Modules.Bas.Services;
using Qlx.Modules.Bas.Services.Numbering;

namespace Qlx.BuildingBlocks.Tests.Bas;

/// <summary>桩雪花 Id 生成器（单调递增，便于审计事件 Id 断言）。</summary>
internal sealed class BasStubSnowflakeIdGenerator : ISnowflakeIdGenerator
{
    private long _current = 700000000000000000L;

    public long NextId() => Interlocked.Increment(ref _current);
}

/// <summary>记录型事件总线：捕获 Outbox 持久发布（组织创建事件断言）。</summary>
internal sealed class BasRecordingEventBus : IEventBus
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

/// <summary>审批中心桩：发起审批返回递增实例 Id，捕获发起请求供断言。</summary>
internal sealed class BasStubApprovalService : IApprovalService
{
    private int _seed;

    public List<StartApprovalRequest> Started { get; } = [];

    public Task<string> StartApprovalAsync(StartApprovalRequest request, CancellationToken cancellationToken)
    {
        Started.Add(request);
        return Task.FromResult($"inst-{Interlocked.Increment(ref _seed)}");
    }

    public Task ApproveAsync(ApproveRequest request, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task RejectAsync(RejectRequest request, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task WithdrawAsync(WithdrawRequest request, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task AddSignerAsync(AddSignerRequest request, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task TransferAsync(TransferRequest request, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task UrgeAsync(UrgeRequest request, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task<IReadOnlyList<ApprovalTaskInfo>> GetTodoListAsync(string userId, CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<ApprovalTaskInfo>>([]);

    public Task<IReadOnlyList<ApprovalInstanceInfo>> GetMyInitiatedListAsync(string userId, CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<ApprovalInstanceInfo>>([]);

    public Task<ApprovalInstanceInfo?> GetInstanceAsync(string instanceId, CancellationToken cancellationToken)
        => Task.FromResult<ApprovalInstanceInfo?>(null);
}

public sealed class OrgPositionMasterDataTests
{
    private const long TenantId = 42L;
    private const string Operator = "user-9";

    /// <summary>测试夹具：共享 Sqlite 内存库 + 全部桩依赖。</summary>
    private sealed class Harness
    {
        public required BasTenantDbContext Db { get; init; }

        public required BasTenantRepository Repo { get; init; }

        public required InMemorySerialSequenceStore Serials { get; init; }

        public required MemoryCacheService Cache { get; init; }

        public required InMemoryAuditService Audit { get; init; }

        public required StubTenantMemberQuery MemberQuery { get; init; }

        public required BasRecordingEventBus Events { get; init; }

        public required BasStubApprovalService Approval { get; init; }

        public required OrgMasterDataService OrgService { get; init; }

        public required PositionMasterDataService PositionService { get; init; }

        public required ItemCategoryService CategoryService { get; init; }

        public required BasApprovalApplier Applier { get; init; }
    }

    private static Harness Build()
    {
        var basConnection = new SqliteConnection("DataSource=:memory:");
        basConnection.Open();
        var logConnection = new SqliteConnection("DataSource=:memory:");
        logConnection.Open();

        var resolver = new JobStubTenantDatabaseResolver();
        var db = new BasTenantDbContext(
            resolver,
            TenantId,
            new DbContextOptionsBuilder<BasTenantDbContext>().UseSqlite(basConnection).Options);
        db.Database.EnsureCreated();
        var logDb = new TenantLogDbContext(
            resolver,
            TenantId,
            new DbContextOptionsBuilder<TenantLogDbContext>().UseSqlite(logConnection).Options);
        logDb.Database.EnsureCreated();

        var repo = new BasTenantRepository(db, logDb);
        var serials = new InMemorySerialSequenceStore();
        var cache = new MemoryCacheService(Options.Create(new CacheOptions()));
        var audit = new InMemoryAuditService();
        var memberQuery = new StubTenantMemberQuery();
        var events = new BasRecordingEventBus();
        var approval = new BasStubApprovalService();
        var tenantCtx = new TestTenantContext(TenantId);
        var userCtx = new TestCurrentUserContext(Operator);

        var codeGenerator = new BasCodeNumberGenerator(repo, serials);
        var orgService = new OrgMasterDataService(
            tenantCtx,
            repo,
            codeGenerator,
            approval,
            events,
            cache,
            audit,
            new BasStubSnowflakeIdGenerator(),
            userCtx,
            memberQuery);
        var positionService = new PositionMasterDataService(
            tenantCtx,
            repo,
            new EncodingGenerator(new PinyinConverter(), serials),
            approval,
            cache,
            audit,
            new BasStubSnowflakeIdGenerator(),
            userCtx);
        var categoryService = new ItemCategoryService(
            tenantCtx,
            repo,
            codeGenerator,
            approval,
            new StubSystemParameterProvider(),
            cache,
            audit,
            new BasStubSnowflakeIdGenerator(),
            userCtx);

        var stubParameters = new StubSystemParameterProvider();
        var snowflake = new BasStubSnowflakeIdGenerator();
        var supplierService = new SupplierService(
            tenantCtx,
            repo,
            codeGenerator,
            approval,
            cache,
            audit,
            snowflake,
            userCtx);
        var customerService = new CustomerService(
            tenantCtx,
            repo,
            codeGenerator,
            approval,
            stubParameters,
            cache,
            audit,
            snowflake,
            userCtx);

        return new Harness
        {
            Db = db,
            Repo = repo,
            Serials = serials,
            Cache = cache,
            Audit = audit,
            MemberQuery = memberQuery,
            Events = events,
            Approval = approval,
            OrgService = orgService,
            PositionService = positionService,
            CategoryService = categoryService,
            Applier = new BasApprovalApplier(
                tenantCtx,
                repo,
                orgService,
                positionService,
                categoryService,
                supplierService,
                customerService,
                events,
                cache),
        };
    }

    private static Organization SeedOrg(Harness h, long id, string name, long? parentId = null)
    {
        // 租户内 Code 唯一索引：测试种子显式给出唯一编码
        var org = new Organization(TenantId, name, OrgNodeType.Department, parentId, code: $"ORG-T-{id}");
        org.AssignId(id);
        h.Db.Organizations.Add(org);
        _ = h.Db.SaveChanges();
        return org;
    }

    private static Position SeedPosition(
        Harness h, long id, long orgId, string name, int headcount, int currentCount = 0, bool enabled = true)
    {
        var position = new Position(TenantId, orgId, name, $"POS-T-{id}", headcount);
        position.AssignId(id);
        position.SetCurrentCount(currentCount);
        if (!enabled)
        {
            position.Disable();
        }

        h.Db.Positions.Add(position);
        _ = h.Db.SaveChanges();
        return position;
    }

    private static string BizId(long requestId) => requestId.ToString(CultureInfo.InvariantCulture);

    // ============ 统一发号器（3.3） ============

    [Fact]
    public async Task NumberGeneratorRendersTypeSegmentAndIncrementsSequence()
    {
        Harness h = Build();
        var generator = new BasCodeNumberGenerator(h.Repo, h.Serials);

        string first = await generator.NextAsync(
            "org", TenantId, null, "BM", (_, _) => Task.FromResult(false), TestContext.Current.CancellationToken);
        string second = await generator.NextAsync(
            "org", TenantId, null, "BM", (_, _) => Task.FromResult(false), TestContext.Current.CancellationToken);
        string supplier = await generator.NextAsync(
            "supplier", TenantId, null, null, (_, _) => Task.FromResult(false), TestContext.Current.CancellationToken);

        // 段解析：{type} 类型代码 + {seq:2} 两位左补零；序号同键连续递增
        Assert.Equal("ORG-BM-01", first);
        Assert.Equal("ORG-BM-02", second);
        // 不同 bizType 的序号互不干扰（各自从 1 起）
        Assert.Equal("SUP-000001", supplier);
    }

    [Fact]
    public async Task NumberGeneratorRetriesOnConflict()
    {
        Harness h = Build();
        var generator = new BasCodeNumberGenerator(h.Repo, h.Serials);
        var attempts = 0;

        string code = await generator.NextAsync(
            "org",
            TenantId,
            null,
            "BM",
            (_, _) => Task.FromResult(Interlocked.Increment(ref attempts) <= 2),
            TestContext.Current.CancellationToken);

        // 前两次候选视为冲突，第三次取号成功（序号已推进到 03）
        Assert.Equal("ORG-BM-03", code);
        Assert.Equal(3, attempts);
    }

    [Fact]
    public async Task NumberGeneratorThrowsAfterRetryLimit()
    {
        Harness h = Build();
        var generator = new BasCodeNumberGenerator(h.Repo, h.Serials);

        InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => generator.NextAsync(
                "org", TenantId, null, "BM", (_, _) => Task.FromResult(true), TestContext.Current.CancellationToken));

        Assert.Contains("冲突重试已达上限", ex.Message, StringComparison.Ordinal);
    }

    // ============ 组织：创建 / 快照 / 审计 / 事件 ============

    [Fact]
    public async Task CreateOrgGeneratesCodeWritesSnapshotAuditAndEvent()
    {
        Harness h = Build();

        OrgRow row = await h.OrgService.CreateAsync(
            new SaveOrgPayload(null, "集团总部", (int)OrgNodeType.Group, null, 0), TestContext.Current.CancellationToken);

        // 编码由统一发号器生成（前端不传码）
        Assert.Equal("ORG-GS-01", row.Code);
        Assert.Equal(1, row.Version);

        // 版本快照：创建后落 v1 扁平 JSON
        IReadOnlyList<OrgHistoryRow> history = await h.OrgService.GetHistoryAsync(row.Id, TestContext.Current.CancellationToken);
        Assert.Single(history);
        Assert.Equal(1, history[0].Version);
        // 快照为扁平原始字段 JSON（名称以 UTF-8 转义存储，反序列化后断言）
        OrgRow snapshot = Assert.IsType<OrgRow>(
            JsonSerializer.Deserialize<OrgRow>(history[0].SnapshotJson, QlxJsonDefaults.PreserveNull));
        Assert.Equal("集团总部", snapshot.Name);

        // 审计：事务提交后调用 CaptureChangeAsync
        AuditLogEntry audit = Assert.Single(h.Audit.GetCapturedChanges());
        Assert.Equal(nameof(Organization), audit.EntityType);
        Assert.Equal(row.Id, audit.EntityId);
        Assert.Equal("INSERT", audit.Operation);
        Assert.Equal(Operator, audit.ChangedBy);
        Assert.Null(audit.OldValues);
        Assert.NotNull(audit.NewValues);

        // 组织创建事件持久发布（Outbox）
        OrganizationCreatedEvent published = Assert.IsType<OrganizationCreatedEvent>(
            Assert.Single(h.Events.DurableEvents));
        Assert.Equal(row.Id, published.OrgId);
        Assert.Equal("ORG-GS-01", published.OrgCode);
        Assert.Equal($"t-{TenantId}", published.TenantCode);
    }

    [Fact]
    public async Task OrgVersionSnapshotSupportsAsOfDateLookback()
    {
        Harness h = Build();
        Organization org = SeedOrg(h, 1, "财务部");

        // 手工种子三天前的 v1 快照（旧名称），服务更新后自动落 v2 快照（新名称）
        OrgRow oldSnapshot = new(
            org.Id,
            null,
            "ORG-T-1",
            "财务部",
            (int)OrgNodeType.Department,
            null,
            DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-3)),
            null,
            1,
            (int)EntityStatus.Enabled,
            0,
            false,
            DateTimeOffset.UtcNow.AddDays(-3),
            DateTimeOffset.UtcNow.AddDays(-3));
        h.Db.OrgNodeHistories.Add(new OrgNodeHistory(
            TenantId,
            org.Id,
            1,
            JsonSerializer.Serialize(oldSnapshot, QlxJsonDefaults.PreserveNull),
            DateTimeOffset.UtcNow.AddDays(-3),
            Operator));
        _ = h.Db.SaveChanges();

        OrgRow updated = await h.OrgService.UpdateAsync(
            org.Id,
            new SaveOrgPayload(null, "财务中心", (int)OrgNodeType.Department, null, 0),
            TestContext.Current.CancellationToken);

        // 每次写操作 Version +1 并追加一条版本快照
        Assert.Equal(2, updated.Version);
        IReadOnlyList<OrgHistoryRow> history = await h.OrgService.GetHistoryAsync(org.Id, TestContext.Current.CancellationToken);
        Assert.Equal(2, history.Count);
        Assert.Equal(1, history[0].Version);
        Assert.Equal(2, history[1].Version);

        // 按日期回看：昨天生效的版本为 v1（旧名称）
        IReadOnlyList<OrgRow> past = await h.OrgService.ListAsync(
            DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1)), TestContext.Current.CancellationToken);
        Assert.Equal("财务部", Assert.Single(past).Name);

        // 当前视图为新名称
        IReadOnlyList<OrgRow> current = await h.OrgService.ListAsync(asOfDate: null, TestContext.Current.CancellationToken);
        Assert.Equal("财务中心", Assert.Single(current).Name);

        // 更新审计：旧值快照非空
        AuditLogEntry audit = Assert.Single(h.Audit.GetCapturedChanges());
        Assert.Equal("UPDATE", audit.Operation);
        Assert.NotNull(audit.OldValues);
        OrgRow newValues = Assert.IsType<OrgRow>(
            JsonSerializer.Deserialize<OrgRow>(audit.NewValues!, QlxJsonDefaults.PreserveNull));
        Assert.Equal("财务中心", newValues.Name);
    }

    [Fact]
    public async Task ToggleOrgIsBlockedByEnabledPositionsWithNamesInDetails()
    {
        Harness h = Build();
        _ = SeedOrg(h, 1, "集团总部");
        Organization dept = SeedOrg(h, 2, "财务部", 1);
        _ = SeedPosition(h, 10, dept.Id, "会计", headcount: 3);

        ConflictException ex = await Assert.ThrowsAsync<ConflictException>(
            () => h.OrgService.ToggleAsync(dept.Id, TestContext.Current.CancellationToken));

        Assert.Equal("ORG_HAS_ACTIVE_REFS", ex.ErrorCode);
        Assert.NotNull(ex.Details);
        System.Reflection.PropertyInfo positionsProperty = ex.Details!.GetType().GetProperty("Positions")
            ?? throw new InvalidOperationException("停用阻断 details 缺少 Positions 字段");
        IEnumerable<string> names = Assert.IsAssignableFrom<IEnumerable<string>>(
            positionsProperty.GetValue(ex.Details));
        Assert.Contains("会计", names);
    }

    [Fact]
    public async Task ToggleOrgIsBlockedByActiveMembers()
    {
        Harness h = Build();
        _ = SeedOrg(h, 1, "集团总部");
        Organization dept = SeedOrg(h, 2, "财务部", 1);
        h.MemberQuery.Members.Add(new TenantMemberSummaryRow(
            501, TenantId, PortalUserId: 1501, LoginAccount: "zhangsan", DisplayName: "张三",
            Phone: null, Email: null, OrgId: dept.Id,
            MemberRole: 2, MemberStatus: 1,
            JoinAt: DateTimeOffset.UtcNow, LastLoginAt: null));

        ConflictException ex = await Assert.ThrowsAsync<ConflictException>(
            () => h.OrgService.ToggleAsync(dept.Id, TestContext.Current.CancellationToken));

        Assert.Equal("ORG_HAS_ACTIVE_REFS", ex.ErrorCode);

        // 组织状态未变更
        OrgRow row = await h.OrgService.GetAsync(dept.Id, TestContext.Current.CancellationToken);
        Assert.Equal((int)EntityStatus.Enabled, row.Status);
    }

    // ============ 审批完成事件回写（3.5） ============

    [Fact]
    public async Task ApprovedOrgAdjustRequestAppliesChangeAndIsIdempotent()
    {
        Harness h = Build();

        ApprovalRequestRow submitted = await h.OrgService.SubmitAdjustRequestAsync(
            new SubmitOrgAdjustPayload(
                Action: "create",
                OrgId: null,
                Name: "项目部A",
                ParentId: null,
                Type: (int)OrgNodeType.Project,
                LeaderPersonId: null,
                SortOrder: 0,
                TargetOrgId: null,
                Reason: "新增项目部"),
            TestContext.Current.CancellationToken);

        // 审批挂起：请求待审批且已回写实例 Id
        Assert.Equal(1, submitted.Status);
        Assert.NotNull(submitted.ApprovalInstanceId);
        Assert.Single(h.Approval.Started);

        var completed = new ApprovalCompletedEvent(
            submitted.ApprovalInstanceId!,
            OrgMasterDataService.OrgBizType,
            OrgMasterDataService.OrgBizType,
            BizId(submitted.BizId),
            ApprovalStatus.Approved,
            Operator,
            DateTimeOffset.UtcNow);

        await h.Applier.ApplyAsync(completed, TestContext.Current.CancellationToken);

        Organization created = await h.Db.Organizations.SingleAsync(
            o => o.Name == "项目部A", TestContext.Current.CancellationToken);
        Assert.Equal(OrgNodeType.Project, created.Type);

        BasApprovalRequest request = await h.Db.ApprovalRequests.SingleAsync(
            r => r.ApprovalInstanceId == submitted.ApprovalInstanceId, TestContext.Current.CancellationToken);
        Assert.Equal(ApprovalRequestStatus.Approved, request.Status);
        Assert.NotNull(request.AppliedAt);

        // 同一 ApprovalInstanceId 二次事件不重复生效
        int orgCount = await h.Db.Organizations.CountAsync(TestContext.Current.CancellationToken);
        await h.Applier.ApplyAsync(completed, TestContext.Current.CancellationToken);
        Assert.Equal(orgCount, await h.Db.Organizations.CountAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task RejectedOrgAdjustRequestDoesNotChangeData()
    {
        Harness h = Build();

        ApprovalRequestRow submitted = await h.OrgService.SubmitAdjustRequestAsync(
            new SubmitOrgAdjustPayload(
                Action: "create", OrgId: null, Name: "项目部B", ParentId: null,
                Type: (int)OrgNodeType.Project, LeaderPersonId: null, SortOrder: 0,
                TargetOrgId: null, Reason: "新增项目部"),
            TestContext.Current.CancellationToken);

        await h.Applier.ApplyAsync(
            new ApprovalCompletedEvent(
                submitted.ApprovalInstanceId!,
                OrgMasterDataService.OrgBizType,
                OrgMasterDataService.OrgBizType,
                BizId(submitted.BizId),
                ApprovalStatus.Rejected,
                Operator,
                DateTimeOffset.UtcNow),
            TestContext.Current.CancellationToken);

        Assert.Null(await h.Db.Organizations.FirstOrDefaultAsync(
            o => o.Name == "项目部B", TestContext.Current.CancellationToken));
        BasApprovalRequest request = await h.Db.ApprovalRequests.SingleAsync(
            r => r.ApprovalInstanceId == submitted.ApprovalInstanceId, TestContext.Current.CancellationToken);
        Assert.Equal(ApprovalRequestStatus.Rejected, request.Status);
        Assert.Null(request.AppliedAt);
    }

    [Fact]
    public async Task ApprovedOverstaffRequestAppliesHeadcount()
    {
        Harness h = Build();
        Organization org = SeedOrg(h, 1, "财务部");
        Position position = SeedPosition(h, 10, org.Id, "会计", headcount: 3);

        ApprovalRequestRow submitted = await h.PositionService.SubmitOverstaffRequestAsync(
            position.Id, new SubmitOverstaffPayload(8, "业务扩张"), TestContext.Current.CancellationToken);

        Assert.Equal(1, submitted.Status);
        Assert.Equal(PositionMasterDataService.OverstaffBizType, submitted.BizType);
        Assert.Equal(PositionMasterDataService.OverstaffBizType, h.Approval.Started[0].ApprovalDefinitionCode);

        await h.Applier.ApplyAsync(
            new ApprovalCompletedEvent(
                submitted.ApprovalInstanceId!,
                PositionMasterDataService.OverstaffBizType,
                PositionMasterDataService.OverstaffBizType,
                BizId(submitted.BizId),
                ApprovalStatus.Approved,
                Operator,
                DateTimeOffset.UtcNow),
            TestContext.Current.CancellationToken);

        Position reloaded = await h.Db.Positions.AsNoTracking().SingleAsync(
            p => p.Id == position.Id, TestContext.Current.CancellationToken);
        Assert.Equal(8, reloaded.Headcount);
    }

    // ============ 岗位守卫与统计 ============

    [Fact]
    public async Task SavePositionGeneratesCodeAndCapturesAudit()
    {
        Harness h = Build();
        Organization org = SeedOrg(h, 1, "财务部");

        PositionMasterRow row = await h.PositionService.SaveAsync(
            new SavePositionCommand(null, "会计", org.Id, 3, "负责日常核算", "编制凭证与报表"),
            TestContext.Current.CancellationToken);

        Assert.StartsWith("pos-", row.Code, StringComparison.Ordinal);
        Assert.Equal("编制凭证与报表", row.Duty);
        Assert.Equal(3, row.Headcount);

        AuditLogEntry audit = Assert.Single(h.Audit.GetCapturedChanges());
        Assert.Equal(nameof(Position), audit.EntityType);
        Assert.Equal("INSERT", audit.Operation);
        Assert.Equal(Operator, audit.ChangedBy);
    }

    [Fact]
    public async Task TogglePositionIsBlockedWhenOnDutyCountPositive()
    {
        Harness h = Build();
        Organization org = SeedOrg(h, 1, "财务部");
        Position position = SeedPosition(h, 10, org.Id, "会计", headcount: 3, currentCount: 2);

        PositionStatsRow stats = await h.PositionService.GetStatsAsync(position.Id, TestContext.Current.CancellationToken);
        Assert.Equal(2, stats.CurrentCount);

        ConflictException ex = await Assert.ThrowsAsync<ConflictException>(
            () => h.PositionService.ToggleAsync(position.Id, TestContext.Current.CancellationToken));
        Assert.Equal("POSITION_HAS_MEMBERS", ex.ErrorCode);

        // 编制不得低于在岗人数
        ConflictException headcountEx = await Assert.ThrowsAsync<ConflictException>(
            () => h.PositionService.AdjustHeadcountAsync(position.Id, 1, TestContext.Current.CancellationToken));
        Assert.Equal("POSITION_HEADCOUNT_BELOW_CURRENT", headcountEx.ErrorCode);
    }

    [Fact]
    public async Task SubmitOverstaffRequestRejectsHeadcountNotAboveCurrent()
    {
        Harness h = Build();
        Organization org = SeedOrg(h, 1, "财务部");
        Position position = SeedPosition(h, 10, org.Id, "会计", headcount: 3);

        ValidationException ex = await Assert.ThrowsAsync<ValidationException>(
            () => h.PositionService.SubmitOverstaffRequestAsync(
                position.Id, new SubmitOverstaffPayload(3, "持平"), TestContext.Current.CancellationToken));

        Assert.Equal("headcount", Assert.Single(ex.FieldErrors).FieldName);
        Assert.Empty(h.Approval.Started);
    }
}
