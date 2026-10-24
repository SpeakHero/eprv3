/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-22 21:10:00 +08:00 中国标准时间 星期四
 * 描述：三阶段登录业务服务：身份认证 / 身份 Token 刷新 / 租户上下文 / 租户 Token 静默续期
 */

using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using OpenIddict.Abstractions;
using OpenIddict.Server;
using Qlx.Abstractions.Constants;
using Qlx.Abstractions.Modules;
using Qlx.Abstractions.Queries;
using Qlx.Abstractions.Tenancy;
using Qlx.BuildingBlocks.Caching;
using Qlx.BuildingBlocks.Ids;
using Qlx.Modules.Portal.Auth;
using Qlx.Modules.Portal.Domain.Identity;
using Qlx.Modules.Portal.Domain.Tenancy;

namespace Qlx.Modules.Portal.Services;

/// <summary>
/// 门户认证结果。成功时返回已构造的 <see cref="ClaimsPrincipal"/> 与需写入 Token 响应的扩展参数；
/// 失败时返回 OpenIddict 错误码与描述，由 API 层转换为 Forbid 响应。
/// </summary>
/// <param name="Principal">认证主体（失败为 null）。</param>
/// <param name="ResponseParameters">Token 响应扩展参数（如租户列表、门户管理员标记）。</param>
/// <param name="Error">OpenIddict 错误码（成功为 null）。</param>
/// <param name="ErrorDescription">错误描述（成功为 null）。</param>
public sealed record PortalAuthResult(
    ClaimsPrincipal? Principal,
    IReadOnlyDictionary<string, string?> ResponseParameters,
    string? Error,
    string? ErrorDescription)
{
    private static readonly IReadOnlyDictionary<string, string?> EmptyParameters =
        new Dictionary<string, string?>(0);

    /// <summary>是否认证成功。</summary>
    public bool Succeeded => Principal is not null && Error is null;

    /// <summary>构造成功结果。</summary>
    /// <param name="principal">认证主体。</param>
    /// <param name="responseParameters">响应扩展参数。</param>
    public static PortalAuthResult Success(
        ClaimsPrincipal principal, IReadOnlyDictionary<string, string?>? responseParameters = null)
        => new(principal, responseParameters ?? EmptyParameters, null, null);

    /// <summary>构造失败结果。</summary>
    /// <param name="error">OpenIddict 错误码。</param>
    /// <param name="description">错误描述。</param>
    public static PortalAuthResult Failure(string error, string description)
        => new(null, EmptyParameters, error, description);
}

/// <summary>
/// 门户认证业务契约（架构规格 6.3 三阶段登录）。API 层仅负责提取 HTTP 请求参数与响应封装，
/// 客户端校验、账号锁定、密码校验、成员关系与 Token 版本校验、Claims 构造均由本服务承载。
/// </summary>
public interface IPortalAuthenticationService
{
    /// <summary>Phase 1 身份认证：账号 + 密码换取身份级 Token。</summary>
    /// <param name="clientId">OpenIddict 客户端标识。</param>
    /// <param name="loginAccount">登录账号（兼容 username 参数）。</param>
    /// <param name="password">明文密码（仅在内存中校验，不落库）。</param>
    /// <param name="clientIpAddress">来源 IP（登录审计）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task<PortalAuthResult> AuthenticateIdentityAsync(
        string? clientId,
        string? loginAccount,
        string? password,
        string clientIpAddress,
        CancellationToken cancellationToken);

    /// <summary>身份级 Token 刷新（refresh_token grant，含 token_version 实时校验）。</summary>
    /// <param name="principal">OpenIddict 已验证的 refresh token 主体。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task<PortalAuthResult> RefreshIdentityAsync(
        ClaimsPrincipal principal, CancellationToken cancellationToken);

    /// <summary>Phase 3 租户上下文：身份级 Token + 混淆租户 Id 换取租户级 Token。</summary>
    /// <param name="clientId">OpenIddict 客户端标识。</param>
    /// <param name="tenantIdRaw">混淆后的租户 Id。</param>
    /// <param name="memberRoleIdRaw">所选租户角色 Id（混淆串，可空；普通成员多角色时必填）。</param>
    /// <param name="identityPrincipal">已验证的身份级 Token 主体。</param>
    /// <param name="clientIpAddress">来源 IP（登录审计）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task<PortalAuthResult> EnterTenantContextAsync(
        string? clientId,
        string? tenantIdRaw,
        string? memberRoleIdRaw,
        ClaimsPrincipal identityPrincipal,
        string clientIpAddress,
        CancellationToken cancellationToken);

    /// <summary>租户级 Token 静默续期（凭宽限期内旧租户 Token 换新租户 Token）。</summary>
    /// <param name="clientId">OpenIddict 客户端标识。</param>
    /// <param name="bearerToken">Authorization: Bearer 头中的租户 access token。</param>
    /// <param name="clientIpAddress">来源 IP。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task<PortalAuthResult> RenewTenantTokenAsync(
        string? clientId,
        string bearerToken,
        string clientIpAddress,
        CancellationToken cancellationToken);
}

/// <summary>
/// 门户认证业务实现。承载架构规格 6.3 / 6.6 的全部认证规则：
/// 账号锁定阈值参数化、密码校验与哈希升级、租户成员与租户状态校验、Token 版本失效校验、Claims 组装。
/// </summary>
public sealed partial class PortalAuthenticationService(
    IOpenIddictApplicationManager applicationManager,
    IPasswordHasher<PortalUser> passwordHasher,
    IIdObfuscator obfuscator,
    ICacheService cache,
    IdentityMappers mappers,
    IOptions<QlxAuthOptions> authOptions,
    IOptions<OpenIddictServerOptions> serverOptions,
    ISystemParameterProvider systemParameters,
    IPortalLoginAuditLogger loginAudit,
    PortalDbContext db,
    IServiceProvider services,
    ITenantContext tenantContext,
    ILogger<PortalAuthenticationService> logger) : IPortalAuthenticationService
{
    /// <summary>LoggerMessage 源生成器要求字段（主构造参数经字段初始化器接入）。</summary>
    private readonly ILogger<PortalAuthenticationService> _logger = logger;

    /// <summary>
    /// 请求租户上下文：Bas 查询契约按当前 TenantId 解析目标租户库，
    /// 登录端点无租户中间件填充，需在查询 Bas 前临时写入本次目标租户并在结束后恢复。
    /// </summary>
    private readonly ITenantContext _tenantContext = tenantContext;

    /// <summary>
    /// 延迟解析提供者：Bas 模块的 <see cref="ITenantRolePermissionQuery"/> 与 Portal 模块自身的
    /// <see cref="IPortalRolePermissionQuery"/> 可能在不同注册阶段可用，构造时不强制存在。
    /// </summary>
    private readonly IServiceProvider _services = services;

    /// <inheritdoc />
    public async Task<PortalAuthResult> AuthenticateIdentityAsync(
        string? clientId,
        string? loginAccount,
        string? password,
        string clientIpAddress,
        CancellationToken cancellationToken)
    {
        if (!await ValidateClientAsync(clientId, cancellationToken).ConfigureAwait(false))
        {
            return PortalAuthResult.Failure(OpenIddictConstants.Errors.InvalidClient, "client_id 无效或缺少端点许可");
        }

        if (string.IsNullOrWhiteSpace(loginAccount) || string.IsNullOrWhiteSpace(password))
        {
            return PortalAuthResult.Failure(OpenIddictConstants.Errors.InvalidRequest, "login_account 与 password 必填");
        }

        var auth = authOptions.Value;

        // 登录安全阈值：数据库参数表优先（系统管理在线维护），config 值仅作参数表未初始化时的兜底
        var maxPasswordFailAttempts = await systemParameters
            .GetIntAsync(SystemParameterKeys.AuthMaxPasswordFailAttempts, auth.MaxPasswordFailAttempts, cancellationToken)
            .ConfigureAwait(false);
        var passwordFailWindowMinutes = await systemParameters
            .GetIntAsync(SystemParameterKeys.AuthPasswordFailWindowMinutes, auth.PasswordFailWindowMinutes, cancellationToken)
            .ConfigureAwait(false);

        // 账号锁定检查（参数化阈值，窗口内累计错误达标锁定，窗口结束自动解锁）
        var lockoutKey = QlxAuthConstants.AuthCacheKeys.LockoutPrefix + loginAccount;
        if (await cache.GetAsync<string>(lockoutKey, cancellationToken).ConfigureAwait(false) is not null)
        {
            await loginAudit.RecordFailureAsync(loginAccount, clientIpAddress, cancellationToken).ConfigureAwait(false);
            return PortalAuthResult.Failure(OpenIddictConstants.Errors.InvalidGrant, "账号已锁定，请稍后再试");
        }

        // 查找用户并校验密码（不区分"用户不存在/密码错误"，防用户枚举）
        var user = await db.PortalUsers
            .AsNoTracking()
            .SingleOrDefaultAsync(u => u.LoginAccount == loginAccount, cancellationToken)
            .ConfigureAwait(false);

        if (user is null || user.PasswordHash is null)
        {
            return await RecordPasswordFailureAndRejectAsync(
                loginAccount, maxPasswordFailAttempts, passwordFailWindowMinutes, clientIpAddress, cancellationToken)
                .ConfigureAwait(false);
        }

        var verify = passwordHasher.VerifyHashedPassword(user, user.PasswordHash, password);
        if (verify == PasswordVerificationResult.Failed)
        {
            return await RecordPasswordFailureAndRejectAsync(
                loginAccount, maxPasswordFailAttempts, passwordFailWindowMinutes, clientIpAddress, cancellationToken)
                .ConfigureAwait(false);
        }

        if (user.Status != UserStatus.Enabled)
        {
            await loginAudit.RecordFailureAsync(loginAccount, clientIpAddress, cancellationToken).ConfigureAwait(false);
            return PortalAuthResult.Failure(OpenIddictConstants.Errors.InvalidGrant, "账号已被停用，请联系管理员");
        }

        await cache.RemoveAsync(QlxAuthConstants.AuthCacheKeys.PasswordFailPrefix + loginAccount, cancellationToken)
            .ConfigureAwait(false);

        // 记录登录时间；哈希算法升级时重签（RehashNeeded）
        var newHash = verify == PasswordVerificationResult.SuccessRehashNeeded
            ? passwordHasher.HashPassword(user, password)
            : null;

        await db.PortalUsers
            .Where(u => u.Id == user.Id)
            .ExecuteUpdateAsync(s => s
                .SetProperty(u => u.LastLoginAt, DateTimeOffset.UtcNow)
                .SetProperty(u => u.PasswordHash, u => newHash ?? u.PasswordHash),
                cancellationToken)
            .ConfigureAwait(false);

        // 聚合门户侧权限码（PortalAdmin 零计算旁路，仅写 PortalAdminFlag；普通成员聚合门户角色权限）
        var portalPermissions = user.IsPortalAdmin
            ? null
            : await ResolvePortalPermissionsAsync(user.Id, cancellationToken).ConfigureAwait(false);

        var principal = CreateIdentityPrincipal(user, portalPermissions);
        var responseParameters = new Dictionary<string, string?>();

        var tenantsJson = await GetTenantsJsonAsync(user.Id, cancellationToken).ConfigureAwait(false);
        if (tenantsJson is not null)
        {
            // qlx_tenants 作为字符串参数出现在 Token 响应中（前端 JSON.parse 还原数组）
            responseParameters[PortalConstants.Parameters.ResponseTenants] = tenantsJson;
        }

        if (user.IsPortalAdmin)
        {
            // 门户管理员标记：进入租户选择页时前端置顶展示「门户管理」入口
            responseParameters[PortalConstants.Parameters.ResponsePortalAdmin] = PortalConstants.Frontend.BooleanTrue;
        }

        // 登录审计：写入平台日志中心（门户库不建登录日志表）
        await loginAudit.RecordSuccessAsync(
            user.LoginAccount, PortalConstants.Labels.PortalScopeName, 0, clientIpAddress, cancellationToken)
            .ConfigureAwait(false);

        return PortalAuthResult.Success(principal, responseParameters);
    }

    /// <inheritdoc />
    public async Task<PortalAuthResult> RefreshIdentityAsync(
        ClaimsPrincipal principal, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(principal);

        if (principal.FindFirst(QlxAuthConstants.Claims.TokenKind)?.Value == QlxAuthConstants.TokenKinds.Tenant)
        {
            return PortalAuthResult.Failure(
                OpenIddictConstants.Errors.InvalidGrant, "租户上下文请通过 tenant_context 授权类型刷新");
        }

        var subject = principal.FindFirst(OpenIddictConstants.Claims.Subject)?.Value;
        if (subject is null || !obfuscator.TryDecode(subject, out var userId))
        {
            return PortalAuthResult.Failure(OpenIddictConstants.Errors.InvalidGrant, "刷新令牌声明无效");
        }

        // token_version / 用户状态实时校验（规格 6.6.3 Layer 3：密码修改/停用后刷新即失效）
        var user = await db.PortalUsers
            .AsNoTracking()
            .SingleOrDefaultAsync(u => u.Id == userId, cancellationToken)
            .ConfigureAwait(false);

        if (user is null
            || user.Status != UserStatus.Enabled
            || principal.FindFirst(QlxAuthConstants.Claims.UserTokenVersion)?.Value
                != user.TokenVersion.ToString(CultureInfo.InvariantCulture))
        {
            return PortalAuthResult.Failure(OpenIddictConstants.Errors.InvalidGrant, "登录状态已失效，请重新登录");
        }

        // 以最新用户状态重签（OpenIddict 自动完成 refresh token 轮换与重放检测）；
        // 租户列表与门户管理员标记随刷新重发，保持与 Phase 1 响应一致
        var portalPermissions = user.IsPortalAdmin
            ? null
            : await ResolvePortalPermissionsAsync(user.Id, cancellationToken).ConfigureAwait(false);

        var renewed = CreateIdentityPrincipal(user, portalPermissions);
        var responseParameters = new Dictionary<string, string?>();

        var tenantsJson = await GetTenantsJsonAsync(user.Id, cancellationToken).ConfigureAwait(false);
        if (tenantsJson is not null)
        {
            responseParameters[PortalConstants.Parameters.ResponseTenants] = tenantsJson;
        }

        if (user.IsPortalAdmin)
        {
            responseParameters[PortalConstants.Parameters.ResponsePortalAdmin] = PortalConstants.Frontend.BooleanTrue;
        }

        return PortalAuthResult.Success(renewed, responseParameters);
    }

    /// <inheritdoc />
    public async Task<PortalAuthResult> EnterTenantContextAsync(
        string? clientId,
        string? tenantIdRaw,
        string? memberRoleIdRaw,
        ClaimsPrincipal identityPrincipal,
        string clientIpAddress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(identityPrincipal);

        if (!await ValidateClientAsync(clientId, cancellationToken).ConfigureAwait(false))
        {
            return PortalAuthResult.Failure(OpenIddictConstants.Errors.InvalidClient, "client_id 无效或缺少端点许可");
        }

        if (string.IsNullOrWhiteSpace(tenantIdRaw))
        {
            return PortalAuthResult.Failure(OpenIddictConstants.Errors.InvalidRequest, "tenant_id 必填");
        }

        if (identityPrincipal.FindFirst(QlxAuthConstants.Claims.TokenKind)?.Value != QlxAuthConstants.TokenKinds.Identity)
        {
            return PortalAuthResult.Failure(
                OpenIddictConstants.Errors.InvalidGrant, "必须使用身份级令牌进入租户上下文");
        }

        var subject = identityPrincipal.FindFirst(OpenIddictConstants.Claims.Subject)?.Value;
        if (subject is null || !obfuscator.TryDecode(subject, out var userId))
        {
            return PortalAuthResult.Failure(OpenIddictConstants.Errors.InvalidGrant, "身份令牌声明无效");
        }

        // 解码租户 Id，校验成员关系与租户状态（数据库为准，规格 6.3.3）
        if (!obfuscator.TryDecode(tenantIdRaw, out var tenantId) || tenantId <= 0)
        {
            return PortalAuthResult.Failure(OpenIddictConstants.Errors.InvalidRequest, "tenant_id 无效");
        }

        var member = await db.TenantMembers
            .Include(m => m.Tenant)
            .SingleOrDefaultAsync(m => m.PortalUserId == userId && m.TenantId == tenantId, cancellationToken)
            .ConfigureAwait(false);

        if (member is null
            || member.Status != MemberStatus.Enabled
            || member.Role == TenantMemberRole.PendingInvite
            || member.Tenant.Status is not (TenantStatus.Active or TenantStatus.GracePeriod))
        {
            return PortalAuthResult.Failure(OpenIddictConstants.Errors.AccessDenied, "无权访问该租户或租户不可用");
        }

        // 门户级用户状态校验（停用用户即使成员关系完好也拒绝）
        var user = await db.PortalUsers
            .SingleOrDefaultAsync(u => u.Id == userId, cancellationToken)
            .ConfigureAwait(false);

        if (user is null || user.Status != UserStatus.Enabled)
        {
            return PortalAuthResult.Failure(OpenIddictConstants.Errors.InvalidGrant, "账号已被停用，请联系管理员");
        }

        // 解析所选角色（普通成员多角色时必须显式指定；单角色自动锁定；管理员/无角色跳过）
        long? requestedRoleId = null;
        if (!string.IsNullOrWhiteSpace(memberRoleIdRaw))
        {
            if (!obfuscator.TryDecode(memberRoleIdRaw, out var decodedRoleId) || decodedRoleId <= 0)
            {
                return PortalAuthResult.Failure(OpenIddictConstants.Errors.InvalidRequest, "member_role_id 无效");
            }

            requestedRoleId = decodedRoleId;
        }

        var (resolvedRoleId, roleError, roleErrorDescription) =
            await ResolveRequestedRoleAsync(member, requestedRoleId, cancellationToken).ConfigureAwait(false);

        if (roleError is not null)
        {
            return PortalAuthResult.Failure(roleError, roleErrorDescription!);
        }

        // 记录进入租户时间
        member.RegisterLogin();
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        // 登录审计：进入租户上下文成功（日志中心按租户维度留痕）
        await loginAudit.RecordSuccessAsync(
            user.LoginAccount, member.Tenant.DisplayName, tenantId, clientIpAddress, cancellationToken)
            .ConfigureAwait(false);

        // 组装租户上下文 principal（claims 按规格 6.2.1，模块清单来自租户许可）
        var licensedModules = await GetLicensedModulesAsync(tenantId, cancellationToken).ConfigureAwait(false);

        // 聚合租户内权限码与数据范围（TenantAdmin 零计算旁路；普通成员按所选角色获取）
        var (tenantPermissions, dataScope, dataScopeDeptId) =
            await ResolveTenantPermissionsAsync(member, resolvedRoleId, cancellationToken).ConfigureAwait(false);

        LogTenantContextIssued(member.Id, tenantId, resolvedRoleId, tenantPermissions.Count);

        var tenantPrincipal = CreateTenantPrincipal(
            user, subject, tenantId, member.Tenant.TenantCode,
            member.DisplayNameInTenant ?? user.DisplayName,
            member.Role, member.Id, member.TokenVersion, licensedModules,
            tenantPermissions, dataScope, dataScopeDeptId, resolvedRoleId);

        return PortalAuthResult.Success(tenantPrincipal);
    }

    /// <inheritdoc />
    public async Task<PortalAuthResult> RenewTenantTokenAsync(
        string? clientId,
        string bearerToken,
        string clientIpAddress,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bearerToken);

        if (!await ValidateClientAsync(clientId, cancellationToken).ConfigureAwait(false))
        {
            return PortalAuthResult.Failure(OpenIddictConstants.Errors.InvalidClient, "client_id 无效或缺少端点许可");
        }

        // 手动校验 JWT（签名 + iss + aud + 有效期，含宽限期）
        // OpenIddict access token 默认 JWE 加密（cty:JWT 内层 JWS），需同时提供解密密钥与签名公钥
        // 注意：OpenIddict 内部对 Issuer 做了 Uri 归一化（自动补尾斜杠），校验时必须使用归一化后的值
        var normalizedIssuer = new Uri(authOptions.Value.Issuer).AbsoluteUri;
        var validationParameters = new TokenValidationParameters
        {
            ValidIssuer = normalizedIssuer,
            ValidAudience = authOptions.Value.Audience,
            IssuerSigningKeys = GetServerSigningKeys(),
            TokenDecryptionKeys = GetServerEncryptionKeys(),
            ClockSkew = TimeSpan.FromMinutes(authOptions.Value.TenantTokenRenewalGraceMinutes),
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateIssuerSigningKey = true,
            ValidateLifetime = true,
            RequireSignedTokens = true,
        };

        ClaimsPrincipal principal;
        try
        {
            // JsonWebTokenHandler 支持 JWE 解密 + JWS 验签（JwtSecurityTokenHandler 7.x 不支持 JWE）
            var handler = new JsonWebTokenHandler
            {
                MapInboundClaims = false,
            };
            var result = await handler.ValidateTokenAsync(bearerToken, validationParameters).ConfigureAwait(false);
            if (!result.IsValid)
            {
                throw result.Exception ?? new SecurityTokenValidationException("token validation failed");
            }

            principal = new ClaimsPrincipal(result.ClaimsIdentity);
        }
        catch (Exception ex)
        {
            // 签名失败 / 超宽限期过期：必须重新登录
            var issuerErr = ex as SecurityTokenInvalidIssuerException;
            LogTenantSilentValidationFailed(ex.GetType().Name, ex.Message, normalizedIssuer, issuerErr?.InvalidIssuer);
            return PortalAuthResult.Failure(
                OpenIddictConstants.Errors.InvalidGrant, "租户令牌无效或已超出续期宽限期，请重新登录");
        }

        // token_kind 必须为 tenant
        if (principal.FindFirst(QlxAuthConstants.Claims.TokenKind)?.Value != QlxAuthConstants.TokenKinds.Tenant)
        {
            return PortalAuthResult.Failure(
                OpenIddictConstants.Errors.InvalidGrant, "必须使用租户级令牌进行静默续期");
        }

        // 解析关键 claims
        var subject = principal.FindFirst(OpenIddictConstants.Claims.Subject)?.Value;
        var tenantIdClaim = principal.FindFirst(QlxAuthConstants.Claims.TenantId)?.Value;
        var memberIdClaim = principal.FindFirst(QlxAuthConstants.Claims.MemberId)?.Value;
        var userTvClaim = principal.FindFirst(QlxAuthConstants.Claims.UserTokenVersion)?.Value;
        var memberTvClaim = principal.FindFirst(QlxAuthConstants.Claims.MemberTokenVersion)?.Value;
        var roleIdClaim = principal.FindFirst(QlxAuthConstants.Claims.MemberRoleId)?.Value;

        if (subject is null || !obfuscator.TryDecode(subject, out var userId)
            || tenantIdClaim is null || !obfuscator.TryDecode(tenantIdClaim, out var tenantId)
            || memberIdClaim is null || !obfuscator.TryDecode(memberIdClaim, out var memberId))
        {
            return PortalAuthResult.Failure(OpenIddictConstants.Errors.InvalidGrant, "租户令牌声明无效");
        }

        // 用户状态 + Token 版本校验（密码修改/停用后旧 Token 立即失效）
        var user = await db.PortalUsers
            .AsNoTracking()
            .SingleOrDefaultAsync(u => u.Id == userId, cancellationToken)
            .ConfigureAwait(false);

        if (user is null || user.Status != UserStatus.Enabled)
        {
            return PortalAuthResult.Failure(OpenIddictConstants.Errors.InvalidGrant, "账号已被停用，请联系管理员");
        }

        if (userTvClaim != user.TokenVersion.ToString(CultureInfo.InvariantCulture))
        {
            return PortalAuthResult.Failure(OpenIddictConstants.Errors.InvalidGrant, "登录状态已失效，请重新登录");
        }

        // 成员关系 + 租户状态 + 成员 Token 版本校验
        var member = await db.TenantMembers
            .Include(m => m.Tenant)
            .SingleOrDefaultAsync(m => m.Id == memberId && m.PortalUserId == userId && m.TenantId == tenantId,
                cancellationToken)
            .ConfigureAwait(false);

        if (member is null
            || member.Status != MemberStatus.Enabled
            || member.Role == TenantMemberRole.PendingInvite
            || member.Tenant.Status is not (TenantStatus.Active or TenantStatus.GracePeriod))
        {
            return PortalAuthResult.Failure(OpenIddictConstants.Errors.AccessDenied, "无权访问该租户或租户不可用");
        }

        if (memberTvClaim != member.TokenVersion.ToString(CultureInfo.InvariantCulture))
        {
            return PortalAuthResult.Failure(
                OpenIddictConstants.Errors.InvalidGrant, "租户成员状态已变更，请重新进入企业");
        }

        // 还原进入时选择的角色（续期不改变权限维度）
        long? requestedRoleId = null;
        if (!string.IsNullOrWhiteSpace(roleIdClaim))
        {
            if (!obfuscator.TryDecode(roleIdClaim, out var decodedRoleId) || decodedRoleId <= 0)
            {
                return PortalAuthResult.Failure(OpenIddictConstants.Errors.InvalidGrant, "租户令牌角色声明无效");
            }

            requestedRoleId = decodedRoleId;
        }

        var (resolvedRoleId, roleError, _) =
            await ResolveRequestedRoleAsync(member, requestedRoleId, cancellationToken).ConfigureAwait(false);

        if (roleError is not null)
        {
            return PortalAuthResult.Failure(
                OpenIddictConstants.Errors.InvalidGrant, "所选角色已失效，请重新进入企业");
        }

        // 重新签发租户级 Token（claims 与 Phase 3 完全一致，模块许可实时取最新）
        var licensedModules = await GetLicensedModulesAsync(tenantId, cancellationToken).ConfigureAwait(false);

        // 聚合租户内权限码与数据范围（同 EnterTenantContextAsync）
        var (tenantPermissions, dataScope, dataScopeDeptId) =
            await ResolveTenantPermissionsAsync(member, resolvedRoleId, cancellationToken).ConfigureAwait(false);

        LogTenantTokenRenewed(member.Id, tenantId, resolvedRoleId, tenantPermissions.Count);

        var renewedPrincipal = CreateTenantPrincipal(
            user, subject, tenantId, member.Tenant.TenantCode,
            member.DisplayNameInTenant ?? user.DisplayName,
            member.Role, member.Id, member.TokenVersion, licensedModules,
            tenantPermissions, dataScope, dataScopeDeptId, resolvedRoleId);

        return PortalAuthResult.Success(renewedPrincipal);
    }

    /// <summary>
    /// 查询用户可进入的租户摘要并序列化为 JSON（PendingInvite 与停用成员/租户不出现）。
    /// </summary>
    private async Task<string?> GetTenantsJsonAsync(long userId, CancellationToken cancellationToken)
    {
        var rows = await db.TenantMembers
            .AsNoTracking()
            .Where(m => m.PortalUserId == userId
                && m.Status == MemberStatus.Enabled
                && m.Role != TenantMemberRole.PendingInvite
                && (m.Tenant.Status == TenantStatus.Active || m.Tenant.Status == TenantStatus.GracePeriod))
            .OrderBy(m => m.Tenant.TenantCode)
            .Select(m => new TenantMemberRow(
                m.TenantId, m.Tenant.TenantCode, m.Tenant.DisplayName, m.Tenant.TaxNo, m.Role, m.LastLoginAt))
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        if (rows.Count == 0)
        {
            return null;
        }

        var items = rows.Select(mappers.ToTenantSummaryItem).ToList();
        return System.Text.Json.JsonSerializer.Serialize(items, Qlx.Abstractions.Serialization.QlxJsonDefaults.Default);
    }

    /// <summary>查询租户已启用的模块编码（租户级 Token 的 modules claim 来源）。</summary>
    private async Task<List<string>> GetLicensedModulesAsync(long tenantId, CancellationToken cancellationToken)
        => await db.TenantModules
            .AsNoTracking()
            .Where(t => t.TenantId == tenantId && t.Status == TenantModuleStatus.Enabled)
            .Select(t => t.ModuleCode)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    /// <summary>累计密码失败次数并返回拒绝结果（达到阈值锁定账号；阈值与窗口来自数据库参数表）。</summary>
    private async Task<PortalAuthResult> RecordPasswordFailureAndRejectAsync(
        string loginAccount,
        int maxPasswordFailAttempts,
        int passwordFailWindowMinutes,
        string clientIpAddress,
        CancellationToken cancellationToken)
    {
        var failKey = QlxAuthConstants.AuthCacheKeys.PasswordFailPrefix + loginAccount;

        // 登录失败审计（含账号不存在场景，符合「不区分用户是否存在」的防枚举策略）
        await loginAudit.RecordFailureAsync(loginAccount, clientIpAddress, cancellationToken).ConfigureAwait(false);

        var fails = await cache.GetAsync<int>(failKey, cancellationToken).ConfigureAwait(false);
        fails++;
        var window = TimeSpan.FromMinutes(passwordFailWindowMinutes);
        await cache.SetAsync(failKey, fails, cancellationToken,
                new CacheEntryOptions { AbsoluteExpirationRelativeToNow = window })
            .ConfigureAwait(false);

        if (fails >= maxPasswordFailAttempts)
        {
            await cache.SetAsync(QlxAuthConstants.AuthCacheKeys.LockoutPrefix + loginAccount,
                    PortalConstants.CacheKeys.LockoutMarker, cancellationToken,
                    new CacheEntryOptions { AbsoluteExpirationRelativeToNow = window })
                .ConfigureAwait(false);
            return PortalAuthResult.Failure(OpenIddictConstants.Errors.InvalidGrant, "密码错误次数过多，账号已临时锁定");
        }

        return PortalAuthResult.Failure(OpenIddictConstants.Errors.InvalidGrant, "账号或密码错误");
    }

    /// <summary>
    /// 从 OpenIddict Server 选项中提取签名密钥（用于静默续期端点手动校验租户 access token 签名）。
    /// </summary>
    private List<SecurityKey> GetServerSigningKeys()
    {
        var keys = new List<SecurityKey>();
        foreach (var credential in serverOptions.Value.SigningCredentials)
        {
            keys.Add(credential.Key);
        }

        return keys;
    }

    /// <summary>
    /// 从 OpenIddict Server 选项中提取加密密钥（用于静默续期端点解密 JWE 格式的租户 access token）。
    /// </summary>
    private List<SecurityKey> GetServerEncryptionKeys()
    {
        var keys = new List<SecurityKey>();
        foreach (var credential in serverOptions.Value.EncryptionCredentials)
        {
            keys.Add(credential.Key);
        }

        return keys;
    }

    /// <summary>校验客户端存在性与 Token 端点 / 各授权类型许可。</summary>
    private async Task<bool> ValidateClientAsync(string? clientId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(clientId))
        {
            return false;
        }

        var app = await applicationManager.FindByClientIdAsync(clientId, cancellationToken).ConfigureAwait(false);
        if (app is null)
        {
            return false;
        }

        return await applicationManager.HasPermissionAsync(
                app, OpenIddictConstants.Permissions.Endpoints.Token, cancellationToken).ConfigureAwait(false)
            && await applicationManager.HasPermissionAsync(
                app, OpenIddictConstants.Permissions.GrantTypes.RefreshToken, cancellationToken).ConfigureAwait(false)
            && await applicationManager.HasPermissionAsync(
                app, OpenIddictConstants.Permissions.Prefixes.GrantType + QlxAuthConstants.GrantTypes.PortalIdentity,
                cancellationToken).ConfigureAwait(false)
            && await applicationManager.HasPermissionAsync(
                app, OpenIddictConstants.Permissions.Prefixes.GrantType + QlxAuthConstants.GrantTypes.TenantContext,
                cancellationToken).ConfigureAwait(false)
            && await applicationManager.HasPermissionAsync(
                app, OpenIddictConstants.Permissions.Prefixes.GrantType + QlxAuthConstants.GrantTypes.TenantSilent,
                cancellationToken).ConfigureAwait(false);
    }

    // =============== 权限码聚合（延迟解析 try-Get 模式，允许实现尚未注册） ===============

    /// <summary>
    /// 解析本次进入所选角色。
    /// <list type="bullet">
    /// <item>TenantAdmin：零计算旁路，不解析角色。</item>
    /// <item>Bas 查询契约未注册 / 成员无角色：返回 null，权限回退全角色并集。</item>
    /// <item>指定角色：必须属于该成员，否则拒绝。</item>
    /// <item>未指定：单角色自动锁定；多角色拒绝（要求显式选择）。</item>
    /// </list>
    /// </summary>
    private async Task<(long? RoleId, string? Error, string? ErrorDescription)> ResolveRequestedRoleAsync(
        TenantMember member, long? requestedRoleId, CancellationToken ct)
    {
        if (member.Role.ToString() == QlxMemberRoleValues.TenantAdmin)
        {
            return (null, null, null);
        }

        // Bas 查询契约按当前 TenantId 解析目标租户库：解析服务前先写入目标租户，结束后恢复
        var originalTenantId = _tenantContext.TenantId;
        try
        {
            _tenantContext.SetTenantId(member.TenantId);

            var query = _services.GetService<ITenantMemberRoleQuery>();
            if (query is null)
            {
                // Bas 模块尚未注册实现：保持历史并集行为
                return (null, null, null);
            }

            var roles = await query.ListMemberRolesAsync(member.Id, member.TenantId, ct).ConfigureAwait(false);

            if (roles.Count == 0)
            {
                // 历史数据未分配 Bas 角色：保持历史并集行为
                return (null, null, null);
            }

            if (requestedRoleId.HasValue)
            {
                return roles.Any(role => role.RoleId == requestedRoleId.Value)
                    ? (requestedRoleId, null, null)
                    : (null, OpenIddictConstants.Errors.AccessDenied, "所选角色不属于当前企业成员");
            }

            return roles.Count == 1
                ? (roles[0].RoleId, null, null)
                : (null, OpenIddictConstants.Errors.InvalidRequest, "该企业下存在多个角色，请选择本次进入的角色");
        }
        finally
        {
            _tenantContext.SetTenantId(originalTenantId ?? 0);
        }
    }

    /// <summary>
    /// 聚合普通成员的租户内权限码 + 数据范围。
    /// 若成员角色为 TenantAdmin 则立即返回（零计算旁路）；
    /// 若指定了角色则仅取该角色的权限码，否则回退成员全角色并集；
    /// 若 ITenantRolePermissionQuery 未注册（Bas 模块尚未接入）则返回空列表。
    /// </summary>
    private async Task<(IReadOnlyList<string> Permissions, string? DataScope, long? DataScopeDeptId)>
        ResolveTenantPermissionsAsync(TenantMember member, long? memberRoleId, CancellationToken ct)
    {
        if (member.Role.ToString() == QlxMemberRoleValues.TenantAdmin)
        {
            // TenantAdmin 零计算旁路：不查权限码、不写 DataScope
            return ([], null, null);
        }

        // Bas 查询契约按当前 TenantId 解析目标租户库：解析服务前先写入目标租户，结束后恢复
        var originalTenantId = _tenantContext.TenantId;
        try
        {
            _tenantContext.SetTenantId(member.TenantId);

            var query = _services.GetService<ITenantRolePermissionQuery>();
            if (query is null)
            {
                // Bas 模块尚未注册实现：写入空列表，后续 Bas 接入后自动生效
                LogTenantRoleQueryNotRegistered(member.Id, member.TenantId);
                return ([], null, null);
            }

            var perms = memberRoleId.HasValue
                ? await query.ListRolePermissionCodesAsync(memberRoleId.Value, member.TenantId, ct).ConfigureAwait(false)
                : await query.ListMemberPermissionCodesAsync(member.Id, member.TenantId, ct).ConfigureAwait(false);

            // DataScope / DataScopeDeptId 来自关联的 Bas 租户内 Role.DefaultDataScope / DataScopeDeptId
            // 当前抽象契约仅返回权限码列表，Bas 后续阶段将扩展返回结构，此处暂保留为 null
            // 消费侧（CreateTenantPrincipal）已对 null 做防护
            return (perms, null, null);
        }
        finally
        {
            _tenantContext.SetTenantId(originalTenantId ?? 0);
        }
    }

    /// <summary>
    /// 聚合普通门户用户的门户侧角色权限码。
    /// PortalAdmin 调用方已提前跳过；此处只处理 IPortalRolePermissionQuery 可能未注册的兜底场景。
    /// </summary>
    private async Task<IReadOnlyList<string>?> ResolvePortalPermissionsAsync(long portalUserId, CancellationToken ct)
    {
        var query = _services.GetService<IPortalRolePermissionQuery>();
        if (query is null)
        {
            // Portal 侧查询服务尚未注册（自身还没实现）：写 null，CreateIdentityPrincipal 不写入 claim
            LogPortalRoleQueryNotRegistered(portalUserId);
            return null;
        }

        return await query.ListUserPermissionCodesAsync(portalUserId, ct).ConfigureAwait(false);
    }

    // =============== Principal 构造 ===============

    private ClaimsPrincipal CreateIdentityPrincipal(PortalUser user, IReadOnlyList<string>? permissions = null)
    {
        var identity = new ClaimsIdentity(
            TokenValidationParameters.DefaultAuthenticationType,
            OpenIddictConstants.Claims.Name,
            OpenIddictConstants.Claims.Role);

        identity.AddClaim(new Claim(OpenIddictConstants.Claims.Subject, obfuscator.Encode(user.Id)));
        identity.AddClaim(new Claim(OpenIddictConstants.Claims.Name, user.DisplayName));
        identity.AddClaim(new Claim(QlxAuthConstants.Claims.LoginAccount, user.LoginAccount));
        identity.AddClaim(new Claim(QlxAuthConstants.Claims.TokenKind, QlxAuthConstants.TokenKinds.Identity));
        identity.AddClaim(new Claim(QlxAuthConstants.Claims.UserTokenVersion,
            user.TokenVersion.ToString(CultureInfo.InvariantCulture)));

        if (user.IsPortalAdmin)
        {
            // 门户管理员标记：进入租户选择页时前端置顶展示「门户管理」入口
            // 同时 PortalAdminFlag 存在时权限拦截器零计算旁路，不枚举权限码
            identity.AddClaim(new Claim(QlxAuthConstants.Claims.PortalAdminFlag, "1"));
        }
        else if (permissions is { Count: > 0 })
        {
            // 普通门户用户：聚合其关联的门户角色权限码写入 permissions claim
            identity.AddClaim(new Claim(
                QlxClaimConstants.Permissions,
                string.Join(',', permissions),
                ClaimValueTypes.String));
        }

        var principal = new ClaimsPrincipal(identity);
        principal.SetScopes(OpenIddictConstants.Scopes.OfflineAccess); // 授权 offline_access → 签发 refresh token
        principal.SetAudiences(authOptions.Value.Audience);
        // OpenIddict 7：标记 access_token 目的地的 claims 同时进入 refresh token（无独立 refresh 目的地）
        principal.SetDestinations(static _ => [OpenIddictConstants.Destinations.AccessToken]);
        return principal;
    }

    private ClaimsPrincipal CreateTenantPrincipal(
        PortalUser user,
        string subject,
        long tenantId,
        string tenantCode,
        string displayName,
        TenantMemberRole role,
        long memberId,
        long memberTokenVersion,
        IReadOnlyCollection<string> licensedModules,
        IReadOnlyList<string>? permissions = null,
        string? dataScope = null,
        long? dataScopeDeptId = null,
        long? memberRoleId = null)
    {
        var identity = new ClaimsIdentity(
            TokenValidationParameters.DefaultAuthenticationType,
            OpenIddictConstants.Claims.Name,
            OpenIddictConstants.Claims.Role);

        identity.AddClaim(new Claim(OpenIddictConstants.Claims.Subject, subject));
        identity.AddClaim(new Claim(OpenIddictConstants.Claims.Name, displayName));
        identity.AddClaim(new Claim(QlxAuthConstants.Claims.LoginAccount, user.LoginAccount));
        identity.AddClaim(new Claim(QlxAuthConstants.Claims.TokenKind, QlxAuthConstants.TokenKinds.Tenant));
        identity.AddClaim(new Claim(QlxAuthConstants.Claims.TenantId, obfuscator.Encode(tenantId)));
        identity.AddClaim(new Claim(QlxAuthConstants.Claims.TenantCode, tenantCode));
        identity.AddClaim(new Claim(QlxAuthConstants.Claims.MemberId, obfuscator.Encode(memberId)));
        identity.AddClaim(new Claim(QlxAuthConstants.Claims.MemberRole, role.ToString()));
        identity.AddClaim(new Claim(QlxAuthConstants.Claims.UserTokenVersion,
            user.TokenVersion.ToString(CultureInfo.InvariantCulture)));
        identity.AddClaim(new Claim(QlxAuthConstants.Claims.MemberTokenVersion,
            memberTokenVersion.ToString(CultureInfo.InvariantCulture)));
        identity.AddClaim(new Claim(QlxAuthConstants.Claims.TokenVersion,
            (user.TokenVersion + memberTokenVersion).ToString(CultureInfo.InvariantCulture)));

        foreach (var module in licensedModules)
        {
            identity.AddClaim(new Claim(QlxAuthConstants.Claims.Modules, module));
        }

        // 租户内权限码 + 数据范围：仅普通成员（TenantAdmin 走零计算旁路，不写入）
        var roleCode = role.ToString();
        if (roleCode != QlxMemberRoleValues.TenantAdmin && memberRoleId.HasValue)
        {
            identity.AddClaim(new Claim(
                QlxAuthConstants.Claims.MemberRoleId,
                obfuscator.Encode(memberRoleId.Value),
                ClaimValueTypes.String));
        }

        if (roleCode != QlxMemberRoleValues.TenantAdmin && permissions is { Count: > 0 })
        {
            identity.AddClaim(new Claim(
                QlxClaimConstants.Permissions,
                string.Join(',', permissions),
                ClaimValueTypes.String));

            if (!string.IsNullOrEmpty(dataScope))
            {
                identity.AddClaim(new Claim(QlxClaimConstants.DataScope, dataScope, ClaimValueTypes.String));
            }

            if (dataScopeDeptId.HasValue)
            {
                identity.AddClaim(new Claim(
                    QlxClaimConstants.DataScopeDeptId,
                    dataScopeDeptId.Value.ToString(CultureInfo.InvariantCulture),
                    ClaimValueTypes.String));
            }
        }

        var principal = new ClaimsPrincipal(identity);
        principal.SetAudiences(authOptions.Value.Audience);
        // 租户上下文只签 access token（claims 仅落 AccessToken 目的地，不产生 refresh token）
        principal.SetDestinations(static _ => [OpenIddictConstants.Destinations.AccessToken]);
        return principal;
    }

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "租户 Token 静默续期校验失败：{ExceptionType}: {ErrorMessage}；expectedIssuer={ExpectedIssuer}；actualIssuer={ActualIssuer}")]
    private partial void LogTenantSilentValidationFailed(
        string exceptionType, string errorMessage, string expectedIssuer, string? actualIssuer);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "未注册 ITenantRolePermissionQuery 实现，跳过租户内权限码聚合。memberId={MemberId}, tenantId={TenantId}")]
    private partial void LogTenantRoleQueryNotRegistered(long memberId, long tenantId);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "租户上下文签发：memberId={MemberId}, tenantId={TenantId}, roleId={RoleId}, permissionCount={PermissionCount}")]
    private partial void LogTenantContextIssued(long memberId, long tenantId, long? roleId, int permissionCount);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "租户 Token 静默续期：memberId={MemberId}, tenantId={TenantId}, roleId={RoleId}, permissionCount={PermissionCount}")]
    private partial void LogTenantTokenRenewed(long memberId, long tenantId, long? roleId, int permissionCount);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "未注册 IPortalRolePermissionQuery 实现，跳过门户侧权限码聚合。portalUserId={PortalUserId}")]
    private partial void LogPortalRoleQueryNotRegistered(long portalUserId);
}
