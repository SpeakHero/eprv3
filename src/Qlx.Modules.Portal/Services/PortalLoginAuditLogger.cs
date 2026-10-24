/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-21 19:50:00 +08:00 中国标准时间 星期三
 * 修改时间：2026-10-21 19:50:00 +08:00 中国标准时间 星期三
 * 描述：登录审计写入器：登录事件统一写平台日志中心（操作日志），门户库不建登录日志表
 */

using System.Text.Json;
using Microsoft.Extensions.Logging;
using Qlx.Abstractions.Audit;
using Qlx.Abstractions.Constants;
using Qlx.Abstractions.Serialization;
using Qlx.BuildingBlocks.Ids;
using Qlx.BuildingBlocks.Support.Logs;

namespace Qlx.Modules.Portal.Services;

/// <summary>门户登录审计写入契约。</summary>
public interface IPortalLoginAuditLogger
{
    /// <summary>记录登录成功（门户身份认证或进入租户上下文）。</summary>
    /// <param name="loginAccount">登录账号。</param>
    /// <param name="tenantName">归属租户名称（门户身份认证为「门户管理」）。</param>
    /// <param name="tenantId">租户 Id（门户身份认证为 0）。</param>
    /// <param name="ipAddress">来源 IP。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task RecordSuccessAsync(
        string loginAccount, string tenantName, long tenantId, string? ipAddress, CancellationToken cancellationToken);

    /// <summary>记录登录失败（账号不存在、密码错误、锁定、停用等）。</summary>
    /// <param name="loginAccount">登录账号。</param>
    /// <param name="ipAddress">来源 IP。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task RecordFailureAsync(string loginAccount, string? ipAddress, CancellationToken cancellationToken);
}

/// <summary>
/// 登录审计写入器实现（B4 后分流）。写入失败不阻断登录主流程（审计降级仅告警）：
/// 租户登录成功 → 本租户库 <c>log_login</c>；门户登录/失败 → 门户库 <c>plog_operation</c>。
/// </summary>
public sealed partial class PortalLoginAuditLogger(
    IAuditService audit,
    ITenantLogWriter tenantLogWriter,
    ISnowflakeIdGenerator snowflake,
    ILogger<PortalLoginAuditLogger> logger) : IPortalLoginAuditLogger
{
    /// <summary>LoggerMessage 源生成器要求字段（主构造参数经字段初始化器接入）。</summary>
    private readonly ILogger<PortalLoginAuditLogger> _logger = logger;

    /// <inheritdoc />
    public Task RecordSuccessAsync(
        string loginAccount, string tenantName, long tenantId, string? ipAddress, CancellationToken cancellationToken)
    {
        // B4-02：租户登录成功 → 租户库 log_login；门户身份认证（tenantId=0）→ 门户库 plog_operation
        if (tenantId > 0)
        {
            return WriteTenantLoginAsync(
                new TenantLogLoginEntry(tenantId, loginAccount, tenantName, ipAddress, true, null, DateTimeOffset.UtcNow),
                cancellationToken);
        }

        var entry = new OperationLogEntry(
            snowflake.NextId(), QlxAuditConstants.PortalModuleCode, PortalConstants.AuditOperations.LoginSuccess,
            PortalConstants.Labels.PortalScopeName, loginAccount, DateTimeOffset.UtcNow, tenantId,
            QlxAuditConstants.TargetPortalUser, null, BuildMetadata(ipAddress));

        return WritePlatformAsync(entry, cancellationToken);
    }

    /// <inheritdoc />
    public Task RecordFailureAsync(
        string loginAccount, string? ipAddress, CancellationToken cancellationToken)
    {
        // 登录失败统一记录到门户库 plog_operation（归属租户未知，不进租户日志）
        var entry = new OperationLogEntry(
            snowflake.NextId(), QlxAuditConstants.PortalModuleCode, PortalConstants.AuditOperations.LoginFailure,
            PortalConstants.Labels.PortalScopeName, loginAccount, DateTimeOffset.UtcNow, 0,
            QlxAuditConstants.TargetPortalUser, null, BuildMetadata(ipAddress));

        return WritePlatformAsync(entry, cancellationToken);
    }

    /// <summary>写租户库 log_login；异常降级为告警，绝不阻断认证主流程。</summary>
    private async Task WriteTenantLoginAsync(TenantLogLoginEntry entry, CancellationToken cancellationToken)
    {
        try
        {
            await tenantLogWriter.WriteLoginAsync(entry, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogAuditWriteFailed(ex, PortalConstants.Labels.TenantLoginLogTag, entry.LoginAccount);
        }
    }

    /// <summary>写门户库 plog_operation；异常降级为告警，绝不阻断认证主流程。</summary>
    private async Task WritePlatformAsync(OperationLogEntry entry, CancellationToken cancellationToken)
    {
        try
        {
            await audit.CaptureOperationAsync(entry, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogAuditWriteFailed(ex, entry.Operation, entry.OperatedBy);
        }
    }

    /// <summary>审计写入失败告警。</summary>
    [LoggerMessage(Level = LogLevel.Warning, Message = "登录审计写入失败（{Operation} {LoginAccount}）")]
    private partial void LogAuditWriteFailed(Exception exception, string operation, string loginAccount);

    /// <summary>构造扩展 JSON（来源 IP）。</summary>
    private static string BuildMetadata(string? ipAddress)
        => JsonSerializer.Serialize(
            new Dictionary<string, string>
            {
                [PortalConstants.Parameters.MetadataIp] = ipAddress ?? string.Empty,
            },
            QlxJsonDefaults.Default);
}
