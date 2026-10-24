/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-22 22:35:00 +08:00 中国标准时间 星期四
 * 描述：后台任务中心领域实体（job_ 前缀 4 张表）：任务定义 / 租户配置 / 执行历史 / 执行日志
 */

using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using Qlx.Abstractions.Jobs;
using Qlx.BuildingBlocks.MultiTenancy;

namespace Qlx.Modules.Support.Domain;

/// <summary>
/// 任务定义实体（表 <c>job_definitions</c>）。来自模块 Manifest.Jobs + DI 处理器注册，
/// 由 <c>SupportJobDefinitionRegistry</c> 启动期非破坏性种子 / 更新。
/// </summary>
[Index(nameof(TenantId), nameof(TaskCode), IsUnique = true)]
[Index(nameof(TenantId), nameof(Enabled), nameof(NextRunAt))]
// 空串前缀：DbSet 名 JobDefinitions 已自带 job_ 前缀语义（job_definitions），显式跳过模块级 fil_ 前缀
[TenantTablePrefix("")]
public sealed class JobDefinitionEntity : Qlx.Abstractions.Domain.Entity
{
    /// <summary>所属租户 Id。</summary>
    public long TenantId { get; internal set; }

    /// <summary>任务编码（全局唯一，租户内唯一索引）。</summary>
    [MaxLength(100)]
    public string TaskCode { get; internal set; } = string.Empty;

    /// <summary>任务显示名。</summary>
    [MaxLength(200)]
    public string Name { get; internal set; } = string.Empty;

    /// <summary>所属模块编码（许可停用时整模块任务暂停）。</summary>
    [MaxLength(50)]
    public string ModuleCode { get; internal set; } = string.Empty;

    /// <summary>处理器类型全名（工厂按名分发辅助信息；实际解析走 IJobHandler.TaskCode）。</summary>
    [MaxLength(300)]
    public string HandlerTypeName { get; internal set; } = string.Empty;

    /// <summary>默认 CRON（来自 Manifest；租户级覆盖见 job_configs）。</summary>
    [MaxLength(50)]
    public string DefaultCron { get; internal set; } = string.Empty;

    /// <summary>默认执行模式（Sync / Async）。</summary>
    public ExecutionMode DefaultMode { get; internal set; } = ExecutionMode.Async;

    /// <summary>默认超时秒数（租户级未覆盖时生效）。</summary>
    public int DefaultTimeoutSeconds { get; internal set; }

    /// <summary>是否启用（许可停用 / 管理员停用时为 false）。</summary>
    public bool Enabled { get; internal set; } = true;

    /// <summary>熔断暂停（连续失败达到阈值后自动置位）。</summary>
    public bool Paused { get; internal set; }

    /// <summary>下次调度执行时间（UTC；乐观锁认领字段）。</summary>
    public DateTimeOffset? NextRunAt { get; internal set; }

    /// <summary>创建时间（UTC）。</summary>
    public DateTimeOffset CreatedAt { get; internal set; }

    /// <summary>最近更新时间（UTC）。</summary>
    public DateTimeOffset UpdatedAt { get; internal set; }

    /// <summary>EF Core 反序列化专用无参构造。</summary>
    public JobDefinitionEntity() { }

    /// <summary>创建任务定义。</summary>
    /// <param name="tenantId">所属租户 Id。</param>
    /// <param name="taskCode">任务编码。</param>
    /// <param name="name">任务显示名。</param>
    /// <param name="moduleCode">所属模块编码。</param>
    /// <param name="handlerTypeName">处理器类型全名。</param>
    /// <param name="defaultCron">默认 CRON。</param>
    /// <param name="defaultMode">默认执行模式。</param>
    /// <param name="defaultTimeoutSeconds">默认超时秒数。</param>
    /// <param name="nextRunAt">首次调度时间（服务任务恒 null，调度扫描永不命中）。</param>
    /// <param name="now">当前时间（UTC）。</param>
    public JobDefinitionEntity(
        long tenantId, string taskCode, string name, string moduleCode, string handlerTypeName,
        string defaultCron, ExecutionMode defaultMode, int defaultTimeoutSeconds,
        DateTimeOffset? nextRunAt, DateTimeOffset now)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(taskCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        TenantId = tenantId;
        TaskCode = taskCode;
        Name = name;
        ModuleCode = moduleCode;
        HandlerTypeName = handlerTypeName;
        DefaultCron = defaultCron;
        DefaultMode = defaultMode;
        DefaultTimeoutSeconds = defaultTimeoutSeconds;
        NextRunAt = nextRunAt;
        CreatedAt = now;
        UpdatedAt = now;
    }

    /// <summary>非破坏性刷新 Manifest 来源字段（保留 Enabled / Paused / NextRunAt 运行态）。</summary>
    /// <param name="name">新显示名。</param>
    /// <param name="moduleCode">新模块编码。</param>
    /// <param name="handlerTypeName">新处理器类型全名。</param>
    /// <param name="defaultCron">新默认 CRON。</param>
    /// <param name="defaultMode">新默认执行模式。</param>
    /// <param name="defaultTimeoutSeconds">新默认超时秒数。</param>
    /// <param name="now">当前时间（UTC）。</param>
    public void RefreshFromManifest(
        string name, string moduleCode, string handlerTypeName, string defaultCron,
        ExecutionMode defaultMode, int defaultTimeoutSeconds, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name;
        ModuleCode = moduleCode;
        HandlerTypeName = handlerTypeName;
        DefaultCron = defaultCron;
        DefaultMode = defaultMode;
        DefaultTimeoutSeconds = defaultTimeoutSeconds;
        UpdatedAt = now;
    }

    /// <summary>熔断暂停 / 恢复。</summary>
    /// <param name="paused">是否暂停。</param>
    /// <param name="now">当前时间（UTC）。</param>
    public void SetPaused(bool paused, DateTimeOffset now)
    {
        Paused = paused;
        UpdatedAt = now;
    }
}

/// <summary>
/// 租户级任务配置实体（表 <c>job_configs</c>）。租户管理员对单任务的 CRON / 超时 / 启停覆盖。
/// </summary>
[Index(nameof(TenantId), nameof(TaskCode), IsUnique = true)]
[TenantTablePrefix("")]
public sealed class JobConfigEntity : Qlx.Abstractions.Domain.Entity
{
    /// <summary>所属租户 Id。</summary>
    public long TenantId { get; internal set; }

    /// <summary>任务编码。</summary>
    [MaxLength(100)]
    public string TaskCode { get; internal set; } = string.Empty;

    /// <summary>CRON 覆盖（null=使用定义默认值）。</summary>
    [MaxLength(50)]
    public string? CronOverride { get; internal set; }

    /// <summary>超时秒数覆盖（null=使用定义默认值）。</summary>
    public int? TimeoutSecondsOverride { get; internal set; }

    /// <summary>是否启用（false=租户级停用）。</summary>
    public bool Enabled { get; internal set; } = true;

    /// <summary>最近更新时间（UTC）。</summary>
    public DateTimeOffset UpdatedAt { get; internal set; }

    /// <summary>EF Core 反序列化专用无参构造。</summary>
    public JobConfigEntity() { }

    /// <summary>创建租户级任务配置。</summary>
    /// <param name="tenantId">所属租户 Id。</param>
    /// <param name="taskCode">任务编码。</param>
    /// <param name="cronOverride">CRON 覆盖（可空）。</param>
    /// <param name="timeoutSecondsOverride">超时秒数覆盖（可空）。</param>
    /// <param name="enabled">是否启用。</param>
    /// <param name="now">当前时间（UTC）。</param>
    public JobConfigEntity(
        long tenantId, string taskCode, string? cronOverride, int? timeoutSecondsOverride,
        bool enabled, DateTimeOffset now)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(taskCode);
        TenantId = tenantId;
        TaskCode = taskCode;
        CronOverride = cronOverride;
        TimeoutSecondsOverride = timeoutSecondsOverride;
        Enabled = enabled;
        UpdatedAt = now;
    }
}

/// <summary>
/// 任务执行历史实体（表 <c>job_executions</c>）。每次提交一行，状态机推进由原子条件更新驱动。
/// </summary>
[Index(nameof(TenantId), nameof(ExecutionId), IsUnique = true)]
[Index(nameof(TenantId), nameof(TaskCode), nameof(Status), nameof(QueuedAt))]
[Index(nameof(TenantId), nameof(IdempotencyKey))]
[TenantTablePrefix("")]
public sealed class JobExecutionEntity : Qlx.Abstractions.Domain.Entity
{
    /// <summary>所属租户 Id。</summary>
    public long TenantId { get; internal set; }

    /// <summary>执行 Id（契约层 Guid，租户内唯一）。</summary>
    public Guid ExecutionId { get; internal set; }

    /// <summary>任务编码。</summary>
    [MaxLength(100)]
    public string TaskCode { get; internal set; } = string.Empty;

    /// <summary>执行模式（Sync / Async）。</summary>
    public ExecutionMode Mode { get; internal set; }

    /// <summary>当前状态（状态机 PENDING→QUEUED→RUNNING→终态）。</summary>
    public JobExecutionStatus Status { get; internal set; } = JobExecutionStatus.Pending;

    /// <summary>已重试次数。</summary>
    public int RetryCount { get; internal set; }

    /// <summary>触发来源（manual / schedule / dependency）。</summary>
    [MaxLength(20)]
    public string TriggerSource { get; internal set; } = string.Empty;

    /// <summary>发起人用户标识（可空：系统调度）。</summary>
    [MaxLength(64)]
    public string? InitiatorUserId { get; internal set; }

    /// <summary>幂等键（可空；窗口内防重）。</summary>
    [MaxLength(128)]
    public string? IdempotencyKey { get; internal set; }

    /// <summary>任务参数 JSON（可空）。</summary>
    public string? ParametersJson { get; internal set; }

    /// <summary>执行结果 JSON（可空）。</summary>
    public string? ResultJson { get; internal set; }

    /// <summary>错误信息（可空）。</summary>
    [MaxLength(2000)]
    public string? Error { get; internal set; }

    /// <summary>执行时长（毫秒，可空：未开始）。</summary>
    public long? DurationMs { get; internal set; }

    /// <summary>超时秒数。</summary>
    public int TimeoutSeconds { get; internal set; }

    /// <summary>重试策略 JSON（null=引擎默认策略）。</summary>
    public string? RetryPolicyJson { get; internal set; }

    /// <summary>Hangfire 作业 Id（ASYNC 模式取消联动用）。</summary>
    [MaxLength(64)]
    public string? HangfireJobId { get; internal set; }

    /// <summary>登记时间（UTC）。</summary>
    public DateTimeOffset QueuedAt { get; internal set; }

    /// <summary>开始执行时间（UTC，可空）。</summary>
    public DateTimeOffset? StartedAt { get; internal set; }

    /// <summary>终态时间（UTC，可空）。</summary>
    public DateTimeOffset? FinishedAt { get; internal set; }

    /// <summary>EF Core 反序列化专用无参构造。</summary>
    public JobExecutionEntity() { }
}

/// <summary>
/// 任务执行日志实体（表 <c>job_logs</c>，只增不改）。批量追加写入，随执行 Id 关联。
/// </summary>
[Index(nameof(TenantId), nameof(ExecutionId), nameof(LogAt))]
[Index(nameof(TenantId), nameof(TaskCode), nameof(LogAt))]
[TenantTablePrefix("")]
public sealed class JobLogEntity : Qlx.Abstractions.Domain.Entity
{
    /// <summary>所属租户 Id。</summary>
    public long TenantId { get; internal set; }

    /// <summary>关联执行 Id。</summary>
    public Guid ExecutionId { get; internal set; }

    /// <summary>任务编码。</summary>
    [MaxLength(100)]
    public string TaskCode { get; internal set; } = string.Empty;

    /// <summary>日志级别。</summary>
    public JobLogLevel Level { get; internal set; }

    /// <summary>日志消息。</summary>
    [MaxLength(1000)]
    public string Message { get; internal set; } = string.Empty;

    /// <summary>详情（可空）。</summary>
    public string? Detail { get; internal set; }

    /// <summary>异常堆栈（可空）。</summary>
    public string? StackTrace { get; internal set; }

    /// <summary>记录时间（UTC）。</summary>
    public DateTimeOffset LogAt { get; internal set; }

    /// <summary>EF Core 反序列化专用无参构造。</summary>
    public JobLogEntity() { }

    /// <summary>创建执行日志。</summary>
    /// <param name="tenantId">所属租户 Id。</param>
    /// <param name="executionId">关联执行 Id。</param>
    /// <param name="taskCode">任务编码。</param>
    /// <param name="level">日志级别。</param>
    /// <param name="message">日志消息。</param>
    /// <param name="detail">详情（可空）。</param>
    /// <param name="stackTrace">堆栈（可空）。</param>
    /// <param name="logAt">记录时间（UTC）。</param>
    public JobLogEntity(
        long tenantId, Guid executionId, string taskCode, JobLogLevel level,
        string message, string? detail, string? stackTrace, DateTimeOffset logAt)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(taskCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        TenantId = tenantId;
        ExecutionId = executionId;
        TaskCode = taskCode;
        Level = level;
        Message = message;
        Detail = detail;
        StackTrace = stackTrace;
        LogAt = logAt;
    }
}
