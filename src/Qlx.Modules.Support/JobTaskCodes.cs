/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-22 22:30:00 +08:00 中国标准时间 星期四
 * 描述：后台任务中心系统级任务编码常量（8 个内置支撑任务）+ 任务参数保留键。
 *       文件域任务编码见 <see cref="FileTaskCodes"/>（2B 既有定义，此处不重复）。
 */

namespace Qlx.Modules.Support;

/// <summary>
/// 后台任务中心系统级任务编码。约定格式 <c>模块码.任务名</c>，全局唯一。
/// 消息/审计/会话归档清理类任务在 2D 信息中心与门户审计域落地后接入真实存储，2C 先按能力边界实现。
/// </summary>
public static class JobTaskCodes
{
    /// <summary>消息归档任务（msg_ 表未在 2C 建库，2D 信息中心落地后接入真实归档存储）。</summary>
    public const string MessageArchiveRun = "message.archive.run";

    /// <summary>审计归档任务（审计域无既有可归档存储，2C 输出审计待归档规模日志，归档动作 2F 接入）。</summary>
    public const string AuditArchive = "audit.archive";

    /// <summary>会话清理任务（登录令牌黑名单走缓存 TTL 自动过期，2C 输出清理统计日志）。</summary>
    public const string SessionClean = "session.clean";

    /// <summary>任务执行历史清理（job_executions 按保留期清理，job_logs 只增不改永久保留）。</summary>
    public const string JobExecutionClean = "job.execution.clean";

    /// <summary>租户隔离巡检任务（调 TenantIsolationAuditor 全量巡检活跃租户并输出告警）。</summary>
    public const string TenantIsolationAudit = "tenant.isolation.audit";

    /// <summary>租户共享库→独立库升级推荐任务（候选识别 + 推荐推送，永不自动升级，升级由门户管理员人工发起）。</summary>
    public const string TenantMigrateSharedToIsolated = "tenant.migrate.shared_to_isolated";

    /// <summary>审批超时扫描任务（2E 审批中心，R8）：扫描到期 Pending 任务 → 节流催办 → 达上限上报上级。</summary>
    public const string ApprovalTimeoutScan = "approval.timeout.scan";

    /// <summary>任务参数保留键：归档 / 清理截止时间（UTC ISO 8601，缺省按保留期推算）。</summary>
    public const string ParamKeyBeforeTime = "beforeTime";

    /// <summary>任务参数保留键：隔离巡检目标租户 Id（long；缺省巡检全部活跃租户）。</summary>
    public const string ParamKeyTenantId = "tenantId";

    /// <summary>任务参数保留键：升级任务发起人显示名（缺省 system）。</summary>
    public const string ParamKeyOperatorName = "operatorName";
}
