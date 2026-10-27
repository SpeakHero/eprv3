/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-25 21:30:00 +08:00 中国标准时间 星期日
 * 描述：阶段三「供应商与客户主数据」测试——准入状态机守卫、黑名单禁采、信用额度边界相等放行、
 *       资质到期扫描预警/冻结幂等、审批回写幂等与事件发布、统一发号器 SUP-/CUS- 编码
 */

using System.Text.Json;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using Qlx.Abstractions.Approvals;
using Qlx.Abstractions.Events;
using Qlx.Abstractions.Events.Bas;
using Qlx.Abstractions.Exceptions;
using Qlx.Abstractions.Jobs;
using Qlx.Abstractions.Messages;
using Qlx.BuildingBlocks.Caching;
using Qlx.BuildingBlocks.Ids;
using Qlx.BuildingBlocks.Numbering;
using Qlx.BuildingBlocks.Numbering.Pinyin;
using Qlx.BuildingBlocks.Support.Logs;
using Qlx.BuildingBlocks.Tests.Files;
using Qlx.BuildingBlocks.Tests.Jobs;
using Qlx.BuildingBlocks.Tests.Portal.Auth;
using Qlx.Modules.Bas;
using Qlx.Modules.Bas.Domain;
using Qlx.Modules.Bas.Domain.Supplier;
using Qlx.Modules.Bas.Dtos;
using Qlx.Modules.Bas.Jobs;
using Qlx.Modules.Bas.Repositories;
using Qlx.Modules.Bas.Services;
using Qlx.Modules.Bas.Services.Numbering;

using CreditGrade = Qlx.Modules.Bas.Domain.Customer.CreditGrade;
using CustomerEntity = Qlx.Modules.Bas.Domain.Customer.Customer;
using SupplierEntity = Qlx.Modules.Bas.Domain.Supplier.Supplier;
using SupplierType = Qlx.Modules.Bas.Domain.Supplier.SupplierType;

namespace Qlx.BuildingBlocks.Tests.Bas;

/// <summary>
/// 供应商与客户主数据测试（领域状态机 + 服务规则 + 审批应用器 + 资质到期任务）。
/// </summary>
public sealed class SupplierCustomerTests
{
    private const long TenantId = 42L;
    private const string Operator = "user-9";
    private const string SupplierUscc = "91510000MA00000001";
    private const string CustomerUscc = "91510000MA00000002";

    private sealed class Harness
    {
        public required BasTenantDbContext Db { get; init; }

        public required BasTenantRepository Repo { get; init; }

        public required MemoryCacheService Cache { get; init; }

        public required InMemoryAuditService Audit { get; init; }

        public required StubSystemParameterProvider Parameters { get; init; }

        public required BasStubApprovalService Approval { get; init; }

        public required BasRecordingEventBus Events { get; init; }

        public required SupplierService SupplierService { get; init; }

        public required CustomerService CustomerService { get; init; }

        public required BasApprovalApplier Applier { get; init; }

        public required RecordingMessageService Messages { get; init; }

        public required SupplierCertExpiryJobHandler Job { get; init; }
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
        var parameters = new StubSystemParameterProvider();
        var approval = new BasStubApprovalService();
        var events = new BasRecordingEventBus();
        var tenantCtx = new TestTenantContext(TenantId);
        var userCtx = new TestCurrentUserContext(Operator);
        var snowflake = new BasStubSnowflakeIdGenerator();

        var codeGenerator = new BasCodeNumberGenerator(repo, serials);
        var orgService = new OrgMasterDataService(
            tenantCtx,
            repo,
            codeGenerator,
            approval,
            events,
            cache,
            audit,
            snowflake,
            userCtx,
            new StubTenantMemberQuery());
        var positionService = new PositionMasterDataService(
            tenantCtx,
            repo,
            new EncodingGenerator(new PinyinConverter(), serials),
            approval,
            cache,
            audit,
            snowflake,
            userCtx);
        var categoryService = new ItemCategoryService(
            tenantCtx,
            repo,
            codeGenerator,
            approval,
            parameters,
            cache,
            audit,
            snowflake,
            userCtx);

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
            parameters,
            cache,
            audit,
            snowflake,
            userCtx);
        var applier = new BasApprovalApplier(
            tenantCtx,
            repo,
            orgService,
            positionService,
            categoryService,
            supplierService,
            customerService,
            events,
            cache);

        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                [SupplierCertExpiryJobHandler.BatchSizeConfigKey] = "1",
            })
            .Build();
        var messages = new RecordingMessageService();
        var job = new SupplierCertExpiryJobHandler(
            tenantCtx,
            repo,
            parameters,
            configuration,
            messages,
            cache,
            audit,
            snowflake,
            userCtx,
            NullLogger<SupplierCertExpiryJobHandler>.Instance);

        return new Harness
        {
            Db = db,
            Repo = repo,
            Cache = cache,
            Audit = audit,
            Parameters = parameters,
            Approval = approval,
            Events = events,
            SupplierService = supplierService,
            CustomerService = customerService,
            Applier = applier,
            Messages = messages,
            Job = job,
        };
    }

    private static Task<SupplierRow> CreateSupplierAsync(
        Harness h, string name, string uscc = SupplierUscc, int type = (int)SupplierType.Dealer)
        => h.SupplierService.CreateAsync(
            new SaveSupplierPayload(name, uscc, type, Contact: "张三", Phone: "13800000000"),
            Ct);

    private static Task<CustomerRow> CreateCustomerAsync(Harness h, string name, string uscc = CustomerUscc)
        => h.CustomerService.CreateAsync(
            new SaveCustomerPayload(name, uscc, CustomerType: 2, Contact: null, Phone: null, PaymentTerms: "月结 30 天"),
            Ct);

    private static JobContext BuildJobContext(SupplierCertExpiryJobHandler job) => new()
    {
        ExecutionId = Guid.NewGuid(),
        TaskCode = job.TaskCode,
        TenantId = GuidIdMapper.ToGuid(TenantId),
        Parameters = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["initiateUserId"] = Operator,
        },
    };

    private static JsonElement ResultOf(CompletedResult result)
        => JsonSerializer.SerializeToElement(result.ResultJson, result.ResultJson!.GetType());

    // ============ 准入状态机 ============

    [Fact]
    public void SupplierStateMachineRejectsIllegalTransitions()
    {
        var supplier = new SupplierEntity(
            TenantId, "SUP-T-000001", "状态机供应商", SupplierUscc, SupplierType.Dealer);

        Assert.Equal(PartnerStatus.Pending, supplier.Status);

        supplier.SubmitAdmission();
        Assert.Equal(PartnerStatus.Pending, supplier.Status);

        supplier.Approve();
        Assert.Equal(PartnerStatus.Active, supplier.Status);

        supplier.Freeze();
        Assert.Equal(PartnerStatus.Frozen, supplier.Status);

        supplier.Approve();
        Assert.Equal(PartnerStatus.Active, supplier.Status);

        supplier.Close();
        Assert.Equal(PartnerStatus.Closed, supplier.Status);

        ConflictException approve = Assert.Throws<ConflictException>(() => supplier.Approve());
        Assert.Equal("SUPPLIER_STATE_INVALID", approve.ErrorCode);
        _ = Assert.Throws<ConflictException>(() => supplier.Freeze());
        _ = Assert.Throws<ConflictException>(() => supplier.Close());
    }

    [Fact]
    public async Task SupplierAdmissionRequiresApprovalAndRejectsDuplicateRequest()
    {
        Harness h = Build();

        SupplierRow created = await CreateSupplierAsync(h, "成都原料供应商");
        Assert.Equal((int)PartnerStatus.Pending, created.Status);
        Assert.StartsWith("SUP-", created.Code);

        ConflictException notActive = await Assert.ThrowsAsync<ConflictException>(
            () => h.SupplierService.EnsurePurchasableAsync(created.Id, Ct));
        Assert.Equal("SUPPLIER_NOT_ACTIVE", notActive.ErrorCode);

        ApprovalRequestRow request = await h.SupplierService.SubmitAdmissionRequestAsync(
            created.Id, "首次合作", Ct);
        Assert.Equal("bas.supplier.admission", request.BizType);
        Assert.False(string.IsNullOrWhiteSpace(request.ApprovalInstanceId));
        Assert.Single(h.Approval.Started);

        await h.SupplierService.ApplyAdmissionAsync(
            new SupplierAdmissionPayload(created.Id, "首次合作"), Ct);
        await h.SupplierService.EnsurePurchasableAsync(created.Id, Ct);

        ConflictException duplicated = await Assert.ThrowsAsync<ConflictException>(
            () => h.SupplierService.SubmitAdmissionRequestAsync(created.Id, null, Ct));
        Assert.Equal("SUPPLIER_STATE_INVALID", duplicated.ErrorCode);
    }

    // ============ 黑名单禁采 ============

    [Fact]
    public async Task BlacklistedSupplierIsBlockedForPurchasingAndGradeLogRecorded()
    {
        Harness h = Build();
        SupplierRow created = await CreateSupplierAsync(h, "黑名单供应商");
        await h.SupplierService.ApplyAdmissionAsync(
            new SupplierAdmissionPayload(created.Id, null), Ct);

        SupplierRow blacklisted = await h.SupplierService.ChangeGradeAsync(
            created.Id, new ChangeSupplierGradePayload(4, "质检不合格"), Ct);
        Assert.Equal(4, blacklisted.Grade);

        ConflictException blocked = await Assert.ThrowsAsync<ConflictException>(
            () => h.SupplierService.EnsurePurchasableAsync(created.Id, Ct));
        Assert.Equal("SUPPLIER_BLACKLISTED", blocked.ErrorCode);

        SupplierDetailRow detail = await h.SupplierService.GetAsync(created.Id, Ct);
        SupplierGradeLogRow log = Assert.Single(detail.GradeLogs);
        Assert.Equal(3, log.OldGrade);
        Assert.Equal(4, log.NewGrade);
        Assert.Equal("质检不合格", log.Reason);

        Assert.Contains(
            h.Audit.GetCapturedChanges(),
            e => e.EntityType == typeof(SupplierEntity).Name && e.Operation == "UPDATE");
    }

    // ============ 信用额度校验 ============

    [Fact]
    public async Task CustomerCreditCheckAllowsBoundaryEqualityAndRejectsOverflow()
    {
        Harness h = Build();
        CustomerRow created = await CreateCustomerAsync(h, "重庆经销商");
        Assert.StartsWith("CUS-", created.Code);

        _ = await h.CustomerService.ApplyCreditAdjustAsync(
            new CustomerCreditAdjustPayload(created.Id, 1000m, (int)CreditGrade.A, "初始授信"), Ct);

        await h.CustomerService.EnsureCreditAvailableAsync(created.Id, 1000m, Ct);

        ConflictException overflow = await Assert.ThrowsAsync<ConflictException>(
            () => h.CustomerService.EnsureCreditAvailableAsync(created.Id, 1000.01m, Ct));
        Assert.Equal("CREDIT_EXCEEDED", overflow.ErrorCode);

        // 已占用额度参与校验：500 + 500 = 1000 放行，超出部分拒绝
        CustomerEntity tracked = (await h.Repo.FindCustomerAsync(created.Id, Ct))!;
        typeof(CustomerEntity)
            .GetProperty(nameof(CustomerEntity.CreditUsed))!
            .SetValue(tracked, 500m);
        _ = await h.Repo.SaveChangesAsync(Ct);

        await h.CustomerService.EnsureCreditAvailableAsync(created.Id, 500m, Ct);
        _ = await Assert.ThrowsAsync<ConflictException>(
            () => h.CustomerService.EnsureCreditAvailableAsync(created.Id, 500.01m, Ct));

        CustomerCreditViewRow view = await h.CustomerService.GetCreditViewAsync(created.Id, Ct);
        Assert.Equal(1000m, view.CreditLimit);
        Assert.Equal(500m, view.CreditUsed);
        Assert.Equal(500m, view.Available);
    }

    // ============ 资质到期任务 ============

    [Fact]
    public async Task SupplierCertExpiryJobWarnsOnceAndFreezesExpiredSupplier()
    {
        Harness h = Build();
        DateOnly today = DateOnly.FromDateTime(DateTime.UtcNow);

        SupplierRow warning = await CreateSupplierAsync(h, "预警供应商", "91510000MA00000011");
        SupplierRow expired = await CreateSupplierAsync(h, "过期供应商", "91510000MA00000012");
        await h.SupplierService.ApplyAdmissionAsync(
            new SupplierAdmissionPayload(warning.Id, null), Ct);
        await h.SupplierService.ApplyAdmissionAsync(
            new SupplierAdmissionPayload(expired.Id, null), Ct);

        _ = await h.SupplierService.SaveCertAsync(
            warning.Id,
            new SaveSupplierCertPayload("营业执照", "CERT-W", null, null, today.AddDays(10), null),
            Ct);
        _ = await h.SupplierService.SaveCertAsync(
            expired.Id,
            new SaveSupplierCertPayload("生产许可证", "CERT-E", null, null, today.AddDays(-2), null),
            Ct);

        JobContext context = BuildJobContext(h.Job);

        h.Db.ChangeTracker.Clear();
        CompletedResult first = await h.Job.ExecuteAsync(context, Ct);
        Assert.True(first.IsSuccess, first.ErrorMessage);
        JsonElement firstResult = ResultOf(first);
        Assert.Equal(2, firstResult.GetProperty("Scanned").GetInt32());
        Assert.Equal(1, firstResult.GetProperty("Warned").GetInt32());
        Assert.Equal(1, firstResult.GetProperty("Frozen").GetInt32());

        PushRequest push = Assert.Single(h.Messages.Pushes);
        Assert.Equal(Operator, push.RecipientUserId);
        Assert.Equal("供应商资质到期预警", push.Subject);
        Assert.Contains("预警供应商", push.Content);

        h.Db.ChangeTracker.Clear();
        CompletedResult second = await h.Job.ExecuteAsync(context, Ct);
        Assert.True(second.IsSuccess, second.ErrorMessage);
        JsonElement secondResult = ResultOf(second);
        Assert.Equal(0, secondResult.GetProperty("Warned").GetInt32());
        Assert.Equal(0, secondResult.GetProperty("Frozen").GetInt32());
        Assert.Single(h.Messages.Pushes);

        h.Db.ChangeTracker.Clear();
        SupplierEntity warnedSupplier = (await h.Repo.FindSupplierAsync(warning.Id, Ct))!;
        SupplierEntity expiredSupplier = (await h.Repo.FindSupplierAsync(expired.Id, Ct))!;
        Assert.Equal(PartnerStatus.Active, warnedSupplier.Status);
        Assert.Equal(PartnerStatus.Frozen, expiredSupplier.Status);

        IReadOnlyList<SupplierCertRow> expiredCerts = await h.SupplierService.ListCertsAsync(expired.Id, Ct);
        Assert.Equal((int)CertificateStatus.Expired, Assert.Single(expiredCerts).Status);
    }

    // ============ 审批回写幂等 ============

    [Fact]
    public async Task SupplierAdmissionApprovalApplierIsIdempotentAndPublishesEventOnce()
    {
        Harness h = Build();
        SupplierRow created = await CreateSupplierAsync(h, "审批供应商");
        ApprovalRequestRow request = await h.SupplierService.SubmitAdmissionRequestAsync(
            created.Id, "准入审批", Ct);

        ApprovalCompletedEvent completed = new(
            request.ApprovalInstanceId!,
            "bas.supplier.admission",
            "bas.supplier.admission",
            request.BizId.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ApprovalStatus.Approved,
            Operator,
            DateTimeOffset.UtcNow);

        await h.Applier.ApplyAsync(completed, Ct);

        SupplierEntity approved = (await h.Repo.FindSupplierAsync(created.Id, Ct))!;
        Assert.Equal(PartnerStatus.Active, approved.Status);

        IntegrationEvent durable = Assert.Single(h.Events.DurableEvents);
        SupplierApprovedEvent approvedEvent = Assert.IsType<SupplierApprovedEvent>(durable);
        Assert.Equal(created.Id, approvedEvent.SupplierId);
        Assert.Equal(created.Code, approvedEvent.SupplierCode);

        await h.Applier.ApplyAsync(completed, Ct);
        Assert.Single(h.Events.DurableEvents);
        SupplierEntity afterReplay = (await h.Repo.FindSupplierAsync(created.Id, Ct))!;
        Assert.Equal(PartnerStatus.Active, afterReplay.Status);
    }

    [Fact]
    public async Task CustomerCreditAdjustApprovalWritesLogAndPublishesEventOnce()
    {
        Harness h = Build();
        CustomerRow created = await CreateCustomerAsync(h, "额度审批客户");

        ApprovalRequestRow request = await h.CustomerService.SubmitCreditAdjustRequestAsync(
            created.Id,
            new CustomerCreditAdjustPayload(created.Id, 5000m, (int)CreditGrade.B, "提升授信"),
            Ct);
        Assert.Equal("bas.customer.credit.adjust", request.BizType);

        ApprovalCompletedEvent completed = new(
            request.ApprovalInstanceId!,
            "bas.customer.credit.adjust",
            "bas.customer.credit.adjust",
            request.BizId.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ApprovalStatus.Approved,
            Operator,
            DateTimeOffset.UtcNow);

        await h.Applier.ApplyAsync(completed, Ct);

        CustomerEntity customer = (await h.Repo.FindCustomerAsync(created.Id, Ct))!;
        Assert.Equal(5000m, customer.CreditLimit);
        Assert.Equal(CreditGrade.B, customer.CreditGrade);

        IReadOnlyList<Qlx.Modules.Bas.Domain.Customer.CustomerCreditLog> creditLogs = await h.Repo
            .ListCustomerCreditLogsAsync(created.Id, Ct);
        Qlx.Modules.Bas.Domain.Customer.CustomerCreditLog creditLog = Assert.Single(creditLogs);
        Assert.Equal(0m, creditLog.OldLimit);
        Assert.Equal(5000m, creditLog.NewLimit);

        CreditLineAdjustedEvent adjusted = Assert.IsType<CreditLineAdjustedEvent>(
            Assert.Single(h.Events.DurableEvents));
        Assert.Equal(created.Id, adjusted.CustomerId);
        Assert.Equal(5000m, adjusted.NewLimit);

        await h.Applier.ApplyAsync(completed, Ct);
        Assert.Single(h.Events.DurableEvents);
        CustomerDetailRow detail = await h.CustomerService.GetAsync(created.Id, Ct);
        Assert.Single(detail.CreditLogs);
    }

    // ============ 统一发号器编码 ============

    [Fact]
    public async Task SupplierAndCustomerCodesFollowUnifiedNumbering()
    {
        Harness h = Build();

        SupplierRow firstSupplier = await CreateSupplierAsync(h, "供应商甲", "91510000MA00000021");
        SupplierRow secondSupplier = await CreateSupplierAsync(h, "供应商乙", "91510000MA00000022");
        Assert.Equal("SUP-000001", firstSupplier.Code);
        Assert.Equal("SUP-000002", secondSupplier.Code);

        CustomerRow firstCustomer = await CreateCustomerAsync(h, "客户甲", "91510000MA00000023");
        CustomerRow secondCustomer = await CreateCustomerAsync(h, "客户乙", "91510000MA00000024");
        Assert.Equal("CUS-000001", firstCustomer.Code);
        Assert.Equal("CUS-000002", secondCustomer.Code);
    }
}
