/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-20 17:00:00 +08:00 中国标准时间 星期一
 * 修改时间：2026-10-20 17:00:00 +08:00 中国标准时间 星期一
 * 描述：门户开发期种子后台服务：首次运行时填充默认租户 + 数据库连接 + 模块许可
 */

using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenIddict.Abstractions;
using Qlx.Abstractions.Constants;
using Qlx.Abstractions.Domain;
using Qlx.BuildingBlocks.Ids;
using Qlx.Modules.Portal.Auth;
using Qlx.Modules.Portal.Domain.Configuration;
using Qlx.Modules.Portal.Domain.Identity;
using Qlx.Modules.Portal.Domain.Tenancy;

namespace Qlx.Modules.Portal.Services;

/// <summary>
/// 门户开发期种子后台服务。首次 Host 启动时，若门户库为空（没有租户），
/// 自动填充一条默认开发租户 + TenantDatabase + 所有必备模块的 TenantModule 许可。
/// <para>
/// 注意：此服务仅在 <c>ASPNETCORE_ENVIRONMENT=Development</c> 环境注册。
/// 在 AddAsync 之前手动调用 <see cref="Entity.AssignId"/> 分配雪花 Id，
/// 避免多个新实体 Id=0 导致 EF Core ChangeTracker 冲突。
/// </para>
/// </summary>
/// <remarks>
/// 构造函数。雪花生成器延迟到 ExecuteAsync 内解析（避免 Host StartAsync 阶段租约未初始化）。
/// </remarks>
/// <param name="serviceProvider">服务提供者。</param>
/// <param name="logger">日志器。</param>
public sealed partial class PortalSeedService(
    IServiceProvider serviceProvider,
    ILogger<PortalSeedService> logger) : BackgroundService
{
    private readonly IServiceProvider _serviceProvider = serviceProvider;
    private readonly ILogger<PortalSeedService> _logger = logger;


    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield(); // 不阻塞 Host 启动

        try
        {
            await using var scope = _serviceProvider.CreateAsyncScope();
            var provider = scope.ServiceProvider;
            var snowflake = provider.GetRequiredService<ISnowflakeIdGenerator>();
            await using var db = await provider
                .GetRequiredService<IDbContextFactory<PortalDbContext>>()
                .CreateDbContextAsync(stoppingToken).ConfigureAwait(false);

            // 1. 已存在租户则跳过租户种子（幂等）
            var tenantCount = await db.Tenants.CountAsync(stoppingToken).ConfigureAwait(false);
            if (tenantCount > 0)
            {
                LogSeedSkipped(tenantCount);
            }
            else
            {
                LogSeedStart();

                // 2. 查询必备模块定义（AsNoTracking 避免 ChangeTracker 干扰）
                var mandatoryCodes = QlxModuleCodes.Mandatory.ToArray();
                var seedModules = await db.ModuleDefinitions
                    .AsNoTracking()
                    .Where(m => mandatoryCodes.Contains(m.ModuleCode))
                    .ToListAsync(stoppingToken).ConfigureAwait(false);

                // 3. 创建实体 + 手动分配雪花 Id（避免 AddAsync 阶段多个 Id=0 同类型实体冲突）
                var tenant = new Tenant(PortalConstants.Seed.TenantCode, PortalConstants.Seed.TenantName)
                {
                    ShortName = PortalConstants.Seed.TenantShortName,
                    TaxNo = PortalConstants.Seed.TaxNo,
                    Industry = PortalConstants.Seed.Industry,
                    ContactName = PortalConstants.Seed.ContactName,
                    ContactEmail = PortalConstants.Seed.ContactEmail,
                    PlanCode = PortalConstants.Seed.PlanCode,
                    SubscribedAt = DateTimeOffset.UtcNow,
                    SubscriptionExpiresAt = DateTimeOffset.UtcNow.AddYears(PortalConstants.Seed.SubscriptionYears),
                };
                tenant.AssignId(snowflake.NextId());

                // 存量开发租户沿用独立库：显式标记 Isolated，保持既有解析行为不变
                tenant.ChangeDatabaseMode(Qlx.Abstractions.Tenancy.TenantDatabaseMode.Isolated);

                var tenantDb = new TenantDatabase(
                    tenant,
                    PortalConstants.ProvisioningDefaults.DbHost,
                    PortalConstants.ProvisioningDefaults.DbPort,
                    PortalConstants.Seed.DatabaseName);
                tenantDb.AssignId(snowflake.NextId());

                var tenantModules = new List<TenantModule>();
                foreach (var module in seedModules)
                {
                    var tm = new TenantModule(tenant, module.ModuleCode, module.CurrentVersion);
                    tm.AssignId(snowflake.NextId());
                    tenantModules.Add(tm);
                }

                // 4. 组装导航关系
                tenant.SetDatabase(tenantDb);
                foreach (var tm in tenantModules) tenant.AddModule(tm);

                // 5. Add + SaveChanges（审计拦截器填充审计字段）
                await db.Tenants.AddAsync(tenant, stoppingToken).ConfigureAwait(false);
                var saved = await db.SaveChangesAsync(stoppingToken).ConfigureAwait(false);

                // 6. 激活租户
                tenant.Activate();
                await db.SaveChangesAsync(stoppingToken).ConfigureAwait(false);

                LogSeedDone(saved, tenantModules.Count);
            }

            // ======= 身份体系种子（幂等，与租户种子独立执行）=======
            await SeedIdentityAsync(db, provider, snowflake, stoppingToken).ConfigureAwait(false);

            // ======= 系统参数种子（幂等：键已存在则跳过，后续由系统管理在线维护）=======
            await SeedSystemParametersAsync(db, provider, snowflake, stoppingToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogSeedFailed(ex);
        }
    }

    /// <summary>
    /// 身份体系种子：qlxadmin 门户管理员 + 开发租户 TENANT_ADMIN 成员 + erp-web OpenIddict 公共客户端。
    /// 全部按唯一键幂等（已存在则跳过）。
    /// </summary>
    private async Task SeedIdentityAsync(
        PortalDbContext db,
        IServiceProvider provider,
        ISnowflakeIdGenerator snowflake,
        CancellationToken stoppingToken)
    {
        var options = provider.GetRequiredService<IOptions<QlxAuthOptions>>().Value;
        var passwordHasher = provider.GetRequiredService<IPasswordHasher<PortalUser>>();

        // 1. qlxadmin 门户用户（密码来自配置，仅开发期；生产走密钥引用）
        var admin = await db.PortalUsers
            .SingleOrDefaultAsync(u => u.LoginAccount == PortalConstants.Seed.AdminAccount, stoppingToken).ConfigureAwait(false);

        if (admin is null)
        {
            if (string.IsNullOrWhiteSpace(options.DevSeedAdminPassword))
            {
                LogIdentitySeedFailed(new InvalidOperationException(
                    "开发期种子管理员密码未配置（Qlx:Portal:Auth:DevSeedAdminPassword），跳过身份种子"));
                return;
            }

            admin = new PortalUser(PortalConstants.Seed.AdminAccount, PortalConstants.Seed.AdminDisplayName, isPortalAdmin: true);
            admin.AssignId(snowflake.NextId());
            admin.SetPasswordHash(passwordHasher.HashPassword(admin, options.DevSeedAdminPassword));
            await db.PortalUsers.AddAsync(admin, stoppingToken).ConfigureAwait(false);
            await db.SaveChangesAsync(stoppingToken).ConfigureAwait(false);

            LogIdentityUserSeeded();
        }

        // 2. 租户管理员成员（挂第一个租户；PendingInvite 角色已激活为 TenantAdmin）
        var firstTenantId = await db.Tenants
            .AsNoTracking()
            .OrderBy(t => t.Id)
            .Select(t => t.Id)
            .FirstOrDefaultAsync(stoppingToken).ConfigureAwait(false);

        if (firstTenantId > 0
            && !await db.TenantMembers.AnyAsync(
                m => m.TenantId == firstTenantId && m.PortalUserId == admin.Id, stoppingToken).ConfigureAwait(false))
        {
            var member = new TenantMember(firstTenantId, admin.Id, TenantMemberRole.TenantAdmin);
            member.AssignId(snowflake.NextId());
            await db.TenantMembers.AddAsync(member, stoppingToken).ConfigureAwait(false);
            await db.SaveChangesAsync(stoppingToken).ConfigureAwait(false);

            LogIdentityMemberSeeded();
        }

        // 3. erp-web OpenIddict 公共客户端（幂等：不存在则创建，已存在则补齐新增授权类型许可）
        var applicationManager = provider.GetRequiredService<IOpenIddictApplicationManager>();
        var app = await applicationManager.FindByClientIdAsync(OpenIddictConfigurator.SpaClientId, stoppingToken)
            .ConfigureAwait(false);
        if (app is null)
        {
            await applicationManager.CreateAsync(new OpenIddictApplicationDescriptor
            {
                ClientId = OpenIddictConfigurator.SpaClientId,
                ClientType = OpenIddictConstants.ClientTypes.Public,
                DisplayName = PortalConstants.Seed.ClientDisplayName,
                Permissions =
                {
                    OpenIddictConstants.Permissions.Endpoints.Token,
                    OpenIddictConstants.Permissions.Endpoints.Revocation,
                    OpenIddictConstants.Permissions.GrantTypes.RefreshToken,
                    OpenIddictConstants.Permissions.Prefixes.Scope + OpenIddictConstants.Scopes.OfflineAccess,
                    OpenIddictConstants.Permissions.Prefixes.GrantType + QlxAuthConstants.GrantTypes.PortalIdentity,
                    OpenIddictConstants.Permissions.Prefixes.GrantType + QlxAuthConstants.GrantTypes.TenantContext,
                    OpenIddictConstants.Permissions.Prefixes.GrantType + QlxAuthConstants.GrantTypes.TenantSilent,
                    OpenIddictConstants.Permissions.Prefixes.Audience + options.Audience,
                },
            }, stoppingToken).ConfigureAwait(false);

            LogIdentityClientSeeded();
        }
        else
        {
            // 已存在客户端：补齐新增的静默续期授权类型许可（版本升级兼容）
            var existingPermissions = await applicationManager.GetPermissionsAsync(app, stoppingToken)
                .ConfigureAwait(false);
            var silentPermission = OpenIddictConstants.Permissions.Prefixes.GrantType
                + QlxAuthConstants.GrantTypes.TenantSilent;
            if (!existingPermissions.Contains(silentPermission))
            {
                var clientId = await applicationManager.GetClientIdAsync(app, stoppingToken).ConfigureAwait(false);
                var clientType = await applicationManager.GetClientTypeAsync(app, stoppingToken).ConfigureAwait(false);
                var displayName = await applicationManager.GetDisplayNameAsync(app, stoppingToken).ConfigureAwait(false);

                var descriptor = new OpenIddictApplicationDescriptor
                {
                    ClientId = clientId,
                    ClientType = clientType,
                    DisplayName = displayName,
                };
                foreach (var p in existingPermissions)
                {
                    descriptor.Permissions.Add(p);
                }

                descriptor.Permissions.Add(silentPermission);
                await applicationManager.UpdateAsync(app, descriptor, stoppingToken).ConfigureAwait(false);
                LogIdentityClientPermissionUpdated();
            }
        }
    }

    /// <summary>
    /// 系统参数种子：登录安全阈值默认值（与 config 示例值一致；参数表优先，config 仅兜底）。
    /// 幂等：键已存在则跳过。
    /// </summary>
    private async Task SeedSystemParametersAsync(
        PortalDbContext db,
        IServiceProvider provider,
        ISnowflakeIdGenerator snowflake,
        CancellationToken stoppingToken)
    {
        var options = provider.GetRequiredService<IOptions<QlxAuthOptions>>().Value;
        var defaults = new (string Key, string Value, string Description)[]
        {
            (SystemParameterKeys.AuthMaxPasswordFailAttempts,
                options.MaxPasswordFailAttempts.ToString(System.Globalization.CultureInfo.InvariantCulture),
                "密码连续错误锁定阈值（次），窗口内累计达标即锁定"),
            (SystemParameterKeys.AuthPasswordFailWindowMinutes,
                options.PasswordFailWindowMinutes.ToString(System.Globalization.CultureInfo.InvariantCulture),
                "密码失败计数窗口（分钟），窗口结束自动清零解锁"),
        };

        foreach (var (key, value, description) in defaults)
        {
            if (await db.SystemParameters.AnyAsync(p => p.ParamKey == key, stoppingToken).ConfigureAwait(false))
            {
                continue;
            }

            var parameter = new SystemParameter(key, value, description);
            parameter.AssignId(snowflake.NextId());
            await db.SystemParameters.AddAsync(parameter, stoppingToken).ConfigureAwait(false);
            await db.SaveChangesAsync(stoppingToken).ConfigureAwait(false);

            LogParameterSeeded(key);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "门户库已有 {TenantCount} 个租户，跳过租户种子数据填充")]
    private partial void LogSeedSkipped(int tenantCount);

    [LoggerMessage(Level = LogLevel.Information, Message = "开始填充门户库开发期种子数据")]
    private partial void LogSeedStart();

    [LoggerMessage(Level = LogLevel.Information, Message = "门户库种子数据填充完成，保存 {Saved} 条记录，许可 {LicensedModules} 个模块")]
    private partial void LogSeedDone(int saved, int licensedModules);

    [LoggerMessage(Level = LogLevel.Information, Message = "种子用户 qlxadmin 已创建")]
    private partial void LogIdentityUserSeeded();

    [LoggerMessage(Level = LogLevel.Information, Message = "种子成员 qlxadmin → 租户管理员已创建")]
    private partial void LogIdentityMemberSeeded();

    [LoggerMessage(Level = LogLevel.Information, Message = "OpenIddict 客户端 erp-web 已创建")]
    private partial void LogIdentityClientSeeded();

    [LoggerMessage(Level = LogLevel.Information, Message = "OpenIddict 客户端 erp-web 已补齐新增授权类型许可")]
    private partial void LogIdentityClientPermissionUpdated();

    [LoggerMessage(Level = LogLevel.Information, Message = "系统参数 {ParamKey} 已写入默认值")]
    private partial void LogParameterSeeded(string paramKey);

    [LoggerMessage(Level = LogLevel.Warning, Message = "身份体系种子失败（详细异常见日志）")]
    private partial void LogIdentitySeedFailed(Exception ex);

    [LoggerMessage(Level = LogLevel.Warning, Message = "门户库种子数据填充失败（详细异常见日志）")]
    private partial void LogSeedFailed(Exception ex);
}
