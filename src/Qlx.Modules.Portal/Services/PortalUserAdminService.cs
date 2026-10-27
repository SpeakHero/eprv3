/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.1.0 beta
 * 创建时间：2026-10-21 19:20:00 +08:00 中国标准时间 星期三
 * 修改时间：2026-10-26 23:20:00 +08:00 中国标准时间 星期一
 * 描述：门户用户管理服务实现：列表 / 启停 / 重置密码 / 详情（成员关系 + 登录日志取自平台日志中心）
 *       重置密码入参的密码策略校验由 ValidationDispatchProxy 按 IValidatableObject 接口自动识别并在方法入口执行
 */

using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Qlx.Abstractions.Audit;
using Qlx.Abstractions.Constants;
using Qlx.Abstractions.Domain;
using Qlx.Abstractions.Exceptions;
using Qlx.BuildingBlocks.Api;
using Qlx.BuildingBlocks.Caching;
using Qlx.BuildingBlocks.Ids;
using Qlx.Modules.Portal.Auth;
using Qlx.Modules.Portal.Domain.Identity;
using Qlx.Modules.Portal.Dtos;
using Qlx.Modules.Portal.Repositories;

namespace Qlx.Modules.Portal.Services;

/// <summary>
/// 门户用户管理服务。
/// <para>
/// 登录日志统一由平台日志中心（<see cref="IAuditService"/> 操作日志）承载，
/// 门户库不落独立登录日志表：写入方为认证端点，读取方为本服务按操作人跨租户查询。
/// </para>
/// </summary>
public sealed partial class PortalUserAdminService(
    IPortalUserAdminRepository users,
    IAuditService audit,
    IPasswordHasher<PortalUser> passwordHasher,
    ICacheService cache,
    ISnowflakeIdGenerator snowflake,
    IIdObfuscator obfuscator,
    ICurrentUserContext currentUser,
    IOptions<QlxAuthOptions> authOptions,
    ILogger<PortalUserAdminService> logger) : IPortalUserAdminService
{
    /// <summary>LoggerMessage 源生成器要求字段（主构造参数经字段初始化器接入）。</summary>
    private readonly ILogger<PortalUserAdminService> _logger = logger;

    /// <inheritdoc />
    public async Task<PagedResult<PortalUserItem>> QueryAsync(
        PortalUserQueryRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var page = await users.QueryAsync(
            request.Keyword, request.Status, request, cancellationToken).ConfigureAwait(false);

        return new PagedResult<PortalUserItem>(
            [.. page.Items.Select(u => PortalAdminMapper.ToUserItem(u, obfuscator))],
            page.TotalCount, page.Page, page.PageSize);
    }

    /// <inheritdoc />
    public async Task<PortalUserItem> ToggleStatusAsync(string userId, CancellationToken cancellationToken)
    {
        var user = await FindTrackedUserAsync(userId, cancellationToken).ConfigureAwait(false);

        if (user.IsPortalAdmin && user.Status == UserStatus.Enabled)
        {
            throw new ConflictException("门户管理员账号不可停用", PortalConstants.ErrorCodes.PortalAdminProtected);
        }

        if (user.Status == UserStatus.Enabled)
        {
            user.Disable();
        }
        else
        {
            user.Enable();
        }

        await users.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await audit.CaptureOperationAsync(new OperationLogEntry(
            snowflake.NextId(), QlxAuditConstants.PortalModuleCode, PortalConstants.AuditOperations.ToggleUserStatus,
            $"{(user.Status == UserStatus.Enabled ? "启用" : "停用")}门户用户 {user.LoginAccount}",
            currentUser.UserName ?? QlxAuditConstants.PortalAdminOperatorName,
            DateTimeOffset.UtcNow, 0, QlxAuditConstants.TargetPortalUser, user.Id), cancellationToken).ConfigureAwait(false);

        return PortalAdminMapper.ToUserItem(user, obfuscator);
    }

    /// <inheritdoc />
    public async Task ResetPasswordAsync(
        string userId, ResetPortalUserPasswordRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        // 新密码复杂度由 ResetPortalUserPasswordRequest 的 IValidatableObject 校验：
        // HTTP 入口走 MVC 模型验证；非 HTTP 入口由 ValidationDispatchProxy 按接口自动识别并在方法入口校验（经代理上下文解析 QlxAuthOptions）
        var options = authOptions.Value;

        var user = await FindTrackedUserAsync(userId, cancellationToken).ConfigureAwait(false);

        // 历史禁重：取最近 N 条密码哈希逐个 VerifyHashedPassword（PBKDF2 salt 每次不同，禁止直接比哈希）
        var histories = options.PasswordNeverRepeatLastN > 0
            ? await users.GetRecentPasswordHistoriesAsync(user.Id, options.PasswordNeverRepeatLastN, cancellationToken).ConfigureAwait(false)
            : [];
        PasswordPolicyValidator.ValidateNotReused(
            request.NewPassword, user, histories, passwordHasher, options.PasswordNeverRepeatLastN);

        var newHash = passwordHasher.HashPassword(user, request.NewPassword);
        user.SetPasswordHash(newHash);

        // 密码修改后所有已签发 Token 刷新即失效（规格 6.6.3）
        user.BumpTokenVersion();

        // 记录新密码哈希到历史表 + 清理最旧的到保留上限
        await users.AddPasswordHistoryAsync(
            new PortalUserPasswordHistory(snowflake.NextId(), user.Id, newHash),
            cancellationToken).ConfigureAwait(false);
        await users.TrimPasswordHistoriesAsync(
            user.Id, options.PasswordHistoryRetentionCount, cancellationToken).ConfigureAwait(false);

        await users.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        // 清理登录失败计数与锁定标记，避免重置后仍被旧锁定拦截（键经哈希构建，与登录侧一致）
        await cache.RemoveAsync(AuthSecurityCacheKeys.PasswordFail(user.LoginAccount), cancellationToken)
            .ConfigureAwait(false);
        await cache.RemoveAsync(AuthSecurityCacheKeys.Lockout(user.LoginAccount), cancellationToken)
            .ConfigureAwait(false);

        LogPasswordReset(user.LoginAccount);

        await audit.CaptureOperationAsync(new OperationLogEntry(
            snowflake.NextId(), QlxAuditConstants.PortalModuleCode, PortalConstants.AuditOperations.ResetUserPassword,
            $"重置门户用户 {user.LoginAccount} 密码",
            currentUser.UserName ?? QlxAuditConstants.PortalAdminOperatorName,
            DateTimeOffset.UtcNow, 0, QlxAuditConstants.TargetPortalUser, user.Id), cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<PortalUserDetailResponse> GetDetailAsync(
        string userId, CancellationToken cancellationToken)
    {
        var id = DecodeId(userId);
        var user = await users.FindTrackedAsync(id, cancellationToken).ConfigureAwait(false)
            ?? throw new NotFoundException(PortalConstants.ResourceNames.PortalUser, userId);

        var memberships = await users.ListMembershipsAsync(id, cancellationToken).ConfigureAwait(false);

        // 登录日志来自平台日志中心（按操作人跨租户查询，含门户登录与各租户进入记录）
        var operations = await audit.QueryOperationsByOperatorAsync(
            user.LoginAccount, cancellationToken, QlxAuditConstants.PortalModuleCode,
            PortalConstants.Validation.LoginLogQueryLimit).ConfigureAwait(false);

        var logs = operations
            .Where(o => o.Operation is PortalConstants.AuditOperations.LoginSuccess or PortalConstants.AuditOperations.LoginFailure)
            .Select(o => ToLoginLogItem(o))
            .ToList();

        return new PortalUserDetailResponse(
            PortalAdminMapper.ToUserItem(user, obfuscator),
            [.. memberships.Select(m => PortalAdminMapper.ToMembershipItem(m, obfuscator))],
            logs);
    }

    /// <summary>操作日志 → 登录日志项。</summary>
    private LoginLogItem ToLoginLogItem(OperationLogEntry entry) => new()
    {
        Id = obfuscator.Encode(entry.EventId),
        LoginAccount = entry.OperatedBy,
        TenantName = string.IsNullOrWhiteSpace(entry.Description) ? PortalConstants.Labels.PortalScopeName : entry.Description,
        Result = entry.Operation == PortalConstants.AuditOperations.LoginFailure ? PortalConstants.Frontend.Failure : PortalConstants.Frontend.Success,
        IpAddress = ExtractIpAddress(entry.Metadata),
        At = entry.OperatedAt,
    };

    /// <summary>从扩展 JSON 中提取来源 IP（解析失败返回空串，不阻断详情查询）。</summary>
    private static string ExtractIpAddress(string? metadata)
    {
        if (string.IsNullOrWhiteSpace(metadata))
        {
            return string.Empty;
        }

        try
        {
            using var document = System.Text.Json.JsonDocument.Parse(metadata);
            return document.RootElement.TryGetProperty(PortalConstants.Parameters.MetadataIp, out var ip)
                ? ip.GetString() ?? string.Empty
                : string.Empty;
        }
        catch (System.Text.Json.JsonException)
        {
            return string.Empty;
        }
    }

    /// <summary>按混淆 Id 取跟踪态用户。</summary>
    private async Task<PortalUser> FindTrackedUserAsync(string userId, CancellationToken cancellationToken)
    {
        var id = DecodeId(userId);
        return await users.FindTrackedAsync(id, cancellationToken).ConfigureAwait(false)
            ?? throw new NotFoundException(PortalConstants.ResourceNames.PortalUser, userId);
    }

    /// <summary>解码对外 Id。</summary>
    private long DecodeId(string publicId)
    {
        if (!obfuscator.TryDecode(publicId, out var id) || id <= 0)
        {
            throw new NotFoundException(PortalConstants.ResourceNames.PortalUser, publicId);
        }

        return id;
    }

    /// <summary>重置密码成功日志。</summary>
    [LoggerMessage(Level = LogLevel.Information, Message = "门户管理员重置用户 {LoginAccount} 密码，token 版本已递增")]
    private partial void LogPasswordReset(string loginAccount);
}
