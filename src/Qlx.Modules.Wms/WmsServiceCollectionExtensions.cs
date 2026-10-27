/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-26 10:00:00 08:00 中国标准时间 星期一
 * 描述：Wms 模块 DI 注册扩展——WmsTenantDbContext 工厂 + 迁移计划 + 仓储/服务/查询/授权策略/任务处理器
 */

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Qlx.Abstractions.Constants;
using Qlx.Abstractions.Tenancy;
using Qlx.BuildingBlocks.MultiTenancy;
using Qlx.Modules.Wms.Jobs;
using Qlx.Modules.Wms.Services;

namespace Qlx.Modules.Wms;

/// <summary>Wms 模块 DI 注册扩展。</summary>
public static class WmsServiceCollectionExtensions
{
    /// <summary>
    /// 注册 Wms 模块组件：WmsTenantDbContext（动态路由）+ 迁移计划 + 仓储 + 服务 + 查询 + 策略 + 任务。
    /// </summary>
    /// <param name="services">服务集合。</param>
    /// <returns>服务集合。</returns>
    public static IServiceCollection AddQlxWms(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddScoped<WmsTenantDbContext>(sp =>
        {
            var tenantCtx = sp.GetRequiredService<ITenantContext>();
            var resolver = sp.GetRequiredService<ITenantDatabaseResolver>();
            var builder = new DbContextOptionsBuilder<WmsTenantDbContext>();
            var interceptors = sp.GetServices<Microsoft.EntityFrameworkCore.Diagnostics.IInterceptor>();
            builder.AddInterceptors(interceptors);
            return new WmsTenantDbContext(resolver, tenantCtx.TenantId ?? 0, builder.Options);
        });

        services.AddSingleton<ITenantMigrationPlan>(new WmsTenantMigrationPlan());

        services.AddScoped<Repositories.IWmsTenantRepository, Repositories.WmsTenantRepository>();
        services.AddScoped<IWarehouseService, WarehouseService>();
        services.AddScoped<IStockInService, StockInService>();
        services.AddScoped<IStockOutService, StockOutService>();
        services.AddScoped<ITransferService, TransferService>();
        services.AddScoped<IStockTakeService, StockTakeService>();
        services.AddScoped<StockTakeAdjustDraftCreator>();
        services.AddScoped<IStockTakeAdjustDraftCreator>(
            sp => sp.GetRequiredService<StockTakeAdjustDraftCreator>());
        services.AddScoped<InventoryAdjustService>();
        services.AddScoped<IInventoryAdjustService>(
            sp => sp.GetRequiredService<InventoryAdjustService>());
        services.AddScoped<IInventoryAdjustApprovalApplier>(
            sp => sp.GetRequiredService<InventoryAdjustService>());
        services.AddScoped<IInventoryAlertService, InventoryAlertService>();
        services.AddScoped<IStockCommandQueue, StockCommandQueue>();
        services.AddScoped<IStockCommandService, StockCommandService>();
        services.AddScoped<Qlx.Abstractions.Queries.IWarehouseQueryService, WarehouseQueryService>();

        // 审批完成事件应用器 + 消费者（Worker 作用域内回填租户后应用挂起变更）
        services.AddScoped<IWmsApprovalApplier, WmsApprovalApplier>();
        services.AddScoped<
            Qlx.Abstractions.Events.IIntegrationEventConsumer<Qlx.Abstractions.Approvals.ApprovalCompletedEvent>,
            Services.Consumers.ApprovalCompletedEventConsumer>();

        services.AddSingleton<Qlx.Abstractions.Authorization.IEndpointPolicyMapProvider, WmsEndpointPolicyMap>();
        services.AddScoped<Qlx.Abstractions.Jobs.IJobHandler, WmsCacheCleanupJobHandler>();
        services.AddScoped<Qlx.Abstractions.Jobs.IJobHandler, WmsStockCommandDispatchJobHandler>();
        services.AddScoped<Qlx.Abstractions.Jobs.IJobHandler, InventoryAlertScanJobHandler>();

        return services;
    }
}

/// <summary>Wms 模块租户迁移计划。</summary>
internal sealed class WmsTenantMigrationPlan : ITenantMigrationPlan
{
    public string ModuleCode => QlxModuleCodes.Wms;

    public Type DbContextType => typeof(WmsTenantDbContext);

    public IReadOnlyList<string> DependsOn => [QlxModuleCodes.Bas];
}
