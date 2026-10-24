/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.1 beta
 * 创建时间：2026-10-20 22:30:00 +08:00 中国标准时间 星期二
 * 修改时间：2026-10-23 11:40:00 +08:00 中国标准时间 星期五
 * 描述：ERPv3 宿主入口，装配四级配置、技术底座组件、多租户管线、模块与基础端点
 */

using Microsoft.EntityFrameworkCore;

using Qlx.Abstractions.Constants;
using Qlx.Abstractions.Tenancy;
using Qlx.BuildingBlocks.Api;
using Qlx.BuildingBlocks.Audit;
using Qlx.BuildingBlocks.Auth;
using Qlx.BuildingBlocks.Caching;
using Qlx.BuildingBlocks.Configuration;
using Qlx.BuildingBlocks.EventBus;
using Qlx.BuildingBlocks.Ids;
using Qlx.BuildingBlocks.Jobs;
using Qlx.BuildingBlocks.Licensing;
using Qlx.BuildingBlocks.Modules;
using Qlx.BuildingBlocks.MultiTenancy;
using Qlx.BuildingBlocks.Numbering;
using Qlx.BuildingBlocks.Security;
using Qlx.BuildingBlocks.Support.Logs;
using Qlx.BuildingBlocks.Web;
using Qlx.Host.OpenApi;
using Qlx.Modules.Bas;
using Qlx.Modules.Portal;

namespace Qlx.Host;

/// <summary>
/// 宿主程序入口。当前为 AllInOne 单体形态：门户与租户配置同时加载；
/// 拆分独立部署时按角色参数分别启动门户宿主与租户宿主。
/// 装配顺序固定：四级配置 → 多租户 → 缓存 → ID 体系 → 单据编号 →
/// 事件总线（Outbox）→ Data Protection → 门户模块 → 业务模块 → 健康检查。
/// </summary>
public static class Program
{
    /// <summary>
    /// 应用入口。
    /// </summary>
    /// <param name="args">命令行参数。</param>
    /// <returns>退出码对应的异步任务。</returns>
    public static async Task Main(string[] args)
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

        _ = builder.Configuration.AddQlxConfiguration(
            builder.Environment.ContentRootPath,
            builder.Environment.EnvironmentName,
            QlxHostRole.AllInOne);

        _ = builder.Services
            .AddQlxAudit(builder.Configuration)
            .AddQlxHttpCurrentUserContext()
            .AddQlxMultiTenancy(builder.Configuration, builder.Environment)
            .AddQlxTenantLog(builder.Configuration)
            .AddQlxCaching(builder.Configuration)
            .AddQlxIds(builder.Configuration)
            .AddQlxNumbering(builder.Configuration)
            .AddQlxEventBus(builder.Configuration)
            .AddQlxDataProtection(builder.Configuration)
            .AddQlxPortal(builder.Configuration, builder.Environment)
            .AddQlxBas()
            .AddQlxAuth(builder.Configuration, builder.Environment)
            .AddQlxJobs(builder.Configuration)
            .AddQlxModules(builder.Configuration)
            .AddQlxCors(builder.Configuration);

        // 认证端点 / 业务 API 控制器（OpenIddict Token/Revoke 端点直通 + /api/portal/me）
        // 全局 JSON 约定：业务 Id（long / long? 且命名符合 Id 约定）由 QlxIdJsonMetadata 自动混淆；
        // 非 Id 语义的 long（Total / RowsCopied / Value 等计数指标）保持数值。
        // 全局参数绑定：路由/查询中的 long / long? 由 HashidsLongModelBinder 自动解码。
        _ = builder.Services.AddControllers()
            .AddApplicationPart(typeof(Qlx.Modules.Portal.PortalDbContext).Assembly)
            .AddApplicationPart(typeof(Qlx.Modules.Bas.BasTenantDbContext).Assembly)
            .AddApplicationPart(typeof(Qlx.Modules.Support.SupportTenantDbContext).Assembly)
            .AddQlxJsonSerialization()
            .AddQlxIdModelBinding()
            .AddQlxOperationAudit();

        // OpenAPI 文档（仅 Development 注册；Bearer Scheme / Hashids schema 修正 / Produces 补全见 QlxOpenApiExtensions）
        _ = builder.Services.AddQlxOpenApi(builder.Environment);

        _ = builder.Services.AddHealthChecks();

        WebApplication app = builder.Build();

        // ======= workerId 租约预初始化（必须先于启动期迁移）=======
        // 启动期迁移创建 PortalDbContext 时会经 IInterceptor 解析 AuditSaveChangesInterceptor →
        // ISnowflakeIdGenerator，该链路早于 WorkerIdLeaseHostedService（app.RunAsync 才启动），
        // 故先显式获取租约；HostedService 对已初始化场景幂等跳过，全程仅获取一次。
        await using (AsyncServiceScope scope0 = app.Services.CreateAsyncScope())
        {
            IWorkerIdLeaseHolder leaseHolder = scope0.ServiceProvider.GetRequiredService<IWorkerIdLeaseHolder>();
            if (leaseHolder.WorkerId is null)
            {
                IWorkerIdLease workerIdLease = scope0.ServiceProvider.GetRequiredService<IWorkerIdLease>();
                leaseHolder.WorkerId = await workerIdLease
                    .AcquireAsync(CancellationToken.None)
                    .ConfigureAwait(false);
#pragma warning disable CA1848 // 启动期一次性日志
                app.Logger.LogInformation("✓ workerId 租约预初始化完成（workerId={WorkerId}）", leaseHolder.WorkerId);
#pragma warning restore CA1848
            }
        }

        // ======= 启动期数据库迁移（替代 EnsureCreated，应用 EF Migration + HasData 种子）=======

        // 审计库
        await using (AsyncServiceScope scope1 = app.Services.CreateAsyncScope())
        {
            IDbContextFactory<AuditDbContext> auditFactory = scope1.ServiceProvider.GetRequiredService<IDbContextFactory<AuditDbContext>>();
            await using AuditDbContext auditDb = await auditFactory.CreateDbContextAsync().ConfigureAwait(false);
            await auditDb.Database.MigrateAsync().ConfigureAwait(false);
        }

        // 门户库（HasData 会插入 12 个 ModuleDefinitions + 8 个 PermissionDefinitions）
        await using (AsyncServiceScope scope2 = app.Services.CreateAsyncScope())
        {
            IDbContextFactory<PortalDbContext> portalFactory = scope2.ServiceProvider.GetRequiredService<IDbContextFactory<PortalDbContext>>();
            await using PortalDbContext portalDb = await portalFactory.CreateDbContextAsync().ConfigureAwait(false);
            await portalDb.Database.MigrateAsync().ConfigureAwait(false);

            // v1.6：Manifest 同步——把所有业务模块的权限点/菜单/导航分组从 IModule.Manifest 物化到 Portal 库
            // 幂等 Upsert + 软删 Manifest 已移除项；必须在 MigrateAsync 之后（确保新表已创建）
            IModuleManifestSyncService manifestSync = scope2.ServiceProvider.GetRequiredService<Qlx.BuildingBlocks.Modules.IModuleManifestSyncService>();
            await manifestSync.SyncAllAsync(CancellationToken.None).ConfigureAwait(false);
#pragma warning disable CA1848 // 启动期一次性日志
            app.Logger.LogInformation("✓ Manifest 同步完成（权限点/菜单/导航分组已物化到门户库）");
#pragma warning restore CA1848

            // 共享库 / 租户共享库：按 ITenantMigrationPlan 通用遍历，应用锁内去重迁移一次（不建库、不删库）
            TenantMigrationRunner runner = scope2.ServiceProvider.GetRequiredService<TenantMigrationRunner>();

            // B4-01：共享库存在性检查。开发期自动建库；生产环境禁止自动建库（运维预配）
            try
            {
                await runner.EnsureSharedDatabaseExistsAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                if (builder.Environment.IsDevelopment())
                {
#pragma warning disable CA1848 // 启动期一次性日志，无需 LoggerMessage 委托
                    app.Logger.LogWarning(ex, "共享库自动创建失败（开发期可接受，租户业务不受影响）");
#pragma warning restore CA1848
                }
                else
                {
                    throw new InvalidOperationException("共享库不存在且生产环境禁止自动建库。请先由运维预配 qlx_tenant_shared 库。", ex);
                }
            }

            try
            {
                await runner.MigrateSharedDatabaseAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
#pragma warning disable CA1848 // 启动期一次性日志，无需 LoggerMessage 委托
                app.Logger.LogWarning(ex, "共享库迁移失败（库可能尚未创建，由平台预配处理）");
#pragma warning restore CA1848
            }

            // 独立租户库：按租户通用遍历（新增模块无需再改本文件）；共享租户只在共享库迁移一次
            ITenantDatabaseResolver resolver = scope2.ServiceProvider.GetRequiredService<ITenantDatabaseResolver>();
            // 关键：先预热 resolver 缓存，否则 Resolve 因缓存未命中而抛 InvalidOperationException
            await resolver.PreloadAsync(CancellationToken.None).ConfigureAwait(false);

            List<long> isolatedTenantIds = await portalDb.TenantDatabases
                .AsNoTracking()
                .Where(d => d.DatabaseMode == TenantDatabaseMode.Isolated)
                .Select(d => d.TenantId)
                .ToListAsync()
                .ConfigureAwait(false);

            foreach (var tenantId in isolatedTenantIds)
            {
                try
                {
                    await runner.MigrateTenantAsync(
                        tenantId, resolver.Resolve(tenantId), CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
#pragma warning disable CA1848 // 启动期一次性日志，无需 LoggerMessage 委托
                    app.Logger.LogWarning(ex, "Tenant migration failed for {TenantId}", tenantId);
#pragma warning restore CA1848
                }
            }
        }

        // ======= 请求管线 =======
        // 顺序：异常处理 → CORS（须在 UseRouting 前，以便提前处理 OpenIddict /connect/* 的预检）→ 认证 → 租户上下文（手动 AuthenticateAsync 不依赖 UseAuthentication）→ 路由 → 授权 → Token 黑名单 → 模块许可过滤
        _ = app.UseExceptionHandlerMiddleware();
        _ = app.UseQlxCors();
        _ = app.UseAuthentication();
        _ = app.UseTenantContext();
        _ = app.UseRouting();
        _ = app.UseAuthorization();
        _ = app.UseTokenBlacklist();
        _ = app.UseModuleLicenseFilter();
        // Hangfire Dashboard（仅 Development 环境注册；需已认证用户）
        _ = app.UseQlxJobsDashboard();
        // OpenAPI JSON 与 Scalar UI（仅 Development 映射，生产 404）
        _ = app.MapQlxApiDocs();
        _ = app.MapControllers();
        _ = app.MapHealthChecks(QlxHostConstants.HealthEndpointPath);

        await app.RunAsync().ConfigureAwait(false);
    }
}
