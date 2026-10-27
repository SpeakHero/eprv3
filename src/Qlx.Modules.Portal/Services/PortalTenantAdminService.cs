/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.2.0 beta
 * 创建时间：2026-10-21 19:00:00 +08:00 中国标准时间 星期三
 * 修改时间：2026-10-26 23:20:00 +08:00 中国标准时间 星期一
 * 描述：租户管理服务实现：列表 / 新建（联动数据库预配 + 默认许可 + 初始管理员）/ 启停 / 详情 / 授权保存 / 成员
 *       入参静态校验由 ValidationDispatchProxy 在服务方法入口自动执行（接口参数 [Validate] 标记驱动），
 *       本实现只保留有状态校验（税号唯一性、未知模块编码、数据库模式兜底）。
 */

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Qlx.Abstractions.Audit;
using Qlx.Abstractions.Constants;
using Qlx.Abstractions.Domain;
using Qlx.Abstractions.Exceptions;
using Qlx.Abstractions.Tenancy;
using Qlx.BuildingBlocks.Api;
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

    /// <inheritdoc />
    public async Task<PagedResult<PortalTenantItem>> QueryAsync(
        PortalTenantQueryRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        PagedResult<Tenant> page = await tenants.QueryAsync(
            request.Keyword, request.Status, request.DatabaseMode, request, cancellationToken).ConfigureAwait(false);

        List<long> ids = [.. page.Items.Select(t => t.Id)];
        IReadOnlyDictionary<long, DateTimeOffset?> lastLogins = await tenants.GetLastLoginTimesAsync(ids, cancellationToken).ConfigureAwait(false);

        return new PagedResult<PortalTenantItem>(
            [.. page.Items.Select(t => PortalAdminMapper.ToTenantItem(t, obfuscator, lastLogins.GetValueOrDefault(t.Id)))],
            page.TotalCount, page.Page, page.PageSize);
    }

    /// <inheritdoc />
    public async Task<PortalTenantItem> CreateAsync(
        CreateTenantRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        // 入参格式校验由接口参数 [Validate] 经 ValidationDispatchProxy 在方法入口完成（唯一性等有状态校验保留在此）

        // 租户编码由系统自动生成：租户名完整拼音，冲突追加 001/002...
        string tenantCode = await encodingGenerator.GenerateAsync(
            request.Name,
            scope: "tenant",
            uniquenessChecker: tenants.CodeExistsAsync,
            options: new EncodingGenerationOptions
            {
                Prefix = string.Empty,
                PinyinMode = PinyinMode.Full,
                SequenceDigits = 3,
                MaxLength = MaxTenantCodeLength,
                Separator = "-",
            },
            cancellationToken).ConfigureAwait(false);

        string taxNo = request.TaxNo.Trim();

        if (await tenants.TaxNoExistsAsync(taxNo, cancellationToken).ConfigureAwait(false))
        {
            throw new ConflictException($"统一社会信用代码 {taxNo} 已存在", PortalConstants.ErrorCodes.TenantTaxNoExists);
        }

        PortalProvisioningOptions options = provisioning.Value;
        TenantDatabaseMode databaseMode = ResolveDatabaseMode(request.DatabaseMode, options.DefaultDatabaseMode);

        // 1. 租户主体 + 注册信息（套餐编码存小写，与前端会员等级一一对应）
        Tenant tenant = new(tenantCode, request.Name.Trim());
        tenant.AssignId(snowflake.NextId());
        bool isTrial = string.Equals(request.MemberLevel, PortalConstants.MemberLevels.Trial, StringComparison.Ordinal);
        tenant.SetRegistrationProfile(
            taxNo,
            PortalAdminMapper.ToPlanCode(request.MemberLevel),
            isTrial ? DateTimeOffset.UtcNow.AddDays(options.TrialDays) : null);

        // 2. 路由行：共享模式指向租户共享库（凭据留空，连接统一来自 SharedDatabaseOptions）；
        //    独立模式按预配参数生成独立库名，凭据由预配流程写入（当前空 → Windows 集成认证）
        TenantDatabase database = databaseMode == TenantDatabaseMode.Shared
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
        IReadOnlyList<ModuleDefinitionRow> definitions = await modules.ListDefinitionsAsync(cancellationToken).ConfigureAwait(false);
        List<string> moduleCodes = ResolveInitialModuleCodes(definitions, options.DefaultModuleCodes);
        Dictionary<string, ModuleDefinitionRow> moduleMap = definitions.ToDictionary(d => d.ModuleCode, StringComparer.Ordinal);
        List<TenantModule> tenantModules = [.. moduleCodes
            .Where(moduleMap.ContainsKey)
            .Select(code =>
            {
                TenantModule module = new(tenant, code, moduleMap[code].CurrentVersion);
                module.AssignId(snowflake.NextId());
                tenant.AddModule(module);
                return module;
            })];

        // 4. 初始租户管理员（已有门户账号则直接建立成员关系；否则创建待激活账号，由门户重置密码后启用）
        string adminAccount = request.AdminAccount.Trim();
        PortalUser? admin = await users.FindTrackedByAccountAsync(adminAccount, cancellationToken).ConfigureAwait(false);
        if (admin is null)
        {
            admin = new PortalUser(adminAccount, PortalConstants.Labels.InitialAdminDisplayName);
            admin.AssignId(snowflake.NextId());
            await users.AddAsync(admin, cancellationToken).ConfigureAwait(false);
        }

        TenantMember member = new(tenant.Id, admin.Id, TenantMemberRole.TenantAdmin)
        {
            DisplayNameInTenant = admin.DisplayName,
            JoinAt = DateTimeOffset.UtcNow,
        };
        member.AssignId(snowflake.NextId());

        // 5. 授权记录（新建即授权，便于授权记录页追溯）
        string operatorName = currentUser.UserDisplayName ?? currentUser.UserName ?? PortalConstants.Labels.OperatorDisplayFallback;
        List<LicenseRecord> records = [.. tenantModules
            .Select(m => new LicenseRecord(
                tenant, m.ModuleCode, moduleMap[m.ModuleCode].DisplayName,
                LicenseAction.Granted, operatorName, "新建租户默认授权"))];
        foreach (LicenseRecord? record in records)
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
        Tenant tenant = await FindTrackedTenantAsync(tenantId, cancellationToken).ConfigureAwait(false);

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
        long id = DecodeId(tenantId, PortalConstants.ResourceNames.Tenant);
        Tenant tenant = await tenants.FindWithDetailsAsync(id, cancellationToken).ConfigureAwait(false)
            ?? throw new NotFoundException(PortalConstants.ResourceNames.Tenant, tenantId);

        IReadOnlyDictionary<long, DateTimeOffset?> lastLogins = await tenants.GetLastLoginTimesAsync([id], cancellationToken).ConfigureAwait(false);
        IReadOnlyList<LicenseRecord> records = await tenants.QueryLicenseRecordsAsync(id, PortalConstants.Validation.DefaultLicenseRecordLimit, cancellationToken).ConfigureAwait(false);

        return new PortalTenantDetailResponse(
            PortalAdminMapper.ToTenantItem(tenant, obfuscator, lastLogins.GetValueOrDefault(id)),
            PortalAdminMapper.ToDatabaseSummary(tenant),
            [.. records.Select(r => PortalAdminMapper.ToLicenseRecordItem(r, obfuscator))]);
    }

    /// <inheritdoc />
    public async Task SaveLicenseAsync(
        string tenantId, SaveTenantLicenseRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        // ModuleCodes 必填等静态规则由接口参数 [Validate] 经 ValidationDispatchProxy 在方法入口完成

        long id = DecodeId(tenantId, PortalConstants.ResourceNames.Tenant);
        Tenant tenant = await tenants.FindTrackedWithModulesAsync(id, cancellationToken).ConfigureAwait(false)
            ?? throw new NotFoundException(PortalConstants.ResourceNames.Tenant, tenantId);

        IReadOnlyList<ModuleDefinitionRow> definitions = await modules.ListDefinitionsAsync(cancellationToken).ConfigureAwait(false);
        Dictionary<string, ModuleDefinitionRow> definitionMap = definitions.ToDictionary(d => d.ModuleCode, StringComparer.Ordinal);

        HashSet<string> requested = new(request.ModuleCodes ?? [], StringComparer.Ordinal);
        List<string> unknown = [.. requested.Where(c => !definitionMap.ContainsKey(c))];
        if (unknown.Count > 0)
        {
            throw new ValidationException("moduleCodes", $"模块编码不存在：{string.Join(',', unknown)}");
        }

        // 必备模块不可停用：自动补入（如系统管理 portal）
        foreach (ModuleDefinitionRow? mandatory in definitions.Where(d => d.IsMandatory && d.Licensable))
        {
            _ = requested.Add(mandatory.ModuleCode);
        }

        string operatorName = currentUser.UserDisplayName ?? currentUser.UserName ?? PortalConstants.Labels.OperatorDisplayFallback;
        string remark = request.Remark?.Trim() ?? string.Empty;
        List<LicenseRecord> records = [];

        foreach (TenantModule module in tenant.Modules)
        {
            bool shouldEnable = requested.Contains(module.ModuleCode);
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

        foreach (string code in requested)
        {
            if (tenant.Modules.Any(m => string.Equals(m.ModuleCode, code, StringComparison.Ordinal)))
            {
                continue;
            }

            TenantModule module = new(tenant, code, definitionMap[code].CurrentVersion);
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
        _ = await cache.RemoveAsync(ModuleLicenseService.BuildCacheKey(tenant.Id), cancellationToken).ConfigureAwait(false);

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
        long id = DecodeId(tenantId, PortalConstants.ResourceNames.Tenant);
        Tenant tenant = await tenants.FindWithDetailsAsync(id, cancellationToken).ConfigureAwait(false)
            ?? throw new NotFoundException(PortalConstants.ResourceNames.Tenant, tenantId);

        IReadOnlyList<TenantMemberRow> rows = await tenants.ListMembersAsync(id, cancellationToken).ConfigureAwait(false);
        return [.. rows.Select(r => PortalAdminMapper.ToMembershipItem(r, tenant, obfuscator))];
    }

    /// <summary>按混淆 Id 取跟踪态租户，非法 Id 视为不存在。</summary>
    private async Task<Tenant> FindTrackedTenantAsync(string tenantId, CancellationToken cancellationToken)
    {
        long id = DecodeId(tenantId, PortalConstants.ResourceNames.Tenant);
        return await tenants.FindTrackedAsync(id, cancellationToken).ConfigureAwait(false)
            ?? throw new NotFoundException(PortalConstants.ResourceNames.Tenant, tenantId);
    }

    /// <summary>解码对外 Id（非法或越界一律视为资源不存在，避免暴露内部编号规律）。</summary>
    private long DecodeId(string publicId, string resourceType)
    {
        return !obfuscator.TryDecode(publicId, out long id) || id <= 0 ? throw new NotFoundException(resourceType, publicId) : id;
    }

    /// <summary>租户状态的中文名（仅用于冲突等错误消息文案，与展示契约无关）。</summary>
    private static string ToStatusDisplayName(TenantStatus status)
    {
        return status switch
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
    }

    /// <summary>生成独立库名（编码中的短横线替换为下划线）。</summary>
    private static string BuildDatabaseName(string prefix, string tenantCode)
    {
        return prefix + tenantCode.Replace('-', '_');
    }

    /// <summary>
    /// 解析新建请求的数据库模式：未指定取配置默认值；非法字面量视为参数错误。
    /// </summary>
    private static TenantDatabaseMode ResolveDatabaseMode(string? requested, TenantDatabaseMode defaultMode)
    {
        return string.IsNullOrWhiteSpace(requested)
            ? defaultMode
            : Enum.TryParse<TenantDatabaseMode>(requested.Trim(), ignoreCase: true, out TenantDatabaseMode mode)
            && Enum.IsDefined(mode)
            ? mode
            : throw new ValidationException("databaseMode", "数据库模式须为 Shared 或 Isolated");
    }

    /// <summary>合并默认授权模块与必备模块（去重，保持稳定顺序）。</summary>
    private static List<string> ResolveInitialModuleCodes(
        IReadOnlyList<ModuleDefinitionRow> definitions, IReadOnlyList<string> defaults)
    {
        List<string> codes = [];
        foreach (string? code in defaults.Concat(definitions.Where(d => d.IsMandatory).Select(d => d.ModuleCode)))
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
    {
        return definitions.TryGetValue(moduleCode, out ModuleDefinitionRow? definition) ? definition.DisplayName : moduleCode;
    }

    /// <summary>新建租户成功日志。</summary>
    [LoggerMessage(Level = LogLevel.Information,
        Message = "门户新建租户 {TenantCode}（Id={TenantId}），初始管理员 {AdminAccount}，默认模块 {Modules}")]
    private partial void LogTenantCreated(string tenantCode, long tenantId, string adminAccount, string modules);
}
