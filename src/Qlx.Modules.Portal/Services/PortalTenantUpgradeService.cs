/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-22 16:30:00 +08:00 中国标准时间 星期四
 * 修改时间：2026-10-22 16:30:00 +08:00 中国标准时间 星期四
 * 描述：租户升级管理服务实现：发起升级（登记任务）/ 查询进度 / 失败重试，全程落审计
 */

using Microsoft.Extensions.Logging;
using Qlx.Abstractions.Audit;
using Qlx.Abstractions.Constants;
using Qlx.Abstractions.Domain;
using Qlx.Abstractions.Exceptions;
using Qlx.Abstractions.Tenancy;
using Qlx.BuildingBlocks.Ids;
using Qlx.Modules.Portal.Domain.Tenancy;
using Qlx.Modules.Portal.Dtos;
using Qlx.Modules.Portal.Repositories;

namespace Qlx.Modules.Portal.Services;

/// <summary>
/// 租户升级管理服务。
/// <para>
/// 发起升级仅登记 Pending 任务（不阻塞请求），由 BuildingBlocks 升级调度后台服务消费执行；
/// 同租户存在进行中任务或未重试的失败任务时拒绝重复登记。
/// </para>
/// </summary>
public sealed partial class PortalTenantUpgradeService(
    IPortalTenantAdminRepository tenants,
    ITenantUpgradeJobRepository upgradeJobs,
    IIdObfuscator obfuscator,
    ICurrentUserContext currentUser,
    IAuditService audit,
    ISnowflakeIdGenerator snowflake,
    ILogger<PortalTenantUpgradeService> logger) : IPortalTenantUpgradeService
{
    /// <summary>LoggerMessage 源生成器要求字段（主构造参数经字段初始化器接入）。</summary>
    private readonly ILogger<PortalTenantUpgradeService> _logger = logger;

    /// <inheritdoc />
    public async Task<TenantUpgradeJobItem> PromoteToIsolatedAsync(string tenantId, CancellationToken cancellationToken)
    {
        var id = DecodeId(tenantId, PortalConstants.ResourceNames.Tenant);
        var tenant = await tenants.FindTrackedAsync(id, cancellationToken).ConfigureAwait(false)
            ?? throw new NotFoundException(PortalConstants.ResourceNames.Tenant, tenantId);

        if (tenant.Status != TenantStatus.Active)
        {
            throw new ConflictException(
                $"租户当前状态 {tenant.Status} 不允许升级独立库（仅正常运行租户可升级）", PortalConstants.ErrorCodes.TenantStatusConflict);
        }

        if (tenant.DatabaseMode != TenantDatabaseMode.Shared)
        {
            throw new ConflictException(
                "租户当前不是共享库模式，无需升级", PortalConstants.ErrorCodes.TenantDatabaseModeInvalid);
        }

        var operatorName = currentUser.UserDisplayName ?? currentUser.UserName ?? PortalConstants.Labels.OperatorDisplayFallback;
        var jobId = await upgradeJobs.TryCreateJobAsync(id, operatorName, cancellationToken).ConfigureAwait(false)
            ?? throw new ConflictException(
                "已存在进行中的升级任务（含待重试的失败任务），请等待完成或先重试失败任务", PortalConstants.ErrorCodes.TenantUpgradeInProgress);

        LogUpgradePromoted(id, jobId, operatorName);

        await audit.CaptureOperationAsync(new OperationLogEntry(
            snowflake.NextId(), QlxAuditConstants.PortalModuleCode, PortalConstants.AuditOperations.PromoteToIsolated,
            $"发起租户 {tenant.DisplayName} 升级独立库（任务 {jobId}）",
            currentUser.UserName ?? QlxAuditConstants.PortalAdminOperatorName,
            DateTimeOffset.UtcNow, id, QlxAuditConstants.TargetTenant, id), cancellationToken).ConfigureAwait(false);

        var snapshot = await upgradeJobs.GetJobSnapshotAsync(jobId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"升级任务 {jobId} 登记后读取失败");

        return ToItem(snapshot, obfuscator);
    }

    /// <inheritdoc />
    public async Task<TenantUpgradeJobItem?> GetUpgradeJobAsync(string tenantId, CancellationToken cancellationToken)
    {
        var id = DecodeId(tenantId, PortalConstants.ResourceNames.Tenant);
        var snapshot = await upgradeJobs.GetLatestJobByTenantAsync(id, cancellationToken).ConfigureAwait(false);
        return snapshot is null ? null : ToItem(snapshot, obfuscator);
    }

    /// <inheritdoc />
    public async Task<TenantUpgradeJobItem> RetryUpgradeJobAsync(string tenantId, CancellationToken cancellationToken)
    {
        var id = DecodeId(tenantId, PortalConstants.ResourceNames.Tenant);
        var latest = await upgradeJobs.GetLatestJobByTenantAsync(id, cancellationToken).ConfigureAwait(false)
            ?? throw new NotFoundException(PortalConstants.ResourceNames.UpgradeJob, tenantId);

        if (latest.Status != TenantUpgradeStatus.Failed)
        {
            throw new ConflictException(
                $"仅失败的升级任务可重试（当前状态 {latest.Status}）", PortalConstants.ErrorCodes.TenantUpgradeRetryInvalid);
        }

        var retried = await upgradeJobs.RetryJobAsync(latest.JobId, cancellationToken).ConfigureAwait(false);
        if (!retried)
        {
            throw new ConflictException("升级任务重试失败：状态已变更，请刷新后重试", PortalConstants.ErrorCodes.TenantUpgradeRetryInvalid);
        }

        LogUpgradeRetried(id, latest.JobId);

        await audit.CaptureOperationAsync(new OperationLogEntry(
            snowflake.NextId(), QlxAuditConstants.PortalModuleCode, PortalConstants.AuditOperations.RetryTenantUpgrade,
            $"重试租户升级独立库任务 {latest.JobId}（第 {latest.RetryCount + 1} 次）",
            currentUser.UserName ?? QlxAuditConstants.PortalAdminOperatorName,
            DateTimeOffset.UtcNow, id, QlxAuditConstants.TargetTenant, id), cancellationToken).ConfigureAwait(false);

        var snapshot = await upgradeJobs.GetJobSnapshotAsync(latest.JobId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"升级任务 {latest.JobId} 重试后读取失败");

        return ToItem(snapshot, obfuscator);
    }

    /// <summary>解码对外 Id（非法或越界一律视为资源不存在，避免暴露内部编号规律）。</summary>
    private long DecodeId(string publicId, string resourceType)
    {
        if (!obfuscator.TryDecode(publicId, out var id) || id <= 0)
        {
            throw new NotFoundException(resourceType, publicId);
        }

        return id;
    }

    private static TenantUpgradeJobItem ToItem(TenantUpgradeJobSnapshot snapshot, IIdObfuscator obfuscator)
        => new(
            obfuscator.Encode(snapshot.JobId),
            obfuscator.Encode(snapshot.TenantId),
            snapshot.Status.ToString(),
            snapshot.ModuleCode,
            snapshot.CurrentTable,
            snapshot.RowsCopied,
            snapshot.ErrorMessage,
            snapshot.RetryCount,
            snapshot.StartedAt,
            snapshot.CompletedAt);

    [LoggerMessage(Level = LogLevel.Information, Message = "租户 {TenantId} 升级独立库任务已登记（任务 {JobId}，发起人 {OperatorName}）")]
    private partial void LogUpgradePromoted(long tenantId, long jobId, string operatorName);

    [LoggerMessage(Level = LogLevel.Information, Message = "租户 {TenantId} 升级任务 {JobId} 已重试（重新进入待处理队列）")]
    private partial void LogUpgradeRetried(long tenantId, long jobId);
}
