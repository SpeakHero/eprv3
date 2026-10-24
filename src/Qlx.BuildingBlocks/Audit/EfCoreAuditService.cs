/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-21 00:30:00 +08:00 中国标准时间 星期三
 * 修改时间：2026-10-21 00:30:00 +08:00 中国标准时间 星期三
 * 描述：生产级 IAuditService 实现（写入 AuditDbContext 审计库）
 */

using Microsoft.EntityFrameworkCore;
using Qlx.Abstractions.Audit;
using Qlx.BuildingBlocks.Ids;

namespace Qlx.BuildingBlocks.Audit;

/// <summary>
/// 生产级审计服务。使用独立 <see cref="AuditDbContext"/> 写入审计库，
/// 避免与 <see cref="AuditSaveChangesInterceptor"/> 循环依赖。
/// </summary>
/// <remarks>
/// 构造函数。
/// </remarks>
/// <param name="factory">AuditDbContext 工厂（Scoped / 瞬态场景避免 DbContext 被并发 SaveChanges 占用）。</param>
/// <param name="snowflake">雪花 ID 生成器，用于审计事件主键。</param>
public sealed class EfCoreAuditService(IDbContextFactory<AuditDbContext> factory, ISnowflakeIdGenerator snowflake) : IAuditService
{
    private readonly IDbContextFactory<AuditDbContext> _factory = factory;
    private readonly ISnowflakeIdGenerator _snowflake = snowflake;


    /// <inheritdoc />
    public async Task CaptureChangeAsync(AuditLogEntry entry, CancellationToken cancellationToken)
    {
        await using var db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        db.AuditLogs.Add(new AuditLogEntity
        {
            EventId = _snowflake.NextId(),
            EntityType = entry.EntityType,
            EntityId = entry.EntityId,
            Operation = entry.Operation,
            OldValues = entry.OldValues,
            NewValues = entry.NewValues,
            ChangedBy = entry.ChangedBy,
            ChangedAt = entry.ChangedAt,
            TenantId = entry.TenantId,
        });
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task CaptureOperationAsync(OperationLogEntry entry, CancellationToken cancellationToken)
    {
        await using var db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        db.OperationLogs.Add(new OperationLogEntity
        {
            EventId = _snowflake.NextId(),
            ModuleCode = entry.ModuleCode,
            Operation = entry.Operation,
            Description = entry.Description,
            OperatedBy = entry.OperatedBy,
            OperatedAt = entry.OperatedAt,
            TenantId = entry.TenantId,
            TargetType = entry.TargetType,
            TargetId = entry.TargetId,
            Metadata = entry.Metadata,
        });
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<AuditLogEntry>> QueryChangesAsync(
        long tenantId, CancellationToken cancellationToken, string? entityType = null, long? entityId = null, string? changedBy = null,
        DateTimeOffset? from = null, DateTimeOffset? toTime = null)
    {
        await using var db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        // 注意：AuditDbContext 刻意不继承 TenantDbContext（§8.11 循环依赖豁免），
        // 故无全局 TenantId 过滤能力，必须在审计查询中手动按 TenantId 隔离记录。
        IQueryable<AuditLogEntity> q = db.AuditLogs.AsNoTracking().Where(e => e.TenantId == tenantId);

        if (!string.IsNullOrWhiteSpace(entityType)) q = q.Where(e => e.EntityType == entityType);
        if (entityId.HasValue) q = q.Where(e => e.EntityId == entityId.Value);
        if (!string.IsNullOrWhiteSpace(changedBy)) q = q.Where(e => e.ChangedBy == changedBy);
        if (from.HasValue) q = q.Where(e => e.ChangedAt >= from.Value);
        if (toTime.HasValue) q = q.Where(e => e.ChangedAt <= toTime.Value);

        return (await q.OrderByDescending(e => e.ChangedAt).ToListAsync(cancellationToken).ConfigureAwait(false))
            .Select(e => new AuditLogEntry(
                e.EventId, e.EntityType, e.EntityId, e.Operation,
                e.OldValues, e.NewValues, e.ChangedBy, e.ChangedAt, e.TenantId))
            .ToList();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<OperationLogEntry>> QueryOperationsAsync(
        long tenantId, CancellationToken cancellationToken, string? moduleCode = null, string? operation = null, string? operatedBy = null,
        DateTimeOffset? from = null, DateTimeOffset? toTime = null)
    {
        await using var db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        // 同上：AuditDbContext 无全局 TenantId 过滤，必须手动隔离。
        IQueryable<OperationLogEntity> q = db.OperationLogs.AsNoTracking().Where(e => e.TenantId == tenantId);

        if (!string.IsNullOrWhiteSpace(moduleCode)) q = q.Where(e => e.ModuleCode == moduleCode);
        if (!string.IsNullOrWhiteSpace(operation)) q = q.Where(e => e.Operation == operation);
        if (!string.IsNullOrWhiteSpace(operatedBy)) q = q.Where(e => e.OperatedBy == operatedBy);
        if (from.HasValue) q = q.Where(e => e.OperatedAt >= from.Value);
        if (toTime.HasValue) q = q.Where(e => e.OperatedAt <= toTime.Value);

        return (await q.OrderByDescending(e => e.OperatedAt).ToListAsync(cancellationToken).ConfigureAwait(false))
            .Select(e => new OperationLogEntry(
                e.EventId, e.ModuleCode, e.Operation, e.Description,
                e.OperatedBy, e.OperatedAt, e.TenantId, e.TargetType, e.TargetId, e.Metadata))
            .ToList();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<OperationLogEntry>> QueryOperationsByOperatorAsync(
        string operatedBy, CancellationToken cancellationToken, string? moduleCode = null, int take = 100)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operatedBy);

        await using var db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        // 审计库为跨租户集中存储，此处按操作人维度查询（门户管理端平台级审计视图）。
        IQueryable<OperationLogEntity> q = db.OperationLogs.AsNoTracking().Where(e => e.OperatedBy == operatedBy);

        if (!string.IsNullOrWhiteSpace(moduleCode)) q = q.Where(e => e.ModuleCode == moduleCode);

        return (await q.OrderByDescending(e => e.OperatedAt).Take(take).ToListAsync(cancellationToken).ConfigureAwait(false))
            .Select(e => new OperationLogEntry(
                e.EventId, e.ModuleCode, e.Operation, e.Description,
                e.OperatedBy, e.OperatedAt, e.TenantId, e.TargetType, e.TargetId, e.Metadata))
            .ToList();
    }
}
