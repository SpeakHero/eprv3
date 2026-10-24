/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-21 00:30:00 +08:00 中国标准时间 星期三
 * 修改时间：2026-10-21 00:30:00 +08:00 中国标准时间 星期三
 * 描述：租户写入守卫拦截器，新增自动填 TenantId、禁止篡改、拒绝跨租户 Update/Delete
 */

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using Qlx.Abstractions.Constants;
using Qlx.Abstractions.Exceptions;

namespace Qlx.BuildingBlocks.MultiTenancy;

/// <summary>
/// 租户写入守卫（三层隔离的 L1 写入侧）。在 <see cref="DbContext.SaveChanges()"/> 之前遍历变更跟踪器：
/// ① 新增：<c>TenantId=0</c> 自动填充为当前租户；已显式指定其他租户则拒绝；
/// ② 修改：禁止篡改 <c>TenantId</c>；禁止修改其他租户的行；
/// ③ 删除：禁止删除其他租户的行。
/// <para>
/// 仅作用于 <see cref="TenantDbContext"/> 派生上下文；门户库等控制面上下文不参与租户守卫。
/// 当前租户 <c>&lt;=0</c>（无租户上下文，如开发兜底、启动期脚本）时守卫不生效，由数据库层 RLS 兜底。
/// </para>
/// </summary>
/// <remarks>
/// 构造函数。
/// </remarks>
/// <param name="logger">日志器（跨租户写入尝试会记录 Warning）。</param>
public sealed partial class TenantWriteGuardInterceptor(ILogger<TenantWriteGuardInterceptor> logger) : SaveChangesInterceptor
{
    private const string TenantIdPropertyName = QlxHostConstants.TenantEntityPropertyName;

    private readonly ILogger<TenantWriteGuardInterceptor> _logger = logger;


    /// <inheritdoc />
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Guard(eventData.Context);
        return result;
    }

    /// <inheritdoc />
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken)
    {
        Guard(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    /// <summary>
    /// 执行守卫：只处理租户库上下文，按实体状态分流校验。
    /// </summary>
    private void Guard(DbContext? context)
    {
        if (context is not TenantDbContext tenantDbContext)
        {
            return;
        }

        var currentTenantId = tenantDbContext.CurrentTenantId;
        if (currentTenantId <= 0)
        {
            // 无租户上下文：不填充、不拦截，交由数据库层 RLS 兜底
            return;
        }

        foreach (var entry in context.ChangeTracker.Entries())
        {
            if (entry.Metadata.FindProperty(TenantIdPropertyName) is null)
            {
                continue;
            }

            switch (entry.State)
            {
                case EntityState.Added:
                    GuardAdded(entry, currentTenantId);
                    break;
                case EntityState.Modified:
                    GuardModified(entry, currentTenantId);
                    break;
                case EntityState.Deleted:
                    GuardDeleted(entry, currentTenantId);
                    break;
            }
        }
    }

    /// <summary>新增：未指定租户则自动填充；显式指定其他租户则拒绝。</summary>
    private void GuardAdded(EntityEntry entry, long currentTenantId)
    {
        var tenantIdProperty = entry.Property(TenantIdPropertyName);
        var value = tenantIdProperty.CurrentValue is long id ? id : 0L;

        if (value == 0L)
        {
            tenantIdProperty.CurrentValue = currentTenantId;
            return;
        }

        if (value != currentTenantId)
        {
            Reject(entry, "INSERT", value, currentTenantId);
        }
    }

    /// <summary>修改：禁止篡改 TenantId，禁止修改其他租户的行。</summary>
    private void GuardModified(EntityEntry entry, long currentTenantId)
    {
        var tenantIdProperty = entry.Property(TenantIdPropertyName);
        var original = tenantIdProperty.OriginalValue is long originalId ? originalId : 0L;
        var current = tenantIdProperty.CurrentValue is long currentId ? currentId : 0L;

        if (original != currentTenantId)
        {
            Reject(entry, "UPDATE", original, currentTenantId);
        }

        if (current != original)
        {
            Reject(entry, "TENANT_ID_TAMPER", original, currentTenantId);
        }
    }

    /// <summary>删除：禁止删除其他租户的行。</summary>
    private void GuardDeleted(EntityEntry entry, long currentTenantId)
    {
        var tenantIdProperty = entry.Property(TenantIdPropertyName);
        var original = tenantIdProperty.OriginalValue is long originalId ? originalId : 0L;

        if (original != currentTenantId)
        {
            Reject(entry, "DELETE", original, currentTenantId);
        }
    }

    /// <summary>记录告警并拒绝本次写入（整批 SaveChanges 不落库）。</summary>
    private void Reject(EntityEntry entry, string operation, long rowTenantId, long currentTenantId)
    {
        var entityType = entry.Metadata.ClrType.Name;
        GuardViolation(entityType, operation, rowTenantId, currentTenantId);

        throw new ForbiddenException(
            $"检测到跨租户写入：{operation} {entityType}（行租户 {rowTenantId}，当前租户 {currentTenantId}）已被拒绝");
    }

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "拒绝跨租户写入：{Operation} {EntityType}（行租户 {RowTenantId}，当前租户 {CurrentTenantId}）")]
    private partial void GuardViolation(string entityType, string operation, long rowTenantId, long currentTenantId);
}
