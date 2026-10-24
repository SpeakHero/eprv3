/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-21 20:30:00 +08:00 中国标准时间 星期三
 * 修改时间：2026-10-21 20:30:00 +08:00 中国标准时间 星期三
 * 描述：门户管理端服务测试：租户新建/启停/授权保存 + 用户启停/重置密码 + 令牌透传
 */

using System.Collections.Immutable;

using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using Qlx.Abstractions.Audit;
using Qlx.Abstractions.Exceptions;
using Qlx.Abstractions.Tenancy;
using Qlx.BuildingBlocks.Api;
using Qlx.BuildingBlocks.Caching;
using Qlx.BuildingBlocks.Ids;
using Qlx.BuildingBlocks.MultiTenancy;
using Qlx.BuildingBlocks.Numbering;
using Qlx.BuildingBlocks.Numbering.Pinyin;
using Qlx.Modules.Portal.Domain.Identity;
using Qlx.Modules.Portal.Domain.Tenancy;
using Qlx.Modules.Portal.Dtos;
using Qlx.Modules.Portal.Repositories;
using Qlx.Modules.Portal.Services;

namespace Qlx.BuildingBlocks.Tests.Portal;

/// <summary>桩雪花 Id 生成器（单调递增，便于断言）。</summary>
internal sealed class StubSnowflakeIdGenerator : ISnowflakeIdGenerator
{
    private long _current = 710000000000000900;

    public long NextId()
    {
        return Interlocked.Increment(ref _current);
    }
}

/// <summary>桩 Id 混淆器（编码为「pub-{id}」，解码还原）。</summary>
internal sealed class StubIdObfuscator : IIdObfuscator
{
    public string Encode(long id)
    {
        return $"pub-{id}";
    }

    public bool TryDecode(string? obfuscated, out long id)
    {
        id = 0;
        return obfuscated is not null
            && obfuscated.StartsWith("pub-", StringComparison.Ordinal)
            && long.TryParse(obfuscated.AsSpan(4), out id);
    }
}

/// <summary>桩租户数据库路由：记录预热调用次数。</summary>
internal sealed class StubTenantDatabaseResolver : ITenantDatabaseResolver
{
    public int PreloadCount { get; private set; }

    public TenantConnectionInfo Resolve(long tenantId)
    {
        throw new NotSupportedException();
    }

    public Task PreloadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        PreloadCount++;
        return Task.CompletedTask;
    }

    public void Invalidate(long tenantId) { }
}

/// <summary>桩审计服务：记录操作日志与调用令牌。</summary>
internal sealed class StubAuditService : IAuditService
{
    public List<OperationLogEntry> Operations { get; } = [];

    public List<CancellationToken> Tokens { get; } = [];

    public List<OperationLogEntry> LoginOperations { get; } = [];

    public Task CaptureChangeAsync(AuditLogEntry entry, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public Task CaptureOperationAsync(OperationLogEntry entry, CancellationToken cancellationToken)
    {
        Tokens.Add(cancellationToken);
        Operations.Add(entry);
        if (entry.Operation is "LOGIN_SUCCESS" or "LOGIN_FAILED")
        {
            LoginOperations.Add(entry);
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<AuditLogEntry>> QueryChangesAsync(
        long tenantId, CancellationToken cancellationToken,
        string? entityType = null, long? entityId = null, string? changedBy = null,
        DateTimeOffset? from = null, DateTimeOffset? toTime = null)
    {
        return Task.FromResult<IReadOnlyList<AuditLogEntry>>([]);
    }

    public Task<IReadOnlyList<OperationLogEntry>> QueryOperationsAsync(
        long tenantId, CancellationToken cancellationToken,
        string? moduleCode = null, string? operation = null, string? operatedBy = null,
        DateTimeOffset? from = null, DateTimeOffset? toTime = null)
    {
        return Task.FromResult<IReadOnlyList<OperationLogEntry>>([]);
    }

    public Task<IReadOnlyList<OperationLogEntry>> QueryOperationsByOperatorAsync(
        string operatedBy, CancellationToken cancellationToken, string? moduleCode = null, int take = 100)
    {
        return Task.FromResult<IReadOnlyList<OperationLogEntry>>(
                [.. LoginOperations.Where(o => o.OperatedBy == operatedBy).Take(take)]);
    }
}

/// <summary>桩租户仓储。</summary>
internal sealed class StubTenantAdminRepository : IPortalTenantAdminRepository
{
    public List<Tenant> Tenants { get; } = [];

    public List<TenantDatabase> Databases { get; } = [];

    public List<TenantModule> Modules { get; } = [];

    public List<TenantMember> Members { get; } = [];

    public List<LicenseRecord> Records { get; } = [];

    public List<CancellationToken> Tokens { get; } = [];

    public Task<PagedResult<Tenant>> QueryAsync(
        string? keyword, string? statusFilter, string? databaseModeFilter, PagedRequest request, CancellationToken cancellationToken)
    {
        Tokens.Add(cancellationToken);
        return Task.FromResult(new PagedResult<Tenant>(Tenants, Tenants.Count, request.Page, request.SafePageSize));
    }

    public Task<Tenant?> FindTrackedAsync(long tenantId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Tokens.Add(cancellationToken);
        return Task.FromResult(Tenants.Find(t => t.Id == tenantId));
    }

    public Task<Tenant?> FindWithDetailsAsync(long tenantId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Tokens.Add(cancellationToken);
        return Task.FromResult(Tenants.Find(t => t.Id == tenantId));
    }

    public Task<Tenant?> FindTrackedWithModulesAsync(long tenantId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Tokens.Add(cancellationToken);
        return Task.FromResult(Tenants.Find(t => t.Id == tenantId));
    }

    public Task<bool> CodeExistsAsync(string tenantCode, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Tokens.Add(cancellationToken);
        return Task.FromResult(Tenants.Exists(t => t.TenantCode == tenantCode));
    }

    public Task<bool> TaxNoExistsAsync(string taxNo, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Tokens.Add(cancellationToken);
        return Task.FromResult(Tenants.Exists(t => t.TaxNo == taxNo));
    }

    public Task<IReadOnlyDictionary<long, DateTimeOffset?>> GetLastLoginTimesAsync(
        IReadOnlyCollection<long> tenantIds, CancellationToken cancellationToken)
    {
        Tokens.Add(cancellationToken);
        return Task.FromResult<IReadOnlyDictionary<long, DateTimeOffset?>>(new Dictionary<long, DateTimeOffset?>());
    }

    public Task<IReadOnlyList<LicenseRecord>> QueryLicenseRecordsAsync(
        long? tenantId, int take, CancellationToken cancellationToken)
    {
        Tokens.Add(cancellationToken);
        return Task.FromResult<IReadOnlyList<LicenseRecord>>(Records);
    }

    public Task<IReadOnlyList<TenantMemberRow>> ListMembersAsync(long tenantId, CancellationToken cancellationToken)
    {
        Tokens.Add(cancellationToken);
        return Task.FromResult<IReadOnlyList<TenantMemberRow>>([]);
    }

    public Task AddAsync(Tenant tenant, CancellationToken cancellationToken)
    {
        Tokens.Add(cancellationToken);
        Tenants.Add(tenant);
        return Task.CompletedTask;
    }

    public Task AddDatabaseAsync(TenantDatabase database, CancellationToken cancellationToken)
    {
        Tokens.Add(cancellationToken);
        Databases.Add(database);
        return Task.CompletedTask;
    }

    public Task AddModulesAsync(IReadOnlyCollection<TenantModule> modules, CancellationToken cancellationToken)
    {
        Tokens.Add(cancellationToken);
        Modules.AddRange(modules);
        return Task.CompletedTask;
    }

    public Task AddLicenseRecordsAsync(IReadOnlyCollection<LicenseRecord> records, CancellationToken cancellationToken)
    {
        Tokens.Add(cancellationToken);
        Records.AddRange(records);
        return Task.CompletedTask;
    }

    public Task AddMemberAsync(TenantMember member, CancellationToken cancellationToken)
    {
        Tokens.Add(cancellationToken);
        Members.Add(member);
        return Task.CompletedTask;
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        Tokens.Add(cancellationToken);
        return Task.CompletedTask;
    }
}

/// <summary>桩门户用户仓储。</summary>
internal sealed class StubPortalUserAdminRepository : IPortalUserAdminRepository
{
    public List<PortalUser> Users { get; } = [];

    public List<CancellationToken> Tokens { get; } = [];

    public Task<PagedResult<PortalUser>> QueryAsync(
        string? keyword, string? statusFilter, PagedRequest request, CancellationToken cancellationToken)
    {
        Tokens.Add(cancellationToken);
        return Task.FromResult(new PagedResult<PortalUser>(Users, Users.Count, request.Page, request.SafePageSize));
    }

    public Task<PortalUser?> FindTrackedAsync(long userId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Tokens.Add(cancellationToken);
        return Task.FromResult(Users.Find(u => u.Id == userId));
    }

    public Task<PortalUser?> FindTrackedByAccountAsync(string loginAccount, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Tokens.Add(cancellationToken);
        return Task.FromResult(Users.Find(u => u.LoginAccount == loginAccount));
    }

    public Task<IReadOnlyList<UserMembershipRow>> ListMembershipsAsync(
        long userId, CancellationToken cancellationToken)
    {
        Tokens.Add(cancellationToken);
        return Task.FromResult<IReadOnlyList<UserMembershipRow>>([]);
    }

    public Task<IReadOnlyList<PortalUserPasswordHistory>> GetRecentPasswordHistoriesAsync(
        long userId, int take, CancellationToken cancellationToken)
    {
        Tokens.Add(cancellationToken);
        return Task.FromResult<IReadOnlyList<PortalUserPasswordHistory>>([]);
    }

    public Task AddPasswordHistoryAsync(PortalUserPasswordHistory history, CancellationToken cancellationToken)
    {
        Tokens.Add(cancellationToken);
        return Task.CompletedTask;
    }

    public Task TrimPasswordHistoriesAsync(long userId, int keepLatest, CancellationToken cancellationToken)
    {
        Tokens.Add(cancellationToken);
        return Task.CompletedTask;
    }

    public Task AddAsync(PortalUser user, CancellationToken cancellationToken)
    {
        Tokens.Add(cancellationToken);
        Users.Add(user);
        return Task.CompletedTask;
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        Tokens.Add(cancellationToken);
        return Task.CompletedTask;
    }
}

/// <summary>桩模块目录仓储。</summary>
internal sealed class StubPortalModuleAdminRepository : IPortalModuleAdminRepository
{
    public List<ModuleDefinitionRow> Definitions { get; } =
    [
        new("bas", "基础信息", "1.0.0", true, true),
        new("stk", "商品管理", "1.0.0", true, false),
        new("sal", "销售管理", "1.1.0", true, false),
        new("portal", "门户管理", "1.0.0", false, true),
    ];

    public List<CancellationToken> Tokens { get; } = [];

    public Task<IReadOnlyList<ModuleCatalogRow>> ListCatalogAsync(CancellationToken cancellationToken)
    {
        Tokens.Add(cancellationToken);
        List<ModuleCatalogRow> rows = [.. Definitions.Select(d => new ModuleCatalogRow(d.ModuleCode, d.DisplayName, d.CurrentVersion, null, 1, []))];
        return Task.FromResult<IReadOnlyList<ModuleCatalogRow>>(rows);
    }

    public Task<IReadOnlyList<ModuleDefinitionRow>> ListDefinitionsAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Tokens.Add(cancellationToken);
        return Task.FromResult<IReadOnlyList<ModuleDefinitionRow>>(Definitions);
    }

    public Task<IReadOnlyList<ModuleDefinition>> ListByCodesAsync(
        IReadOnlyCollection<string> moduleCodes, CancellationToken cancellationToken)
    {
        return Task.FromResult<IReadOnlyList<ModuleDefinition>>([]);
    }

    public Task<PortalWorkbenchCounters> GetWorkbenchCountersAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult(new PortalWorkbenchCounters(2, 1, 1, 0, 3, 1, 5));
    }
}

/// <summary>
/// 门户管理端服务测试。令牌透传断言：桩仓储记录每个方法收到的令牌，断言与 cts.Token 为同一实例。
/// </summary>
public sealed class PortalAdminServiceTests
{
    private static readonly PortalProvisioningOptions ProvisioningOptions = new()
    {
        DbHost = "(localdb)",
        DbPort = 1433,
        DbNamePrefix = "qlx_tenant_",
        TrialDays = 14,
        DefaultModuleCodes = ["bas", "portal"],
    };

    private static readonly SharedDatabaseOptions SharedDatabase = new()
    {
        DbHost = "(localdb)",
        DbPort = 1433,
        DbName = "qlx_tenant_shared",
    };

    private sealed record Harness(
        PortalTenantAdminService TenantService,
        PortalUserAdminService UserService,
        StubTenantAdminRepository TenantRepo,
        StubPortalUserAdminRepository UserRepo,
        StubPortalModuleAdminRepository ModuleRepo,
        StubAuditService Audit,
        StubTenantDatabaseResolver Resolver,
        StubIdObfuscator Obfuscator,
        MemoryCacheService Cache);

    private static Harness CreateHarness()
    {
        StubTenantAdminRepository tenantRepo = new();
        StubPortalUserAdminRepository userRepo = new();
        StubPortalModuleAdminRepository moduleRepo = new();
        StubAuditService audit = new();
        StubTenantDatabaseResolver resolver = new();
        StubIdObfuscator obfuscator = new();
        StubSnowflakeIdGenerator ids = new();
        MemoryCacheService cache = new(Options.Create(new CacheOptions()));
        IEncodingGenerator encodingGenerator = new EncodingGenerator(new PinyinConverter(), new InMemorySerialSequenceStore());

        PortalTenantAdminService tenantService = new(
            tenantRepo, userRepo, moduleRepo, ids, obfuscator,
            new StubCurrentUserContext("qlxadmin"), resolver, audit,
            cache, encodingGenerator,
            Options.Create(ProvisioningOptions),
            Options.Create(SharedDatabase),
            NullLogger<PortalTenantAdminService>.Instance);

        PortalUserAdminService userService = new(
            userRepo, audit, new PasswordHasher<PortalUser>(),
            new MemoryCacheService(Options.Create(new CacheOptions())),
            ids, obfuscator, new StubCurrentUserContext("qlxadmin"),
            Options.Create(new Qlx.Modules.Portal.Auth.QlxAuthOptions()),
            NullLogger<PortalUserAdminService>.Instance);

        return new Harness(tenantService, userService, tenantRepo, userRepo, moduleRepo, audit, resolver, obfuscator, cache);
    }

    /// <summary>构造带 Id 的租户（Active / Provisioning 可选）。</summary>
    private static Tenant CreateTenant(long id, string code = "demo-ten", TenantStatus status = TenantStatus.Provisioning)
    {
        Tenant tenant = new(code, "演示租户");
        tenant.AssignId(id);
        tenant.SetRegistrationProfile("91510100MA88888888", "standard", null);
        if (status == TenantStatus.Active)
        {
            tenant.Activate();
        }
        else if (status == TenantStatus.Disabled)
        {
            tenant.Disable();
        }

        return tenant;
    }

    [Fact]
    public async Task CreateTenantRegistersDatabaseModulesAndAdminMembership()
    {
        Harness h = CreateHarness();
        using CancellationTokenSource cts = new();

        PortalTenantItem item = await h.TenantService.CreateAsync(new CreateTenantRequest
        {
            Name = "成都测试科技有限公司",
            TaxNo = "91510100MA77777777",
            MemberLevel = "Standard",
            AdminAccount = "13900000001",
        }, cts.Token);

        // 未指定模式 → 配置默认 Shared：不建库、直接激活、路由行指向租户共享库
        Assert.Equal("Enabled", item.Status);
        Assert.Equal("Shared", item.DatabaseMode);
        // 编码由系统自动生成：租户名完整拼音（小写、无分隔符）
        Assert.Equal("chengduceshikejiyouxiangongsi", item.Code);
        Assert.Equal("Standard", item.MemberLevel);

        Tenant sharedTenant = Assert.Single(h.TenantRepo.Tenants);
        Assert.Equal(TenantStatus.Active, sharedTenant.Status);
        Assert.Equal(TenantDatabaseMode.Shared, sharedTenant.DatabaseMode);

        TenantDatabase database = Assert.Single(h.TenantRepo.Databases);
        Assert.Equal("qlx_tenant_shared", database.DbName);
        Assert.Equal(TenantDatabaseMode.Shared, database.DatabaseMode);
        Assert.Equal("default", database.PoolCode);
        Assert.Empty(database.EncryptedDbUser);
        Assert.Empty(database.EncryptedDbPassword);

        // 默认授权 bas + portal（必备模块自动补齐且去重）
        Assert.Equal(2, h.TenantRepo.Modules.Count);
        Assert.Contains(h.TenantRepo.Modules, m => m.ModuleCode == "bas");
        Assert.Contains(h.TenantRepo.Modules, m => m.ModuleCode == "portal");

        // 初始管理员不存在 → 自动创建 PortalUser 并建立 TenantAdmin 成员关系
        PortalUser admin = Assert.Single(h.UserRepo.Users);
        Assert.Equal("13900000001", admin.LoginAccount);
        TenantMember member = Assert.Single(h.TenantRepo.Members);
        Assert.Equal(TenantMemberRole.TenantAdmin, member.Role);
        Assert.Equal(admin.Id, member.PortalUserId);

        // 授权记录按模块落库
        Assert.Equal(2, h.TenantRepo.Records.Count);
        Assert.All(h.TenantRepo.Records, r => Assert.Equal(LicenseAction.Granted, r.Action));

        // 新建后预热路由缓存，后台迁移执行器才能解析连接串
        Assert.Equal(1, h.Resolver.PreloadCount);

        Assert.Contains(h.Audit.Operations, o => o.Operation == "CREATE_TENANT");
        Assert.All(h.TenantRepo.Tokens, token => Assert.Equal(cts.Token, token));
    }

    [Fact]
    public async Task CreateIsolatedTenantKeepsProvisioningAndDedicatedDatabaseName()
    {
        Harness h = CreateHarness();
        using CancellationTokenSource cts = new();

        PortalTenantItem item = await h.TenantService.CreateAsync(new CreateTenantRequest
        {
            Name = "成都独立库科技有限公司",
            TaxNo = "91510100MA33333333",
            MemberLevel = "Professional",
            AdminAccount = "13900000009",
            DatabaseMode = "Isolated",
        }, cts.Token);

        // 独立模式保持 Provisioning + 独立库名，由后台迁移执行器建库后激活
        Assert.Equal("Pending", item.Status);
        Assert.Equal("Isolated", item.DatabaseMode);

        Tenant tenant = Assert.Single(h.TenantRepo.Tenants);
        Assert.Equal(TenantStatus.Provisioning, tenant.Status);
        Assert.Equal(TenantDatabaseMode.Isolated, tenant.DatabaseMode);

        TenantDatabase database = Assert.Single(h.TenantRepo.Databases);
        // 库名 = 前缀 + 编码（短横线换下划线）；编码由租户名完整拼音自动生成
        Assert.Equal("qlx_tenant_chengdudulikukejiyouxiangongsi", database.DbName);
        Assert.Equal(TenantDatabaseMode.Isolated, database.DatabaseMode);
    }

    [Fact]
    public async Task CreateTenantRejectsUnknownDatabaseMode()
    {
        Harness h = CreateHarness();
        using CancellationTokenSource cts = new();

        ValidationException ex = await Assert.ThrowsAsync<ValidationException>(() => h.TenantService.CreateAsync(new CreateTenantRequest
        {
            Name = "非法模式公司",
            TaxNo = "91510100MA44444444",
            MemberLevel = "Trial",
            AdminAccount = "13900000010",
            DatabaseMode = "Cluster",
        }, cts.Token));

        Assert.Contains(ex.FieldErrors, e => e.FieldName == "databaseMode");
        Assert.Empty(h.TenantRepo.Tenants);
    }

    [Fact]
    public async Task CreateTenantAutoGeneratesUniqueCodeOnConflict()
    {
        // 编码由系统自动生成；同名租户重复创建时自动追加 001/002 序号去重
        Harness h = CreateHarness();
        using CancellationTokenSource cts = new();

        var first = await h.TenantService.CreateAsync(new CreateTenantRequest
        {
            Name = "成都测试科技有限公司",
            TaxNo = "91510100MA77777777",
            MemberLevel = "Standard",
            AdminAccount = "13900000001",
        }, cts.Token);

        var second = await h.TenantService.CreateAsync(new CreateTenantRequest
        {
            Name = "成都测试科技有限公司",
            TaxNo = "91510100MA88888888",
            MemberLevel = "Standard",
            AdminAccount = "13900000002",
        }, cts.Token);

        Assert.Equal("chengduceshikejiyouxiangongsi", first.Code);
        Assert.Equal("chengduceshikejiyouxiangongsi-001", second.Code);
    }

    [Fact]
    public async Task ToggleStatusSwitchesActiveAndDisabled()
    {
        Harness h = CreateHarness();
        using CancellationTokenSource cts = new();
        Tenant tenant = CreateTenant(710000000000001002, "tg-ten", TenantStatus.Active);
        h.TenantRepo.Tenants.Add(tenant);

        PortalTenantItem disabled = await h.TenantService.ToggleStatusAsync($"pub-{tenant.Id}", cts.Token);
        Assert.Equal("Disabled", disabled.Status);
        Assert.Equal(TenantStatus.Disabled, tenant.Status);

        PortalTenantItem enabled = await h.TenantService.ToggleStatusAsync($"pub-{tenant.Id}", cts.Token);
        Assert.Equal("Enabled", enabled.Status);
        Assert.Equal(TenantStatus.Active, tenant.Status);

        Assert.All(h.TenantRepo.Tokens, token => Assert.Equal(cts.Token, token));
    }

    [Fact]
    public async Task ToggleStatusRejectsProvisioningTenant()
    {
        Harness h = CreateHarness();
        using CancellationTokenSource cts = new();
        Tenant tenant = CreateTenant(710000000000001003, "pending-ten");
        h.TenantRepo.Tenants.Add(tenant);

        ConflictException ex = await Assert.ThrowsAsync<ConflictException>(
            () => h.TenantService.ToggleStatusAsync($"pub-{tenant.Id}", cts.Token));

        Assert.Equal("TENANT_STATUS_CONFLICT", ex.ErrorCode);
        // P1-2 回归锚定：冲突文案须使用中文状态名，禁止混入前端英文枚举字面量（如 Pending）
        Assert.Contains("预配中", ex.Message);
    }

    [Fact]
    public async Task SaveLicenseGrantsNewAndRevokesRemovedModules()
    {
        Harness h = CreateHarness();
        using CancellationTokenSource cts = new();
        Tenant tenant = CreateTenant(710000000000001004, "lic-ten", TenantStatus.Active);
        TenantModule existing = new(tenant, "stk", "1.0.0");
        existing.AssignId(710000000000001104);
        tenant.AddModule(existing);
        h.TenantRepo.Tenants.Add(tenant);

        // 提交 sal + bas：stk 撤销，sal 新授权，bas 必备自动补入
        await h.TenantService.SaveLicenseAsync(
            $"pub-{tenant.Id}",
            new SaveTenantLicenseRequest { ModuleCodes = ["sal", "bas"], Remark = "扩容销售模块" },
            cts.Token);

        Assert.Equal(TenantModuleStatus.Disabled, existing.Status);
        Assert.Contains(tenant.Modules, m => m.ModuleCode == "sal" && m.Status == TenantModuleStatus.Enabled);
        Assert.Contains(tenant.Modules, m => m.ModuleCode == "bas" && m.Status == TenantModuleStatus.Enabled);

        Assert.Equal(3, h.TenantRepo.Records.Count);
        Assert.Contains(h.TenantRepo.Records, r => r.ModuleCode == "stk" && r.Action == LicenseAction.Revoked);
        Assert.Contains(h.TenantRepo.Records, r => r.ModuleCode == "sal" && r.Action == LicenseAction.Granted);
        Assert.All(h.TenantRepo.Records, r => Assert.Equal("扩容销售模块", r.Remark));
        Assert.Contains(h.Audit.Operations, o => o.Operation == "SAVE_TENANT_LICENSE");
    }

    [Fact]
    public async Task SaveLicenseInvalidatesLicenseCache()
    {
        Harness h = CreateHarness();
        using CancellationTokenSource cts = new();
        Tenant tenant = CreateTenant(710000000000001006, "cache-ten", TenantStatus.Active);
        h.TenantRepo.Tenants.Add(tenant);

        // 预先写入旧许可缓存（模拟登录后残留的 _portal:license:{tenantId}）
        string cacheKey = ModuleLicenseService.BuildCacheKey(tenant.Id);
        await h.Cache.SetAsync(
            cacheKey,
            ImmutableHashSet.Create("stk"),
            cts.Token,
            new CacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5) });
        Assert.NotNull(await h.Cache.GetAsync<ImmutableHashSet<string>>(cacheKey, cts.Token));

        await h.TenantService.SaveLicenseAsync(
            $"pub-{tenant.Id}",
            new SaveTenantLicenseRequest { ModuleCodes = ["sal"] },
            cts.Token);

        // C-5：写路径只删缓存不写缓存——提交后旧键必须已被删除
        Assert.Null(await h.Cache.GetAsync<ImmutableHashSet<string>>(cacheKey, cts.Token));
    }

    [Fact]
    public async Task SaveLicenseRejectsUnknownModuleCode()
    {
        Harness h = CreateHarness();
        using CancellationTokenSource cts = new();
        Tenant tenant = CreateTenant(710000000000001005, "unk-ten", TenantStatus.Active);
        h.TenantRepo.Tenants.Add(tenant);

        _ = await Assert.ThrowsAsync<ValidationException>(() => h.TenantService.SaveLicenseAsync(
            $"pub-{tenant.Id}",
            new SaveTenantLicenseRequest { ModuleCodes = ["not-exists"] },
            cts.Token));
    }

    [Fact]
    public async Task ToggleUserStatusRejectsPortalAdmin()
    {
        Harness h = CreateHarness();
        using CancellationTokenSource cts = new();
        PortalUser admin = new("qlxadmin", "系统管理员", isPortalAdmin: true);
        admin.AssignId(710000000000001201);
        h.UserRepo.Users.Add(admin);

        ConflictException ex = await Assert.ThrowsAsync<ConflictException>(
            () => h.UserService.ToggleStatusAsync($"pub-{admin.Id}", cts.Token));

        Assert.Equal("PORTAL_ADMIN_PROTECTED", ex.ErrorCode);
        Assert.Equal(UserStatus.Enabled, admin.Status);
    }

    [Fact]
    public async Task ToggleUserStatusDisablesOrdinaryUser()
    {
        Harness h = CreateHarness();
        using CancellationTokenSource cts = new();
        PortalUser user = new("13800000009", "张三");
        user.AssignId(710000000000001202);
        h.UserRepo.Users.Add(user);

        PortalUserItem item = await h.UserService.ToggleStatusAsync($"pub-{user.Id}", cts.Token);

        Assert.Equal("Disabled", item.Status);
        Assert.Equal(UserStatus.Disabled, user.Status);
        Assert.True(user.TokenVersion > 0);
    }

    [Fact]
    public async Task ResetPasswordHashesPasswordAndBumpsTokenVersion()
    {
        Harness h = CreateHarness();
        using CancellationTokenSource cts = new();
        PortalUser user = new("13800000010", "李四");
        user.AssignId(710000000000001203);
        h.UserRepo.Users.Add(user);
        var versionBefore = user.TokenVersion;

        await h.UserService.ResetPasswordAsync(
            $"pub-{user.Id}", new ResetPortalUserPasswordRequest { NewPassword = "Qlx@2026abc" }, cts.Token);

        Assert.NotNull(user.PasswordHash);
        Assert.Equal(versionBefore + 1, user.TokenVersion);

        PasswordVerificationResult verify = new PasswordHasher<PortalUser>().VerifyHashedPassword(user, user.PasswordHash!, "Qlx@2026abc");
        Assert.Equal(PasswordVerificationResult.Success, verify);
    }

    [Fact]
    public async Task ResetPasswordRejectsWeakPassword()
    {
        Harness h = CreateHarness();
        using CancellationTokenSource cts = new();
        PortalUser user = new("13800000011", "王五");
        user.AssignId(710000000000001204);
        h.UserRepo.Users.Add(user);

        // 纯字母：不满足「同时含字母与数字」
        _ = await Assert.ThrowsAsync<ValidationException>(() => h.UserService.ResetPasswordAsync(
            $"pub-{user.Id}", new ResetPortalUserPasswordRequest { NewPassword = "abcdefgh" }, cts.Token));
        Assert.Null(user.PasswordHash);
    }

    [Fact]
    public async Task UserDetailReturnsLoginLogsFromAuditCenter()
    {
        Harness h = CreateHarness();
        using CancellationTokenSource cts = new();
        PortalUser user = new("13800000012", "赵六");
        user.AssignId(710000000000001205);
        h.UserRepo.Users.Add(user);

        await h.Audit.CaptureOperationAsync(new OperationLogEntry(
            9001, "portal", "LOGIN_SUCCESS", "门户管理", user.LoginAccount,
            DateTimeOffset.UtcNow, 0, "PortalUser", null, "{\"ip\":\"10.0.0.7\"}"), cts.Token);
        await h.Audit.CaptureOperationAsync(new OperationLogEntry(
            9002, "portal", "LOGIN_FAILED", "门户管理", user.LoginAccount,
            DateTimeOffset.UtcNow, 0, "PortalUser", null, "{\"ip\":\"10.0.0.8\"}"), cts.Token);

        PortalUserDetailResponse detail = await h.UserService.GetDetailAsync($"pub-{user.Id}", cts.Token);

        Assert.Equal(2, detail.Logs.Count);
        Assert.Contains(detail.Logs, l => l.Result == "Success" && l.IpAddress == "10.0.0.7");
        Assert.Contains(detail.Logs, l => l.Result == "Failure" && l.IpAddress == "10.0.0.8");
    }

    [Fact]
    public async Task CancellationTokenPropagatesToRepositoriesUnchanged()
    {
        Harness h = CreateHarness();
        using CancellationTokenSource cts = new();

        _ = await h.UserService.QueryAsync(new PortalUserQueryRequest(), cts.Token);
        _ = await h.TenantService.QueryAsync(new PortalTenantQueryRequest(), cts.Token);

        Assert.NotEmpty(h.UserRepo.Tokens);
        Assert.All(h.UserRepo.Tokens, token => Assert.Equal(cts.Token, token));
        Assert.All(h.TenantRepo.Tokens, token => Assert.Equal(cts.Token, token));
    }

    [Fact]
    public async Task RepositoryCancellationIsNotSwallowed()
    {
        Harness h = CreateHarness();
        using CancellationTokenSource cts = new();
        Tenant tenant = CreateTenant(710000000000001006, "ct-ten", TenantStatus.Active);
        h.TenantRepo.Tenants.Add(tenant);
        await cts.CancelAsync();

        _ = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => h.TenantService.ToggleStatusAsync($"pub-{tenant.Id}", cts.Token));
    }

    [Fact]
    public async Task TenantListExposesDatabaseModeAndDbName()
    {
        Harness h = CreateHarness();
        using CancellationTokenSource cts = new();
        Tenant tenant = CreateTenant(710000000000001007, "db-ten", TenantStatus.Active);
        tenant.ChangeDatabaseMode(TenantDatabaseMode.Isolated);
        TenantDatabase database = new(tenant, "(localdb)", 1433, "qlx_tenant_db_ten");
        database.AssignId(710000000000002007);
        tenant.SetDatabase(database);
        h.TenantRepo.Tenants.Add(tenant);

        PortalPagedResult<PortalTenantItem> page = await h.TenantService.QueryAsync(new PortalTenantQueryRequest(), cts.Token);

        PortalTenantItem item = Assert.Single(page.Items);
        Assert.Equal("Isolated", item.DatabaseMode);
        Assert.Equal("qlx_tenant_db_ten", item.DbName);
        Assert.Equal("Ready", item.DbStatus);
    }

    [Fact]
    public async Task TenantDetailReturnsSharedDatabaseSummary()
    {
        Harness h = CreateHarness();
        using CancellationTokenSource cts = new();
        Tenant tenant = CreateTenant(710000000000001008, "shared-ten", TenantStatus.Active);
        TenantDatabase database = new(
            tenant, TenantDatabaseMode.Shared, "(localdb)", 1433, "qlx_tenant_shared");
        database.AssignId(710000000000002008);
        tenant.SetDatabase(database);
        h.TenantRepo.Tenants.Add(tenant);

        PortalTenantDetailResponse detail = await h.TenantService.GetDetailAsync($"pub-{tenant.Id}", cts.Token);

        Assert.Equal("Shared", detail.Tenant.DatabaseMode);
        Assert.Equal("qlx_tenant_shared", detail.Tenant.DbName);
        PortalTenantDatabaseSummary summary = Assert.IsType<PortalTenantDatabaseSummary>(detail.Database);
        Assert.Equal("Shared", summary.Mode);
        Assert.Equal("qlx_tenant_shared", summary.DbName);
        Assert.Equal("(localdb)", summary.DbHost);
        Assert.Equal(1433, summary.DbPort);
        Assert.Equal("Ready", summary.Status);
        Assert.Equal("default", summary.PoolCode);
    }

    [Fact]
    public async Task TenantDetailReturnsNullDatabaseWhenRoutingRowMissing()
    {
        Harness h = CreateHarness();
        using CancellationTokenSource cts = new();
        Tenant tenant = CreateTenant(710000000000001009, "norow-ten", TenantStatus.Active);
        h.TenantRepo.Tenants.Add(tenant);

        PortalTenantDetailResponse detail = await h.TenantService.GetDetailAsync($"pub-{tenant.Id}", cts.Token);

        Assert.Null(detail.Database);
        Assert.Equal("Shared", detail.Tenant.DatabaseMode);
        Assert.Null(detail.Tenant.DbName);
    }
}
