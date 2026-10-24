/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-22 15:45:00 +08:00 中国标准时间 星期三
 * 修改时间：2026-10-22 15:45:00 +08:00 中国标准时间 星期三
 * 描述：内置后台任务声明（从 Modules/JobDefinition.cs 迁移过来）
 */

namespace Qlx.Abstractions.Jobs;

/// <summary>
/// 内置后台任务声明。处理器类型须实现 <see cref="IJobHandler"/> 约定的统一任务处理接口。
/// 任务经 ModuleManifest 注册，许可停用时自动暂停调度。
/// </summary>
/// <param name="JobCode">任务编码，全局唯一，约定格式：<c>模块码.任务名</c>。</param>
/// <param name="Name">任务显示名。</param>
/// <param name="DefaultCron">默认六段式 CRON 表达式（含秒与年）。</param>
/// <param name="HandlerTypeName">任务处理器类型全名；由后台任务中心工厂按名分发。</param>
public sealed record JobDefinition(string JobCode, string Name, string DefaultCron, string HandlerTypeName);

/// <summary>
/// 服务任务声明（无调度、按需提交）。用于模块对外服务方法任务化（S18/D22 红线）：
/// 定义落入 <c>job_definitions</c>（默认 CRON 为空、NextRunAt 恒为 null，调度扫描永不命中），
/// 仅经 <c>IJobScheduler.SubmitAsync</c> 按声明模式直执行 / 入队。
/// </summary>
/// <param name="JobCode">任务编码，全局唯一，约定格式：<c>模块码.任务名</c>。</param>
/// <param name="Name">任务显示名。</param>
/// <param name="HandlerTypeName">任务处理器类型全名；由后台任务中心工厂按名分发。</param>
/// <param name="Mode">默认执行模式（SYNC 直执行 / ASYNC 入队，默认 SYNC）。</param>
public sealed record ServiceJobDefinition(
    string JobCode, string Name, string HandlerTypeName, ExecutionMode Mode = ExecutionMode.Sync);
