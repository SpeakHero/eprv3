/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-21 00:30:00 +08:00 中国标准时间 星期三
 * 修改时间：2026-10-21 00:30:00 +08:00 中国标准时间 星期三
 * 描述：IAuditService 内存占位实现（仅单元测试用，生产/开发统一用 EfCoreAuditService）
 */

using Qlx.Abstractions.Audit;

namespace Qlx.BuildingBlocks.Tests;

/// <summary>
/// 单元测试专用内存审计服务。生产与开发均使用 <c>EfCoreAuditService</c> 写入审计库；
/// 仅在不依赖真实数据库的单元测试中使用此实现。
/// </summary>
public sealed class InMemoryAuditService : IAuditService
{
    private readonly List<AuditLogEntry> _changes = [];
    private readonly List<OperationLogEntry> _operations = [];

    /// <inheritdoc />
    public Task CaptureChangeAsync(AuditLogEntry entry, CancellationToken cancellationToken)
    {
        _changes.Add(entry);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task CaptureOperationAsync(OperationLogEntry entry, CancellationToken cancellationToken)
    {
        _operations.Add(entry);
        return Task.CompletedTask;
    }

    /// <summary>单元测试辅助：读取已捕获的审计变更。</summary>
    public IReadOnlyList<AuditLogEntry> GetCapturedChanges() => _changes;

    /// <summary>单元测试辅助：读取已捕获的操作日志。</summary>
    public IReadOnlyList<OperationLogEntry> GetCapturedOperations() => _operations;

    /// <inheritdoc />
    public Task<IReadOnlyList<AuditLogEntry>> QueryChangesAsync(
        long tenantId, CancellationToken cancellationToken, string? entityType = null, long? entityId = null, string? changedBy = null,
        DateTimeOffset? from = null, DateTimeOffset? toTime = null)
    {
        var q = _changes.AsEnumerable();
        q = q.Where(e => e.TenantId == tenantId);
        if (!string.IsNullOrWhiteSpace(entityType)) q = q.Where(e => string.Equals(e.EntityType, entityType, StringComparison.Ordinal));
        if (entityId.HasValue) q = q.Where(e => e.EntityId == entityId.Value);
        if (!string.IsNullOrWhiteSpace(changedBy)) q = q.Where(e => string.Equals(e.ChangedBy, changedBy, StringComparison.Ordinal));
        if (from.HasValue) q = q.Where(e => e.ChangedAt >= from.Value);
        if (toTime.HasValue) q = q.Where(e => e.ChangedAt <= toTime.Value);
        return Task.FromResult<IReadOnlyList<AuditLogEntry>>(q.ToList());
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<OperationLogEntry>> QueryOperationsAsync(
        long tenantId, CancellationToken cancellationToken, string? moduleCode = null, string? operation = null, string? operatedBy = null,
        DateTimeOffset? from = null, DateTimeOffset? toTime = null)
    {
        var q = _operations.AsEnumerable();
        q = q.Where(e => e.TenantId == tenantId);
        if (!string.IsNullOrWhiteSpace(moduleCode)) q = q.Where(e => string.Equals(e.ModuleCode, moduleCode, StringComparison.Ordinal));
        if (!string.IsNullOrWhiteSpace(operation)) q = q.Where(e => string.Equals(e.Operation, operation, StringComparison.Ordinal));
        if (!string.IsNullOrWhiteSpace(operatedBy)) q = q.Where(e => string.Equals(e.OperatedBy, operatedBy, StringComparison.Ordinal));
        if (from.HasValue) q = q.Where(e => e.OperatedAt >= from.Value);
        if (toTime.HasValue) q = q.Where(e => e.OperatedAt <= toTime.Value);
        return Task.FromResult<IReadOnlyList<OperationLogEntry>>(q.ToList());
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<OperationLogEntry>> QueryOperationsByOperatorAsync(
        string operatedBy, CancellationToken cancellationToken, string? moduleCode = null, int take = 100)
    {
        var q = _operations.AsEnumerable()
            .Where(e => string.Equals(e.OperatedBy, operatedBy, StringComparison.Ordinal));
        if (!string.IsNullOrWhiteSpace(moduleCode)) q = q.Where(e => string.Equals(e.ModuleCode, moduleCode, StringComparison.Ordinal));
        return Task.FromResult<IReadOnlyList<OperationLogEntry>>(
            q.OrderByDescending(e => e.OperatedAt).Take(take).ToList());
    }
}
