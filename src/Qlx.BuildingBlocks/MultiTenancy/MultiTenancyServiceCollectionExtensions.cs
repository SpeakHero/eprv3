/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-20 23:00:00 +08:00 中国标准时间 星期二
 * 修改时间：2026-10-22 16:30:00 +08:00 中国标准时间 星期四
 * 描述：多租户 DI 注册扩展：Resolver 占位 + 预热 + 迁移后台服务 + 升级流水线与隔离周期巡检
 */

using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Qlx.Abstractions.Tenancy;
using Qlx.BuildingBlocks.MultiTenancy.Isolation;

namespace Qlx.BuildingBlocks.MultiTenancy;

/// <summary>
/// 多租户基础设施 DI 注册扩展。
/// </summary>
/// 
public static class MultiTenancyServiceCollectionExtensions
{     
    /// <param name="services">服务集合。</param>
    extension(IServiceCollection services)
    {
        /// <summary>
        /// 注册多租户基础组件：ITenantContext 请求作用域 + Resolver + 连接预热后台服务 + 租户迁移后台服务 + 强类型配置。
        /// <para>
        /// 开发环境（<c>ASPNETCORE_ENVIRONMENT=Development</c>）注册 <see cref="InMemoryTenantDatabaseResolver"/> 占位；
        /// 生产环境由 Portal 模块在 <c>AddQlxPortal</c> 中显式覆盖为 <c>PortalTenantDatabaseResolver</c>。
        /// </para>
        /// </summary>
        /// <param name="configuration">配置根，绑定 <see cref="TenantMigrationOptions.SectionName"/>。</param>
        /// <param name="environment">宿主环境（用于判定开发/生产）。</param>
        public IServiceCollection AddQlxMultiTenancy(
            IConfiguration configuration,
            IHostEnvironment environment)
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(configuration);
            ArgumentNullException.ThrowIfNull(environment);

            services.AddScoped<ITenantContext, RequestTenantContext>();

            // 强类型 TenantMigrationOptions + 启动校验
            services.AddOptions<TenantMigrationOptions>()
                .Bind(configuration.GetSection(TenantMigrationOptions.SectionName))
                .ValidateDataAnnotations()
                .ValidateOnStart();

            // 租户共享库连接配置：共享租户统一使用本配置，不读取路由行凭据
            services.AddOptions<SharedDatabaseOptions>()
                .Bind(configuration.GetSection(SharedDatabaseOptions.SectionName))
                .ValidateDataAnnotations()
                .ValidateOnStart();

            // 租户数据库模式默认值（预留 PlanMappings，本期不读取）
            services.AddOptions<TenantDatabaseModeOptions>()
                .Bind(configuration.GetSection(TenantDatabaseModeOptions.SectionName))
                .Validate(
                    static options => Enum.IsDefined(options.DefaultMode),
                    "Qlx:Tenancy:DatabaseMode:DefaultMode 必须为 Shared 或 Isolated")
                .ValidateOnStart();

            // TenantId=0 开发兜底连接（默认关闭，生产不得启用）
            services.AddOptions<TenantConnectionFallbackOptions>()
                .Bind(configuration.GetSection(TenantConnectionFallbackOptions.SectionName))
                .Validate(
                    static options => !options.Enabled || !string.IsNullOrWhiteSpace(options.ConnectionString),
                    "Qlx:Tenancy:DevelopmentFallback 启用时必须配置 ConnectionString")
                .ValidateOnStart();

            // 门户库连接配置（控制面固定连接串，供四库启动校验与门户模块共用）
            services.AddOptions<Configuration.PortalConnectionOptions>()
                .Bind(configuration.GetSection(Configuration.PortalConnectionOptions.SectionName))
                .ValidateDataAnnotations()
                .ValidateOnStart();

            // 平台共享库（glb_*/Hangfire）：本期只落配置与校验，业务随 2D 落地
            services.AddOptions<PlatformSharedOptions>()
                .Bind(configuration.GetSection(PlatformSharedOptions.SectionName))
                .ValidateDataAnnotations()
                .ValidateOnStart();

            if (environment.IsDevelopment())
            {
                // 开发期：内存占位，由 PortalTenantDatabaseResolver 或手工 Register 覆盖
                services.AddSingleton<ITenantDatabaseResolver, InMemoryTenantDatabaseResolver>();
            }
            else
            {
                // 生产期：允许 Portal 模块的 AddSingleton<ITenantDatabaseResolver, PortalTenantDatabaseResolver> 覆盖
                services.TryAddSingleton<ITenantDatabaseResolver, InMemoryTenantDatabaseResolver>();
            }

            // 迁移原语（启动装配与后台预配共用）：应用锁去重、按模块历史表、禁止删库
            services.AddSingleton<TenantMigrationRunner>();

            // 租户写入守卫（L1 写入侧）：新增自动填 TenantId、禁止篡改、拒绝跨租户 Update/Delete
            services.TryAddSingleton<TenantWriteGuardInterceptor>();
            services.AddSingleton<IInterceptor>(sp => sp.GetRequiredService<TenantWriteGuardInterceptor>());

            // 租户 RLS（L2 数据库层，默认关闭）：提供程序按配置选择，会话拦截器在连接打开时写 TenantId
            services.AddOptions<Rls.TenantRlsOptions>()
                .Bind(configuration.GetSection(Rls.TenantRlsOptions.SectionName))
                .ValidateDataAnnotations()
                .ValidateOnStart();

            services.TryAddSingleton<Rls.ITenantRlsProvider>(
                sp => Rls.TenantRlsProviderFactory.Create(
                    sp.GetRequiredService<IOptions<Rls.TenantRlsOptions>>().Value));
            services.TryAddSingleton<Rls.TenantRlsInstaller>();
            services.AddSingleton<IInterceptor>(
                sp => new TenantSessionInterceptor(sp.GetRequiredService<Rls.ITenantRlsProvider>()));

            // HostedService：启动期预热 + 运行期迁移
            services.AddHostedService<TenantConnectionPreloadService>();
            services.AddHostedService<TenantMigrationService>();

            // 租户连接池闸门：50+ 租户场景下限制总连接数
            services.AddSingleton<ITenantConnectionPool, TenantConnectionPool>();

            // 隔离巡检服务（L3）：手动触发，产出报告
            services.TryAddSingleton<TenantIsolationAuditor>();

            // B4-08：四库启动校验（生产环境断言四类连接串账号互不相同）
            services.AddHostedService<Configuration.FourDatabaseStartupValidator>();

            // ===== B5：共享库升级独立库 + 隔离周期巡检 =====

            // 升级流水线配置（扫描周期 / 复制批量 / 抽样校验 / 清理保留期）
            services.AddOptions<Upgrade.TenantUpgradeOptions>()
                .Bind(configuration.GetSection(Upgrade.TenantUpgradeOptions.SectionName))
                .ValidateDataAnnotations()
                .ValidateOnStart();

            // 隔离周期巡检配置（开关 / 周期）
            services.AddOptions<Upgrade.TenantIsolationAuditOptions>()
                .Bind(configuration.GetSection(Upgrade.TenantIsolationAuditOptions.SectionName))
                .ValidateDataAnnotations()
                .ValidateOnStart();

            // 升级流水线组件（无 Scoped 依赖，Scoped 仓储经方法参数传入）
            services.TryAddSingleton<Upgrade.TenantDataCopier>();
            services.TryAddSingleton<Upgrade.TenantDataVerifier>();
            services.TryAddSingleton<Upgrade.TenantUpgradePipeline>();

            // 升级调度 + 共享库残留清理 + 隔离周期巡检后台服务
            services.AddHostedService<Upgrade.TenantUpgradeJobDispatcher>();
            services.AddHostedService<Upgrade.TenantSharedDataCleanService>();
            services.AddHostedService<Upgrade.TenantIsolationAuditJobService>();

            return services;
        }
    }
}
