/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-25 22:50:00 +08:00 中国标准时间 星期日
 * 描述：阶段三「人员主数据」测试——EMP-/LAB- 统一发号、证件号 SHA-256 防重、
 *       敏感字段加密落库与脱敏输出、离职终态与在岗数回写、再入职复用人员 Id、
 *       明文端点审计（不记录明文）、组织停用在职人员阻断、跨模块查询缓存与失效
 */

using System.Text.Json;

using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

using Qlx.Abstractions.Authorization;
using Qlx.Abstractions.Exceptions;
using Qlx.Abstractions.Queries;
using Qlx.Abstractions.Serialization;
using Qlx.BuildingBlocks.Caching;
using Qlx.BuildingBlocks.Numbering;
using Qlx.BuildingBlocks.Support.Logs;
using Qlx.BuildingBlocks.Tests.Files;
using Qlx.BuildingBlocks.Tests.Jobs;
using Qlx.Modules.Bas;
using Qlx.Modules.Bas.Domain;
using Qlx.Modules.Bas.Domain.Person;
using Qlx.Modules.Bas.Dtos;
using Qlx.Modules.Bas.Repositories;
using Qlx.Modules.Bas.Services;
using Qlx.Modules.Bas.Services.Numbering;

namespace Qlx.BuildingBlocks.Tests.Bas;

/// <summary>人员主数据测试（领域状态机 + 服务规则 + 加解密/脱敏 + 缓存 + 组织引用保护）。</summary>
public sealed class PersonTests
{
    private const long TenantId = 42L;
    private const string Operator = "user-9";
    private const string IdNo = "510100199001010011";
    private const string Mobile = "13800000001";
    private const string BankCardNo = "6222020000001234";

    private sealed class Harness
    {
        public required BasTenantDbContext Db { get; init; }

        public required BasTenantRepository Repo { get; init; }

        public required MemoryCacheService Cache { get; init; }

        public required InMemoryAuditService Audit { get; init; }

        public required BasRecordingEventBus Events { get; init; }

        public required IDataProtectionProvider Protection { get; init; }

        public required PersonService Service { get; init; }

        public required PersonQueryService Query { get; init; }

        public required OrgMasterDataService OrgService { get; init; }
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

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
        _ = db.Database.EnsureCreated();

        var logDb = new TenantLogDbContext(
            resolver,
            TenantId,
            new DbContextOptionsBuilder<TenantLogDbContext>().UseSqlite(logConnection).Options);
        _ = logDb.Database.EnsureCreated();

        var repo = new BasTenantRepository(db, logDb);
        var serials = new InMemorySerialSequenceStore();
        var cache = new MemoryCacheService(Options.Create(new CacheOptions()));
        var audit = new InMemoryAuditService();
        var events = new BasRecordingEventBus();
        var tenantCtx = new TestTenantContext(TenantId);
        var userCtx = new TestCurrentUserContext(Operator);
        var snowflake = new BasStubSnowflakeIdGenerator();
        IDataProtectionProvider protection = new EphemeralDataProtectionProvider();

        var codeGenerator = new BasCodeNumberGenerator(repo, serials);
        var service = new PersonService(
            tenantCtx, repo, codeGenerator, protection, cache, audit, snowflake, userCtx, events);
        var query = new PersonQueryService(tenantCtx, repo, cache);
        var orgService = new OrgMasterDataService(
            tenantCtx,
            repo,
            codeGenerator,
            new BasStubApprovalService(),
            events,
            cache,
            audit,
            snowflake,
            userCtx,
            new StubTenantMemberQuery());

        return new Harness
        {
            Db = db,
            Repo = repo,
            Cache = cache,
            Audit = audit,
            Events = events,
            Protection = protection,
            Service = service,
            Query = query,
            OrgService = orgService,
        };
    }

    private static Organization SeedOrg(Harness h, long id, string name, long? parentId = null)
    {
        var org = new Organization(TenantId, name, OrgNodeType.Department, parentId, code: $"ORG-T-{id}");
        org.AssignId(id);
        h.Db.Organizations.Add(org);
        _ = h.Db.SaveChanges();
        return org;
    }

    private static Position SeedPosition(
        Harness h, long id, long orgId, string name, int headcount, int currentCount = 0)
    {
        var position = new Position(TenantId, orgId, name, $"POS-T-{id}", headcount);
        position.AssignId(id);
        position.SetCurrentCount(currentCount);
        h.Db.Positions.Add(position);
        _ = h.Db.SaveChanges();
        return position;
    }

    private static void SeedOrgAndPosition(Harness h, int headcount = 5)
    {
        _ = SeedOrg(h, 1, "集团总部");
        _ = SeedOrg(h, 2, "财务部", 1);
        _ = SeedPosition(h, 10, 2, "会计", headcount);
    }

    private static Task<PersonRow> CreateEmployeeAsync(
        Harness h,
        string name = "张三",
        string idNo = IdNo,
        string mobile = Mobile,
        long? orgId = 2,
        long? positionId = 10,
        string? bankCardNo = BankCardNo,
        int employStatus = (int)EmployStatus.Probation)
        => h.Service.CreateAsync(
            new SavePersonPayload(
                name,
                (int)PersonType.Employee,
                (int)PersonIdType.IdCard,
                idNo,
                mobile,
                "李四",
                "13900000001",
                orgId,
                positionId,
                employStatus,
                EntryDate: new DateOnly(2026, 1, 1),
                bankCardNo),
            Ct);

    private static Task<PersonRow> CreateLabourAsync(
        Harness h, string name = "王劳务", string idNo = "510100199202020022", string mobile = "13800000002")
        => h.Service.CreateAsync(
            new SavePersonPayload(
                name,
                (int)PersonType.Labour,
                (int)PersonIdType.IdCard,
                idNo,
                mobile,
                EmergencyContact: null,
                EmergencyPhone: null,
                OrgId: null,
                PositionId: null,
                (int)EmployStatus.Probation,
                EntryDate: new DateOnly(2026, 1, 1),
                BankCardNo: null),
            Ct);

    // ============ 编码、加密与脱敏 ============

    [Fact]
    public async Task CreateEmployeeGeneratesEmpCodeEncryptsAndMasksSensitiveFields()
    {
        Harness h = Build();
        SeedOrgAndPosition(h);

        PersonRow row = await CreateEmployeeAsync(h);

        Assert.StartsWith("EMP-", row.Code, StringComparison.Ordinal);
        Assert.Equal("510100****0011", row.IdNoMasked);
        Assert.Equal("138****0001", row.MobileMasked);
        Assert.Equal("****1234", row.BankCardNoMasked);

        Person stored = await h.Db.Persons.AsNoTracking().SingleAsync(p => p.Id == row.Id, Ct);
        Assert.NotEqual(IdNo, stored.IdNoCipher);
        Assert.NotEqual(Mobile, stored.MobileCipher);
        Assert.NotEqual(BankCardNo, stored.BankCardNoCipher);
        Assert.Equal(64, stored.IdNoHash.Length);

        IDataProtector protector = h.Protection.CreateProtector(
            Qlx.Abstractions.Constants.QlxDataProtectionPurposes.BasPersonSensitive);
        Assert.Equal(IdNo, protector.Unprotect(stored.IdNoCipher));
        Assert.Equal(Mobile, protector.Unprotect(stored.MobileCipher));
        Assert.Equal(BankCardNo, protector.Unprotect(stored.BankCardNoCipher!));

        // 入职即写入当前任职记录
        PersonAssignment assignment = await h.Db.PersonAssignments.AsNoTracking()
            .SingleAsync(a => a.PersonId == row.Id, Ct);
        Assert.True(assignment.IsCurrent);
        Assert.Equal(10, assignment.PositionId);
    }

    [Fact]
    public async Task CreateLabourGeneratesLabCodeWithEmptyAssignment()
    {
        Harness h = Build();

        PersonRow row = await CreateLabourAsync(h);

        Assert.StartsWith("LAB-", row.Code, StringComparison.Ordinal);
        Assert.Null(row.OrgId);
        Assert.Null(row.PositionId);
        Assert.Equal((int)PersonType.Labour, row.PersonType);
    }

    [Fact]
    public async Task DuplicateIdNoIsRejectedWithPersonIdNoDuplicate()
    {
        Harness h = Build();
        SeedOrgAndPosition(h);
        _ = await CreateEmployeeAsync(h);

        ConflictException ex = await Assert.ThrowsAsync<ConflictException>(
            () => CreateEmployeeAsync(h, name: "张三丰", mobile: "13800000003"));

        Assert.Equal("PERSON_ID_NO_DUPLICATE", ex.ErrorCode);
    }

    [Fact]
    public async Task InvalidIdCardAndMobileAreRejectedByBackendValidation()
    {
        Harness h = Build();
        SeedOrgAndPosition(h);

        ValidationException idEx = await Assert.ThrowsAsync<ValidationException>(
            () => CreateEmployeeAsync(h, idNo: "12345"));
        Assert.Contains(idEx.FieldErrors, e => e.FieldName == "idNo");

        ValidationException mobileEx = await Assert.ThrowsAsync<ValidationException>(
            () => CreateEmployeeAsync(h, mobile: "12345"));
        Assert.Contains(mobileEx.FieldErrors, e => e.FieldName == "mobile");
    }

    [Fact]
    public async Task AuditSnapshotContainsMaskedValuesOnly()
    {
        Harness h = Build();
        SeedOrgAndPosition(h);
        _ = await CreateEmployeeAsync(h);

        Qlx.Abstractions.Audit.AuditLogEntry audit = h.Audit.GetCapturedChanges()
            .Single(e => e.Operation == "INSERT" && e.EntityType == nameof(Person));

        Assert.NotNull(audit.NewValues);
        Assert.DoesNotContain(IdNo, audit.NewValues!, StringComparison.Ordinal);
        Assert.DoesNotContain(Mobile, audit.NewValues!, StringComparison.Ordinal);
        Assert.DoesNotContain(BankCardNo, audit.NewValues!, StringComparison.Ordinal);

        PersonRow snapshot = Assert.IsType<PersonRow>(
            JsonSerializer.Deserialize<PersonRow>(audit.NewValues!, QlxJsonDefaults.PreserveNull));
        Assert.Equal("510100****0011", snapshot.IdNoMasked);
        Assert.Equal(Operator, audit.ChangedBy);
    }

    // ============ 在职状态机与岗位在岗数 ============

    [Fact]
    public async Task CreateAndResignWriteBackPositionCurrentCount()
    {
        Harness h = Build();
        SeedOrgAndPosition(h);

        PersonRow row = await CreateEmployeeAsync(h);
        Assert.Equal(1, await PositionCountAsync(h, 10));

        PersonRow left = await h.Service.ResignAsync(
            row.Id, new ResignPersonPayload(new DateOnly(2026, 6, 30), "个人原因"), Ct);

        Assert.Equal((int)EmployStatus.Left, left.EmployStatus);
        Assert.Equal(new DateOnly(2026, 6, 30), left.LeaveDate);
        Assert.Equal(0, await PositionCountAsync(h, 10));

        Assert.Contains(
            h.Events.DurableEvents,
            e => e is Qlx.Abstractions.Events.Bas.PersonLeftEvent p && p.PersonId == row.Id);
    }

    [Fact]
    public async Task LeftStatusIsTerminalAndCannotChangeStatusOrResignAgain()
    {
        Harness h = Build();
        SeedOrgAndPosition(h);
        PersonRow row = await CreateEmployeeAsync(h);
        _ = await h.Service.ResignAsync(row.Id, new ResignPersonPayload(null, null), Ct);

        ConflictException resignAgain = await Assert.ThrowsAsync<ConflictException>(
            () => h.Service.ResignAsync(row.Id, new ResignPersonPayload(null, null), Ct));
        Assert.Equal("PERSON_ALREADY_LEFT", resignAgain.ErrorCode);

        Person tracked = await h.Db.Persons.SingleAsync(p => p.Id == row.Id, Ct);
        ConflictException changeStatus = Assert.Throws<ConflictException>(
            () => tracked.ChangeEmployStatus(EmployStatus.Regular));
        Assert.Equal("PERSON_STATUS_LEFT_TERMINAL", changeStatus.ErrorCode);
    }

    [Fact]
    public async Task RehireReusesPersonIdAndCreatesNewCurrentAssignment()
    {
        Harness h = Build();
        SeedOrgAndPosition(h);
        PersonRow row = await CreateEmployeeAsync(h);
        _ = await h.Service.ResignAsync(row.Id, new ResignPersonPayload(new DateOnly(2026, 3, 31), "离职"), Ct);

        PersonRow rehired = await h.Service.RehireAsync(
            row.Id,
            new RehirePersonPayload(new DateOnly(2026, 7, 1), 2, 10, (int)EmployStatus.Regular),
            Ct);

        Assert.Equal(row.Id, rehired.Id);
        Assert.Equal((int)EmployStatus.Regular, rehired.EmployStatus);
        Assert.Null(rehired.LeaveDate);
        Assert.Equal(new DateOnly(2026, 7, 1), rehired.EntryDate);
        Assert.Equal(1, await PositionCountAsync(h, 10));

        PersonDetailRow detail = await h.Service.GetAsync(row.Id, Ct);
        Assert.NotNull(detail.CurrentAssignment);
        Assert.Equal(2, detail.Assignments.Count);
        Assert.Single(detail.Assignments, a => a.IsCurrent);
        Assert.Single(detail.Assignments, a => !a.IsCurrent && a.EndDate != null);
    }

    [Fact]
    public async Task OverstaffedPositionIsRejectedWithPositionOverstaff()
    {
        Harness h = Build();
        SeedOrgAndPosition(h, headcount: 1);
        _ = await CreateEmployeeAsync(h);

        ConflictException ex = await Assert.ThrowsAsync<ConflictException>(
            () => CreateEmployeeAsync(h, name: "第二人", idNo: "510100199303030033", mobile: "13800000004"));

        Assert.Equal("POSITION_OVERSTAFF", ex.ErrorCode);
    }

    // ============ 明文端点与审计 ============

    [Fact]
    public async Task PlaintextEndpointReturnsDecryptedValuesAndAuditsWithoutPlaintext()
    {
        Harness h = Build();
        SeedOrgAndPosition(h);
        PersonRow row = await CreateEmployeeAsync(h);

        PersonPlaintextRow plaintext = await h.Service.GetPlaintextAsync(row.Id, Ct);

        Assert.Equal(IdNo, plaintext.IdNo);
        Assert.Equal(Mobile, plaintext.Mobile);
        Assert.Equal(BankCardNo, plaintext.BankCardNo);

        Qlx.Abstractions.Audit.AuditLogEntry audit = h.Audit.GetCapturedChanges()
            .Last(e => e.Operation == "VIEW_PLAINTEXT");
        Assert.Equal(nameof(Person), audit.EntityType);
        Assert.Equal(row.Id, audit.EntityId);
        Assert.NotNull(audit.NewValues);
        Assert.Contains("idNo", audit.NewValues!, StringComparison.Ordinal);
        Assert.DoesNotContain(IdNo, audit.NewValues!, StringComparison.Ordinal);
        Assert.DoesNotContain(Mobile, audit.NewValues!, StringComparison.Ordinal);
    }

    [Fact]
    public void PlaintextEndpointIsBoundToFieldLevelPermission()
    {
        EndpointPolicyRule rule = new BasEndpointPolicyMap().GetRules()
            .Single(r => string.Equals(r.Controller, "Person", StringComparison.Ordinal)
                && string.Equals(r.Action, "GetPlaintext", StringComparison.Ordinal));

        Assert.Equal("bas.person.field.plaintext", rule.PermissionCode);
    }

    // ============ 组织引用保护（Task 3.4） ============

    [Fact]
    public async Task OrgDisableIsBlockedWhenActivePersonsExist()
    {
        Harness h = Build();
        SeedOrgAndPosition(h);
        _ = await CreateEmployeeAsync(h);

        ConflictException ex = await Assert.ThrowsAsync<ConflictException>(
            () => h.OrgService.ToggleAsync(2, Ct));

        Assert.Equal("ORG_HAS_ACTIVE_PERSONS", ex.ErrorCode);
    }

    // ============ 证件档案子表 ============

    [Fact]
    public async Task PersonCertCrudPersistsAndAudits()
    {
        Harness h = Build();
        SeedOrgAndPosition(h);
        PersonRow row = await CreateEmployeeAsync(h);

        PersonCertRow created = await h.Service.SaveCertAsync(
            row.Id,
            new SavePersonCertPayload("特种作业", "CERT-001", new DateOnly(2025, 1, 1), new DateOnly(2028, 1, 1), null),
            Ct);

        PersonCertRow updated = await h.Service.UpdateCertAsync(
            row.Id,
            created.Id,
            new SavePersonCertPayload("特种作业", "CERT-002", new DateOnly(2025, 1, 1), new DateOnly(2029, 1, 1), "复审"),
            Ct);
        Assert.Equal("CERT-002", updated.CertNo);

        IReadOnlyList<PersonCertRow> certs = await h.Service.ListCertsAsync(row.Id, Ct);
        Assert.Single(certs);

        await h.Service.DeleteCertAsync(row.Id, created.Id, Ct);
        Assert.Empty(await h.Service.ListCertsAsync(row.Id, Ct));

        Assert.Contains(
            h.Audit.GetCapturedChanges(),
            e => e.EntityType == nameof(PersonCert) && e.Operation == "DELETE");
    }

    // ============ 跨模块查询缓存与失效（C-2 / C-5） ============

    [Fact]
    public async Task PersonQueryServiceCachesAndInvalidatesOnWrite()
    {
        Harness h = Build();
        SeedOrgAndPosition(h);
        PersonRow row = await CreateEmployeeAsync(h);

        PersonQueryRow? first = await h.Query.FindAsync(row.Id, Ct);
        Assert.NotNull(first);
        Assert.Equal("张三", first!.Name);

        // 绕过服务直接改库：读缓存命中，仍返回旧值
        Person tracked = await h.Db.Persons.SingleAsync(p => p.Id == row.Id, Ct);
        tracked.UpdateProfile("缓存旧名", PersonType.Employee, null, null);
        _ = await h.Db.SaveChangesAsync(Ct);

        PersonQueryRow? cached = await h.Query.FindAsync(row.Id, Ct);
        Assert.Equal("张三", cached!.Name);

        // 写路径按 Bas 前缀失效后回源新值
        _ = await h.Service.UpdateAsync(
            row.Id,
            new UpdatePersonPayload("张三改名", (int)PersonType.Employee, "李四", "13900000001", 2, 10, null, null),
            Ct);

        PersonQueryRow? refreshed = await h.Query.FindAsync(row.Id, Ct);
        Assert.Equal("张三改名", refreshed!.Name);

        IReadOnlyList<PersonQueryRow> byPosition = await h.Query.ListByPositionAsync(10, Ct);
        Assert.Single(byPosition);
        Assert.Equal(row.Id, byPosition[0].Id);
    }

    private static async Task<int> PositionCountAsync(Harness h, long positionId)
        => (await h.Db.Positions.AsNoTracking().SingleAsync(p => p.Id == positionId, Ct)).CurrentCount;
}
