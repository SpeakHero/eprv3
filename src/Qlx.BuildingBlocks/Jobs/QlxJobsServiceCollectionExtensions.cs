/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-22 22:15:00 +08:00 中国标准时间 星期四
 * 描述：任务中心 DI 装配扩展（AddQlxJobs：Hangfire 共享库存储 + Worker + 调度扫描 + Dashboard 开发期注册）
 */

using Hangfire;
using Hangfire.Dashboard;
using Hangfire.SqlServer;

using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

using Qlx.Abstractions.Messages;
using Qlx.BuildingBlocks.Messaging;
using Qlx.BuildingBlocks.MultiTenancy;

namespace Qlx.BuildingBlocks.Jobs;

/// <summary>
/// 后台任务中心装配扩展。
/// </summary>
public static class QlxJobsServiceCollectionExtensions
{
    /// <summary>
    /// 注册后台任务中心：Hangfire 存储（并入共享库 <c>qlx_shared</c> 的 hangfire_* 表，复用
    /// <see cref="SharedDatabaseOptions"/> 连接解析）+ Worker（WorkerCount 走强类型 Options）+
    /// 调度扫描 HostedService + 执行运行器。共享库连接配置缺失时启动期（ValidateOnStart）失败。
    /// </summary>
    /// <param name="services">服务集合。</param>
    /// <param name="configuration">应用配置。</param>
    /// <returns>服务集合（链式调用）。</returns>
    public static IServiceCollection AddQlxJobs(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        _ = services.AddOptions<QlxJobsOptions>()
            .BindConfiguration(QlxJobsOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // Hangfire 存储并入共享库：连接串复用多租户共享库配置（Qlx:Tenancy:SharedDatabase），
        // 配置缺失在 Hangfire 初始化时抛出（AddHangfire 的服务提供者回调在启动期执行，等效 ValidateOnStart）
        _ = services.AddHangfire((serviceProvider, globalConfiguration) =>
        {
            SharedDatabaseOptions shared = serviceProvider.GetRequiredService<IOptions<SharedDatabaseOptions>>().Value;
            if (string.IsNullOrWhiteSpace(shared.DbHost) || string.IsNullOrWhiteSpace(shared.DbName))
            {
                throw new InvalidOperationException(
                    $"后台任务中心存储配置缺失：{SharedDatabaseOptions.SectionName}:DbHost / DbName 未配置，" +
                    "Hangfire 共享库（qlx_shared）存储无法初始化，启动失败。");
            }

            // 共享库连接信息与租户无关（tenantId 仅为连接信息携带字段），传 0
            _ = globalConfiguration
                .UseSqlServerStorage(
                    shared.ToConnectionInfo(0).ToConnectionString(),
                    new SqlServerStorageOptions
                    {
                        // 队列轮询改为长轮询，降低空扫描对共享库的压力
                        QueuePollInterval = TimeSpan.FromSeconds(5),
                        JobExpirationCheckInterval = TimeSpan.FromHours(1),
                        CountersAggregateInterval = TimeSpan.FromMinutes(5),
                    });
        });

        _ = services.AddHangfireServer((serviceProvider, serverOptions) =>
        {
            QlxJobsOptions jobsOptions = serviceProvider.GetRequiredService<IOptions<QlxJobsOptions>>().Value;
            serverOptions.WorkerCount = jobsOptions.WorkerCount;
            serverOptions.Queues = [.. jobsOptions.Queues.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];
            serverOptions.ServerName = Environment.MachineName;
        });

        // 任务中心基础设施（IMessageService 默认 NoOp：2D 信息中心落地后由 DI 覆盖注册真实实现）
        services.TryAddScoped<IMessageService, NotConfiguredMessageService>();
        _ = services.AddScoped<JobExecutionRunner>();
        _ = services.AddScoped<IJobHandlerFactory, JobHandlerFactory>();
        _ = services.AddSingleton<IJobBackgroundEnqueuer, HangfireJobEnqueuer>();
        _ = services.AddScoped<JobBackgroundInvoker>();
        _ = services.AddHostedService<JobDispatchHostedService>();

        return services;
    }

    /// <summary>
    /// 注册 Hangfire Dashboard（仅 Development 环境；需已认证用户，非 Development 直接跳过）。
    /// </summary>
    /// <param name="app">应用构建器。</param>
    /// <returns>应用构建器（链式调用）。</returns>
    public static IApplicationBuilder UseQlxJobsDashboard(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        IHostEnvironment environment = app.ApplicationServices.GetRequiredService<IHostEnvironment>();
        if (!environment.IsDevelopment())
        {
            return app;
        }

        // Dashboard 权限守卫：仅已认证用户可访问（开发期联调），生产环境整段不注册
        _ = app.UseHangfireDashboard(QlxJobsOptions.DashboardPath, new DashboardOptions
        {
            Authorization = [new AuthenticatedDashboardAuthorizationFilter()],
        });
        return app;
    }
}

/// <summary>
/// Dashboard 权限守卫过滤器：仅允许已认证用户访问（防匿名窥探任务参数与执行历史）。
/// </summary>
public sealed class AuthenticatedDashboardAuthorizationFilter : IDashboardAuthorizationFilter
{
    /// <summary>判定当前用户是否已认证。</summary>
    /// <param name="context">Dashboard 上下文。</param>
    /// <returns>已认证返回 true。</returns>
    public bool Authorize(DashboardContext context)
    {
        return context.GetHttpContext().User.Identity?.IsAuthenticated == true;
    }
}
