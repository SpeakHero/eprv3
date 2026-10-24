/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-23
 * 描述：Bas 租户自管理域 TenantAdminService 测试——角色/组织聚合计数（GroupJoin 单往返）、
 *       角色权限/成员回显 404 分支与混淆对称、岗位启停往返、重发邀请（状态机 409 + 站内通知）、
 *       批量导入行级隔离、登录日志页上限与越租户 404（Sqlite 内存库 + 真实 BasTenantRepository/TenantLogDbContext）
 */

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Qlx.Abstractions.Exceptions;
using Qlx.Abstractions.Messages;
using Qlx.Abstractions.Queries;
using Qlx.Abstractions.Tenancy;
using Qlx.BuildingBlocks.Api;
using Qlx.BuildingBlocks.Ids;
using Qlx.BuildingBlocks.MultiTenancy;
using Qlx.BuildingBlocks.Numbering;
using Qlx.BuildingBlocks.Numbering.Pinyin;
using Qlx.BuildingBlocks.Support.Logs;
using Qlx.BuildingBlocks.Tests.Files;
using Qlx.BuildingBlocks.Tests.Jobs;
using Qlx.Modules.Bas;
using Qlx.Modules.Bas.Domain;
using Qlx.Modules.Bas.Dtos;
using Qlx.Modules.Bas.Repositories;
using Qlx.Modules.Bas.Services;

namespace Qlx.BuildingBlocks.Tests.Bas;

/// <summary>租户成员只读查询桩（Portal 库视图由测试直接编排）。</summary>
internal sealed class StubTenantMemberQuery : ITenantMemberQuery
{
    public List<TenantMemberSummaryRow> Members { get; } = [];

    public Task<bool> IsMemberExistsAsync(long tenantId, string account, CancellationToken cancellationToken)
        => Task.FromResult(Members.Exists(m => m.TenantId == tenantId && m.LoginAccount == account));

    public Task<PortalUserPreview?> FindPortalUserByAccountAsync(string account, CancellationToken cancellationToken)
        => Task.FromResult<PortalUserPreview?>(null);

    public Task<(IReadOnlyList<TenantMemberSummaryRow> Items, int Total)> QueryMembersAsync(
        long tenantId, string? keyword, long? orgId, int? role, int? status,
        int skip, int take, CancellationToken cancellationToken)
    {
        List<TenantMemberSummaryRow> filtered = [.. Members.Where(m => m.TenantId == tenantId)];
        return Task.FromResult<(IReadOnlyList<TenantMemberSummaryRow>, int)>(
            ([.. filtered.Skip(skip).Take(take)], filtered.Count));
    }

    public Task<TenantMemberSummaryRow?> GetMemberDetailAsync(long tenantMemberId, CancellationToken cancellationToken)
        => Task.FromResult(Members.Find(m => m.TenantMemberId == tenantMemberId));

    public Task<IReadOnlyList<TenantMemberSummaryRow>> ListAllMembersAsync(long tenantId, CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<TenantMemberSummaryRow>>([.. Members.Where(m => m.TenantId == tenantId)]);
}

/// <summary>租户成员写入桩：邀请结果可编排，重发邀请可模拟 409 分支。</summary>
internal sealed class StubTenantMemberAdmin : ITenantMemberAdmin
{
    /// <summary>按账号编排邀请结果（null 表示已是成员）。</summary>
    public Dictionary<string, (long TenantMemberId, long PortalUserId)?> InviteResults { get; } = new(StringComparer.Ordinal);

    /// <summary>重发邀请返回的门户用户 Id 编排（按 TenantMemberId）。</summary>
    public Dictionary<long, long> ResendResults { get; } = [];

    /// <summary>重发邀请抛 409 的成员集合（模拟非 PendingInvite）。</summary>
    public HashSet<long> ResendConflictIds { get; } = [];

    public List<(long TenantMemberId, long RoleId, long TenantId)> ReplacedRoles { get; } = [];

    public Task<(long TenantMemberId, long PortalUserId)?> InviteMemberAsync(
        long tenantId, string account, string displayName, long orgId, CancellationToken cancellationToken)
        => Task.FromResult(InviteResults.TryGetValue(account, out (long, long)? result) ? result : null);

    public Task ToggleMemberStatusAsync(long tenantMemberId, CancellationToken cancellationToken)
        => Task.CompletedTask;

    public Task<long> ResendInviteAsync(long tenantMemberId, CancellationToken cancellationToken)
    {
        if (ResendConflictIds.Contains(tenantMemberId))
        {
            throw new ConflictException("仅待激活成员可重发邀请", "MEMBER_NOT_PENDING_INVITE");
        }

        return Task.FromResult(ResendResults[tenantMemberId]);
    }

    public Task RemoveMemberFromTenantAsync(long tenantMemberId, CancellationToken cancellationToken)
        => Task.CompletedTask;

    public Task ResetMemberPasswordAsync(long tenantMemberId, string newPassword, CancellationToken cancellationToken)
        => Task.CompletedTask;

    public Task UpdateMemberOrgAsync(long tenantMemberId, long? orgId, CancellationToken cancellationToken)
        => Task.CompletedTask;
}

/// <summary>模块权限查询桩（本测试不消费，返回空集）。</summary>
internal sealed class StubModulePermissionQuery : IModulePermissionQuery
{
    public Task<IReadOnlyList<ModuleDefinitionRow>> ListAllModulesWithPermissionsAsync(CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<ModuleDefinitionRow>>([]);

    public Task<IReadOnlyList<TenantModuleCardRow>> ListTenantModulesAsync(long tenantId, CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<TenantModuleCardRow>>([]);
}

public sealed class TenantAdminServiceTests
{
    private const long TenantId = 42L;
    private const long OtherTenantId = 99L;

    private static readonly HashidsObfuscator Obfuscator = new("bas-test-salt", minLength: 8);

    private static (
        TenantAdminService Service,
        BasTenantDbContext Db,
        TenantLogDbContext LogDb,
        StubTenantMemberQuery MemberQuery,
        StubTenantMemberAdmin MemberAdmin,
        RecordingMessageService Messages) Build(long tenantId = TenantId)
    {
        // Sqlite 内存库（ExecuteDelete 等关系型 API 需真实关系提供程序；选项预配 → 跳过 SqlServer 租户路由）
        var basConnection = new SqliteConnection("DataSource=:memory:");
        basConnection.Open();
        var logConnection = new SqliteConnection("DataSource=:memory:");
        logConnection.Open();

        var resolver = new JobStubTenantDatabaseResolver();
        var db = new BasTenantDbContext(
            resolver,
            tenantId,
            new DbContextOptionsBuilder<BasTenantDbContext>().UseSqlite(basConnection).Options);
        db.Database.EnsureCreated();
        var logDb = new TenantLogDbContext(
            resolver,
            tenantId,
            new DbContextOptionsBuilder<TenantLogDbContext>().UseSqlite(logConnection).Options);
        logDb.Database.EnsureCreated();

        var memberQuery = new StubTenantMemberQuery();
        var memberAdmin = new StubTenantMemberAdmin();
        var messages = new RecordingMessageService();
        var encodingGenerator = new EncodingGenerator(new PinyinConverter(), new InMemorySerialSequenceStore());
        var service = new TenantAdminService(
            new TestTenantContext(tenantId),
            new BasTenantRepository(db, logDb),
            memberQuery,
            memberAdmin,
            new StubModulePermissionQuery(),
            Obfuscator,
            messages,
            encodingGenerator);
        return (service, db, logDb, memberQuery, memberAdmin, messages);
    }

    private static Organization SeedOrg(BasTenantDbContext db, long id, string name, long? parentId = null)
    {
        var org = new Organization(TenantId, name, OrgNodeType.Department, parentId);
        org.AssignId(id);
        db.Organizations.Add(org);
        return org;
    }

    private static Role SeedRole(BasTenantDbContext db, long id, string code, string name, bool isBuiltin = false)
    {
        var role = new Role(TenantId, code, name, isBuiltin);
        role.AssignId(id);
        db.Roles.Add(role);
        return role;
    }

    private static TenantMemberSummaryRow MemberRow(
        long tenantMemberId, long tenantId, string account, long? orgId = null, string displayName = "张三")
        => new(
            tenantMemberId, tenantId, PortalUserId: tenantMemberId + 1000, account, displayName,
            Phone: null, Email: null, orgId,
            MemberRole: 2, MemberStatus: 1,
            JoinAt: DateTimeOffset.UtcNow, LastLoginAt: null);

    // ============ B-3：角色列表聚合计数（单次往返 GroupJoin 投影）============

    [Fact]
    public async Task ListRolesAsyncReturnsMemberAndPermissionCounts()
    {
        var (service, db, _, _, _, _) = Build();
        SeedRole(db, 1, "role-admin", "管理员");
        SeedRole(db, 2, "role-finance", "财务");
        db.MemberRoles.Add(new MemberRole(101, 1, TenantId));
        db.MemberRoles.Add(new MemberRole(102, 1, TenantId));
        db.RolePermissions.Add(new RolePermission(1, TenantId, "bas.admin.member.action"));
        db.RolePermissions.Add(new RolePermission(1, TenantId, "bas.admin.org.menu"));
        db.RolePermissions.Add(new RolePermission(1, TenantId, "bas.admin.role.menu"));
        // 他租户同名角色的关联不得串租户（全局过滤生效）
        db.MemberRoles.Add(new MemberRole(201, 1, OtherTenantId));
        _ = await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        IReadOnlyList<RoleRow> rows = await service.ListRolesAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, rows.Count);
        RoleRow admin = rows.Single(r => r.Code == "role-admin");
        Assert.Equal(2, admin.MemberCount);
        Assert.Equal(3, admin.PermissionCount);
        RoleRow finance = rows.Single(r => r.Code == "role-finance");
        Assert.Equal(0, finance.MemberCount);
        Assert.Equal(0, finance.PermissionCount);
    }

    // ============ B-1/B-2：角色权限点与成员回显 ============

    [Fact]
    public async Task GetRolePermissionCodesAsyncReturnsSeededCodes()
    {
        var (service, db, _, _, _, _) = Build();
        SeedRole(db, 1, "role-admin", "管理员");
        db.RolePermissions.Add(new RolePermission(1, TenantId, "bas.admin.org.menu"));
        db.RolePermissions.Add(new RolePermission(1, TenantId, "bas.admin.member.action"));
        _ = await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        IReadOnlyList<string> codes = await service.GetRolePermissionCodesAsync(1, TestContext.Current.CancellationToken);

        Assert.Equal(2, codes.Count);
        Assert.Contains("bas.admin.org.menu", codes);
        Assert.Contains("bas.admin.member.action", codes);
    }

    [Fact]
    public async Task GetRolePermissionCodesAsyncRoleMissingThrows404()
    {
        var (service, _, _, _, _, _) = Build();

        _ = await Assert.ThrowsAsync<NotFoundException>(
            () => service.GetRolePermissionCodesAsync(999, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GetRoleMemberIdsAsyncReturnsObfuscatedStringsSymmetricWithPut()
    {
        var (service, db, _, _, _, _) = Build();
        SeedRole(db, 1, "role-admin", "管理员");
        db.MemberRoles.Add(new MemberRole(101, 1, TenantId));
        db.MemberRoles.Add(new MemberRole(102, 1, TenantId));
        _ = await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        IReadOnlyList<string> ids = await service.GetRoleMemberIdsAsync(1, TestContext.Current.CancellationToken);

        Assert.Equal(2, ids.Count);
        Assert.Contains(Obfuscator.Encode(101), ids);
        Assert.Contains(Obfuscator.Encode(102), ids);
        Assert.DoesNotContain(ids, id => long.TryParse(id, out _));
    }

    [Fact]
    public async Task GetRoleMemberIdsAsyncRoleMissingThrows404()
    {
        var (service, _, _, _, _, _) = Build();

        _ = await Assert.ThrowsAsync<NotFoundException>(
            () => service.GetRoleMemberIdsAsync(999, TestContext.Current.CancellationToken));
    }

    // ============ B-5：岗位启停状态机往返 ============

    [Fact]
    public async Task TogglePositionStatusAsyncFlipsEnabledAndDisabledRoundTrip()
    {
        var (service, db, _, _, _, _) = Build();
        SeedOrg(db, 10, "技术部");
        var position = new Position(TenantId, 10, "后端工程师", "dev-backend", headcount: 3);
        position.AssignId(100);
        db.Positions.Add(position);
        _ = await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        PositionRow disabled = await service.TogglePositionStatusAsync(100, TestContext.Current.CancellationToken);
        Assert.Equal("Disabled", disabled.Status);

        PositionRow enabled = await service.TogglePositionStatusAsync(100, TestContext.Current.CancellationToken);
        Assert.Equal("Enabled", enabled.Status);
    }

    [Fact]
    public async Task TogglePositionStatusAsyncMissingThrows404()
    {
        var (service, _, _, _, _, _) = Build();

        _ = await Assert.ThrowsAsync<NotFoundException>(
            () => service.TogglePositionStatusAsync(999, TestContext.Current.CancellationToken));
    }

    // ============ 岗位按组织子树筛选（选中组织含全部下级）============

    [Fact]
    public async Task QueryPositionsByOrgIncludesDescendantSubtree()
    {
        var (service, db, _, _, _, _) = Build();
        SeedOrg(db, 10, "淇利馨测试总部");
        SeedOrg(db, 11, "技术部", parentId: 10);
        SeedOrg(db, 12, "后端组", parentId: 11);
        SeedOrg(db, 20, "其他公司");
        AddPosition(db, 1001, 10, "总部专员", "hq-staff");
        AddPosition(db, 1002, 11, "技术经理", "tech-manager");
        AddPosition(db, 1003, 12, "后端工程师", "dev-backend");
        AddPosition(db, 1004, 20, "外部岗位", "external");
        _ = await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        PagedResult<PositionRow> page = await service.QueryPositionsAsync(
            10, keyword: null, new PagedRequest { Page = 1, PageSize = 20 }, TestContext.Current.CancellationToken);

        Assert.Equal(3, page.TotalCount);
        Assert.Equal(
            ["dev-backend", "hq-staff", "tech-manager"],
            [.. page.Items.Select(p => p.Code).OrderBy(c => c, StringComparer.Ordinal)]);
    }

    [Fact]
    public async Task QueryPositionsWithoutOrgReturnsAllPositions()
    {
        var (service, db, _, _, _, _) = Build();
        SeedOrg(db, 10, "淇利馨测试总部");
        SeedOrg(db, 20, "其他公司");
        AddPosition(db, 1001, 10, "总部专员", "hq-staff");
        AddPosition(db, 1004, 20, "外部岗位", "external");
        _ = await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        PagedResult<PositionRow> page = await service.QueryPositionsAsync(
            null, keyword: null, new PagedRequest { Page = 1, PageSize = 20 }, TestContext.Current.CancellationToken);

        Assert.Equal(2, page.TotalCount);
    }

    private static void AddPosition(BasTenantDbContext db, long id, long orgId, string name, string code)
    {
        var position = new Position(TenantId, orgId, name, code, headcount: 1);
        position.AssignId(id);
        db.Positions.Add(position);
    }

    // ============ B-6：重发邀请（越权 404 / 409 分支 / 站内通知）============

    [Fact]
    public async Task ResendMemberInviteAsyncPushesInAppNotificationToObfuscatedPortalUserId()
    {
        var (service, _, _, memberQuery, memberAdmin, messages) = Build();
        memberQuery.Members.Add(MemberRow(500, TenantId, "13800000001", displayName: "李四"));
        memberAdmin.ResendResults[500] = 1500;

        await service.ResendMemberInviteAsync(500, TestContext.Current.CancellationToken);

        PushRequest push = Assert.Single(messages.Pushes);
        Assert.Equal(Obfuscator.Encode(1500), push.RecipientUserId);
        Assert.Equal(MessageChannel.InApp, push.Channel);
        Assert.Equal("TenantMemberInvite", push.BizRef?.BizType);
        Assert.Contains("李四", push.Content);
    }

    [Fact]
    public async Task ResendMemberInviteAsyncCrossTenantMemberThrows404AndNoPush()
    {
        var (service, _, _, memberQuery, memberAdmin, messages) = Build();
        memberQuery.Members.Add(MemberRow(500, OtherTenantId, "13800000001"));
        memberAdmin.ResendResults[500] = 1500;

        _ = await Assert.ThrowsAsync<NotFoundException>(
            () => service.ResendMemberInviteAsync(500, TestContext.Current.CancellationToken));
        Assert.Empty(messages.Pushes);
    }

    [Fact]
    public async Task ResendMemberInviteAsyncNotPendingInvitePropagates409AndNoPush()
    {
        var (service, _, _, memberQuery, memberAdmin, messages) = Build();
        memberQuery.Members.Add(MemberRow(500, TenantId, "13800000001"));
        _ = memberAdmin.ResendConflictIds.Add(500);

        ConflictException ex = await Assert.ThrowsAsync<ConflictException>(
            () => service.ResendMemberInviteAsync(500, TestContext.Current.CancellationToken));
        Assert.Equal("MEMBER_NOT_PENDING_INVITE", ex.ErrorCode);
        Assert.Empty(messages.Pushes);
    }

    // ============ B-7：批量导入（行级隔离 / 格式校验 / 部分失败明细）============

    [Fact]
    public async Task ImportMembersAsyncEmptyRowsReturnsZero()
    {
        var (service, _, _, _, _, _) = Build();

        ImportMembersResult result = await service.ImportMembersAsync([], TestContext.Current.CancellationToken);

        Assert.Equal(0, result.Invited);
        Assert.Empty(result.Failed);
    }

    [Fact]
    public async Task ImportMembersAsyncAllValidRowsInvitedAndRoleBound()
    {
        var (service, db, _, _, memberAdmin, _) = Build();
        SeedOrg(db, 10, "技术部");
        SeedRole(db, 1, "role-dev", "开发");
        _ = await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        memberAdmin.InviteResults["13800000001"] = (500, 1500);
        memberAdmin.InviteResults["a@b.com"] = (501, 1501);

        ImportMembersResult result = await service.ImportMembersAsync(
            [
                new ImportMemberRow(1, "张三", "13800000001", "技术部", "开发"),
                new ImportMemberRow(2, "李四", "a@b.com", "技术部", ""),
            ],
            TestContext.Current.CancellationToken);

        Assert.Equal(2, result.Invited);
        Assert.Empty(result.Failed);
        // 第 1 行绑定角色（Bas 库 member_roles），第 2 行无角色不绑定
        Assert.Equal(1, await db.MemberRoles.CountAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ImportMembersAsyncPartialFailureReportsLineLevelReasons()
    {
        var (service, db, _, _, memberAdmin, _) = Build();
        SeedOrg(db, 10, "技术部");
        SeedRole(db, 1, "role-dev", "开发");
        _ = await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        memberAdmin.InviteResults["13800000001"] = (500, 1500);
        memberAdmin.InviteResults["13800000002"] = null; // 已是成员

        ImportMembersResult result = await service.ImportMembersAsync(
            [
                new ImportMemberRow(1, "张三", "not-an-account", "技术部", "开发"),
                new ImportMemberRow(2, "", "13800000001", "技术部", "开发"),
                new ImportMemberRow(3, "王五", "13800000001", "不存在部", "开发"),
                new ImportMemberRow(4, "赵六", "13800000001", "技术部", "不存在角色"),
                new ImportMemberRow(5, "钱七", "13800000002", "技术部", ""),
                new ImportMemberRow(6, "孙八", "13800000001", "技术部", "开发"),
            ],
            TestContext.Current.CancellationToken);

        Assert.Equal(1, result.Invited);
        Assert.Equal(5, result.Failed.Count);
        Assert.Equal([1, 2, 3, 4, 5], result.Failed.Select(f => f.Line));
        Assert.Contains("账号格式", result.Failed[0].Reason);
        Assert.Contains("姓名", result.Failed[1].Reason);
        Assert.Contains("组织", result.Failed[2].Reason);
        Assert.Contains("角色", result.Failed[3].Reason);
        Assert.Contains("已是本企业成员", result.Failed[4].Reason);
    }

    // ============ B-8：成员登录日志（页上限 20 / 越租户 404 / 账号过滤 + 倒序）============

    [Fact]
    public async Task GetMemberLoginLogsAsyncClampsPageSizeTo20AndOrdersByLoginAtDesc()
    {
        var (service, _, logDb, memberQuery, _, _) = Build();
        memberQuery.Members.Add(MemberRow(500, TenantId, "13800000001"));
        var baseTime = DateTimeOffset.UtcNow.AddHours(-3);
        for (int i = 0; i < 25; i++)
        {
            logDb.LoginLogs.Add(new TenantLogLoginEntity
            {
                EventId = 1000 + i,
                TenantId = TenantId,
                LoginAccount = "13800000001",
                IsSuccess = true,
                LoginAt = baseTime.AddMinutes(i),
            });
        }

        // 他租户同账号日志与其他账号日志均不得混入
        logDb.LoginLogs.Add(new TenantLogLoginEntity
        {
            EventId = 2000,
            TenantId = OtherTenantId,
            LoginAccount = "13800000001",
            IsSuccess = true,
            LoginAt = baseTime.AddHours(1),
        });
        logDb.LoginLogs.Add(new TenantLogLoginEntity
        {
            EventId = 2001,
            TenantId = TenantId,
            LoginAccount = "other@x.com",
            IsSuccess = false,
            FailureReason = "密码错误",
            LoginAt = baseTime.AddHours(2),
        });
        _ = await logDb.SaveChangesAsync(TestContext.Current.CancellationToken);

        PagedResult<MemberLoginLogRow> page = await service.GetMemberLoginLogsAsync(
            500, new PagedRequest { Page = 1, PageSize = 100 }, TestContext.Current.CancellationToken);

        Assert.Equal(20, page.PageSize);
        Assert.Equal(25, page.TotalCount);
        Assert.Equal(20, page.Items.Count);
        Assert.True(page.Items[0].LoginAt > page.Items[^1].LoginAt);
        Assert.All(page.Items, item => Assert.NotEqual(2000, item.EventId));
    }

    [Fact]
    public async Task GetMemberLoginLogsAsyncCrossTenantMemberThrows404()
    {
        var (service, _, _, memberQuery, _, _) = Build();
        memberQuery.Members.Add(MemberRow(500, OtherTenantId, "13800000001"));

        _ = await Assert.ThrowsAsync<NotFoundException>(
            () => service.GetMemberLoginLogsAsync(500, new PagedRequest(), TestContext.Current.CancellationToken));
    }

    // ============ B-4：组织列表直属成员计数（一次成员查询内存分组，禁 N+1）============

    [Fact]
    public async Task ListAllOrgsAsyncFillsMemberCountFromSingleMemberQuery()
    {
        var (service, db, _, memberQuery, _, _) = Build();
        SeedOrg(db, 10, "技术部");
        SeedOrg(db, 11, "财务部");
        SeedOrg(db, 12, "空置部");
        _ = await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        memberQuery.Members.Add(MemberRow(500, TenantId, "13800000001", orgId: 10));
        memberQuery.Members.Add(MemberRow(501, TenantId, "13800000002", orgId: 10));
        memberQuery.Members.Add(MemberRow(502, TenantId, "13800000003", orgId: 11));
        memberQuery.Members.Add(MemberRow(503, TenantId, "13800000004")); // 未分配组织
        memberQuery.Members.Add(MemberRow(504, OtherTenantId, "13800000005", orgId: 10)); // 他租户不计

        IReadOnlyList<OrganizationFlatRow> rows = await service.ListAllOrgsAsync(TestContext.Current.CancellationToken);

        Assert.Equal(3, rows.Count);
        Assert.Equal(2, rows.Single(r => r.Name == "技术部").MemberCount);
        Assert.Equal(1, rows.Single(r => r.Name == "财务部").MemberCount);
        Assert.Equal(0, rows.Single(r => r.Name == "空置部").MemberCount);
    }

    // ============ 成员详情越租户 404（既有缺口修复回归）============

    [Fact]
    public async Task GetMemberDetailAsyncCrossTenantMemberThrows404()
    {
        var (service, _, _, memberQuery, _, _) = Build();
        memberQuery.Members.Add(MemberRow(500, OtherTenantId, "13800000001"));

        _ = await Assert.ThrowsAsync<NotFoundException>(
            () => service.GetMemberDetailAsync(500, TestContext.Current.CancellationToken));
    }
}
