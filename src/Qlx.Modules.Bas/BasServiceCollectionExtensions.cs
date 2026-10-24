/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-21
 * 描述：Bas 模块 DI 注册扩展——BasTenantDbContext + 仓储 + 服务。
 *       BasTenantDbContext 由工厂注册，动态从 ITenantContext 取当前请求的 tenantId。
 */

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Qlx.Abstractions.Constants;
using Qlx.Abstractions.Tenancy;
using Qlx.BuildingBlocks.MultiTenancy;
using Qlx.Modules.Bas.Services;

namespace Qlx.Modules.Bas;

public static class BasServiceCollectionExtensions
{
    /// <summary>
    /// 注册 Bas 模块组件：BasTenantDbContext（动态路由）+ 迁移计划 + 仓储 + 服务。
    /// </summary>
    public static IServiceCollection AddQlxBas(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // BasTenantDbContext 工厂注册——每个请求 scope 内按 tenantId 动态构建
        // 关键：手动解析应用容器的 IInterceptor（AuditSaveChangesInterceptor 等）并注入，
        // 否则 TenantDbContext.OnConfiguring 无法解析拦截器（因为 DbContextOptionsBuilder 没 AddDbContext 注册）
        services.AddScoped<BasTenantDbContext>(sp =>
        {
            var tenantCtx = sp.GetRequiredService<ITenantContext>();
            var resolver = sp.GetRequiredService<ITenantDatabaseResolver>();
            var builder = new DbContextOptionsBuilder<BasTenantDbContext>();
            // 直接从请求 scope 解析 IInterceptor 列表并注入
            var interceptors = sp.GetServices<Microsoft.EntityFrameworkCore.Diagnostics.IInterceptor>();
            builder.AddInterceptors(interceptors);
            return new BasTenantDbContext(resolver, tenantCtx.TenantId ?? 0, builder.Options);
        });

        // TenantLogDbContext（租户库 log_login/log_operation 只读查询）——同 BasTenantDbContext 工厂模式按请求 tenantId 构建
        services.AddScoped<Qlx.BuildingBlocks.Support.Logs.TenantLogDbContext>(sp =>
        {
            var tenantCtx = sp.GetRequiredService<ITenantContext>();
            var resolver = sp.GetRequiredService<ITenantDatabaseResolver>();
            var builder = new DbContextOptionsBuilder<Qlx.BuildingBlocks.Support.Logs.TenantLogDbContext>();
            var interceptors = sp.GetServices<Microsoft.EntityFrameworkCore.Diagnostics.IInterceptor>();
            builder.AddInterceptors(interceptors);
            return new Qlx.BuildingBlocks.Support.Logs.TenantLogDbContext(resolver, tenantCtx.TenantId ?? 0, builder.Options);
        });

        // Bas 模块租户迁移计划（TenantMigrationService 启动期自动扫描）
        services.AddSingleton<ITenantMigrationPlan>(new BasTenantMigrationPlan());

        // 仓储层
        services.AddScoped<Repositories.IBasTenantRepository, Repositories.BasTenantRepository>();

        // 服务层
        services.AddScoped<ITenantAdminService, Services.TenantAdminService>();
        services.AddScoped<Qlx.Abstractions.Tenancy.ITenantSubjectQuery, Services.BasTenantSubjectQuery>();

        // 组织侧审批人来源（2E 审批中心：ROLE / DEPT_LEADER；Support 组合解析器经 DI 聚合，模块间零引用）
        services.AddScoped<Qlx.Abstractions.Approvals.IApprovalApproverSource, Services.BasApprovalApproverSource>();

        // 租户侧角色权限只读查询（Portal 认证服务签发租户级 Token 时消费）
        services.AddScoped<Qlx.Abstractions.Queries.ITenantRolePermissionQuery, Services.BasTenantRolePermissionQuery>();

        // 租户成员角色只读查询（Portal 选择角色步骤与按角色签发租户级 Token 时消费）
        services.AddScoped<Qlx.Abstractions.Queries.ITenantMemberRoleQuery, Services.BasTenantMemberRoleQuery>();

        return services;
    }
}

/// <summary>Bas 模块租户迁移计划。</summary>
internal sealed class BasTenantMigrationPlan : ITenantMigrationPlan
{
    public string ModuleCode => QlxModuleCodes.Bas;
    public Type DbContextType => typeof(BasTenantDbContext);
    public IReadOnlyList<string> DependsOn => [];
}
