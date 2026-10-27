/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.1.0 beta
 * 创建时间：2026-10-21 04:30:00 +08:00 中国标准时间 星期三
 * 修改时间：2026-10-26 23:20:00 +08:00 中国标准时间 星期一
 * 描述：Portal 模块 DI 注册扩展：PortalDbContext + 生产版 Resolver + Provisioning 仓储 + 升级任务仓储/服务
 *       租户 / 用户管理服务经 AddValidatedScoped 注册（ValidationDispatchProxy 方法入口自动校验）
 */

using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Qlx.Abstractions.Authorization;
using Qlx.Abstractions.Tenancy;
using Qlx.BuildingBlocks.Authorization;
using Qlx.BuildingBlocks.Configuration;
using Qlx.BuildingBlocks.Modules;
using Qlx.BuildingBlocks.MultiTenancy;
using Qlx.BuildingBlocks.Validation;
using Qlx.Modules.Portal.Services;

namespace Qlx.Modules.Portal;

/// <summary>
/// Portal 模块 DI 注册扩展。
/// </summary>
public static class PortalServiceCollectionExtensions
{
    /// <summary>
    /// 注册门户模块基础组件：PortalDbContext 固定连接串 + 生产版租户路由 Resolver + 租户预配仓储 + 开发期种子服务。
    /// <para>
    /// 必须在 <see cref="MultiTenancyServiceCollectionExtensions.AddQlxMultiTenancy"/> **之后** 调用，
    /// 覆盖其注册的开发期 <see cref="InMemoryTenantDatabaseResolver"/>。
    /// </para>
    /// </summary>
    /// <param name="services">服务集合。</param>
    /// <param name="configuration">配置根，绑定 <c>Qlx:Portal:PortalConnectionString</c>。</param>
    /// <param name="environment">Host 环境，用于控制开发期种子服务注册。</param>
    public static IServiceCollection AddQlxPortal(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment? environment = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var portalOptions = configuration.GetSection(PortalConnectionOptions.SectionName)
            .Get<PortalConnectionOptions>()
            ?? throw new InvalidOperationException($"缺少配置节 {PortalConnectionOptions.SectionName}");
        var portalConnStr = portalOptions.PortalConnectionString;
        if (string.IsNullOrWhiteSpace(portalConnStr))
        {
            throw new InvalidOperationException(
                $"缺少配置 {PortalConnectionOptions.SectionName}:{PortalConnectionOptions.PortalConnectionStringKey}");
        }

        // ======= Manifest 只读守卫拦截器（ModuleManifestSyncService 以外的 SaveChanges 将被拒绝）=======
        // 必须在 AddDbContextFactory 之前注册，确保 options callback 首次解析 IInterceptor 列表时可被发现。
        services.TryAddSingleton<ManifestDefinitionReadOnlyGuardInterceptor>();
        services.AddSingleton<IInterceptor>(sp => sp.GetRequiredService<ManifestDefinitionReadOnlyGuardInterceptor>());

        // 门户库 DbContext 工厂（固定连接串，跨租户共享）。
        // UseOpenIddict()：将 OpenIddict 默认实体并入 EF 模型（por_oidc_* 表）。
        // 拦截器由 options callback 惰性解析：GuardInterceptor + AuditSaveChangesInterceptor 等
        // 均在 DbContext 首次创建时从容器获取，避免启动期递归解析。
        services.AddDbContextFactory<PortalDbContext>((sp, options) =>
        {
            options.UseSqlServer(portalConnStr).UseOpenIddict();
            var interceptors = sp.GetServices<IInterceptor>();
            options.AddInterceptors(interceptors);
        });

        // 生产版租户数据库路由解析器（覆盖开发期 InMemory）
        services.AddSingleton<ITenantDatabaseResolver, PortalTenantDatabaseResolver>();

        // 租户预配仓储（被 TenantMigrationService 消费）
        services.AddScoped<ITenantProvisioningRepository, PortalTenantProvisioningRepository>();

        // 升级任务仓储 + 隔离巡检落库（同实例两接口，被 B5 升级流水线 / 周期巡检服务消费）
        services.AddScoped<PortalTenantUpgradeRepository>();
        services.AddScoped<ITenantUpgradeJobRepository>(
            sp => sp.GetRequiredService<PortalTenantUpgradeRepository>());
        services.AddScoped<ITenantIsolationAuditRepository>(
            sp => sp.GetRequiredService<PortalTenantUpgradeRepository>());
        services.AddScoped<IPortalTenantUpgradeService, PortalTenantUpgradeService>();

        // 门户系统参数服务（业务运行参数阈值 X 在线维护，Cache-Aside）
        services.AddScoped<Qlx.Abstractions.Modules.ISystemParameterProvider, PortalSystemParameterService>();

        // 外部身份提供方（SSO/OAuth2 Client，R-9）：启动期一次性装载到内存注册表；
        // 未配置任何提供方时注册表为空集，不注册、不报错（真实 IdP 联调延后）
        services.AddSingleton<ExternalIdentityProviderRegistry>();
        services.AddScoped<IExternalIdentityProviderSource, PortalExternalIdentityProviderSource>();
        services.AddHostedService<ExternalIdentityProviderLoader>();

        // 系统参数管理（阶段 1D 第一批）：仓储 + 管理服务（分层样板：Controller→Service→Repository）
        services.AddScoped<Repositories.ISystemParameterRepository, Repositories.SystemParameterRepository>();
        services.AddScoped<ISystemParameterAdminService, SystemParameterAdminService>();

        // 门户管理端（阶段 1D）：租户 / 用户 / 模块 / 工作台，共享同一 scoped PortalDbContext 保证新建租户单事务
        // 租户 / 用户服务经 AddValidatedScoped 注册：ValidationDispatchProxy 在接口方法入口自动校验
        // [Validate] 标记参数与实现 IValidatableObject 的 DTO（如 ResetPortalUserPasswordRequest 的密码策略）
        services.AddScoped<Repositories.IPortalTenantAdminRepository, Repositories.PortalTenantAdminRepository>();
        services.AddScoped<Repositories.IPortalUserAdminRepository, Repositories.PortalUserAdminRepository>();
        services.AddScoped<Repositories.IPortalModuleAdminRepository, Repositories.PortalModuleAdminRepository>();
        services.AddValidatedScoped<IPortalTenantAdminService, PortalTenantAdminService>();
        services.AddValidatedScoped<IPortalUserAdminService, PortalUserAdminService>();
        services.AddScoped<IPortalModuleAdminService, PortalModuleAdminService>();
        services.AddScoped<IPortalOverviewService, PortalOverviewService>();
        services.AddScoped<IPortalLoginAuditLogger, PortalLoginAuditLogger>();

        // 认证与会话服务（Controller 仅做 HTTP 转换与响应封装，业务规则在 Service 层）
        services.AddScoped<IPortalAuthenticationService, PortalAuthenticationService>();
        // 待激活成员首登激活器（X-6：密码校验通过即接受邀请 + MEMBER_ACTIVATE 审计）
        services.AddScoped<IPendingInviteActivator, PendingInviteActivator>();
        services.AddScoped<ICurrentUserService, CurrentUserService>();
        services.AddScoped<IPortalLogoutService, PortalLogoutService>();

        // 选择角色步骤（身份级 Token 按企业懒加载角色）
        services.AddScoped<ITenantMemberRoleService, TenantMemberRoleService>();

        // 新建租户预配参数（强类型 Options，配置节 Qlx:Portal:Provisioning）
        services.AddOptions<PortalProvisioningOptions>()
            .Bind(configuration.GetSection(PortalProvisioningOptions.SectionName))
            .ValidateDataAnnotations()
            .Validate(
                static options => Enum.IsDefined(options.DefaultDatabaseMode),
                $"{PortalProvisioningOptions.SectionName}:DefaultDatabaseMode 必须为 Shared 或 Isolated")
            .ValidateOnStart();

        // 门户审计配置（配置节 Qlx:Portal:Audit，B4 审计并库后按保留期清理）
        services.AddOptions<PortalAuditOptions>()
            .Bind(configuration.GetSection(PortalAuditOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // 跨模块只读查询契约实现（供 Bas 等业务模块依赖 Abstractions 接口消费 Portal 库数据）
        services.AddScoped<Qlx.Abstractions.Queries.ITenantMemberQuery, TenantMemberQuery>();
        services.AddScoped<Qlx.Abstractions.Queries.IModulePermissionQuery, ModulePermissionQuery>();
        services.AddScoped<Qlx.Abstractions.Queries.IPortalRolePermissionQuery, PortalRolePermissionQuery>();
        services.AddScoped<Qlx.Abstractions.Tenancy.ITenantMemberAdmin, TenantMemberAdmin>();

        // ======= ModuleManifest 同步服务（宿主启动期调用 SyncAllAsync 将 Manifest 物化到 Portal 库）=======
        // 依赖 IReadOnlyList<IModule>（AddQlxModules 以列表单例注册），Scoped 生命周期
        services.AddScoped<IModuleManifestSyncService, PortalManifestSyncService>();

        // 门户管理员授权策略：身份级 Token 的 is_portal_admin claim = 1
        services.AddAuthorization(options =>
        {
            options.AddPolicy(Auth.QlxAuthConstants.Policies.PortalAdmin, policy => policy
                .RequireAuthenticatedUser()
                .RequireClaim(Auth.QlxAuthConstants.Claims.PortalAdminFlag, PortalConstants.PortalAdminFlagValue));

            // 租户上下文前置策略：仅租户级 Token（携带 tenant_id claim）可访问租户侧 API，
            // 身份级 Token 统一 403（杜绝 TenantDbContext 500 内部细节直出）
            options.AddPolicy(Qlx.Abstractions.Constants.QlxPolicyNames.TenantContext, policy => policy
                .RequireAuthenticatedUser()
                .RequireClaim(Auth.QlxAuthConstants.Claims.TenantId));
        });

        // 权限点策略自动注册：按全部模块 Manifest 的 PermissionDefinitions 物化 Permission:{code} 策略
        services.AddSingleton<
            Microsoft.Extensions.Options.IConfigureOptions<Microsoft.AspNetCore.Authorization.AuthorizationOptions>,
            Qlx.BuildingBlocks.Authorization.PermissionPolicyConfigurer>();

        // Permission:xxx 通用授权 Handler——Controller 侧 [Authorize(Policy = "Permission:portal.tenant.view")]
        // 依赖策略内携带 PermissionRequirement(permissionCode)；各模块在各自 ConfigureServices 按需 AddPolicy。
        // Handler 区分租户/门户上下文，Admin 零计算旁路 + permissions claim Split 包含匹配。
        services.AddSingleton<IAuthorizationHandler, PermissionAuthorizationHandler>();

        // 端点授权规则基线（集中式授权映射，替代控制器硬编码 [Authorize]）
        services.AddSingleton<Qlx.Abstractions.Authorization.IEndpointPolicyMapProvider, PortalEndpointPolicyMap>();

        // 开发期种子后台服务：首次启动时填充默认租户 + 模块许可（幂等，已有租户则跳过）
        if (environment?.IsDevelopment() == true)
        {
            services.AddHostedService<PortalSeedService>();
        }

        // 门户角色管理（CRUD + 权限分配 + 用户-角色双向分配）
        services.AddScoped<IPortalRoleAdminService, PortalRoleAdminService>();
        services.AddScoped<IPortalRolePermissionAdminService, PortalRolePermissionAdminService>();
        services.AddScoped<IPortalUserRoleAdminService, PortalUserRoleAdminService>();

        return services;
    }
}
