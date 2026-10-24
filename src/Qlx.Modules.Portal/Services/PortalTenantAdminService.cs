/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-21 19:00:00 +08:00 中国标准时间 星期三
 * 修改时间：2026-10-21 19:00:00 +08:00 中国标准时间 星期三
 * 描述：租户管理服务实现：列表 / 新建（联动数据库预配 + 默认许可 + 初始管理员）/ 启停 / 详情 / 授权保存 / 成员
 */

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Qlx.Abstractions.Audit;
using Qlx.Abstractions.Constants;
using Qlx.Abstractions.Domain;
using Qlx.Abstractions.Exceptions;
using Qlx.Abstractions.Tenancy;
using Qlx.BuildingBlocks.Caching;
using Qlx.BuildingBlocks.Ids;
using Qlx.BuildingBlocks.MultiTenancy;
using Qlx.BuildingBlocks.Numbering;
using Qlx.Modules.Portal.Domain.Identity;
using Qlx.Modules.Portal.Domain.Tenancy;
using Qlx.Modules.Portal.Dtos;
using Qlx.Modules.Portal.Repositories;

namespace Qlx.Modules.Portal.Services;

/// <summary>
/// 租户管理服务。
/// <para>
/// 新建租户为门户侧唯一入口：登记 <c>por_tenants</c> + <c>por_tenant_databases</c> + 默认模块许可 + 初始租户管理员成员关系。
/// 共享模式**不建库**、事务提交后租户直接 <see cref="TenantStatus.Active"/>；
/// 独立模式保持 <see cref="TenantStatus.Provisioning"/>，由后台迁移执行器建库并激活。
/// </para>
/// </summary>
public sealed partial class PortalTenantAdminService(
    IPortalTenantAdminRepository tenants,
    IPortalUserAdminRepository users,
    IPortalModuleAdminRepository modules,
    ISnowflakeIdGenerator snowflake,
    IIdObfuscator obfuscator,
    ICurrentUserContext currentUser,
    ITenantDatabaseResolver databaseResolver,
    IAuditService audit,
    ICacheService cache,
    IEncodingGenerator encodingGenerator,
    IOptions<PortalProvisioningOptions> provisioning,
    IOptions<SharedDatabaseOptions> sharedDatabase,
    ILogger<PortalTenantAdminService> logger) : IPortalTenantAdminService
{
    /// <summary>LoggerMessage 源生成器要求字段（主构造参数经字段初始化器接入）。</summary>
    private readonly ILogger<PortalTenantAdminService> _logger = logger;

    /// <summary>租户编码最大长度。</summary>
    private const int MaxTenantCodeLength = PortalConstants.Validation.MaxTenantCodeLength;

    /// <summary>统一社会信用代码长度（18 位）。</summary>
    private const int TaxNoLength = PortalConstants.Validation.TaxNoLength;

    /// <inheritdoc />
    public async Task<PortalPagedResult<PortalTenantItem>> QueryAsync(
        PortalTenantQueryRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var page = await tenants.QueryAsync(
            request.Keyword, request.Status, request.DatabaseMode, request, cancellationToken).ConfigureAwait(false);

        var ids = page.Items.Select(t => t.Id).ToList();
        var lastLogins = await tenants.GetLastLoginTimesAsync(ids, cancellationToken).ConfigureAwait(false);

        var items = page.Items
            .Select(t => PortalAdminMapper.ToTenantItem(t, obfuscator, lastLogins.GetValueOrDefault(t.Id)))
            .ToList();

        return new PortalPagedResult<PortalTenantItem>(items, page.TotalCount);
    }

    /// <inheritdoc />
    public async Task<PortalTenantItem> CreateAsync(
        CreateTenantRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateCreateRequest(request);

        // 租户编码由系统自动生成：租户名完整拼音，冲突追加 001/002...
        var tenantCode = await encodingGenerator.GenerateAsync(
            request.Name,
            scope: "tenant",
            uniquenessChecker: async (code, ct2) =>
                await tenants.CodeExistsAsync(code, ct2).ConfigureAwait(false),
            options: new EncodingGenerationOptions
            {
                Prefix = string.Empty,
                PinyinMode = PinyinMode.Full,
                SequenceDigits = 3,
                MaxLength = MaxTenantCodeLength,
                Separator = "-",
            },
            cancellationToken).ConfigureAwait(false);

        var taxNo = request.TaxNo.Trim();

        if (await tenants.TaxNoExistsAsync(taxNo, cancellationToken).ConfigureAwait(false))
        {
            throw new ConflictException($"统一社会信用代码 {taxNo} 已存在", PortalConstants.ErrorCodes.TenantTaxNoExists);
        }

        var options = provisioning.Value;
        var databaseMode = ResolveDatabaseMode(request.DatabaseMode, options.DefaultDatabaseMode);

        // 1. 租户主体 + 注册信息（套餐编码存小写，与前端会员等级一一对应）
        var tenant = new Tenant(tenantCode, request.Name.Trim());
        tenant.AssignId(snowflake.NextId());
        var isTrial = string.Equals(request.MemberLevel, PortalConstants.MemberLevels.Trial, StringComparison.Ordinal);
        tenant.SetRegistrationProfile(
            taxNo,
            PortalAdminMapper.ToPlanCode(request.MemberLevel),
            isTrial ? DateTimeOffset.UtcNow.AddDays(options.TrialDays) : null);

        // 2. 路由行：共享模式指向租户共享库（凭据留空，连接统一来自 SharedDatabaseOptions）；
        //    独立模式按预配参数生成独立库名，凭据由预配流程写入（当前空 → Windows 集成认证）
        var database = databaseMode == TenantDatabaseMode.Shared
            ? new TenantDatabase(
                tenant, TenantDatabaseMode.Shared,
                sharedDatabase.Value.DbHost, sharedDatabase.Value.DbPort, sharedDatabase.Value.DbName)
            : new TenantDatabase(
                tenant, options.DbHost, options.DbPort, BuildDatabaseName(options.DbNamePrefix, tenantCode));
        database.AssignId(snowflake.NextId());
        tenant.SetDatabase(database);

        // 共享模式不建库：租户直接激活；独立模式保持 Provisioning，由后台迁移执行器建库后激活
        if (databaseMode == TenantDatabaseMode.Isolated)
        {
            tenant.ChangeDatabaseMode(TenantDatabaseMode.Isolated);
        }
        else
        {
            tenant.Activate();
        }

        // 3. 默认模块许可（配置默认项 + 必备模块）
        var definitions = await modules.ListDefinitionsAsync(cancellationToken).ConfigureAwait(false);
        var moduleCodes = ResolveInitialModuleCodes(definitions, options.DefaultModuleCodes);
        var moduleMap = definitions.ToDictionary(d => d.ModuleCode, StringComparer.Ordinal);
        var tenantModules = moduleCodes
            .Where(moduleMap.ContainsKey)
            .Select(code =>
            {
                var module = new TenantModule(tenant, code, moduleMap[code].CurrentVersion);
                module.AssignId(snowflake.NextId());
                tenant.AddModule(module);
                return module;
            })
            .ToList();

        // 4. 初始租户管理员（已有门户账号则直接建立成员关系；否则创建待激活账号，由门户重置密码后启用）
        var adminAccount = request.AdminAccount.Trim();
        var admin = await users.FindTrackedByAccountAsync(adminAccount, cancellationToken).ConfigureAwait(false);
        if (admin is null)
        {
            admin = new PortalUser(adminAccount, PortalConstants.Labels.InitialAdminDisplayName);
            admin.AssignId(snowflake.NextId());
            await users.AddAsync(admin, cancellationToken).ConfigureAwait(false);
        }

        var member = new TenantMember(tenant.Id, admin.Id, TenantMemberRole.TenantAdmin)
        {
            DisplayNameInTenant = admin.DisplayName,
            JoinAt = DateTimeOffset.UtcNow,
        };
        member.AssignId(snowflake.NextId());

        // 5. 授权记录（新建即授权，便于授权记录页追溯）
        var operatorName = currentUser.UserDisplayName ?? currentUser.UserName ?? PortalConstants.Labels.OperatorDisplayFallback;
        var records = tenantModules
            .Select(m => new LicenseRecord(
                tenant, m.ModuleCode, moduleMap[m.ModuleCode].DisplayName,
                LicenseAction.Granted, operatorName, "新建租户默认授权"))
            .ToList();
        foreach (var record in records)
        {
            record.AssignId(snowflake.NextId());
        }

        // 6. 单事务提交（PortalDbContext 作用域实例共享，同一 SaveChanges 原子落库）
        await tenants.AddAsync(tenant, cancellationToken).ConfigureAwait(false);
        await tenants.AddDatabaseAsync(database, cancellationToken).ConfigureAwait(false);
        await tenants.AddModulesAsync(tenantModules, cancellationToken).ConfigureAwait(false);
        await tenants.AddMemberAsync(member, cancellationToken).ConfigureAwait(false);
        await tenants.AddLicenseRecordsAsync(records, cancellationToken).ConfigureAwait(false);
        await tenants.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        // 7. 统一刷新路由缓存（失效 + 全量预热）：后台迁移执行器依此 Resolve 出连接串完成建库与迁移
        await databaseResolver.RefreshAsync(tenant.Id, cancellationToken).ConfigureAwait(false);

        LogTenantCreated(tenantCode, tenant.Id, adminAccount, string.Join(',', moduleCodes));

        await audit.CaptureOperationAsync(new OperationLogEntry(
            snowflake.NextId(), QlxAuditConstants.PortalModuleCode, PortalConstants.AuditOperations.CreateTenant,
            $"新建租户 {tenant.DisplayName}（{tenantCode}）",
            currentUser.UserName ?? QlxAuditConstants.PortalAdminOperatorName,
            DateTimeOffset.UtcNow, tenant.Id,
            QlxAuditConstants.TargetTenant, tenant.Id), cancellationToken).ConfigureAwait(false);

        return PortalAdminMapper.ToTenantItem(tenant, obfuscator, null);
    }

    /// <inheritdoc />
    public async Task<PortalTenantItem> ToggleStatusAsync(string tenantId, CancellationToken cancellationToken)
    {
        var tenant = await FindTrackedTenantAsync(tenantId, cancellationToken).ConfigureAwait(false);

        switch (tenant.Status)
        {
            case TenantStatus.Active:
                tenant.Disable();
                break;
            case TenantStatus.Disabled:
                tenant.Enable();
                break;
            default:
                throw new ConflictException(
                    $"租户当前状态为「{ToStatusDisplayName(tenant.Status)}」，不支持启用/停用",
                    PortalConstants.ErrorCodes.TenantStatusConflict);
        }

        await tenants.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await audit.CaptureOperationAsync(new OperationLogEntry(
            snowflake.NextId(), QlxAuditConstants.PortalModuleCode, PortalConstants.AuditOperations.ToggleTenantStatus,
            $"{(tenant.Status == TenantStatus.Active ? "启用" : "停用")}租户 {tenant.DisplayName}",
            currentUser.UserName ?? QlxAuditConstants.PortalAdminOperatorName,
            DateTimeOffset.UtcNow, tenant.Id, QlxAuditConstants.TargetTenant, tenant.Id), cancellationToken).ConfigureAwait(false);

        return PortalAdminMapper.ToTenantItem(tenant, obfuscator, null);
    }

    /// <inheritdoc />
    public async Task<PortalTenantDetailResponse> GetDetailAsync(
        string tenantId, CancellationToken cancellationToken)
    {
        var id = DecodeId(tenantId, PortalConstants.ResourceNames.Tenant);
        var tenant = await tenants.FindWithDetailsAsync(id, cancellationToken).ConfigureAwait(false)
            ?? throw new NotFoundException(PortalConstants.ResourceNames.Tenant, tenantId);

        var lastLogins = await tenants.GetLastLoginTimesAsync([id], cancellationToken).ConfigureAwait(false);
        var records = await tenants.QueryLicenseRecordsAsync(id, PortalConstants.Validation.DefaultLicenseRecordLimit, cancellationToken).ConfigureAwait(false);

        return new PortalTenantDetailResponse(
            PortalAdminMapper.ToTenantItem(tenant, obfuscator, lastLogins.GetValueOrDefault(id)),
            PortalAdminMapper.ToDatabaseSummary(tenant),
            records.Select(r => PortalAdminMapper.ToLicenseRecordItem(r, obfuscator)).ToList());
    }

    /// <inheritdoc />
    public async Task SaveLicenseAsync(
        string tenantId, SaveTenantLicenseRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var id = DecodeId(tenantId, PortalConstants.ResourceNames.Tenant);
        var tenant = await tenants.FindTrackedWithModulesAsync(id, cancellationToken).ConfigureAwait(false)
            ?? throw new NotFoundException(PortalConstants.ResourceNames.Tenant, tenantId);

        var definitions = await modules.ListDefinitionsAsync(cancellationToken).ConfigureAwait(false);
        var definitionMap = definitions.ToDictionary(d => d.ModuleCode, StringComparer.Ordinal);

        var requested = new HashSet<string>(request.ModuleCodes ?? [], StringComparer.Ordinal);
        var unknown = requested.Where(c => !definitionMap.ContainsKey(c)).ToList();
        if (unknown.Count > 0)
        {
            throw new ValidationException("moduleCodes", $"模块编码不存在：{string.Join(',', unknown)}");
        }

        // 必备模块不可停用：自动补入（如系统管理 portal）
        foreach (var mandatory in definitions.Where(d => d.IsMandatory && d.Licensable))
        {
            requested.Add(mandatory.ModuleCode);
        }

        var operatorName = currentUser.UserDisplayName ?? currentUser.UserName ?? PortalConstants.Labels.OperatorDisplayFallback;
        var remark = request.Remark?.Trim() ?? string.Empty;
        var records = new List<LicenseRecord>();

        foreach (var module in tenant.Modules)
        {
            var shouldEnable = requested.Contains(module.ModuleCode);
            if (shouldEnable && module.Status != TenantModuleStatus.Enabled)
            {
                module.Enable();
                records.Add(new LicenseRecord(
                    tenant, module.ModuleCode, GetModuleName(definitionMap, module.ModuleCode),
                    LicenseAction.Granted, operatorName, remark));
            }
            else if (!shouldEnable && module.Status == TenantModuleStatus.Enabled)
            {
                module.Disable();
                records.Add(new LicenseRecord(
                    tenant, module.ModuleCode, GetModuleName(definitionMap, module.ModuleCode),
                    LicenseAction.Revoked, operatorName, remark));
            }
        }

        foreach (var code in requested)
        {
            if (tenant.Modules.Any(m => string.Equals(m.ModuleCode, code, StringComparison.Ordinal)))
            {
                continue;
            }

            var module = new TenantModule(tenant, code, definitionMap[code].CurrentVersion);
            // Id 由 EF 值生成器（SnowflakeIdValueGenerator）在 SaveChanges 时统一赋值，
            // 禁止显式 AssignId——在 ValueGenerated.OnAdd + 非临时值生成器配置下，
            // 显式置键会让 EF 误判实体为已存在，生成 UPDATE 而非 INSERT，触发并发异常。
            tenant.AddModule(module);
            records.Add(new LicenseRecord(
                tenant, code, definitionMap[code].DisplayName,
                LicenseAction.Granted, operatorName, remark));
        }

        await tenants.AddLicenseRecordsAsync(records, cancellationToken).ConfigureAwait(false);
        await tenants.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        // C-5 失效优先：写路径只删缓存不写缓存，事务提交后删除许可缓存，
        // 后续登录/鉴权读请求经 ModuleLicenseService 的 Cache-Aside 重新构建。
        await cache.RemoveAsync(ModuleLicenseService.BuildCacheKey(tenant.Id), cancellationToken).ConfigureAwait(false);

        await audit.CaptureOperationAsync(new OperationLogEntry(
            snowflake.NextId(), QlxAuditConstants.PortalModuleCode, PortalConstants.AuditOperations.SaveTenantLicense,
            $"调整租户 {tenant.DisplayName} 模块授权（{requested.Count} 个模块）",
            currentUser.UserName ?? QlxAuditConstants.PortalAdminOperatorName,
            DateTimeOffset.UtcNow, tenant.Id, QlxAuditConstants.TargetTenant, tenant.Id), cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<TenantMembershipItem>> ListMembersAsync(
        string tenantId, CancellationToken cancellationToken)
    {
        var id = DecodeId(tenantId, PortalConstants.ResourceNames.Tenant);
        var tenant = await tenants.FindWithDetailsAsync(id, cancellationToken).ConfigureAwait(false)
            ?? throw new NotFoundException(PortalConstants.ResourceNames.Tenant, tenantId);

        var rows = await tenants.ListMembersAsync(id, cancellationToken).ConfigureAwait(false);
        return rows.Select(r => PortalAdminMapper.ToMembershipItem(r, tenant, obfuscator)).ToList();
    }

    /// <summary>按混淆 Id 取跟踪态租户，非法 Id 视为不存在。</summary>
    private async Task<Tenant> FindTrackedTenantAsync(string tenantId, CancellationToken cancellationToken)
    {
        var id = DecodeId(tenantId, PortalConstants.ResourceNames.Tenant);
        return await tenants.FindTrackedAsync(id, cancellationToken).ConfigureAwait(false)
            ?? throw new NotFoundException(PortalConstants.ResourceNames.Tenant, tenantId);
    }

    /// <summary>解码对外 Id（非法或越界一律视为资源不存在，避免暴露内部编号规律）。</summary>
    private long DecodeId(string publicId, string resourceType)
    {
        if (!obfuscator.TryDecode(publicId, out var id) || id <= 0)
        {
            throw new NotFoundException(resourceType, publicId);
        }

        return id;
    }

    /// <summary>新建租户入参校验（租户编码由系统自动生成，无需前端传入与校验）。</summary>
    private static void ValidateCreateRequest(CreateTenantRequest request)
    {
        var errors = new List<FieldError>();

        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Trim().Length > PortalConstants.Validation.MaxTenantNameLength)
        {
            errors.Add(new FieldError("name", $"企业全称必填且不超过 {PortalConstants.Validation.MaxTenantNameLength} 字"));
        }

        var taxNo = request.TaxNo?.Trim() ?? string.Empty;
        if (taxNo.Length != TaxNoLength || !taxNo.All(c => char.IsAsciiLetterUpper(c) || char.IsAsciiDigit(c)))
        {
            errors.Add(new FieldError("taxNo", "统一社会信用代码须为 18 位大写字母或数字"));
        }

        if (!PortalConstants.MemberLevels.All.Contains(request.MemberLevel, StringComparer.Ordinal))
        {
            errors.Add(new FieldError("memberLevel", "会员等级须为 Trial / Standard / Professional / Flagship"));
        }

        if (string.IsNullOrWhiteSpace(request.AdminAccount) || request.AdminAccount.Trim().Length > PortalConstants.Validation.MaxAdminAccountLength)
        {
            errors.Add(new FieldError("adminAccount",
                $"初始管理员登录账号必填且不超过 {PortalConstants.Validation.MaxAdminAccountLength} 字"));
        }

        if (errors.Count > 0)
        {
            throw new ValidationException(errors);
        }
    }

    /// <summary>租户状态的中文名（仅用于冲突等错误消息文案，与展示契约无关）。</summary>
    private static string ToStatusDisplayName(TenantStatus status) => status switch
    {
        TenantStatus.Active => "启用中",
        TenantStatus.Disabled => "已停用",
        TenantStatus.Provisioning => "预配中",
        TenantStatus.ProvisioningFailed => "预配失败",
        TenantStatus.GracePeriod => "宽限期",
        TenantStatus.Archived => "已归档",
        TenantStatus.Deleted => "已删除",
        _ => status.ToString(),
    };

    /// <summary>生成独立库名（编码中的短横线替换为下划线）。</summary>
    private static string BuildDatabaseName(string prefix, string tenantCode)
        => prefix + tenantCode.Replace('-', '_');

    /// <summary>
    /// 解析新建请求的数据库模式：未指定取配置默认值；非法字面量视为参数错误。
    /// </summary>
    private static TenantDatabaseMode ResolveDatabaseMode(string? requested, TenantDatabaseMode defaultMode)
    {
        if (string.IsNullOrWhiteSpace(requested))
        {
            return defaultMode;
        }

        if (Enum.TryParse<TenantDatabaseMode>(requested.Trim(), ignoreCase: true, out var mode)
            && Enum.IsDefined(mode))
        {
            return mode;
        }

        throw new ValidationException("databaseMode", "数据库模式须为 Shared 或 Isolated");
    }

    /// <summary>合并默认授权模块与必备模块（去重，保持稳定顺序）。</summary>
    private static List<string> ResolveInitialModuleCodes(
        IReadOnlyList<ModuleDefinitionRow> definitions, IReadOnlyList<string> defaults)
    {
        var codes = new List<string>();
        foreach (var code in defaults.Concat(definitions.Where(d => d.IsMandatory).Select(d => d.ModuleCode)))
        {
            if (!codes.Contains(code, StringComparer.Ordinal))
            {
                codes.Add(code);
            }
        }

        return codes;
    }

    /// <summary>取模块显示名（定义缺失时回落编码）。</summary>
    private static string GetModuleName(
        Dictionary<string, ModuleDefinitionRow> definitions, string moduleCode)
        => definitions.TryGetValue(moduleCode, out var definition) ? definition.DisplayName : moduleCode;

    /// <summary>新建租户成功日志。</summary>
    [LoggerMessage(Level = LogLevel.Information,
        Message = "门户新建租户 {TenantCode}（Id={TenantId}），初始管理员 {AdminAccount}，默认模块 {Modules}")]
    private partial void LogTenantCreated(string tenantCode, long tenantId, string adminAccount, string modules);
}
