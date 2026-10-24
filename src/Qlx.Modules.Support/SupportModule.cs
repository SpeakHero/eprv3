/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-23 02:15:00 +08:00 中国标准时间 星期五
 * 修改时间：2026-10-23 02:40:00 +08:00 中国标准时间 星期五
 * 描述：Support（支撑服务中心）模块装配入口：清单声明 + 服务注册（2B 文件存储中心 + 2C 后台任务中心 + 2F 打印与导入导出）
 */

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Qlx.Abstractions.Constants;
using Qlx.Abstractions.Jobs;
using Qlx.Abstractions.Modules;
using Qlx.Modules.Support.Jobs.Files;
using Qlx.Modules.Support.Jobs.ImportExport;
using Qlx.Modules.Support.Jobs.SystemJobs;

namespace Qlx.Modules.Support;

/// <summary>
/// Support 模块（2B：文件存储中心 + 2C：后台任务中心）。宿主经程序集扫描发现本类型并装配；
/// 许可停用时：路由 404、菜单不下发、Manifest 任务暂停、事件订阅不激活。
/// </summary>
public sealed class SupportModule : IModule
{
    /// <inheritdoc />
    public ModuleManifest Manifest { get; } = new()
    {
        ModuleCode = QlxModuleCodes.Support,
        DisplayName = "支撑服务中心",
        TablePrefix = QlxModuleCodes.SupportFileTablePrefix,
        Permissions =
        [
            new PermissionDefinition("support.file.menu", "文件中心", PermissionKind.Menu),
            new PermissionDefinition("support.file.upload", "文件上传", PermissionKind.Action),
            new PermissionDefinition("support.file.download", "文件下载", PermissionKind.Action),
            new PermissionDefinition("support.file.delete", "文件删除", PermissionKind.Action),
            new PermissionDefinition("support.file.restore", "回收站还原", PermissionKind.Action),
            new PermissionDefinition("support.file.acl", "文件授权管理", PermissionKind.Action),
            new PermissionDefinition("support.job.menu", "后台任务中心", PermissionKind.Menu),
            new PermissionDefinition("support.job.trigger", "任务触发", PermissionKind.Action),
            new PermissionDefinition("support.job.cancel", "任务取消", PermissionKind.Action),
            new PermissionDefinition("support.job.view", "执行历史与日志查看", PermissionKind.Action),
            new PermissionDefinition("support.job.config", "任务定义启停管理", PermissionKind.Action),
            new PermissionDefinition("support.message.menu", "信息中心", PermissionKind.Menu),
            new PermissionDefinition("support.message.read", "收件箱与全局公告查看", PermissionKind.Action),
            new PermissionDefinition("support.message.send", "消息发送", PermissionKind.Action),
            new PermissionDefinition("support.message.subscription", "订阅配置管理", PermissionKind.Action),
            new PermissionDefinition("support.approval.menu", "审批中心", PermissionKind.Menu),
            new PermissionDefinition("support.approval.todo", "待办处理", PermissionKind.Action),
            new PermissionDefinition("support.approval.initiate", "发起审批", PermissionKind.Action),
            new PermissionDefinition("support.approval.instance", "审批实例查看", PermissionKind.Action),
            new PermissionDefinition("support.approval.flow", "流程定义管理", PermissionKind.Action),
            new PermissionDefinition("support.approval.delegate", "审批委托管理", PermissionKind.Action),
            new PermissionDefinition("support.print.menu", "打印中心", PermissionKind.Menu),
            new PermissionDefinition("support.print.template", "打印模板管理", PermissionKind.Action),
            new PermissionDefinition("support.print.render", "打印模板渲染预览", PermissionKind.Action),
        ],
        Menus = [],
        Jobs =
        [
            // 文件域（2B 声明，2C 起为真实实现）
            new JobDefinition(FileTaskCodes.RecycleCleanup, "回收站到期清理", "0 0 3 * * *",
                typeof(FileRecycleCleanupHandler).FullName!),
            new JobDefinition(FileTaskCodes.TempPurge, "临时区到期清理", "0 0 4 * * *",
                typeof(FileTempPurgeHandler).FullName!),
            // 系统级（2C；message/audit/session 为观测模式，能力落地后替换 Handler 实现，编码不变）
            new JobDefinition(JobTaskCodes.MessageArchiveRun, "消息归档", "0 30 2 * * *",
                typeof(MessageArchiveRunHandler).FullName!),
            new JobDefinition(JobTaskCodes.AuditArchive, "审计归档", "0 40 2 * * *",
                typeof(AuditArchiveHandler).FullName!),
            new JobDefinition(JobTaskCodes.SessionClean, "会话清理", "0 50 2 * * *",
                typeof(SessionCleanHandler).FullName!),
            new JobDefinition(JobTaskCodes.JobExecutionClean, "任务执行历史清理", "0 10 3 * * *",
                typeof(JobExecutionCleanHandler).FullName!),
            new JobDefinition(JobTaskCodes.TenantIsolationAudit, "租户隔离巡检", "0 0 5 * * *",
                typeof(TenantIsolationAuditJobHandler).FullName!),
            new JobDefinition(JobTaskCodes.TenantMigrateSharedToIsolated, "独立库升级推荐", "0 20 5 * * 1",
                typeof(TenantMigrateRecommendHandler).FullName!),
            // 审批中心（2E，R8 超时扫描：每 10 分钟一轮，六段式 CRON）
            new JobDefinition(JobTaskCodes.ApprovalTimeoutScan, "审批超时扫描", "0 */10 * * * *",
                typeof(ApprovalTimeoutScanHandler).FullName!),
        ],
        SubscribedEvents = [],
        // 服务任务（无调度按需提交，S18/D22 任务化红线）：定义落 job_definitions（CRON 空、NextRunAt 恒 null），
        // 调度扫描永不命中，仅经 IJobScheduler.SubmitAsync 按声明模式执行；分片组装/格式转换处理器已落地（R-7），
        // 待分片上传端点接入后再声明，避免产生无法触达的幽灵定义
        ServiceJobs =
        [
            new ServiceJobDefinition(FileTaskCodes.Upload, "文件上传", typeof(FileUploadHandler).FullName!),
            new ServiceJobDefinition(FileTaskCodes.Download, "文件下载", typeof(FileDownloadHandler).FullName!),
            new ServiceJobDefinition(FileTaskCodes.Preview, "文件预览", typeof(FilePreviewHandler).FullName!),
            new ServiceJobDefinition(FileTaskCodes.Metadata, "文件元数据查询", typeof(FileMetadataHandler).FullName!),
            new ServiceJobDefinition(FileTaskCodes.List, "文件列表查询", typeof(FileListHandler).FullName!),
            new ServiceJobDefinition(FileTaskCodes.Delete, "文件删除（回收站）", typeof(FileSoftDeleteHandler).FullName!),
            new ServiceJobDefinition(FileTaskCodes.Restore, "回收站还原", typeof(FileRestoreHandler).FullName!),
            new ServiceJobDefinition(FileTaskCodes.RecycleList, "回收站列表", typeof(FileRecycleListHandler).FullName!),
            new ServiceJobDefinition(FileTaskCodes.AclSet, "文件 ACL 设置", typeof(FileAclSetHandler).FullName!),
            new ServiceJobDefinition(ImportExportTaskCodes.Export, "大数据量导出", typeof(ExportJobHandler).FullName!, ExecutionMode.Async),
            new ServiceJobDefinition(ImportExportTaskCodes.BatchImport, "批量导入", typeof(BatchJobHandler).FullName!, ExecutionMode.Async),
        ],
    };

    /// <inheritdoc />
    public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
        => services.AddQlxSupport(configuration);
}
