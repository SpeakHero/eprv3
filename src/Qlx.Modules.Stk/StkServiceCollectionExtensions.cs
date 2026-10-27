/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-26 14:00:00 +08:00 中国标准时间 星期一
 * 描述：Stk 模块 DI 注册扩展——StkTenantDbContext 工厂 + 迁移计划 + 授权策略 + 任务处理器（业务服务逐批追加）
 */

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Qlx.Abstractions.Constants;
using Qlx.Abstractions.Tenancy;
using Qlx.BuildingBlocks.MultiTenancy;
using Qlx.Modules.Stk.Jobs;

namespace Qlx.Modules.Stk;

/// <summary>Stk 模块 DI 注册扩展。</summary>
public static class StkServiceCollectionExtensions
{
    /// <summary>
    /// 注册 Stk 模块组件：StkTenantDbContext（动态路由）+ 迁移计划 + 策略 + 任务。
    /// </summary>
    /// <param name="services">服务集合。</param>
    /// <returns>服务集合。</returns>
    public static IServiceCollection AddQlxStk(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddScoped<StkTenantDbContext>(sp =>
        {
            var tenantCtx = sp.GetRequiredService<ITenantContext>();
            var resolver = sp.GetRequiredService<ITenantDatabaseResolver>();
            var builder = new DbContextOptionsBuilder<StkTenantDbContext>();
            var interceptors = sp.GetServices<Microsoft.EntityFrameworkCore.Diagnostics.IInterceptor>();
            builder.AddInterceptors(interceptors);
            return new StkTenantDbContext(resolver, tenantCtx.TenantId ?? 0, builder.Options);
        });

        services.AddSingleton<ITenantMigrationPlan>(new StkTenantMigrationPlan());

        // 仓储层
        services.AddScoped<Repositories.IStkTenantRepository, Repositories.StkTenantRepository>();

        // 服务层
        services.AddScoped<Services.IItemService, Services.ItemService>();
        services.AddScoped<Services.Numbering.IStkItemCodeGenerator, Services.Numbering.StkItemCodeGenerator>();

        // 库存台账：唯一写入口（A-5）+ 只读查询
        services.AddScoped<Qlx.Abstractions.Stock.IStockTxnCommandService, Services.StockLedgerService>();
        services.AddScoped<Services.IInventoryQueryService, Services.InventoryQueryService>();

        // 跨模块库存只读查询契约（Wms 作业/下游读取台账与批次）
        services.AddScoped<Qlx.Abstractions.Queries.IStockQueryService, Services.StockQueryService>();
        services.AddScoped<Qlx.Abstractions.Queries.IStockAlertScanQueryService, Services.StockAlertScanQueryService>();
        services.AddScoped<Qlx.Abstractions.Queries.IItemQueryService, Services.ItemQueryService>();

        // 期初建账 / 成本调整（审批通过后经唯一台账入口写流水）
        services.AddScoped<Services.IOpeningService, Services.OpeningService>();
        services.AddScoped<Services.ICostAdjustService, Services.CostAdjustService>();

        // 审批完成事件消费者（Worker 作用域内回填租户后应用挂起变更）
        services.AddScoped<
            Qlx.Abstractions.Events.IIntegrationEventConsumer<Qlx.Abstractions.Approvals.ApprovalCompletedEvent>,
            Services.Consumers.ApprovalCompletedEventConsumer>();

        // 成本字段权限判定依赖 HTTP 上下文
        services.AddHttpContextAccessor();

        services.AddSingleton<Qlx.Abstractions.Authorization.IEndpointPolicyMapProvider, StkEndpointPolicyMap>();
        services.AddScoped<Qlx.Abstractions.Jobs.IJobHandler, StkCacheCleanupJobHandler>();

        return services;
    }
}

/// <summary>Stk 模块租户迁移计划。</summary>
internal sealed class StkTenantMigrationPlan : ITenantMigrationPlan
{
    public string ModuleCode => QlxModuleCodes.Stk;

    public Type DbContextType => typeof(StkTenantDbContext);

    public IReadOnlyList<string> DependsOn => [QlxModuleCodes.Bas];
}
