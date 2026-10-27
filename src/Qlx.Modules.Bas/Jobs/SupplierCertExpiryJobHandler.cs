/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.1.0 beta
 * 创建时间：2026-10-25 11:00:00 +08:00 中国标准时间 星期日
 * 修改时间：2026-10-25 20:00:00 +08:00 中国标准时间 星期日
 * 描述：供应商资质到期扫描任务（bas.supplier.cert.expiry）——分批键集扫描，
 *       预警期内经消息服务推送并打幂等标记，已过期未更新则自动冻结供应商
 */

using System.Diagnostics;
using System.Globalization;
using System.Text.Json;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

using Qlx.Abstractions.Audit;
using Qlx.Abstractions.Constants;
using Qlx.Abstractions.Domain;
using Qlx.Abstractions.Jobs;
using Qlx.Abstractions.Messages;
using Qlx.Abstractions.Modules;
using Qlx.Abstractions.Serialization;
using Qlx.Abstractions.Tenancy;
using Qlx.BuildingBlocks.Caching;
using Qlx.BuildingBlocks.Ids;
using Qlx.Modules.Bas.Domain;
using Qlx.Modules.Bas.Repositories;

namespace Qlx.Modules.Bas.Jobs;

/// <summary>
/// 供应商资质到期扫描任务处理器。批次大小读 <c>Qlx:Bas:CertScanBatchSize</c>（默认 200），
/// 预警天数读 <c>bas.supplier.cert.warnDays</c>（默认 30）；扫描幂等可重入
/// （预警打 <c>WarnedAt</c> 标记、冻结仅对合作中供应商生效）。
/// </summary>
/// <param name="tenantContext">当前租户上下文。</param>
/// <param name="repo">Bas 租户仓储。</param>
/// <param name="parameters">系统参数读取（预警天数）。</param>
/// <param name="configuration">配置读取（批次大小）。</param>
/// <param name="messageService">消息服务（预警推送）。</param>
/// <param name="cache">统一缓存服务（冻结后失效 Bas 租户前缀）。</param>
/// <param name="audit">审计服务。</param>
/// <param name="snowflake">雪花 Id 生成器（审计事件 Id）。</param>
/// <param name="userContext">当前用户上下文（接收人缺省值 / 审计变更人）。</param>
/// <param name="logger">日志器。</param>
internal sealed partial class SupplierCertExpiryJobHandler(
    ITenantContext tenantContext,
    IBasTenantRepository repo,
    ISystemParameterProvider parameters,
    IConfiguration configuration,
    IMessageService messageService,
    ICacheService cache,
    IAuditService audit,
    ISnowflakeIdGenerator snowflake,
    ICurrentUserContext userContext,
    ILogger<SupplierCertExpiryJobHandler> logger) : IJobHandler
{
    /// <summary>资质预警天数参数键（por_parameters）。</summary>
    internal const string WarnDaysParamKey = "bas.supplier.cert.warnDays";

    /// <summary>资质预警天数默认值。</summary>
    internal const int DefaultWarnDays = 30;

    /// <summary>批次大小配置键。</summary>
    internal const string BatchSizeConfigKey = "Qlx:Bas:CertScanBatchSize";

    /// <summary>批次大小默认值。</summary>
    internal const int DefaultBatchSize = 200;

    private readonly ITenantContext _tenantContext = tenantContext;
    private readonly IBasTenantRepository _repo = repo;
    private readonly ISystemParameterProvider _parameters = parameters;
    private readonly IConfiguration _configuration = configuration;
    private readonly IMessageService _messageService = messageService;
    private readonly ICacheService _cache = cache;
    private readonly IAuditService _audit = audit;
    private readonly ISnowflakeIdGenerator _snowflake = snowflake;
    private readonly ICurrentUserContext _userContext = userContext;
    private readonly ILogger<SupplierCertExpiryJobHandler> _logger = logger;

    /// <inheritdoc />
    public string TaskCode => BasJobCodes.SupplierCertExpiry;

    /// <inheritdoc />
    public async Task<CompletedResult> ExecuteAsync(JobContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var stopwatch = Stopwatch.StartNew();

        if (!GuidIdMapper.TryToLong(context.TenantId, out long tenantId))
        {
            LogTenantUnresolved(context.ExecutionId, context.TenantId);
            return Failure(stopwatch, "任务租户标识无法还原为租户 Id。");
        }

        long? currentTenantId = _tenantContext.TenantId;
        if (currentTenantId is > 0 && currentTenantId.Value != tenantId)
        {
            LogTenantMismatch(context.ExecutionId, tenantId, currentTenantId.Value);
            return Failure(stopwatch, "任务租户与当前租户上下文不一致，已跳过扫描。");
        }

        int batchSize = Math.Clamp(
            _configuration.GetValue(BatchSizeConfigKey, DefaultBatchSize), 1, 500);
        int warnDays = await _parameters
            .GetIntAsync(WarnDaysParamKey, DefaultWarnDays, cancellationToken).ConfigureAwait(false);

        DateOnly today = DateOnly.FromDateTime(DateTime.UtcNow);
        DateOnly warnUntil = today.AddDays(warnDays);
        string recipient = ResolveRecipient(context);
        string changedBy = _userContext.UserId ?? "system";

        int scanned = 0;
        int warned = 0;
        int frozen = 0;
        long afterId = 0;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            IReadOnlyList<Domain.Supplier.SupplierCert> certs = await _repo
                .ListSupplierCertsForScanAsync(afterId, batchSize, cancellationToken).ConfigureAwait(false);
            if (certs.Count == 0)
            {
                break;
            }

            afterId = certs[^1].Id;
            scanned += certs.Count;

            List<long> supplierIds = [.. certs.Select(c => c.SupplierId).Distinct()];
            IReadOnlyList<Domain.Supplier.Supplier> suppliers = await _repo
                .ListSuppliersByIdsAsync(supplierIds, cancellationToken).ConfigureAwait(false);
            Dictionary<long, Domain.Supplier.Supplier> supplierById = suppliers.ToDictionary(s => s.Id);

            DateTimeOffset now = DateTimeOffset.UtcNow;
            foreach (Domain.Supplier.SupplierCert cert in certs)
            {
                if (cert.ExpiryDate is not { } expiryDate || expiryDate > warnUntil)
                {
                    continue;
                }

                supplierById.TryGetValue(cert.SupplierId, out Domain.Supplier.Supplier? supplier);

                if (expiryDate < today)
                {
                    cert.MarkExpired();
                    await _repo.UpdateSupplierCertAsync(cert, cancellationToken).ConfigureAwait(false);

                    if (supplier is not null && supplier.Status == PartnerStatus.Active)
                    {
                        string oldJson = JsonSerializer.Serialize(
                            new { supplier.Id, supplier.Code, Status = (int)supplier.Status },
                            QlxJsonDefaults.PreserveNull);
                        supplier.Freeze();
                        await _repo.UpdateSupplierAsync(supplier, cancellationToken).ConfigureAwait(false);
                        frozen++;
                        await CaptureAuditAsync(
                            supplier,
                            oldJson,
                            changedBy,
                            tenantId,
                            cancellationToken).ConfigureAwait(false);
                    }

                    continue;
                }

                if (cert.WarnedAt is not null)
                {
                    continue;
                }

                cert.MarkWarned(now);
                await _repo.UpdateSupplierCertAsync(cert, cancellationToken).ConfigureAwait(false);
                warned++;

                await _messageService.PushAsync(
                    new PushRequest(
                        recipient,
                        "供应商资质到期预警",
                        $"供应商 {supplier?.Name ?? supplier?.Code ?? cert.SupplierId.ToString(CultureInfo.InvariantCulture)} 的资质「{cert.CertType}」（{cert.CertNo}）将于 {expiryDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)} 到期，请及时更新。",
                        MessageChannel.InApp,
                        MessagePriority.High,
                        new MessageBizRef(
                            "bas.supplier.cert",
                            cert.Id.ToString(CultureInfo.InvariantCulture),
                            supplier?.Code)),
                    cancellationToken).ConfigureAwait(false);
            }

            _ = await _repo.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        if (warned > 0 || frozen > 0)
        {
            _ = await _cache.RemoveByPrefixAsync(
                $"{QlxCacheKey.TenantKeyPrefix}:{tenantId.ToString(CultureInfo.InvariantCulture)}:{QlxModuleCodes.Bas}:",
                cancellationToken).ConfigureAwait(false);
        }

        LogCompleted(context.ExecutionId, tenantId, scanned, warned, frozen);
        return new CompletedResult(
            new { TenantId = tenantId, Scanned = scanned, Warned = warned, Frozen = frozen },
            null, null, stopwatch.ElapsedMilliseconds, IsSuccess: true);
    }

    private string ResolveRecipient(JobContext context)
    {
        if (context.Parameters.TryGetValue("initiateUserId", out object? value)
            && value is string initiateUserId
            && !string.IsNullOrWhiteSpace(initiateUserId))
        {
            return initiateUserId;
        }

        return string.IsNullOrWhiteSpace(_userContext.UserId) ? "admin" : _userContext.UserId;
    }

    private Task CaptureAuditAsync(
        Domain.Supplier.Supplier supplier, string oldJson, string changedBy, long tenantId, CancellationToken ct)
        => _audit.CaptureChangeAsync(
            new AuditLogEntry(
                _snowflake.NextId(),
                nameof(Domain.Supplier.Supplier),
                supplier.Id,
                "UPDATE",
                oldJson,
                JsonSerializer.Serialize(
                    new { supplier.Id, supplier.Code, Status = (int)supplier.Status },
                    QlxJsonDefaults.PreserveNull),
                changedBy,
                DateTimeOffset.UtcNow,
                tenantId),
            ct);

    private static CompletedResult Failure(Stopwatch stopwatch, string message)
        => new(
            new { Scanned = 0, Warned = 0, Frozen = 0 },
            null, null, stopwatch.ElapsedMilliseconds, IsSuccess: false, message);

    [LoggerMessage(Level = LogLevel.Warning, Message = "供应商资质到期扫描跳过：任务租户标识无法还原（executionId={ExecutionId}, tenantId={TenantId}）")]
    private partial void LogTenantUnresolved(Guid executionId, Guid tenantId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "供应商资质到期扫描跳过：任务租户与上下文不一致（executionId={ExecutionId}, jobTenantId={JobTenantId}, contextTenantId={ContextTenantId}）")]
    private partial void LogTenantMismatch(Guid executionId, long jobTenantId, long contextTenantId);

    [LoggerMessage(Level = LogLevel.Information, Message = "供应商资质到期扫描完成：租户 {TenantId}，扫描 {Scanned}，预警 {Warned}，冻结 {Frozen}（executionId={ExecutionId}）")]
    private partial void LogCompleted(Guid executionId, long tenantId, int scanned, int warned, int frozen);
}
