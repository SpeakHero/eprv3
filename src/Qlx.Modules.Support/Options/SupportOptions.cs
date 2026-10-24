/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-22 22:50:00 +08:00 中国标准时间 星期四
 * 描述：Support 模块强类型 Options（Files/Jobs/Messages/Approvals/Print 五节），配置节 Qlx:Support
 */

using System.ComponentModel.DataAnnotations;

namespace Qlx.Modules.Support.Options;

/// <summary>
/// 支撑服务中心模块配置根。对应配置节 <c>Qlx:Support</c>，
/// 加载自 <c>config/modules/support/support.json</c>，启动期 ValidateOnStart 校验。
/// </summary>
public sealed record SupportOptions
{
    /// <summary>配置节名。</summary>
    public const string SectionName = "Qlx:Support";

    /// <summary>文件存储中心配置。</summary>
    [Required]
    public SupportFilesOptions Files { get; init; } = new();

    /// <summary>后台任务中心配置（2C 启用，2B 占位）。</summary>
    [Required]
    public SupportJobsOptions Jobs { get; init; } = new();

    /// <summary>信息中心配置（2D 启用，占位）。</summary>
    [Required]
    public SupportMessagesOptions Messages { get; init; } = new();

    /// <summary>审批中心配置（2E 启用，占位）。</summary>
    [Required]
    public SupportApprovalsOptions Approvals { get; init; } = new();

    /// <summary>打印中心配置（占位）。</summary>
    [Required]
    public SupportPrintOptions Print { get; init; } = new();
}

/// <summary>
/// 文件存储中心配置（业务规则 R1~R12 的静态参数部分；租户级运行参数走数据库）。
/// 本地存储根目录由 <see cref="Qlx.BuildingBlocks.Files.LocalFileStorageOptions.Root"/> 承载（同节同名绑定）。
/// </summary>
public sealed record SupportFilesOptions
{
    /// <summary>同步上传大小上限（字节），默认 100 MB；超过走 ASYNC 分片（2C）。</summary>
    [Range(1, 1_099_511_627_776)]
    public long MaxSyncUploadBytes { get; init; } = 104_857_600;

    /// <summary>回收站保留天数（默认 30，规则 R3；2C 清理任务按此到期物理清理）。</summary>
    [Range(1, 3650)]
    public int RecycleDays { get; init; } = 30;

    /// <summary>临时区 TTL 天数（默认 7，规则 R4）。</summary>
    [Range(1, 365)]
    public int TempTtlDays { get; init; } = 7;

    /// <summary>配额预警百分比（默认 90%，规则 R7）。</summary>
    [Range(1, 100)]
    public int QuotaPercent { get; init; } = 90;

    /// <summary>配额阻断百分比（默认 100%，规则 R7）。</summary>
    [Range(1, 100)]
    public int QuotaBlockPercent { get; init; } = 100;

    /// <summary>租户存储配额基数（字节，默认 10 GB）；预警/阻断按此百分比计算。</summary>
    [Range(1, 1_099_511_627_776)]
    public long QuotaTotalBytes { get; init; } = 10_737_418_240;
}

/// <summary>
/// 后台任务中心配置（模块级运行参数；调度引擎 / Worker 参数走 Qlx:Jobs 节的 QlxJobsOptions）。
/// </summary>
public sealed record SupportJobsOptions
{
    /// <summary>任务默认超时秒数（定义与请求均未指定时生效），默认 1800（30 分钟）。</summary>
    [Range(1, 86400)]
    public int DefaultTimeoutSeconds { get; init; } = 1800;

    /// <summary>执行历史保留天数（job.execution.clean 清理 job_executions，job_logs 永久保留），默认 30。</summary>
    [Range(1, 3650)]
    public int ExecutionRetentionDays { get; init; } = 30;

    /// <summary>幂等键防重窗口（秒），默认 300（5 分钟）。</summary>
    [Range(1, 86400)]
    public int IdempotencyWindowSeconds { get; init; } = 300;
}

/// <summary>
/// 信息中心配置（2D）。业务运行参数（阈值 X，如归档保留期调整）走 por_parameters 参数表在线维护，
/// 本节仅承载模块级静态装配参数。
/// </summary>
public sealed record SupportMessagesOptions
{
    /// <summary>消息归档保留天数（已读消息超过该期限由 message.archive.run 批量归档）。</summary>
    [Range(1, 3650)]
    public int ArchiveRetentionDays { get; init; } = 90;

    /// <summary>幂等去重窗口（分钟）：相同 BizRef 收件人在窗口内的重复发送被跳过。</summary>
    [Range(1, 1440)]
    public int DedupMinutes { get; init; } = 5;

    /// <summary>邮件渠道失败重试上限（超过后标记最终失败并降级站内）。</summary>
    [Range(0, 10)]
    public int EmailRetryLimit { get; init; } = 3;

    /// <summary>邮件重试基础退避（秒），按指数退避：base × 2^重试序号。</summary>
    [Range(1, 3600)]
    public int RetryBaseDelaySeconds { get; init; } = 60;

    /// <summary>收件箱 / 已发送分页页大小上限。</summary>
    [Range(1, 200)]
    public int InboxPageSizeMax { get; init; } = 100;
}

/// <summary>
/// 审批中心配置（2E）。静态装配参数：默认超时小时 / 超时催办上限 / 催办节流间隔 / 超时扫描批量。
/// 业务运行阈值（如按租户差异化催办策略）走 por_parameters 参数表在线维护（接入待办，2F 管控域统一挂接）。
/// </summary>
public sealed record SupportApprovalsOptions
{
    /// <summary>节点未配置超时小时数时的默认超时（小时），默认 72。</summary>
    [Range(1, 8760)]
    public int DefaultTimeoutHours { get; init; } = 72;

    /// <summary>超时催办次数上限（R8）：同一任务累计催办达到该次数后上报上级（DEPT_LEADER），默认 3。</summary>
    [Range(1, 100)]
    public int MaxUrgeCount { get; init; } = 3;

    /// <summary>催办节流间隔（分钟）：距离最近一次催办不足该间隔时跳过本次催办，默认 60。</summary>
    [Range(1, 1440)]
    public int UrgeIntervalMinutes { get; init; } = 60;

    /// <summary>超时扫描单轮最大处理任务数（分批防抖，默认 200）。</summary>
    [Range(1, 10_000)]
    public int TimeoutScanBatchSize { get; init; } = 200;
}

/// <summary>打印中心配置占位。</summary>
public sealed record SupportPrintOptions
{
    /// <summary>模板缓存时长（分钟）。</summary>
    [Range(1, 1440)]
    public int TemplateCacheMinutes { get; init; } = 10;
}
