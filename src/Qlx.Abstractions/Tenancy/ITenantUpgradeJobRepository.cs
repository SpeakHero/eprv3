/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-22 16:30:00 +08:00 中国标准时间 星期四
 * 修改时间：2026-10-22 16:30:00 +08:00 中国标准时间 星期四
 * 描述：租户升级任务仓储契约，BuildingBlocks 升级流水线消费、门户模块实现
 */

namespace Qlx.Abstractions.Tenancy;

/// <summary>
/// 升级任务快照（跨层只读传输）。
/// </summary>
/// <param name="JobId">任务 ID。</param>
/// <param name="TenantId">租户 ID。</param>
/// <param name="Status">当前状态。</param>
/// <param name="ModuleCode">当前处理模块编码（复制中填充）。</param>
/// <param name="CurrentTable">当前处理表名（复制中填充）。</param>
/// <param name="RowsCopied">已累计复制行数。</param>
/// <param name="ErrorMessage">最近失败原因。</param>
/// <param name="RetryCount">累计重试次数。</param>
/// <param name="OperatorName">发起人。</param>
/// <param name="StartedAt">任务创建时间（UTC）。</param>
/// <param name="CompletedAt">完成 / 失败时间（UTC）。</param>
public sealed record TenantUpgradeJobSnapshot(
    long JobId,
    long TenantId,
    TenantUpgradeStatus Status,
    string? ModuleCode,
    string? CurrentTable,
    long RowsCopied,
    string? ErrorMessage,
    int RetryCount,
    string OperatorName,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt);

/// <summary>
/// 表级复制断点快照。
/// </summary>
/// <param name="ModuleCode">模块编码。</param>
/// <param name="TableName">表名。</param>
/// <param name="LastKey">已复制到的最大主键。</param>
/// <param name="RowsCopied">该表累计复制行数。</param>
public sealed record TenantUpgradeCheckpointSnapshot(
    string ModuleCode,
    string TableName,
    long LastKey,
    long RowsCopied);

/// <summary>
/// 升级目标独立库定位信息（由门户侧按预配参数生成）。
/// </summary>
/// <param name="DbHost">数据库主机。</param>
/// <param name="DbPort">数据库端口。</param>
/// <param name="DbName">目标库名。</param>
public sealed record IsolatedTargetSnapshot(
    string DbHost,
    int DbPort,
    string DbName);

/// <summary>
/// 租户升级任务仓储契约。抽象门户库中"升级任务 + 表级断点"的读写，
/// 让 BuildingBlocks 层的升级流水线无需直接依赖 Portal 模块。
/// </summary>
public interface ITenantUpgradeJobRepository
{
    /// <summary>
    /// 尝试登记升级任务。同租户存在未完成任务（Pending~Cutover，或 Failed 未重试）时返回 <c>null</c>，
    /// 否则创建 Pending 任务并返回任务 ID。
    /// </summary>
    /// <param name="tenantId">租户 ID。</param>
    /// <param name="operatorName">发起人显示名。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task<long?> TryCreateJobAsync(long tenantId, string operatorName, CancellationToken cancellationToken);

    /// <summary>
    /// 获取全部待处理（Pending）任务 ID，按创建顺序返回。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    Task<IReadOnlyList<long>> GetPendingJobIdsAsync(CancellationToken cancellationToken);

    /// <summary>
    /// 获取任务快照；任务不存在返回 <c>null</c>。
    /// </summary>
    /// <param name="jobId">任务 ID。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task<TenantUpgradeJobSnapshot?> GetJobSnapshotAsync(long jobId, CancellationToken cancellationToken);

    /// <summary>
    /// 推进任务状态（实体内部校验合法迁移，非法迁移抛冲突异常）。
    /// </summary>
    /// <param name="jobId">任务 ID。</param>
    /// <param name="toStatus">目标状态。</param>
    /// <param name="moduleCode">当前模块编码（无则 null）。</param>
    /// <param name="currentTable">当前表名（无则 null）。</param>
    /// <param name="rowsCopied">已累计复制行数。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task AdvanceStatusAsync(
        long jobId, TenantUpgradeStatus toStatus, string? moduleCode, string? currentTable,
        long rowsCopied, CancellationToken cancellationToken);

    /// <summary>
    /// 标记任务失败并记录原因（保留独立库现场，可重试）。
    /// </summary>
    /// <param name="jobId">任务 ID。</param>
    /// <param name="errorMessage">失败原因。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task MarkFailedAsync(long jobId, string errorMessage, CancellationToken cancellationToken);

    /// <summary>
    /// 重试失败任务：仅 Failed → Pending，重试计数 +1 并清空错误信息；状态不符返回 <c>false</c>。
    /// </summary>
    /// <param name="jobId">任务 ID。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task<bool> RetryJobAsync(long jobId, CancellationToken cancellationToken);

    /// <summary>
    /// 写入 / 更新表级复制断点（按 JobId + ModuleCode + TableName 唯一）。
    /// </summary>
    /// <param name="jobId">任务 ID。</param>
    /// <param name="moduleCode">模块编码。</param>
    /// <param name="tableName">表名。</param>
    /// <param name="lastKey">已复制到的最大主键。</param>
    /// <param name="rowsCopied">该表累计复制行数。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task UpsertCheckpointAsync(
        long jobId, string moduleCode, string tableName, long lastKey, long rowsCopied, CancellationToken cancellationToken);

    /// <summary>
    /// 获取任务的全部表级断点。
    /// </summary>
    /// <param name="jobId">任务 ID。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task<IReadOnlyList<TenantUpgradeCheckpointSnapshot>> GetCheckpointsAsync(long jobId, CancellationToken cancellationToken);

    /// <summary>
    /// 按租户解析升级目标独立库定位信息（库名按预配前缀 + 租户编码生成）；租户不存在返回 <c>null</c>。
    /// </summary>
    /// <param name="tenantId">租户 ID。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task<IsolatedTargetSnapshot?> GetIsolatedTargetAsync(long tenantId, CancellationToken cancellationToken);

    /// <summary>
    /// 尝试切换门户路由：租户模式 Shared → Isolated 并改写路由行主机 / 端口 / 库名；
    /// 当前模式不符返回 <c>false</c>。
    /// </summary>
    /// <param name="tenantId">租户 ID。</param>
    /// <param name="dbHost">目标主机。</param>
    /// <param name="dbPort">目标端口。</param>
    /// <param name="dbName">目标库名。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task<bool> TryCutoverAsync(long tenantId, string dbHost, int dbPort, string dbName, CancellationToken cancellationToken);

    /// <summary>
    /// 获取已完成且完成时间早于阈值、等待清理共享库残留的任务列表。
    /// </summary>
    /// <param name="completedBefore">完成时间阈值（UTC）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task<IReadOnlyList<TenantUpgradeJobSnapshot>> GetJobsAwaitingCleanupAsync(
        DateTimeOffset completedBefore, CancellationToken cancellationToken);

    /// <summary>
    /// 标记任务共享库残留已清理（Done → Cleaned）。
    /// </summary>
    /// <param name="jobId">任务 ID。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task MarkCleanedAsync(long jobId, CancellationToken cancellationToken);

    /// <summary>
    /// 获取租户最近一次升级任务；无任务返回 <c>null</c>。
    /// </summary>
    /// <param name="tenantId">租户 ID。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task<TenantUpgradeJobSnapshot?> GetLatestJobByTenantAsync(long tenantId, CancellationToken cancellationToken);
}
