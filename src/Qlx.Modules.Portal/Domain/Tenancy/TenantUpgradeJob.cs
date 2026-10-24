/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-22 16:30:00 +08:00 中国标准时间 星期四
 * 修改时间：2026-10-22 16:30:00 +08:00 中国标准时间 星期四
 * 描述：租户升级任务实体（共享库 → 独立库），状态机字段与重试控制
 */

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using Qlx.Abstractions.Exceptions;
using Qlx.Abstractions.Tenancy;

namespace Qlx.Modules.Portal.Domain.Tenancy;

/// <summary>
/// 租户升级任务（表前缀 por_）。承载共享库 → 独立库升级流水线的状态机字段：
/// 当前状态、处理位置（模块 / 表）、累计复制行数、失败原因与重试计数。
/// 继承 <see cref="Qlx.Abstractions.Domain.AuditableEntity"/>：雪花 Id + 审计字段自动填充。
/// </summary>
[Table("por_tenant_upgrade_jobs")]
[Index(nameof(TenantId))]
[Index(nameof(Status))]
public sealed class TenantUpgradeJob : Qlx.Abstractions.Domain.AuditableEntity
{
    /// <summary>租户 ID（外键 → por_tenants）。</summary>
    public long TenantId { get; private set; }

    /// <summary>任务状态。</summary>
    public TenantUpgradeStatus Status { get; private set; } = TenantUpgradeStatus.Pending;

    /// <summary>当前处理模块编码（复制中填充）。</summary>
    [MaxLength(64)]
    public string? ModuleCode { get; private set; }

    /// <summary>当前处理表名（复制中填充）。</summary>
    [MaxLength(128)]
    public string? CurrentTable { get; private set; }

    /// <summary>已累计复制行数。</summary>
    public long RowsCopied { get; private set; }

    /// <summary>最近失败原因（重试时清空）。</summary>
    [MaxLength(4096)]
    public string? ErrorMessage { get; private set; }

    /// <summary>累计重试次数。</summary>
    public int RetryCount { get; private set; }

    /// <summary>发起人显示名。</summary>
    [MaxLength(64)]
    public string OperatorName { get; private set; } = string.Empty;

    /// <summary>任务创建时间（UTC），增量补差的时间下界。</summary>
    public DateTimeOffset? StartedAt { get; private set; }

    /// <summary>完成 / 失败时间（UTC）。</summary>
    public DateTimeOffset? CompletedAt { get; private set; }

    /// <summary>
    /// EF Core 无参构造。
    /// </summary>
    public TenantUpgradeJob() { }

    /// <summary>
    /// 登记升级任务（初始 Pending）。
    /// </summary>
    /// <param name="tenantId">租户 ID。</param>
    /// <param name="operatorName">发起人显示名。</param>
    public TenantUpgradeJob(long tenantId, string operatorName)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(operatorName);

        TenantId = tenantId;
        OperatorName = operatorName;
        Status = TenantUpgradeStatus.Pending;
        StartedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// 推进任务状态。仅允许 <see cref="TenantUpgradeStateTransitions"/> 白名单内的迁移，
    /// 非法迁移抛 <see cref="ConflictException"/>。
    /// </summary>
    /// <param name="to">目标状态。</param>
    /// <param name="moduleCode">当前模块编码（无则 null）。</param>
    /// <param name="currentTable">当前表名（无则 null）。</param>
    /// <param name="rowsCopied">已累计复制行数。</param>
    public void MarkStatus(TenantUpgradeStatus to, string? moduleCode, string? currentTable, long rowsCopied)
    {
        if (!TenantUpgradeStateTransitions.CanTransition(Status, to))
        {
            throw new ConflictException(
                $"升级任务 {Id} 不允许 {Status} → {to} 状态迁移", PortalConstants.ErrorCodes.TenantUpgradeTransitionInvalid);
        }

        Status = to;
        ModuleCode = moduleCode;
        CurrentTable = currentTable;
        RowsCopied = rowsCopied;

        if (to is TenantUpgradeStatus.Done or TenantUpgradeStatus.Failed or TenantUpgradeStatus.Cleaned)
        {
            CompletedAt ??= DateTimeOffset.UtcNow;
        }
    }

    /// <summary>
    /// 标记任务失败（保留独立库现场，可重试）。
    /// </summary>
    /// <param name="error">失败原因。</param>
    public void MarkFailed(string error)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(error);

        if (!TenantUpgradeStateTransitions.CanTransition(Status, TenantUpgradeStatus.Failed))
        {
            throw new ConflictException(
                $"升级任务 {Id} 当前状态 {Status} 不允许标记失败", PortalConstants.ErrorCodes.TenantUpgradeTransitionInvalid);
        }

        Status = TenantUpgradeStatus.Failed;
        ErrorMessage = error;
        CompletedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// 重试失败任务：Failed → Pending，重试计数 +1 并清空错误与处理位置。
    /// </summary>
    public void MarkRetry()
    {
        if (!TenantUpgradeStateTransitions.CanTransition(Status, TenantUpgradeStatus.Pending))
        {
            throw new ConflictException(
                $"升级任务 {Id} 当前状态 {Status} 不允许重试（仅失败任务可重试）", PortalConstants.ErrorCodes.TenantUpgradeRetryInvalid);
        }

        Status = TenantUpgradeStatus.Pending;
        RetryCount++;
        ErrorMessage = null;
        ModuleCode = null;
        CurrentTable = null;
        CompletedAt = null;
    }
}
