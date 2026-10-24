/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-22 22:05:00 +08:00 中国标准时间 星期四
 * 描述：后台任务中心强类型 Options（Hangfire Worker / 调度扫描 / 熔断 / 超时重试），配置节 Qlx:Jobs
 */

using System.ComponentModel.DataAnnotations;

namespace Qlx.BuildingBlocks.Jobs;

/// <summary>
/// 后台任务中心全局配置。对应配置节 <c>Qlx:Jobs</c>，启动期 ValidateOnStart 校验；
/// 存储连接复用 <c>Qlx:Tenancy:SharedDatabase</c>（qlx_shared 库的 hangfire_* 表），缺失时启动失败。
/// </summary>
public sealed record QlxJobsOptions
{
    /// <summary>配置节名。</summary>
    public const string SectionName = "Qlx:Jobs";

    /// <summary>Hangfire Dashboard 访问路径（仅 Development 环境注册）。</summary>
    public const string DashboardPath = "/jobs-dashboard";

    /// <summary>Hangfire Worker 数量（方案语义：任务中心最大并发执行数）。</summary>
    [Range(1, 256)]
    public int WorkerCount { get; init; } = 4;

    /// <summary>Hangfire 队列（逗号分隔，默认仅 default）。</summary>
    [Required(AllowEmptyStrings = false)]
    public string Queues { get; init; } = "default";

    /// <summary>调度扫描间隔（秒），JobDispatchHostedService 周期。</summary>
    [Range(1, 3600)]
    public int DispatchIntervalSeconds { get; init; } = 1;

    /// <summary>单轮调度扫描的到期任务条数上限。</summary>
    [Range(1, 500)]
    public int DispatchBatchSize { get; init; } = 20;

    /// <summary>
    /// 连续失败熔断阈值（方案参数 <c>job.failure_threshold</c> 语义，默认 5）：
    /// 同租户同任务连续失败达阈值自动暂停定义并推 SYSTEM_ALERT。
    /// </summary>
    [Range(1, 100)]
    public int FailureThreshold { get; init; } = 5;

    /// <summary>任务超时后是否自动重试一次（方案 D19：RetryOnTimeout，默认 false）。</summary>
    public bool RetryOnTimeout { get; init; }
}
