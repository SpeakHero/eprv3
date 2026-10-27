/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-23 02:05:00 +08:00 中国标准时间 星期五
 * 修改时间：2026-10-23 02:40:00 +08:00 中国标准时间 星期五
 * 描述：Support（支撑服务中心）模块 DI 注册扩展——SupportTenantDbContext + 迁移计划 +
 *       后台任务中心（2C：Hangfire 支撑调度器 / 执行存储 / 定义注册表 / 日志查询 / 门面服务）+
 *       文件域处理器与服务 + 系统级任务处理器 + 导入导出与打印中心（2F）
 */

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Qlx.Abstractions.Approvals;
using Qlx.Abstractions.Constants;
using Qlx.Abstractions.Files;
using Qlx.Abstractions.Jobs;
using Qlx.Abstractions.Messages;
using Qlx.Abstractions.Printing;
using Qlx.Abstractions.Tenancy;
using Qlx.BuildingBlocks.Files;
using Qlx.BuildingBlocks.ImportExport;
using Qlx.BuildingBlocks.Jobs;
using Qlx.BuildingBlocks.Messaging;
using Qlx.BuildingBlocks.MultiTenancy;
using Qlx.BuildingBlocks.MultiTenancy.PlatformShared;
using Qlx.BuildingBlocks.Sse;
using Qlx.Modules.Support.Jobs.Files;
using Qlx.Modules.Support.Jobs.ImportExport;
using Qlx.Modules.Support.Jobs.SystemJobs;
using Qlx.Modules.Support.Options;
using Qlx.Modules.Support.Services.Approvals;
using Qlx.Modules.Support.Services.Files;
using Qlx.Modules.Support.Services.Jobs;
using Qlx.Modules.Support.Services.Messages;
using Qlx.Modules.Support.Services.Printing;

namespace Qlx.Modules.Support;

/// <summary>
/// Support 模块装配扩展。IIdObfuscator 复用宿主 AddQlxIds 注册（同盐，禁止重复注册）；
/// ITenantSubjectQuery 由 Bas 模块提供（处理器经 IServiceProvider 可选解析，未注册时按匿名主体降级）。
/// </summary>
public static class SupportServiceCollectionExtensions
{
    /// <summary>
    /// 注册 Support 模块组件：SupportTenantDbContext（动态路由）+ 迁移计划 + 后台任务中心（2C）+
    /// 信息中心（2D：消息服务 + SSE 基座 + 渠道分发 + 共享库全局项读取）+ 文件域任务处理器 + 门面服务。
    /// </summary>
    /// <param name="services">服务集合。</param>
    /// <param name="configuration">配置根（Messaging SMTP / PlatformShared / Support 模块节绑定）。</param>
    public static IServiceCollection AddQlxSupport(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        // 强类型 Options（配置源 config/modules/support/support.json，启动期校验）
        services.AddOptions<SupportFilesOptions>()
            .BindConfiguration($"{SupportOptions.SectionName}:Files")
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddOptions<LocalFileStorageOptions>()
            .BindConfiguration(LocalFileStorageOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddOptions<SupportJobsOptions>()
            .BindConfiguration($"{SupportOptions.SectionName}:Jobs")
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddOptions<SupportMessagesOptions>()
            .BindConfiguration($"{SupportOptions.SectionName}:Messages")
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddOptions<SupportApprovalsOptions>()
            .BindConfiguration($"{SupportOptions.SectionName}:Approvals")
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddOptions<SupportPrintOptions>()
            .BindConfiguration($"{SupportOptions.SectionName}:Print")
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // SupportTenantDbContext 工厂注册——每个请求 scope 内按 tenantId 动态构建（复刻 Bas 模式：
        // 手动解析应用容器的 IInterceptor 集合注入，供审计拦截器在 OnConfiguring 生效）
        services.AddScoped<SupportTenantDbContext>(sp =>
        {
            var tenantCtx = sp.GetRequiredService<ITenantContext>();
            var resolver = sp.GetRequiredService<ITenantDatabaseResolver>();
            var builder = new DbContextOptionsBuilder<SupportTenantDbContext>();
            var interceptors = sp.GetServices<Microsoft.EntityFrameworkCore.Diagnostics.IInterceptor>();
            builder.AddInterceptors(interceptors);
            return new SupportTenantDbContext(resolver, tenantCtx.TenantId ?? 0, builder.Options);
        });

        // Support 模块租户迁移计划（TenantMigrationService 启动期自动扫描；依赖 Bas 与租户日志底座先行）
        services.AddSingleton<ITenantMigrationPlan>(new SupportTenantMigrationPlan());

        // ======= 后台任务中心（2C，调度引擎基座 AddQlxJobs 由宿主注册，模块侧注册实现）=======
        // IJobScheduler 换为 Hangfire 支撑实现（D20 五步）；InProcessJobScheduler 临时实现已移除
        services.AddScoped<SupportJobDefinitionRegistry>();
        services.AddScoped<IJobExecutionStore, SupportJobExecutionStore>();
        services.AddScoped<IJobScheduler, SupportJobScheduler>();
        services.AddScoped<IJobExecutionLogQuery, SupportJobExecutionLogQuery>();
        services.AddScoped<ISupportJobService, SupportJobService>();

        // 本地文件存储后端（单例；根目录相对宿主内容根解析）
        services.AddSingleton<IFileStorageBackend>(static sp => new LocalFileStorageBackend(
            sp.GetRequiredService<IOptions<LocalFileStorageOptions>>(),
            sp.GetRequiredService<IHostEnvironment>().ContentRootPath));

        // 文件域任务处理器（14 个 TaskCode；模块许可停用时入口由许可中间件整体 404）
        services.AddScoped<IJobHandler, FileUploadHandler>();
        services.AddScoped<IJobHandler, FileChunkAssembleHandler>();
        // FilePreviewHandler 依赖具体 FileDownloadHandler 复用下载逻辑：具体类型注册为 Self，
        // IJobHandler 经工厂转发，保证同一作用域内单实例且可被 JobHandlerFactory 按 TaskCode 解析
        services.AddScoped<FileDownloadHandler>();
        services.AddScoped<IJobHandler>(static sp => sp.GetRequiredService<FileDownloadHandler>());
        services.AddScoped<IJobHandler, FilePreviewHandler>();
        services.AddScoped<IJobHandler, FileFormatConvertHandler>();
        services.AddScoped<IJobHandler, FileRecycleCleanupHandler>();
        services.AddScoped<IJobHandler, FileTempPurgeHandler>();
        services.AddScoped<IJobHandler, FileAclSetHandler>();
        services.AddScoped<IJobHandler, FileMetadataHandler>();
        services.AddScoped<IJobHandler, FileListHandler>();
        services.AddScoped<IJobHandler, FileSoftDeleteHandler>();
        services.AddScoped<IJobHandler, FileRestoreHandler>();
        services.AddScoped<IJobHandler, FilePurgeHandler>();
        services.AddScoped<IJobHandler, FileRecycleListHandler>();

        // 系统级任务处理器（6 个；message/audit/session 为观测模式，能力落地后替换实现）
        services.AddScoped<IJobHandler, MessageArchiveRunHandler>();
        services.AddScoped<IJobHandler, AuditArchiveHandler>();
        services.AddScoped<IJobHandler, SessionCleanHandler>();
        services.AddScoped<IJobHandler, JobExecutionCleanHandler>();
        services.AddScoped<IJobHandler, TenantIsolationAuditJobHandler>();
        services.AddScoped<IJobHandler, TenantMigrateRecommendHandler>();
        // 审批中心超时扫描任务（2E，R8）
        services.AddScoped<IJobHandler, ApprovalTimeoutScanHandler>();
        // 导入导出任务（2F：大数据量导出 / 批量导入，按需触发不进 Manifest.Jobs 调度清单）
        services.AddScoped<IJobHandler, ExportJobHandler>();
        services.AddScoped<IJobHandler, BatchJobHandler>();

        // ======= 导入导出 + 打印中心（2F）=======
        // 导入导出公共能力（ClosedXML 引擎；模板注册表 / 行落点 / 数据源 / 字典校验器由业务模块注册）
        services.AddQlxImportExport();
        // 打印模板服务（同实例承载 2A 冻结契约与模块级管理契约）
        services.AddScoped<PrintTemplateService>();
        services.AddScoped<IPrintTemplateService>(static sp => sp.GetRequiredService<PrintTemplateService>());
        services.AddScoped<ISupportPrintTemplateService>(static sp => sp.GetRequiredService<PrintTemplateService>());
        // PDF 渲染（阶段二空实现占位，宿主接入真实引擎后整体替换）
        services.TryAddSingleton<IPdfRenderer, NullPdfRenderer>();

        // ======= 审批中心（2E）=======
        // 流程注册表（2A 契约 IApprovalFlowRegistry 内存注册 + DB 启用版本解析 R1/R2）
        services.AddScoped<ApprovalFlowRegistry>();
        services.AddScoped<IApprovalFlowRegistry>(static sp => sp.GetRequiredService<ApprovalFlowRegistry>());
        // 审批人来源聚合：Support 兜底（INITIATOR/USER/FIELD_USER）+ 组合解析器（2A 冻结契约实现）；
        // Bas 组织侧来源（ROLE/POSITION/DEPT_LEADER）由 Bas 模块自注册，宿主 DI 自动聚合
        services.AddSingleton<SupportApprovalApproverSource>();
        services.TryAddEnumerable(new ServiceDescriptor(
            typeof(IApprovalApproverSource), typeof(SupportApprovalApproverSource), ServiceLifetime.Singleton));
        services.AddScoped<ApprovalCompositeApproverResolver>();
        services.AddScoped<IApprovalApproverResolver>(static sp => sp.GetRequiredService<ApprovalCompositeApproverResolver>());
        // 审批服务（2A 冻结契约 IApprovalService 实现）
        services.AddScoped<SupportApprovalService>();
        services.AddScoped<IApprovalService>(static sp => sp.GetRequiredService<SupportApprovalService>());
        // 模块门面服务（流程定义查询 / 已办列表 / 委托管理，2A 契约之外的模块级能力）
        services.AddScoped<ISupportApprovalAdminService, SupportApprovalAdminService>();
        // 条件表达式引擎（分支求值 R3 + 种子 expr_json 产出；TryAdd 允许宿主以自定义扩展配置覆盖默认引擎）
        services.TryAddSingleton<Qlx.Abstractions.Expressions.IConditionExpressionEngine>(
            Qlx.BuildingBlocks.Expressions.ConditionExpressionEngineFactory.Default);

        // 门面服务（SupportFileStorageService 同实例承载 Abstractions 契约与模块门面契约）
        services.AddScoped<SupportSeedService>();
        services.AddScoped<SupportFileStorageService>();
        services.AddScoped<IFileStorageService>(static sp => sp.GetRequiredService<SupportFileStorageService>());
        services.AddScoped<ISupportFileService>(static sp => sp.GetRequiredService<SupportFileStorageService>());
        services.AddScoped<IFileAclService, SupportFileAclService>();

        // ======= 信息中心（2D）=======
        // 消息渠道分发管线底座（SMTP/SMS/分发入队；配置 Qlx:Messaging 节）
        services.AddQlxMessaging(configuration);
        // SSE 基座（连接管理器单例承载 2A 契约与端点通道双接口）
        services.AddQlxSse();
        // 共享库全局项只读读取器（glb_* 收件箱合并数据源；ICacheService 短缓存 + 故障降级）
        services.AddQlxPlatformShared(configuration);

        // 信息中心门面服务（SupportMessageService 同实例承载 2A 冻结契约与模块门面契约）
        services.AddScoped<SupportMessageService>();
        services.AddScoped<IMessageService>(static sp => sp.GetRequiredService<SupportMessageService>());
        services.AddScoped<ISupportMessageService>(static sp => sp.GetRequiredService<SupportMessageService>());
        // 渠道分发执行器（Hangfire Worker 作用域内经 MessageDeliveryInvoker 惰性解析）
        services.AddScoped<IMessageDeliveryExecutor, MessageDeliveryExecutor>();

        // 端点授权规则基线（集中式授权映射，替代控制器硬编码 [Authorize]）
        services.AddSingleton<Qlx.Abstractions.Authorization.IEndpointPolicyMapProvider, SupportEndpointPolicyMap>();

        return services;
    }
}

/// <summary>Support 模块租户迁移计划（fil_ 表；依赖 Bas 成员/组织底座与租户日志底座先行迁移）。</summary>
internal sealed class SupportTenantMigrationPlan : ITenantMigrationPlan
{
    public string ModuleCode => QlxModuleCodes.Support;
    public Type DbContextType => typeof(SupportTenantDbContext);
    public IReadOnlyList<string> DependsOn => [QlxModuleCodes.Bas, "log"];
}
