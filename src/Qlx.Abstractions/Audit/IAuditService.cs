/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-20 23:30:00 +08:00 中国标准时间 星期二
 * 修改时间：2026-10-20 23:30:00 +08:00 中国标准时间 星期二
 * 描述：审计日志服务契约，统一记录实体变更快照 + 业务级操作日志
 */


namespace Qlx.Abstractions.Audit;

/// <summary>
/// 审计日志条目（实体变更快照）。
/// </summary>
public sealed record AuditLogEntry(
    long EventId,
    string EntityType,
    long EntityId,
    string Operation,    // INSERT / UPDATE / DELETE / SOFT_DELETE
    string? OldValues,   // JSON 快照（UPDATE/DELETE 时）
    string? NewValues,   // JSON 快照（INSERT/UPDATE 时）
    string ChangedBy,
    DateTimeOffset ChangedAt,
    long TenantId);

/// <summary>
/// 操作日志条目（业务级，面向用户可读）。
/// </summary>
public sealed record OperationLogEntry(
    long EventId,
    string ModuleCode,
    string Operation,    // 如 APPROVE / REJECT / CREATE
    string Description,  // 如 "审批了采购单 CG-202610-000001"
    string OperatedBy,
    DateTimeOffset OperatedAt,
    long TenantId,
    string? TargetType = null,
    long? TargetId = null,
    string? Metadata = null);   // 结构化扩展（JSON，如登录 IP、客户端信息）

/// <summary>
/// 审计 + 操作日志服务契约。EF Core SaveChanges 拦截器在变更时自动调用 CaptureChangeAsync，
/// 业务层主动调用 CaptureOperationAsync 记录操作日志。
/// </summary>
public interface IAuditService
{
    /// <summary>
    /// 捕获实体变更快照（由 EF Core 拦截器调用，业务层通常不直接调用）。
    /// </summary>
    Task CaptureChangeAsync(AuditLogEntry entry, CancellationToken cancellationToken);

    /// <summary>
    /// 记录业务级操作日志（业务层主动调用）。
    /// </summary>
    Task CaptureOperationAsync(OperationLogEntry entry, CancellationToken cancellationToken);

    /// <summary>
    /// 查询审计日志。
    /// </summary>
    Task<IReadOnlyList<AuditLogEntry>> QueryChangesAsync(
        long tenantId,
        CancellationToken cancellationToken,
        string? entityType = null,
        long? entityId = null,
        string? changedBy = null,
        DateTimeOffset? from = null,
        DateTimeOffset? toTime = null);

    /// <summary>
    /// 查询操作日志。
    /// </summary>
    Task<IReadOnlyList<OperationLogEntry>> QueryOperationsAsync(
        long tenantId,
        CancellationToken cancellationToken,
        string? moduleCode = null,
        string? operation = null,
        string? operatedBy = null,
        DateTimeOffset? from = null,
        DateTimeOffset? toTime = null);

    /// <summary>
    /// 按操作人跨租户查询操作日志（平台级审计视图：门户管理端用户登录轨迹、跨租户操作追溯）。
    /// </summary>
    /// <param name="operatedBy">操作人（登录账号）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <param name="moduleCode">模块编码过滤（可选）。</param>
    /// <param name="take">最大返回条数。</param>
    Task<IReadOnlyList<OperationLogEntry>> QueryOperationsByOperatorAsync(
        string operatedBy,
        CancellationToken cancellationToken,
        string? moduleCode = null,
        int take = 100);
}
