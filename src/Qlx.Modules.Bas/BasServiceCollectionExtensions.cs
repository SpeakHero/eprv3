/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-21
 * 修改时间：2026-10-25 11:00:00 +08:00 中国标准时间 星期日
 * 描述：Bas 模块 DI 注册扩展——BasTenantDbContext + 仓储 + 服务 + 后台任务处理器 +
 *       组合导入模板注册表。BasTenantDbContext 由工厂注册，动态从 ITenantContext 取当前请求的 tenantId。
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

        // 阶段三：统一发号器 + 组织/岗位主数据 + 跨模块组织只读查询（读穿缓存）
        services.AddScoped<Services.Numbering.IBasCodeNumberGenerator, Services.Numbering.BasCodeNumberGenerator>();
        services.AddScoped<IOrgMasterDataService, Services.OrgMasterDataService>();
        services.AddScoped<IPositionMasterDataService, Services.PositionMasterDataService>();
        services.AddScoped<Qlx.Abstractions.Queries.IOrgQueryService, Services.OrgQueryService>();

        // 阶段三：商品分类 / 计量单位主数据 + 跨模块商品分类只读查询（读穿缓存）
        services.AddScoped<IItemCategoryService, Services.ItemCategoryService>();
        services.AddScoped<IUnitService, Services.UnitService>();
        services.AddScoped<Qlx.Abstractions.Queries.IProductCategoryQueryService, Services.ProductCategoryQueryService>();

        // 阶段三：供应商 / 客户主数据 + 跨模块只读查询（读穿缓存）
        services.AddScoped<ISupplierService, Services.SupplierService>();
        services.AddScoped<ICustomerService, Services.CustomerService>();
        services.AddScoped<Qlx.Abstractions.Queries.ISupplierQueryService, Services.SupplierQueryService>();
        services.AddScoped<Qlx.Abstractions.Queries.ICustomerQueryService, Services.CustomerQueryService>();

        // 阶段三：人员主数据 + 跨模块人员只读查询（读穿缓存，敏感字段走独立明文端点）
        services.AddScoped<IPersonService, Services.PersonService>();
        services.AddScoped<Qlx.Abstractions.Queries.IPersonQueryService, Services.PersonQueryService>();

        // 阶段三：数据字典 / 编码规则 / 变更日志（读穿缓存；变更日志经审计库只读查询）
        services.AddScoped<IDictionaryService, Services.DictionaryService>();
        services.AddScoped<ICodeRuleService, Services.CodeRuleService>();
        services.AddScoped<IChangeLogService, Services.ChangeLogService>();

        // 阶段三：导入导出（模板定义 Singleton + 行落点/导出数据源 Scoped + 服务层模板级权限二次校验）
        services.AddSingleton<Services.ImportExport.BasImportTemplateDefinitionProvider>();
        services.AddSingleton<Qlx.BuildingBlocks.ImportExport.IImportTemplateDefinitionProvider>(
            sp => sp.GetRequiredService<Services.ImportExport.BasImportTemplateDefinitionProvider>());

        services.AddScoped<Qlx.BuildingBlocks.ImportExport.IImportRowSink, Services.ImportExport.SupplierImportSink>();
        services.AddScoped<Qlx.BuildingBlocks.ImportExport.IImportRowSink, Services.ImportExport.CustomerImportSink>();
        services.AddScoped<Qlx.BuildingBlocks.ImportExport.IImportRowSink, Services.ImportExport.PersonImportSink>();
        services.AddScoped<Qlx.BuildingBlocks.ImportExport.IImportRowSink, Services.ImportExport.CategoryImportSink>();

        services.AddScoped<Qlx.BuildingBlocks.ImportExport.IExportDataProvider, Services.ImportExport.SupplierExportProvider>();
        services.AddScoped<Qlx.BuildingBlocks.ImportExport.IExportDataProvider, Services.ImportExport.CustomerExportProvider>();
        services.AddScoped<Qlx.BuildingBlocks.ImportExport.IExportDataProvider, Services.ImportExport.PersonExportProvider>();
        services.AddScoped<Qlx.BuildingBlocks.ImportExport.IExportDataProvider, Services.ImportExport.CategoryExportProvider>();

        services.AddScoped<Services.ImportExport.IBasImportExportService, Services.ImportExport.BasImportExportService>();

        // 审批完成事件：应用器 + 消费者（Worker 作用域内回填租户后应用挂起变更）
        services.AddScoped<Services.IBasApprovalApplier, Services.BasApprovalApplier>();
        services.AddScoped<
            Qlx.Abstractions.Events.IIntegrationEventConsumer<Qlx.Abstractions.Approvals.ApprovalCompletedEvent>,
            Services.Consumers.ApprovalCompletedEventConsumer>();

        // 当前成员数据范围解析（X-5：查询接口按角色 DefaultDataScope 过滤）
        services.AddHttpContextAccessor();
        services.AddScoped<Services.ICurrentMemberDataScopeResolver, Services.HttpCurrentMemberDataScopeResolver>();

        // 组织侧审批人来源（2E 审批中心：ROLE / DEPT_LEADER；Support 组合解析器经 DI 聚合，模块间零引用）
        services.AddScoped<Qlx.Abstractions.Approvals.IApprovalApproverSource, Services.BasApprovalApproverSource>();

        // 租户侧角色权限只读查询（Portal 认证服务签发租户级 Token 时消费）
        services.AddScoped<Qlx.Abstractions.Queries.ITenantRolePermissionQuery, Services.BasTenantRolePermissionQuery>();

        // 租户成员角色只读查询（Portal 选择角色步骤与按角色签发租户级 Token 时消费）
        services.AddScoped<Qlx.Abstractions.Queries.ITenantMemberRoleQuery, Services.BasTenantMemberRoleQuery>();

        // 端点授权规则基线（集中式授权映射，替代控制器硬编码 [Authorize]）
        services.AddSingleton<Qlx.Abstractions.Authorization.IEndpointPolicyMapProvider, BasEndpointPolicyMap>();

        // 后台任务处理器（Scoped：多租户场景按请求 / Worker 作用域解析，经 TaskCode 分发）
        services.AddScoped<Qlx.Abstractions.Jobs.IJobHandler, Jobs.SupplierCertExpiryJobHandler>();
        services.AddScoped<Qlx.Abstractions.Jobs.IJobHandler, Jobs.BasCacheCleanupJobHandler>();

        // 组合导入模板注册表（D8：本注册先于 AddQlxModules → AddQlxImportExport 的 TryAddSingleton
        // 空实现自动失效；各业务模块另行注册 IImportTemplateDefinitionProvider 即被汇总）
        services.AddSingleton<
            Qlx.BuildingBlocks.ImportExport.IImportTemplateRegistry,
            Qlx.BuildingBlocks.ImportExport.CompositeImportTemplateRegistry>();

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
