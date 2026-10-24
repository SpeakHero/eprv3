/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-22 21:50:00 +08:00 中国标准时间 星期四
 * 描述：任务提交元数据常量（触发来源参数键 / 触发来源取值），2A 契约 Parameters 字典的保留键
 */

using Qlx.Abstractions.Jobs;

namespace Qlx.BuildingBlocks.Jobs;

/// <summary>
/// 任务提交元数据常量。<see cref="Qlx.Abstractions.Jobs.TaskSubmitRequest"/> 契约已冻结（无 TriggerSource 字段），
/// 触发来源经 Parameters 字典保留键传递，由调度器落库 job_executions.trigger_source。
/// </summary>
public static class JobSubmitMetadata
{
    /// <summary>触发来源参数保留键（提交时写入 Parameters，调度器读取后落库）。</summary>
    public const string TriggerSourceParameterKey = "__triggerSource";

    /// <summary>触发来源：手动（任务中心 / API 触发）。</summary>
    public const string TriggerManual = "manual";

    /// <summary>触发来源：调度（JobDispatchHostedService 周期扫描）。</summary>
    public const string TriggerSchedule = "schedule";

    /// <summary>触发来源：依赖（前置任务完成联动）。</summary>
    public const string TriggerDependency = "dependency";

    /// <summary>触发来源：失败重试（前次执行 FAILED 后按退避策略重新入队）。</summary>
    public const string TriggerRetry = "retry";

    /// <summary>系统操作人标识（无登录态的调度 / 系统任务的兜底推送接收人）。</summary>
    public const string SystemUserId = "system";
}

/// <summary>
/// 同步执行的中间结果：状态机终态 + 完成结果（供调度器组装 TaskSubmitResponse）。
/// </summary>
/// <param name="Status">终态（Success / Failed / Timeout / Canceled）。</param>
/// <param name="Result">完成结果。</param>
public sealed record JobDirectExecutionResult(JobExecutionStatus Status, CompletedResult Result);
