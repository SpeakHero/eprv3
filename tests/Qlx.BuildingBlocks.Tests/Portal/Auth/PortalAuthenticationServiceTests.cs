/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-25
 * 描述：PortalAuthenticationService 白盒测试——三阶段登录 + 刷新 + 静默续期全分支覆盖：
 *       客户端校验、凭证边界（128 长度上限）、IP 封禁（20/窗口）、账号锁定（参数化阈值）、
 *       密码校验、停用账号、无租户消息三分支、门户管理员旁路、角色解析（单角色/多角色/越权/未注册回退）、
 *       Token 版本失效、成员/租户状态机、租户上下文写入与恢复、JWE 静默续期成功/失败路径。
 */

using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using OpenIddict.Abstractions;
using OpenIddict.Server;
using Qlx.Abstractions.Constants;
using Qlx.Abstractions.Queries;
using Qlx.Modules.Portal;
using Qlx.Modules.Portal.Auth;
using Qlx.Modules.Portal.Domain.Identity;
using Qlx.Modules.Portal.Domain.Tenancy;
using Qlx.Modules.Portal.Services;

namespace Qlx.BuildingBlocks.Tests.Portal.Auth;

public sealed class PortalAuthenticationServiceTests : IDisposable
{
    private const string Ip = "10.1.2.3";
    private const string ClientId = "portal-web";
    private const string CorrectPassword = "Correct@123";
    private const string WrongPassword = "Wrong@999";

    private static readonly SymmetricSecurityKey ServerKey =
        new(Convert.FromHexString("00112233445566778899AABBCCDDEEFF00112233445566778899AABBCCDDEEFF"));

    // Foreign Keys=False：部分用例需构造「成员存在但门户用户缺失」等孤儿数据以验证防御分支
    private readonly Microsoft.Data.Sqlite.SqliteConnection _connection = new("Data Source=:memory:;Foreign Keys=False");
    private readonly StubCacheService _cache = new();
    private readonly StubSystemParameterProvider _params = new();
    private readonly StubPortalLoginAuditLogger _audit = new();
    private readonly StubTenantContext _tenantContext = new();
    private readonly StubServiceProvider _services = new();
    private readonly StubPendingInviteActivator _activator = new();
    private readonly StubIdObfuscator _obfuscator = new();
    private readonly QlxAuthOptions _authOptions = new();
    private readonly OpenIddictServerOptions _serverOptions = new();
    private readonly PasswordHasher<PortalUser> _hasher = new();

    public PortalAuthenticationServiceTests()
    {
        _serverOptions.SigningCredentials.Add(
            new SigningCredentials(ServerKey, SecurityAlgorithms.HmacSha256));
        _serverOptions.EncryptionCredentials.Add(
            new EncryptingCredentials(ServerKey, SecurityAlgorithms.Aes256KW, SecurityAlgorithms.Aes256CbcHmacSha512));
        _connection.Open();
    }

    public void Dispose() => _connection.Dispose();

    // ============ 基础设施 ============

    // 认证成功路径使用 ExecuteUpdateAsync（InMemory 提供方不支持），故本套件使用 SQLite 内存库
    private PortalDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<PortalDbContext>()
            .UseSqlite(_connection)
            .Options;
        var db = new PortalDbContext(options);
        db.Database.EnsureCreated();
        return db;
    }

    private PortalAuthenticationService CreateService(
        IOpenIddictApplicationManager? apps = null, IPasswordHasher<PortalUser>? hasher = null)
        => new(
            apps ?? StubOpenIddictApplicationManagerProxy.Create(new object(), OpenIddictClientPermissions.All),
            hasher ?? _hasher,
            _obfuscator,
            _cache,
            new IdentityMappers(_obfuscator),
            Options.Create(_authOptions),
            Options.Create(_serverOptions),
            _params,
            _audit,
            CreateDb(),
            _services,
            _tenantContext,
            _activator,
            NullLogger<PortalAuthenticationService>.Instance);

    private async Task<PortalUser> SeedUserAsync(
        string account = "13800000001", bool disabled = false, bool isPortalAdmin = false, bool withPassword = true)
    {
        await using var db = CreateDb();
        var user = new PortalUser(account, "演示用户", isPortalAdmin);
        if (withPassword)
        {
            user.SetPasswordHash(_hasher.HashPassword(user, CorrectPassword));
        }

        if (disabled)
        {
            user.Disable();
        }

        db.PortalUsers.Add(user);
        _ = await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return user;
    }

    private async Task<Tenant> SeedTenantAsync(string code = "t001", Action<Tenant>? configure = null)
    {
        await using var db = CreateDb();
        var tenant = new Tenant(code, $"租户-{code}");
        tenant.Activate();
        configure?.Invoke(tenant);
        db.Tenants.Add(tenant);
        _ = await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return tenant;
    }

    private async Task<TenantMember> SeedMemberAsync(
        long tenantId, long userId,
        TenantMemberRole role = TenantMemberRole.OrdinaryMember, bool disabled = false)
    {
        await using var db = CreateDb();
        var member = new TenantMember(tenantId, userId, role);
        if (disabled)
        {
            member.Disable();
        }

        db.TenantMembers.Add(member);
        _ = await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return member;
    }

    private static ClaimsPrincipal IdentityPrincipal(string? subject, string tokenKind, string? userTv = null)
    {
        var claims = new List<Claim> { new(QlxAuthConstants.Claims.TokenKind, tokenKind) };
        if (subject is not null)
        {
            claims.Add(new Claim(OpenIddictConstants.Claims.Subject, subject));
        }

        if (userTv is not null)
        {
            claims.Add(new Claim(QlxAuthConstants.Claims.UserTokenVersion, userTv));
        }

        return new ClaimsPrincipal(new ClaimsIdentity(claims));
    }

    private string CreateTenantJwt(
        IEnumerable<KeyValuePair<string, object>> claims, DateTimeOffset? expires = null, string? issuer = null)
    {
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = issuer ?? new Uri(_authOptions.Issuer).AbsoluteUri,
            Audience = _authOptions.Audience,
            Expires = (expires ?? DateTimeOffset.UtcNow.AddMinutes(5)).UtcDateTime,
            SigningCredentials = _serverOptions.SigningCredentials[0],
            EncryptingCredentials = _serverOptions.EncryptionCredentials[0],
            Claims = claims.ToDictionary(kv => kv.Key, kv => kv.Value),
        };
        return new JsonWebTokenHandler().CreateToken(descriptor);
    }

    private static string? Claim(ClaimsPrincipal? principal, string type)
        => principal?.FindFirst(type)?.Value;

    // ============ Phase 1：客户端校验 ============

    [Fact]
    public async Task AuthenticateNullClientIdRejected()
    {
        var result = await CreateService().AuthenticateIdentityAsync(
            null, "acc", "pwd", Ip, TestContext.Current.CancellationToken);

        Assert.Equal(OpenIddictConstants.Errors.InvalidClient, result.Error);
        Assert.Empty(_audit.Failures); // 客户端校验失败不记登录失败审计
    }

    [Fact]
    public async Task AuthenticateUnknownClientRejected()
    {
        var apps = StubOpenIddictApplicationManagerProxy.Create(null, OpenIddictClientPermissions.All);

        var result = await CreateService(apps).AuthenticateIdentityAsync(
            ClientId, "acc", "pwd", Ip, TestContext.Current.CancellationToken);

        Assert.Equal(OpenIddictConstants.Errors.InvalidClient, result.Error);
    }

    [Fact]
    public async Task AuthenticateClientMissingGrantPermissionRejected()
    {
        // 缺 TenantSilent 许可
        var granted = OpenIddictClientPermissions.All
            .Where(p => !p.EndsWith(QlxAuthConstants.GrantTypes.TenantSilent, StringComparison.Ordinal))
            .ToArray();
        var apps = StubOpenIddictApplicationManagerProxy.Create(new object(), granted);

        var result = await CreateService(apps).AuthenticateIdentityAsync(
            ClientId, "acc", "pwd", Ip, TestContext.Current.CancellationToken);

        Assert.Equal(OpenIddictConstants.Errors.InvalidClient, result.Error);
    }

    // ============ Phase 1：入参校验（含 128 长度边界）============

    [Theory]
    [InlineData(null, "pwd")]
    [InlineData("", "pwd")]
    [InlineData("   ", "pwd")]
    [InlineData("acc", null)]
    [InlineData("acc", "")]
    [InlineData("acc", "  ")]
    public async Task AuthenticateMissingCredentialsRejected(string? account, string? password)
    {
        var result = await CreateService().AuthenticateIdentityAsync(
            ClientId, account, password, Ip, TestContext.Current.CancellationToken);

        Assert.Equal(OpenIddictConstants.Errors.InvalidRequest, result.Error);
        Assert.Contains("必填", result.ErrorDescription);
    }

    [Theory]
    [InlineData(129, 10)]  // 账号超长
    [InlineData(10, 129)]  // 密码超长
    public async Task AuthenticateOversizedCredentialsRejected(int accountLength, int passwordLength)
    {
        var result = await CreateService().AuthenticateIdentityAsync(
            ClientId, new string('a', accountLength), new string('b', passwordLength),
            Ip, TestContext.Current.CancellationToken);

        Assert.Equal(OpenIddictConstants.Errors.InvalidRequest, result.Error);
        Assert.Contains("长度超出限制", result.ErrorDescription);
        Assert.Empty(_cache.Snapshot); // 长度短路发生在缓存键构造之前
    }

    [Fact]
    public async Task AuthenticateExactly128LengthPassesLengthCheck()
    {
        // 边界值 128：不因长度拒绝（进入凭据校验，因账号不存在返回统一失败文案）
        var result = await CreateService().AuthenticateIdentityAsync(
            ClientId, new string('a', 128), new string('b', 128), Ip, TestContext.Current.CancellationToken);

        Assert.Equal(OpenIddictConstants.Errors.InvalidGrant, result.Error);
        Assert.Equal("账号或密码错误，或账号已被锁定", result.ErrorDescription);
    }

    // ============ Phase 1：IP 封禁 / 账号锁定前置拦截 ============

    [Fact]
    public async Task AuthenticateBlockedIpRejectedBeforeAnyLookup()
    {
        var user = await SeedUserAsync();
        await _cache.SetAsync(
            AuthSecurityCacheKeys.IpBlock(Ip), "locked", TestContext.Current.CancellationToken);

        var result = await CreateService().AuthenticateIdentityAsync(
            ClientId, user.LoginAccount, CorrectPassword, Ip, TestContext.Current.CancellationToken);

        Assert.Equal(OpenIddictConstants.Errors.InvalidGrant, result.Error);
        Assert.Equal("账号或密码错误，或账号已被锁定", result.ErrorDescription);
        Assert.Single(_audit.Failures); // 封禁命中也记失败审计
    }

    [Fact]
    public async Task AuthenticateLockedAccountRejectedWithUnifiedMessage()
    {
        var user = await SeedUserAsync();
        await _cache.SetAsync(
            AuthSecurityCacheKeys.Lockout(user.LoginAccount), "locked", TestContext.Current.CancellationToken);

        var result = await CreateService().AuthenticateIdentityAsync(
            ClientId, user.LoginAccount, CorrectPassword, Ip, TestContext.Current.CancellationToken);

        Assert.Equal(OpenIddictConstants.Errors.InvalidGrant, result.Error);
        Assert.Equal("账号或密码错误，或账号已被锁定", result.ErrorDescription);
    }

    // ============ Phase 1：密码失败计数 / 锁定阈值 / IP 阈值 ============

    [Fact]
    public async Task AuthenticateUnknownUserCountsFailureWithUnifiedMessage()
    {
        var result = await CreateService().AuthenticateIdentityAsync(
            ClientId, "ghost-user", WrongPassword, Ip, TestContext.Current.CancellationToken);

        Assert.Equal(OpenIddictConstants.Errors.InvalidGrant, result.Error);
        Assert.Equal("账号或密码错误，或账号已被锁定", result.ErrorDescription);
        Assert.Equal(1, await _cache.GetAsync<int>(
            AuthSecurityCacheKeys.PasswordFail("ghost-user"), TestContext.Current.CancellationToken));
        Assert.Equal(1, await _cache.GetAsync<int>(
            AuthSecurityCacheKeys.IpFail(Ip), TestContext.Current.CancellationToken));
        Assert.Single(_audit.Failures);
    }

    [Fact]
    public async Task AuthenticateUserWithoutPasswordHashRejectedAsCredentialFailure()
    {
        var user = await SeedUserAsync(withPassword: false); // 外部 IdP 用户无本地密码

        var result = await CreateService().AuthenticateIdentityAsync(
            ClientId, user.LoginAccount, CorrectPassword, Ip, TestContext.Current.CancellationToken);

        Assert.Equal(OpenIddictConstants.Errors.InvalidGrant, result.Error);
        Assert.Equal("账号或密码错误，或账号已被锁定", result.ErrorDescription);
    }

    [Fact]
    public async Task AuthenticateLockoutAtConfiguredThreshold()
    {
        _params.Ints[SystemParameterKeys.AuthMaxPasswordFailAttempts] = 2;
        _params.Ints[SystemParameterKeys.AuthPasswordFailWindowMinutes] = 15;
        var user = await SeedUserAsync();
        var service = CreateService();

        // 第 1 次失败：未锁定
        var first = await service.AuthenticateIdentityAsync(
            ClientId, user.LoginAccount, WrongPassword, Ip, TestContext.Current.CancellationToken);
        Assert.Equal(OpenIddictConstants.Errors.InvalidGrant, first.Error);
        Assert.Null(await _cache.GetAsync<string>(
            AuthSecurityCacheKeys.Lockout(user.LoginAccount), TestContext.Current.CancellationToken));

        // 第 2 次失败：达到阈值 → 写入锁定键
        var second = await service.AuthenticateIdentityAsync(
            ClientId, user.LoginAccount, WrongPassword, Ip, TestContext.Current.CancellationToken);
        Assert.Equal(OpenIddictConstants.Errors.InvalidGrant, second.Error);
        Assert.NotNull(await _cache.GetAsync<string>(
            AuthSecurityCacheKeys.Lockout(user.LoginAccount), TestContext.Current.CancellationToken));

        // 第 3 次：即使密码正确也被锁定前置拦截
        var third = await service.AuthenticateIdentityAsync(
            ClientId, user.LoginAccount, CorrectPassword, Ip, TestContext.Current.CancellationToken);
        Assert.Equal("账号或密码错误，或账号已被锁定", third.ErrorDescription);
    }

    [Fact]
    public async Task AuthenticateIpBlockedAfterTwentyFailuresAcrossAccounts()
    {
        var service = CreateService();

        // 20 个不同账号各失败 1 次（规避单账号 5 次锁定短路 IP 计数的路径）
        for (var i = 1; i <= 20; i++)
        {
            var result = await service.AuthenticateIdentityAsync(
                ClientId, $"ghost-{i:D2}", WrongPassword, Ip, TestContext.Current.CancellationToken);
            Assert.Equal(OpenIddictConstants.Errors.InvalidGrant, result.Error);
        }

        Assert.NotNull(await _cache.GetAsync<string>(
            AuthSecurityCacheKeys.IpBlock(Ip), TestContext.Current.CancellationToken));

        // 新账号 + 正确密码也被 IP 封禁前置拦截
        var user = await SeedUserAsync();
        var blocked = await service.AuthenticateIdentityAsync(
            ClientId, user.LoginAccount, CorrectPassword, Ip, TestContext.Current.CancellationToken);
        Assert.Equal("账号或密码错误，或账号已被锁定", blocked.ErrorDescription);
    }

    // ============ Phase 1：停用账号与成功路径 ============

    [Fact]
    public async Task AuthenticateDisabledUserRejectedWithDistinctMessage()
    {
        var user = await SeedUserAsync(disabled: true);

        var result = await CreateService().AuthenticateIdentityAsync(
            ClientId, user.LoginAccount, CorrectPassword, Ip, TestContext.Current.CancellationToken);

        Assert.Equal(OpenIddictConstants.Errors.InvalidGrant, result.Error);
        Assert.Equal("账号已被停用，请联系管理员", result.ErrorDescription);
        Assert.Single(_audit.Failures);
    }

    [Fact]
    public async Task AuthenticateSuccessClearsFailCounterAndUpdatesLastLogin()
    {
        var user = await SeedUserAsync();
        var tenant = await SeedTenantAsync();
        _ = await SeedMemberAsync(tenant.Id, user.Id);
        var service = CreateService();

        // 先制造 1 次失败计数
        _ = await service.AuthenticateIdentityAsync(
            ClientId, user.LoginAccount, WrongPassword, Ip, TestContext.Current.CancellationToken);
        Assert.Equal(1, await _cache.GetAsync<int>(
            AuthSecurityCacheKeys.PasswordFail(user.LoginAccount), TestContext.Current.CancellationToken));

        var result = await service.AuthenticateIdentityAsync(
            ClientId, user.LoginAccount, CorrectPassword, Ip, TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded);
        // 失败计数被清除
        Assert.Null(await _cache.GetAsync<string>(
            AuthSecurityCacheKeys.PasswordFail(user.LoginAccount), TestContext.Current.CancellationToken));
        // 首登激活器被调用
        Assert.Equal(1, _activator.CallCount);
        // 身份级 principal：sub 混淆、token_kind=identity、tv_u
        Assert.Equal(_obfuscator.Encode(user.Id), Claim(result.Principal, OpenIddictConstants.Claims.Subject));
        Assert.Equal(QlxAuthConstants.TokenKinds.Identity, Claim(result.Principal, QlxAuthConstants.Claims.TokenKind));
        Assert.Equal("0", Claim(result.Principal, QlxAuthConstants.Claims.UserTokenVersion));
        // 租户列表参数下发
        Assert.True(result.ResponseParameters.ContainsKey(PortalConstants.Parameters.ResponseTenants));
        Assert.Contains("t001", result.ResponseParameters[PortalConstants.Parameters.ResponseTenants]);
        // 成功审计
        Assert.Single(_audit.Successes);
        Assert.Equal(0, _audit.Successes[0].TenantId);
        // LastLoginAt 落库
        await using var db = CreateDb();
        var reloaded = await db.PortalUsers.SingleAsync(
            u => u.Id == user.Id, TestContext.Current.CancellationToken);
        Assert.NotNull(reloaded.LastLoginAt);
    }

    [Fact]
    public async Task AuthenticatePortalAdminSucceedsWithoutTenantsAndBypassesPermissions()
    {
        var user = await SeedUserAsync(isPortalAdmin: true);
        _services.Register(new StubPortalRolePermissionQuery()); // 注册也不应被调用（旁路）

        var result = await CreateService().AuthenticateIdentityAsync(
            ClientId, user.LoginAccount, CorrectPassword, Ip, TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded);
        Assert.Equal("1", Claim(result.Principal, QlxAuthConstants.Claims.PortalAdminFlag));
        Assert.Null(Claim(result.Principal, QlxClaimConstants.Permissions));
        Assert.Equal(
            PortalConstants.Frontend.BooleanTrue,
            result.ResponseParameters[PortalConstants.Parameters.ResponsePortalAdmin]);
        Assert.False(result.ResponseParameters.ContainsKey(PortalConstants.Parameters.ResponseTenants));
    }

    [Fact]
    public async Task AuthenticateOrdinaryUserAggregatesPortalPermissionsWhenQueryRegistered()
    {
        var user = await SeedUserAsync();
        var tenant = await SeedTenantAsync();
        _ = await SeedMemberAsync(tenant.Id, user.Id);
        var query = new StubPortalRolePermissionQuery();
        query.Permissions.AddRange(["portal.role.read", "portal.user.read"]);
        _services.Register(query);

        var result = await CreateService().AuthenticateIdentityAsync(
            ClientId, user.LoginAccount, CorrectPassword, Ip, TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded);
        Assert.Equal(
            "portal.role.read,portal.user.read",
            Claim(result.Principal, QlxClaimConstants.Permissions));
        Assert.Null(Claim(result.Principal, QlxAuthConstants.Claims.PortalAdminFlag));
    }

    [Fact]
    public async Task AuthenticateOrdinaryUserWithoutPortalPermissionQueryWritesNoClaim()
    {
        var user = await SeedUserAsync();
        var tenant = await SeedTenantAsync();
        _ = await SeedMemberAsync(tenant.Id, user.Id);
        // 不注册 IPortalRolePermissionQuery → 回退 null，不写 permissions claim

        var result = await CreateService().AuthenticateIdentityAsync(
            ClientId, user.LoginAccount, CorrectPassword, Ip, TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded);
        Assert.Null(Claim(result.Principal, QlxClaimConstants.Permissions));
    }

    // ============ Phase 1：无可进入租户的消息三分支 ============

    [Fact]
    public async Task AuthenticateNoMembershipsRejectedWithLinkMessage()
    {
        var user = await SeedUserAsync();

        var result = await CreateService().AuthenticateIdentityAsync(
            ClientId, user.LoginAccount, CorrectPassword, Ip, TestContext.Current.CancellationToken);

        Assert.Equal(OpenIddictConstants.Errors.InvalidGrant, result.Error);
        Assert.Equal("该账号尚未关联任何企业，请联系集团管理员", result.ErrorDescription);
    }

    [Fact]
    public async Task AuthenticatePendingInviteOnlyRejectedWithActivationMessage()
    {
        var user = await SeedUserAsync();
        var tenant = await SeedTenantAsync();
        _ = await SeedMemberAsync(tenant.Id, user.Id, TenantMemberRole.PendingInvite);

        var result = await CreateService().AuthenticateIdentityAsync(
            ClientId, user.LoginAccount, CorrectPassword, Ip, TestContext.Current.CancellationToken);

        Assert.Equal("账号待激活：已收到企业邀请，请联系企业管理员完成邀请激活后再登录", result.ErrorDescription);
    }

    [Fact]
    public async Task AuthenticateDisabledMembershipOnlyRejectedWithDisabledMessage()
    {
        var user = await SeedUserAsync();
        var tenant = await SeedTenantAsync();
        _ = await SeedMemberAsync(tenant.Id, user.Id, disabled: true);

        var result = await CreateService().AuthenticateIdentityAsync(
            ClientId, user.LoginAccount, CorrectPassword, Ip, TestContext.Current.CancellationToken);

        Assert.Equal("账号所在企业成员身份已被停用，请联系企业管理员", result.ErrorDescription);
    }

    [Fact]
    public async Task AuthenticateMembershipOfDisabledTenantRejectedWithUnavailableMessage()
    {
        var user = await SeedUserAsync();
        var tenant = await SeedTenantAsync(configure: t => t.Disable());
        _ = await SeedMemberAsync(tenant.Id, user.Id);

        var result = await CreateService().AuthenticateIdentityAsync(
            ClientId, user.LoginAccount, CorrectPassword, Ip, TestContext.Current.CancellationToken);

        Assert.Equal("该账号关联的企业当前不可用（已停用或已过期），请联系集团管理员", result.ErrorDescription);
    }

    // ============ 身份 Token 刷新 ============

    [Fact]
    public async Task RefreshNullPrincipalThrows()
    {
        _ = await Assert.ThrowsAsync<ArgumentNullException>(
            () => CreateService().RefreshIdentityAsync(null!, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task RefreshTenantKindTokenRejected()
    {
        var principal = IdentityPrincipal(_obfuscator.Encode(1), QlxAuthConstants.TokenKinds.Tenant);

        var result = await CreateService().RefreshIdentityAsync(principal, TestContext.Current.CancellationToken);

        Assert.Equal(OpenIddictConstants.Errors.InvalidGrant, result.Error);
        Assert.Contains("tenant_context", result.ErrorDescription);
    }

    [Theory]
    [InlineData(null)]          // 缺 sub
    [InlineData("bad-subject")] // sub 不可解码
    public async Task RefreshInvalidSubjectRejected(string? subject)
    {
        var principal = IdentityPrincipal(subject, QlxAuthConstants.TokenKinds.Identity);

        var result = await CreateService().RefreshIdentityAsync(principal, TestContext.Current.CancellationToken);

        Assert.Equal("刷新令牌声明无效", result.ErrorDescription);
    }

    [Fact]
    public async Task RefreshUserNotFoundRejected()
    {
        var principal = IdentityPrincipal(_obfuscator.Encode(987654), QlxAuthConstants.TokenKinds.Identity, "0");

        var result = await CreateService().RefreshIdentityAsync(principal, TestContext.Current.CancellationToken);

        Assert.Equal("登录状态已失效，请重新登录", result.ErrorDescription);
    }

    [Fact]
    public async Task RefreshDisabledUserRejected()
    {
        var user = await SeedUserAsync(disabled: true); // Disable() 使 TokenVersion=1
        var principal = IdentityPrincipal(_obfuscator.Encode(user.Id), QlxAuthConstants.TokenKinds.Identity, "1");

        var result = await CreateService().RefreshIdentityAsync(principal, TestContext.Current.CancellationToken);

        Assert.Equal("登录状态已失效，请重新登录", result.ErrorDescription);
    }

    [Fact]
    public async Task RefreshTokenVersionMismatchRejected()
    {
        var user = await SeedUserAsync();
        await using (var db = CreateDb())
        {
            var tracked = await db.PortalUsers.SingleAsync(
                u => u.Id == user.Id, TestContext.Current.CancellationToken);
            tracked.BumpTokenVersion(); // 0 → 1，旧 Token(tv_u=0) 失效
            _ = await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var principal = IdentityPrincipal(_obfuscator.Encode(user.Id), QlxAuthConstants.TokenKinds.Identity, "0");

        var result = await CreateService().RefreshIdentityAsync(principal, TestContext.Current.CancellationToken);

        Assert.Equal("登录状态已失效，请重新登录", result.ErrorDescription);
    }

    [Fact]
    public async Task RefreshSuccessRenewsClaimsAndResponseParameters()
    {
        var user = await SeedUserAsync();
        var tenant = await SeedTenantAsync();
        _ = await SeedMemberAsync(tenant.Id, user.Id);
        var query = new StubPortalRolePermissionQuery();
        query.Permissions.Add("portal.role.read");
        _services.Register(query);
        var principal = IdentityPrincipal(_obfuscator.Encode(user.Id), QlxAuthConstants.TokenKinds.Identity, "0");

        var result = await CreateService().RefreshIdentityAsync(principal, TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded);
        Assert.Equal("0", Claim(result.Principal, QlxAuthConstants.Claims.UserTokenVersion));
        Assert.Equal("portal.role.read", Claim(result.Principal, QlxClaimConstants.Permissions));
        Assert.True(result.ResponseParameters.ContainsKey(PortalConstants.Parameters.ResponseTenants));
    }

    [Fact]
    public async Task RefreshPortalAdminCarriesAdminFlagWithoutPermissions()
    {
        var user = await SeedUserAsync(isPortalAdmin: true);
        var principal = IdentityPrincipal(_obfuscator.Encode(user.Id), QlxAuthConstants.TokenKinds.Identity, "0");

        var result = await CreateService().RefreshIdentityAsync(principal, TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded);
        Assert.Equal("1", Claim(result.Principal, QlxAuthConstants.Claims.PortalAdminFlag));
        Assert.Equal(
            PortalConstants.Frontend.BooleanTrue,
            result.ResponseParameters[PortalConstants.Parameters.ResponsePortalAdmin]);
    }

    // ============ Phase 3：租户上下文 ============

    [Fact]
    public async Task EnterTenantNullPrincipalThrows()
    {
        _ = await Assert.ThrowsAsync<ArgumentNullException>(
            () => CreateService().EnterTenantContextAsync(
                ClientId, "pub-1", null, null!, Ip, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task EnterTenantInvalidClientRejected()
    {
        var apps = StubOpenIddictApplicationManagerProxy.Create(null, OpenIddictClientPermissions.All);

        var result = await CreateService(apps).EnterTenantContextAsync(
            ClientId, "pub-1", null,
            IdentityPrincipal("pub-1", QlxAuthConstants.TokenKinds.Identity),
            Ip, TestContext.Current.CancellationToken);

        Assert.Equal(OpenIddictConstants.Errors.InvalidClient, result.Error);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public async Task EnterTenantMissingTenantIdRejected(string? tenantIdRaw)
    {
        var result = await CreateService().EnterTenantContextAsync(
            ClientId, tenantIdRaw, null,
            IdentityPrincipal("pub-1", QlxAuthConstants.TokenKinds.Identity),
            Ip, TestContext.Current.CancellationToken);

        Assert.Equal(OpenIddictConstants.Errors.InvalidRequest, result.Error);
        Assert.Contains("tenant_id 必填", result.ErrorDescription);
    }

    [Fact]
    public async Task EnterTenantWithTenantKindTokenRejected()
    {
        var result = await CreateService().EnterTenantContextAsync(
            ClientId, "pub-1", null,
            IdentityPrincipal("pub-1", QlxAuthConstants.TokenKinds.Tenant),
            Ip, TestContext.Current.CancellationToken);

        Assert.Equal("必须使用身份级令牌进入租户上下文", result.ErrorDescription);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("bad-subject")]
    public async Task EnterTenantInvalidSubjectRejected(string? subject)
    {
        var result = await CreateService().EnterTenantContextAsync(
            ClientId, "pub-1", null,
            IdentityPrincipal(subject, QlxAuthConstants.TokenKinds.Identity),
            Ip, TestContext.Current.CancellationToken);

        Assert.Equal("身份令牌声明无效", result.ErrorDescription);
    }

    [Theory]
    [InlineData("bad-tenant")] // 不可解码
    [InlineData("pub-0")]      // 解码为 0
    [InlineData("pub--5")]     // 解码为负数
    public async Task EnterTenantInvalidTenantIdRejected(string tenantIdRaw)
    {
        var user = await SeedUserAsync();

        var result = await CreateService().EnterTenantContextAsync(
            ClientId, tenantIdRaw, null,
            IdentityPrincipal(_obfuscator.Encode(user.Id), QlxAuthConstants.TokenKinds.Identity),
            Ip, TestContext.Current.CancellationToken);

        Assert.Equal(OpenIddictConstants.Errors.InvalidRequest, result.Error);
        Assert.Equal("tenant_id 无效", result.ErrorDescription);
    }

    [Fact]
    public async Task EnterTenantMemberNotFoundRejected()
    {
        var user = await SeedUserAsync();

        var result = await CreateService().EnterTenantContextAsync(
            ClientId, _obfuscator.Encode(42), null,
            IdentityPrincipal(_obfuscator.Encode(user.Id), QlxAuthConstants.TokenKinds.Identity),
            Ip, TestContext.Current.CancellationToken);

        Assert.Equal(OpenIddictConstants.Errors.AccessDenied, result.Error);
        Assert.Equal("无权访问该租户或租户不可用", result.ErrorDescription);
    }

    [Theory]
    [InlineData("disabled-member")]
    [InlineData("pending-invite")]
    [InlineData("disabled-tenant")]
    public async Task EnterTenantInvalidMemberOrTenantStateRejected(string scenario)
    {
        var user = await SeedUserAsync();
        var tenant = await SeedTenantAsync(configure: scenario == "disabled-tenant" ? t => t.Disable() : null);
        _ = await SeedMemberAsync(
            tenant.Id, user.Id,
            scenario == "pending-invite" ? TenantMemberRole.PendingInvite : TenantMemberRole.OrdinaryMember,
            disabled: scenario == "disabled-member");

        var result = await CreateService().EnterTenantContextAsync(
            ClientId, _obfuscator.Encode(tenant.Id), null,
            IdentityPrincipal(_obfuscator.Encode(user.Id), QlxAuthConstants.TokenKinds.Identity),
            Ip, TestContext.Current.CancellationToken);

        Assert.Equal(OpenIddictConstants.Errors.AccessDenied, result.Error);
    }

    [Theory]
    [InlineData(true)]   // 用户不存在
    [InlineData(false)]  // 用户已停用
    public async Task EnterTenantUserUnavailableRejected(bool userMissing)
    {
        long userId;
        if (userMissing)
        {
            var tenant0 = await SeedTenantAsync();
            var ghost = await SeedMemberAsync(tenant0.Id, 88888);
            userId = ghost.PortalUserId;
        }
        else
        {
            var user = await SeedUserAsync(disabled: true);
            var tenant = await SeedTenantAsync("t002");
            _ = await SeedMemberAsync(tenant.Id, user.Id);
            userId = user.Id;
        }

        var tenantIdRaw = _obfuscator.Encode(userMissing ? 0 : 0); // 占位，下方重建
        await using (var db = CreateDb())
        {
            var member = await db.TenantMembers.FirstAsync(TestContext.Current.CancellationToken);
            tenantIdRaw = _obfuscator.Encode(member.TenantId);
        }

        var result = await CreateService().EnterTenantContextAsync(
            ClientId, tenantIdRaw, null,
            IdentityPrincipal(_obfuscator.Encode(userId), QlxAuthConstants.TokenKinds.Identity),
            Ip, TestContext.Current.CancellationToken);

        Assert.Equal(OpenIddictConstants.Errors.InvalidGrant, result.Error);
        Assert.Equal("账号已被停用，请联系管理员", result.ErrorDescription);
    }

    [Theory]
    [InlineData("bad-role")]
    [InlineData("pub-0")]
    public async Task EnterTenantInvalidMemberRoleIdRejected(string roleIdRaw)
    {
        var user = await SeedUserAsync();
        var tenant = await SeedTenantAsync();
        _ = await SeedMemberAsync(tenant.Id, user.Id);

        var result = await CreateService().EnterTenantContextAsync(
            ClientId, _obfuscator.Encode(tenant.Id), roleIdRaw,
            IdentityPrincipal(_obfuscator.Encode(user.Id), QlxAuthConstants.TokenKinds.Identity),
            Ip, TestContext.Current.CancellationToken);

        Assert.Equal(OpenIddictConstants.Errors.InvalidRequest, result.Error);
        Assert.Equal("member_role_id 无效", result.ErrorDescription);
    }

    [Fact]
    public async Task EnterTenantAdminBypassesRoleResolution()
    {
        var user = await SeedUserAsync();
        var tenant = await SeedTenantAsync();
        _ = await SeedMemberAsync(tenant.Id, user.Id, TenantMemberRole.TenantAdmin);
        // 即使角色查询返回角色，TenantAdmin 也不解析（零计算旁路）
        var roleQuery = new StubTenantMemberRoleQuery();
        roleQuery.Roles.Add(new TenantMemberRoleItem(5001, "role-x", "角色X", false));
        _services.Register(roleQuery);

        var result = await CreateService().EnterTenantContextAsync(
            ClientId, _obfuscator.Encode(tenant.Id), null,
            IdentityPrincipal(_obfuscator.Encode(user.Id), QlxAuthConstants.TokenKinds.Identity),
            Ip, TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded);
        Assert.Equal("TenantAdmin", Claim(result.Principal, QlxAuthConstants.Claims.MemberRole));
        Assert.Null(Claim(result.Principal, QlxAuthConstants.Claims.MemberRoleId));
        Assert.Null(Claim(result.Principal, QlxClaimConstants.Permissions));
        Assert.Empty(_tenantContext.Writes); // 旁路不触碰租户上下文
    }

    [Fact]
    public async Task EnterTenantWithoutRoleQueryFallsBackToUnionBehavior()
    {
        var user = await SeedUserAsync();
        var tenant = await SeedTenantAsync();
        var member = await SeedMemberAsync(tenant.Id, user.Id);
        // 不注册 ITenantMemberRoleQuery → resolvedRoleId = null

        var result = await CreateService().EnterTenantContextAsync(
            ClientId, _obfuscator.Encode(tenant.Id), null,
            IdentityPrincipal(_obfuscator.Encode(user.Id), QlxAuthConstants.TokenKinds.Identity),
            Ip, TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded);
        Assert.Null(Claim(result.Principal, QlxAuthConstants.Claims.MemberRoleId));
        Assert.Equal(_obfuscator.Encode(member.Id), Claim(result.Principal, QlxAuthConstants.Claims.MemberId));
    }

    [Fact]
    public async Task EnterTenantEmptyRolesFallsBackToUnionBehavior()
    {
        var user = await SeedUserAsync();
        var tenant = await SeedTenantAsync();
        _ = await SeedMemberAsync(tenant.Id, user.Id);
        _services.Register(new StubTenantMemberRoleQuery()); // 注册但返回空

        var result = await CreateService().EnterTenantContextAsync(
            ClientId, _obfuscator.Encode(tenant.Id), null,
            IdentityPrincipal(_obfuscator.Encode(user.Id), QlxAuthConstants.TokenKinds.Identity),
            Ip, TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded);
        Assert.Null(Claim(result.Principal, QlxAuthConstants.Claims.MemberRoleId));
    }

    [Fact]
    public async Task EnterTenantEmptyRolesWithPermissionQueryUsesMemberUnionPermissions()
    {
        // 成员无角色 + 权限查询已注册 → memberRoleId 为 null 时走 ListMemberPermissionCodesAsync（全角色并集）
        var user = await SeedUserAsync();
        var tenant = await SeedTenantAsync();
        _ = await SeedMemberAsync(tenant.Id, user.Id);
        _services.Register(new StubTenantMemberRoleQuery()); // 注册但返回空角色
        var permQuery = new StubTenantRolePermissionQuery();
        permQuery.MemberPermissions.Add("bas.member.read");
        permQuery.MemberPermissions.Add("bas.member.write");
        _services.Register(permQuery);

        var result = await CreateService().EnterTenantContextAsync(
            ClientId, _obfuscator.Encode(tenant.Id), null,
            IdentityPrincipal(_obfuscator.Encode(user.Id), QlxAuthConstants.TokenKinds.Identity),
            Ip, TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded);
        Assert.Null(Claim(result.Principal, QlxAuthConstants.Claims.MemberRoleId));
        Assert.Equal("bas.member.read,bas.member.write", Claim(result.Principal, QlxClaimConstants.Permissions));
    }

    [Fact]
    public async Task EnterTenantRequestedRoleNotBelongingToMemberRejected()
    {
        var user = await SeedUserAsync();
        var tenant = await SeedTenantAsync();
        _ = await SeedMemberAsync(tenant.Id, user.Id);
        var roleQuery = new StubTenantMemberRoleQuery();
        roleQuery.Roles.Add(new TenantMemberRoleItem(5001, "role-a", "角色A", false));
        _services.Register(roleQuery);

        var result = await CreateService().EnterTenantContextAsync(
            ClientId, _obfuscator.Encode(tenant.Id), _obfuscator.Encode(9999), // 9999 不在成员角色内
            IdentityPrincipal(_obfuscator.Encode(user.Id), QlxAuthConstants.TokenKinds.Identity),
            Ip, TestContext.Current.CancellationToken);

        Assert.Equal(OpenIddictConstants.Errors.AccessDenied, result.Error);
        Assert.Equal("所选角色不属于当前企业成员", result.ErrorDescription);
    }

    [Fact]
    public async Task EnterTenantMultipleRolesWithoutSelectionRejected()
    {
        var user = await SeedUserAsync();
        var tenant = await SeedTenantAsync();
        _ = await SeedMemberAsync(tenant.Id, user.Id);
        var roleQuery = new StubTenantMemberRoleQuery();
        roleQuery.Roles.Add(new TenantMemberRoleItem(5001, "role-a", "角色A", false));
        roleQuery.Roles.Add(new TenantMemberRoleItem(5002, "role-b", "角色B", false));
        _services.Register(roleQuery);

        var result = await CreateService().EnterTenantContextAsync(
            ClientId, _obfuscator.Encode(tenant.Id), null,
            IdentityPrincipal(_obfuscator.Encode(user.Id), QlxAuthConstants.TokenKinds.Identity),
            Ip, TestContext.Current.CancellationToken);

        Assert.Equal(OpenIddictConstants.Errors.InvalidRequest, result.Error);
        Assert.Equal("该企业下存在多个角色，请选择本次进入的角色", result.ErrorDescription);
    }

    [Fact]
    public async Task EnterTenantSingleRoleAutoSelected()
    {
        var user = await SeedUserAsync();
        var tenant = await SeedTenantAsync();
        _ = await SeedMemberAsync(tenant.Id, user.Id);
        var roleQuery = new StubTenantMemberRoleQuery();
        roleQuery.Roles.Add(new TenantMemberRoleItem(5001, "role-a", "角色A", false));
        _services.Register(roleQuery);
        var permQuery = new StubTenantRolePermissionQuery();
        permQuery.RolePermissions.Add("bas.member.read");
        _services.Register(permQuery);

        var result = await CreateService().EnterTenantContextAsync(
            ClientId, _obfuscator.Encode(tenant.Id), null,
            IdentityPrincipal(_obfuscator.Encode(user.Id), QlxAuthConstants.TokenKinds.Identity),
            Ip, TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded);
        Assert.Equal(_obfuscator.Encode(5001), Claim(result.Principal, QlxAuthConstants.Claims.MemberRoleId));
        Assert.Equal("bas.member.read", Claim(result.Principal, QlxClaimConstants.Permissions));
    }

    [Fact]
    public async Task EnterTenantSuccessFullAssertions()
    {
        var user = await SeedUserAsync();
        var tenant = await SeedTenantAsync();
        var member = await SeedMemberAsync(tenant.Id, user.Id);
        var roleQuery = new StubTenantMemberRoleQuery();
        roleQuery.Roles.Add(new TenantMemberRoleItem(5001, "role-a", "角色A", false));
        roleQuery.Roles.Add(new TenantMemberRoleItem(5002, "role-b", "角色B", false));
        _services.Register(roleQuery);
        var permQuery = new StubTenantRolePermissionQuery();
        permQuery.RolePermissions.Add("bas.member.read");
        _services.Register(permQuery);
        // 模块许可：仅 Enabled 下发
        await using (var db = CreateDb())
        {
            var trackedTenant = await db.Tenants.SingleAsync(
                t => t.Id == tenant.Id, TestContext.Current.CancellationToken);
            db.TenantModules.Add(new TenantModule(trackedTenant, "bas"));
            var disabledModule = new TenantModule(trackedTenant, "sal");
            disabledModule.Disable();
            db.TenantModules.Add(disabledModule);
            _ = await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var result = await CreateService().EnterTenantContextAsync(
            ClientId, _obfuscator.Encode(tenant.Id), _obfuscator.Encode(5002), // 显式选择第二个角色
            IdentityPrincipal(_obfuscator.Encode(user.Id), QlxAuthConstants.TokenKinds.Identity),
            Ip, TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded);
        Assert.Equal(QlxAuthConstants.TokenKinds.Tenant, Claim(result.Principal, QlxAuthConstants.Claims.TokenKind));
        Assert.Equal(_obfuscator.Encode(tenant.Id), Claim(result.Principal, QlxAuthConstants.Claims.TenantId));
        Assert.Equal("t001", Claim(result.Principal, QlxAuthConstants.Claims.TenantCode));
        Assert.Equal(_obfuscator.Encode(5002), Claim(result.Principal, QlxAuthConstants.Claims.MemberRoleId));
        // 模块 claim 仅含 Enabled
        var moduleClaims = result.Principal!.FindAll(QlxAuthConstants.Claims.Modules).Select(c => c.Value).ToArray();
        Assert.Equal(["bas"], moduleClaims);
        // 合并 Token 版本 = user(0) + member(0)
        Assert.Equal("0", Claim(result.Principal, QlxAuthConstants.Claims.TokenVersion));
        // 租户上下文：进入写入目标租户、结束恢复 0（角色解析 + 权限聚合各一轮）
        Assert.Equal(new long?[] { tenant.Id, 0L, tenant.Id, 0L }, _tenantContext.Writes);
        // 进入租户成功审计（按租户维度）
        Assert.Single(_audit.Successes);
        Assert.Equal(tenant.Id, _audit.Successes[0].TenantId);
        // 成员 LastLoginAt 更新
        await using var verifyDb = CreateDb();
        var reloaded = await verifyDb.TenantMembers.SingleAsync(
            m => m.Id == member.Id, TestContext.Current.CancellationToken);
        Assert.NotNull(reloaded.LastLoginAt);
    }

    [Fact]
    public async Task EnterTenantGracePeriodTenantAllowed()
    {
        var user = await SeedUserAsync();
        var tenant = await SeedTenantAsync(configure: t => t.EnterGracePeriod(7));
        _ = await SeedMemberAsync(tenant.Id, user.Id);

        var result = await CreateService().EnterTenantContextAsync(
            ClientId, _obfuscator.Encode(tenant.Id), null,
            IdentityPrincipal(_obfuscator.Encode(user.Id), QlxAuthConstants.TokenKinds.Identity),
            Ip, TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded);
    }

    // ============ 租户 Token 静默续期 ============

    private async Task<(PortalUser User, Tenant Tenant, TenantMember Member, string Token)> SeedRenewalFixtureAsync(
        string tokenKind = QlxAuthConstants.TokenKinds.Tenant,
        string? userTvOverride = null,
        string? memberTvOverride = null,
        string? roleClaim = null)
    {
        var user = await SeedUserAsync();
        var tenant = await SeedTenantAsync();
        var member = await SeedMemberAsync(tenant.Id, user.Id);
        var claims = new List<KeyValuePair<string, object>>
        {
            new(OpenIddictConstants.Claims.Subject, _obfuscator.Encode(user.Id)),
            new(QlxAuthConstants.Claims.TokenKind, tokenKind),
            new(QlxAuthConstants.Claims.TenantId, _obfuscator.Encode(tenant.Id)),
            new(QlxAuthConstants.Claims.MemberId, _obfuscator.Encode(member.Id)),
            new(QlxAuthConstants.Claims.UserTokenVersion, userTvOverride ?? user.TokenVersion.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            new(QlxAuthConstants.Claims.MemberTokenVersion, memberTvOverride ?? member.TokenVersion.ToString(System.Globalization.CultureInfo.InvariantCulture)),
        };
        if (roleClaim is not null)
        {
            claims.Add(new KeyValuePair<string, object>(QlxAuthConstants.Claims.MemberRoleId, roleClaim));
        }

        return (user, tenant, member, CreateTenantJwt(claims));
    }

    [Fact]
    public async Task RenewNullOrEmptyTokenThrows()
    {
        var service = CreateService();

        _ = await Assert.ThrowsAsync<ArgumentNullException>(
            () => service.RenewTenantTokenAsync(ClientId, null!, Ip, TestContext.Current.CancellationToken));
        _ = await Assert.ThrowsAsync<ArgumentException>(
            () => service.RenewTenantTokenAsync(ClientId, "  ", Ip, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task RenewInvalidClientRejected()
    {
        var apps = StubOpenIddictApplicationManagerProxy.Create(null, OpenIddictClientPermissions.All);

        var result = await CreateService(apps).RenewTenantTokenAsync(
            ClientId, "any-token", Ip, TestContext.Current.CancellationToken);

        Assert.Equal(OpenIddictConstants.Errors.InvalidClient, result.Error);
    }

    [Fact]
    public async Task RenewMalformedTokenRejected()
    {
        var result = await CreateService().RenewTenantTokenAsync(
            ClientId, "not-a-jwt", Ip, TestContext.Current.CancellationToken);

        Assert.Equal(OpenIddictConstants.Errors.InvalidGrant, result.Error);
        Assert.Contains("租户令牌无效或已超出续期宽限期", result.ErrorDescription);
    }

    [Fact]
    public async Task RenewExpiredBeyondGraceRejected()
    {
        // 过期 1 小时 > 宽限期 10 分钟 → 拒绝
        var (_, _, _, token) = await SeedRenewalFixtureAsync();
        var expired = CreateTenantJwt(
            [
                new(OpenIddictConstants.Claims.Subject, _obfuscator.Encode(1)),
                new(QlxAuthConstants.Claims.TokenKind, QlxAuthConstants.TokenKinds.Tenant),
            ],
            expires: DateTimeOffset.UtcNow.AddHours(-1));

        var result = await CreateService().RenewTenantTokenAsync(
            ClientId, expired, Ip, TestContext.Current.CancellationToken);

        Assert.Equal(OpenIddictConstants.Errors.InvalidGrant, result.Error);
        Assert.Contains("续期宽限期", result.ErrorDescription);
    }

    [Fact]
    public async Task RenewIdentityKindTokenRejected()
    {
        var (_, _, _, token) = await SeedRenewalFixtureAsync(tokenKind: QlxAuthConstants.TokenKinds.Identity);

        var result = await CreateService().RenewTenantTokenAsync(
            ClientId, token, Ip, TestContext.Current.CancellationToken);

        Assert.Equal("必须使用租户级令牌进行静默续期", result.ErrorDescription);
    }

    [Fact]
    public async Task RenewMissingSubjectClaimRejected()
    {
        await SeedRenewalFixtureAsync(); // 仅种子数据
        var token = CreateTenantJwt(
        [
            new(QlxAuthConstants.Claims.TokenKind, QlxAuthConstants.TokenKinds.Tenant),
            new(QlxAuthConstants.Claims.TenantId, _obfuscator.Encode(1)),
            new(QlxAuthConstants.Claims.MemberId, _obfuscator.Encode(1)),
        ]);

        var result = await CreateService().RenewTenantTokenAsync(
            ClientId, token, Ip, TestContext.Current.CancellationToken);

        Assert.Equal("租户令牌声明无效", result.ErrorDescription);
    }

    [Fact]
    public async Task RenewUndecodableTenantIdClaimRejected()
    {
        await SeedRenewalFixtureAsync();
        var token = CreateTenantJwt(
        [
            new(OpenIddictConstants.Claims.Subject, _obfuscator.Encode(1)),
            new(QlxAuthConstants.Claims.TokenKind, QlxAuthConstants.TokenKinds.Tenant),
            new(QlxAuthConstants.Claims.TenantId, "bad-tenant"),
            new(QlxAuthConstants.Claims.MemberId, _obfuscator.Encode(1)),
        ]);

        var result = await CreateService().RenewTenantTokenAsync(
            ClientId, token, Ip, TestContext.Current.CancellationToken);

        Assert.Equal("租户令牌声明无效", result.ErrorDescription);
    }

    [Fact]
    public async Task RenewUserNotFoundRejected()
    {
        // 令牌指向不存在的用户
        var token = CreateTenantJwt(
        [
            new(OpenIddictConstants.Claims.Subject, _obfuscator.Encode(424242)),
            new(QlxAuthConstants.Claims.TokenKind, QlxAuthConstants.TokenKinds.Tenant),
            new(QlxAuthConstants.Claims.TenantId, _obfuscator.Encode(1)),
            new(QlxAuthConstants.Claims.MemberId, _obfuscator.Encode(1)),
        ]);

        var result = await CreateService().RenewTenantTokenAsync(
            ClientId, token, Ip, TestContext.Current.CancellationToken);

        Assert.Equal("账号已被停用，请联系管理员", result.ErrorDescription);
    }

    [Fact]
    public async Task RenewDisabledUserRejected()
    {
        var (user, _, _, _) = await SeedRenewalFixtureAsync();
        await using (var db = CreateDb())
        {
            var tracked = await db.PortalUsers.SingleAsync(
                u => u.Id == user.Id, TestContext.Current.CancellationToken);
            tracked.Disable();
            _ = await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        // 重新生成携带旧 tv_u=0 的令牌（停用后 TokenVersion=1，但用户状态检查先行）
        var token = CreateTenantJwt(
            [
                new(OpenIddictConstants.Claims.Subject, _obfuscator.Encode(user.Id)),
                new(QlxAuthConstants.Claims.TokenKind, QlxAuthConstants.TokenKinds.Tenant),
                new(QlxAuthConstants.Claims.TenantId, _obfuscator.Encode(1)),
                new(QlxAuthConstants.Claims.MemberId, _obfuscator.Encode(1)),
                new(QlxAuthConstants.Claims.UserTokenVersion, "0"),
                new(QlxAuthConstants.Claims.MemberTokenVersion, "0"),
            ]);

        var result = await CreateService().RenewTenantTokenAsync(
            ClientId, token, Ip, TestContext.Current.CancellationToken);

        Assert.Equal("账号已被停用，请联系管理员", result.ErrorDescription);
    }

    [Fact]
    public async Task RenewUserTokenVersionMismatchRejected()
    {
        var (user, tenant, member, _) = await SeedRenewalFixtureAsync(userTvOverride: "9"); // 令牌 tv_u=9 ≠ 库 0

        var token = CreateTenantJwt(
            [
                new(OpenIddictConstants.Claims.Subject, _obfuscator.Encode(user.Id)),
                new(QlxAuthConstants.Claims.TokenKind, QlxAuthConstants.TokenKinds.Tenant),
                new(QlxAuthConstants.Claims.TenantId, _obfuscator.Encode(tenant.Id)),
                new(QlxAuthConstants.Claims.MemberId, _obfuscator.Encode(member.Id)),
                new(QlxAuthConstants.Claims.UserTokenVersion, "9"),
                new(QlxAuthConstants.Claims.MemberTokenVersion, "0"),
            ]);

        var result = await CreateService().RenewTenantTokenAsync(
            ClientId, token, Ip, TestContext.Current.CancellationToken);

        Assert.Equal("登录状态已失效，请重新登录", result.ErrorDescription);
    }

    [Theory]
    [InlineData("member-missing")]
    [InlineData("member-disabled")]
    [InlineData("member-pending")]
    [InlineData("tenant-disabled")]
    public async Task RenewInvalidMemberOrTenantStateRejected(string scenario)
    {
        var (user, tenant, member, _) = await SeedRenewalFixtureAsync();
        await using (var db = CreateDb())
        {
            if (scenario is "member-disabled" or "member-pending")
            {
                var tracked = await db.TenantMembers.SingleAsync(
                    m => m.Id == member.Id, TestContext.Current.CancellationToken);
                if (scenario == "member-disabled")
                {
                    tracked.Disable();
                }
                else
                {
                    tracked.Disable();
                    tracked.Enable();
                    typeof(TenantMember).GetProperty(nameof(TenantMember.Role))!
                        .SetValue(tracked, TenantMemberRole.PendingInvite);
                }
            }
            else if (scenario == "tenant-disabled")
            {
                var trackedTenant = await db.Tenants.SingleAsync(
                    t => t.Id == tenant.Id, TestContext.Current.CancellationToken);
                trackedTenant.Disable();
            }
            else if (scenario == "member-missing")
            {
                var tracked = await db.TenantMembers.SingleAsync(
                    m => m.Id == member.Id, TestContext.Current.CancellationToken);
                db.TenantMembers.Remove(tracked);
            }

            _ = await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var token = CreateTenantJwt(
            [
                new(OpenIddictConstants.Claims.Subject, _obfuscator.Encode(user.Id)),
                new(QlxAuthConstants.Claims.TokenKind, QlxAuthConstants.TokenKinds.Tenant),
                new(QlxAuthConstants.Claims.TenantId, _obfuscator.Encode(tenant.Id)),
                new(QlxAuthConstants.Claims.MemberId, _obfuscator.Encode(member.Id)),
                new(QlxAuthConstants.Claims.UserTokenVersion, "0"),
                new(QlxAuthConstants.Claims.MemberTokenVersion, "0"),
            ]);

        var result = await CreateService().RenewTenantTokenAsync(
            ClientId, token, Ip, TestContext.Current.CancellationToken);

        Assert.Equal(OpenIddictConstants.Errors.AccessDenied, result.Error);
        Assert.Equal("无权访问该租户或租户不可用", result.ErrorDescription);
    }

    [Fact]
    public async Task RenewMemberTokenVersionMismatchRejected()
    {
        var (user, tenant, member, _) = await SeedRenewalFixtureAsync();

        var token = CreateTenantJwt(
            [
                new(OpenIddictConstants.Claims.Subject, _obfuscator.Encode(user.Id)),
                new(QlxAuthConstants.Claims.TokenKind, QlxAuthConstants.TokenKinds.Tenant),
                new(QlxAuthConstants.Claims.TenantId, _obfuscator.Encode(tenant.Id)),
                new(QlxAuthConstants.Claims.MemberId, _obfuscator.Encode(member.Id)),
                new(QlxAuthConstants.Claims.UserTokenVersion, "0"),
                new(QlxAuthConstants.Claims.MemberTokenVersion, "7"), // ≠ 库 0
            ]);

        var result = await CreateService().RenewTenantTokenAsync(
            ClientId, token, Ip, TestContext.Current.CancellationToken);

        Assert.Equal("租户成员状态已变更，请重新进入企业", result.ErrorDescription);
    }

    [Fact]
    public async Task RenewUndecodableRoleClaimRejected()
    {
        var (user, tenant, member, _) = await SeedRenewalFixtureAsync(roleClaim: "bad-role");

        var token = CreateTenantJwt(
            [
                new(OpenIddictConstants.Claims.Subject, _obfuscator.Encode(user.Id)),
                new(QlxAuthConstants.Claims.TokenKind, QlxAuthConstants.TokenKinds.Tenant),
                new(QlxAuthConstants.Claims.TenantId, _obfuscator.Encode(tenant.Id)),
                new(QlxAuthConstants.Claims.MemberId, _obfuscator.Encode(member.Id)),
                new(QlxAuthConstants.Claims.UserTokenVersion, "0"),
                new(QlxAuthConstants.Claims.MemberTokenVersion, "0"),
                new(QlxAuthConstants.Claims.MemberRoleId, "bad-role"),
            ]);

        var result = await CreateService().RenewTenantTokenAsync(
            ClientId, token, Ip, TestContext.Current.CancellationToken);

        Assert.Equal("租户令牌角色声明无效", result.ErrorDescription);
    }

    [Fact]
    public async Task RenewStaleRoleClaimRejected()
    {
        var (user, tenant, member, _) = await SeedRenewalFixtureAsync();
        var roleQuery = new StubTenantMemberRoleQuery();
        roleQuery.Roles.Add(new TenantMemberRoleItem(5001, "role-a", "角色A", false));
        _services.Register(roleQuery);

        var token = CreateTenantJwt(
            [
                new(OpenIddictConstants.Claims.Subject, _obfuscator.Encode(user.Id)),
                new(QlxAuthConstants.Claims.TokenKind, QlxAuthConstants.TokenKinds.Tenant),
                new(QlxAuthConstants.Claims.TenantId, _obfuscator.Encode(tenant.Id)),
                new(QlxAuthConstants.Claims.MemberId, _obfuscator.Encode(member.Id)),
                new(QlxAuthConstants.Claims.UserTokenVersion, "0"),
                new(QlxAuthConstants.Claims.MemberTokenVersion, "0"),
                new(QlxAuthConstants.Claims.MemberRoleId, _obfuscator.Encode(9999)), // 角色已被回收
            ]);

        var result = await CreateService().RenewTenantTokenAsync(
            ClientId, token, Ip, TestContext.Current.CancellationToken);

        Assert.Equal(OpenIddictConstants.Errors.InvalidGrant, result.Error);
        Assert.Equal("所选角色已失效，请重新进入企业", result.ErrorDescription);
    }

    [Fact]
    public async Task RenewSuccessRenewsTenantPrincipal()
    {
        var (user, tenant, member, token) = await SeedRenewalFixtureAsync();
        var roleQuery = new StubTenantMemberRoleQuery();
        roleQuery.Roles.Add(new TenantMemberRoleItem(5001, "role-a", "角色A", false));
        _services.Register(roleQuery);
        var permQuery = new StubTenantRolePermissionQuery();
        permQuery.RolePermissions.Add("bas.member.read");
        _services.Register(permQuery);
        await using (var db = CreateDb())
        {
            var trackedTenant = await db.Tenants.SingleAsync(
                t => t.Id == tenant.Id, TestContext.Current.CancellationToken);
            db.TenantModules.Add(new TenantModule(trackedTenant, "bas"));
            _ = await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var result = await CreateService().RenewTenantTokenAsync(
            ClientId, token, Ip, TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded);
        Assert.Equal(QlxAuthConstants.TokenKinds.Tenant, Claim(result.Principal, QlxAuthConstants.Claims.TokenKind));
        Assert.Equal(_obfuscator.Encode(tenant.Id), Claim(result.Principal, QlxAuthConstants.Claims.TenantId));
        // 单角色自动锁定（与原 Token 一致维度）
        Assert.Equal(_obfuscator.Encode(5001), Claim(result.Principal, QlxAuthConstants.Claims.MemberRoleId));
        Assert.Equal("bas.member.read", Claim(result.Principal, QlxClaimConstants.Permissions));
        var moduleClaims = result.Principal!.FindAll(QlxAuthConstants.Claims.Modules).Select(c => c.Value).ToArray();
        Assert.Equal(["bas"], moduleClaims);
    }

    [Fact]
    public async Task RenewMissingTokenKindClaimRejected()
    {
        // token_kind claim 完全缺失 → null != tenant → 拒绝
        var (user, tenant, member, _) = await SeedRenewalFixtureAsync();
        var token = CreateTenantJwt(
            [
                new(OpenIddictConstants.Claims.Subject, _obfuscator.Encode(user.Id)),
                new(QlxAuthConstants.Claims.TenantId, _obfuscator.Encode(tenant.Id)),
                new(QlxAuthConstants.Claims.MemberId, _obfuscator.Encode(member.Id)),
            ]);

        var result = await CreateService().RenewTenantTokenAsync(
            ClientId, token, Ip, TestContext.Current.CancellationToken);

        Assert.Equal("必须使用租户级令牌进行静默续期", result.ErrorDescription);
    }

    // ============ Phase 3 / 刷新：token_kind 缺失防御 ============

    [Fact]
    public async Task EnterTenantMissingTokenKindClaimRejected()
    {
        // token_kind claim 完全缺失 → null != identity → 拒绝
        var user = await SeedUserAsync();
        var tenant = await SeedTenantAsync();
        _ = await SeedMemberAsync(tenant.Id, user.Id);
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(OpenIddictConstants.Claims.Subject, _obfuscator.Encode(user.Id))]));

        var result = await CreateService().EnterTenantContextAsync(
            ClientId, _obfuscator.Encode(tenant.Id), null,
            principal, Ip, TestContext.Current.CancellationToken);

        Assert.Equal(OpenIddictConstants.Errors.InvalidGrant, result.Error);
        Assert.Equal("必须使用身份级令牌进入租户上下文", result.ErrorDescription);
    }

    [Fact]
    public async Task RefreshMissingTokenKindClaimTreatedAsIdentity()
    {
        // token_kind claim 完全缺失 → null != tenant → 按身份级令牌继续刷新（旧版 Token 兼容）
        var user = await SeedUserAsync();
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim(OpenIddictConstants.Claims.Subject, _obfuscator.Encode(user.Id)),
                new Claim(QlxAuthConstants.Claims.UserTokenVersion,
                    user.TokenVersion.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            ]));

        var result = await CreateService().RefreshIdentityAsync(
            principal, TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded);
        Assert.Equal(QlxAuthConstants.TokenKinds.Identity, Claim(result.Principal, QlxAuthConstants.Claims.TokenKind));
    }

    // ============ CreateTenantPrincipal：数据范围预留分支（当前契约恒 null，经反射直测）============

    [Fact]
    public void CreateTenantPrincipalWritesDataScopeClaimsWhenProvided()
    {
        var service = CreateService();
        var user = new PortalUser("13800000099", "数据范围用户");
        var method = typeof(PortalAuthenticationService).GetMethod(
            "CreateTenantPrincipal", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;

        var principal = (ClaimsPrincipal)method.Invoke(service,
        [
            user, _obfuscator.Encode(1), 1L, "t001", "数据范围用户",
            TenantMemberRole.OrdinaryMember, 2L, 0L,
            new List<string> { "bas" }, new List<string> { "bas.member.read" },
            "dept", 42L, 5001L,
        ])!;

        Assert.Equal("dept", Claim(principal, QlxClaimConstants.DataScope));
        Assert.Equal("42", Claim(principal, QlxClaimConstants.DataScopeDeptId));
    }

    [Fact]
    public void CreateTenantPrincipalNullPermissionsWritesNoPermissionOrScopeClaims()
    {
        // permissions 为 null（普通成员但权限查询返回 null 的防御场景）→ 不写 permissions / data_scope
        var service = CreateService();
        var user = new PortalUser("13800000098", "空权限用户");
        var method = typeof(PortalAuthenticationService).GetMethod(
            "CreateTenantPrincipal", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;

        var principal = (ClaimsPrincipal)method.Invoke(service,
        [
            user, _obfuscator.Encode(1), 1L, "t001", "空权限用户",
            TenantMemberRole.OrdinaryMember, 2L, 0L,
            new List<string> { "bas" }, null,
            "dept", 42L, 5001L,
        ])!;

        Assert.Null(Claim(principal, QlxClaimConstants.Permissions));
        Assert.Null(Claim(principal, QlxClaimConstants.DataScope));
        Assert.Null(Claim(principal, QlxClaimConstants.DataScopeDeptId));
    }

    [Fact]
    public async Task RenewWrongIssuerTokenRejected()
    {
        // 合法签名/加密但 Issuer 不匹配 → SecurityTokenInvalidIssuerException 分支
        await SeedRenewalFixtureAsync();
        var token = CreateTenantJwt(
            [
                new(OpenIddictConstants.Claims.Subject, _obfuscator.Encode(1)),
                new(QlxAuthConstants.Claims.TokenKind, QlxAuthConstants.TokenKinds.Tenant),
            ],
            issuer: "https://evil.example.com/");

        var result = await CreateService().RenewTenantTokenAsync(
            ClientId, token, Ip, TestContext.Current.CancellationToken);

        Assert.Equal(OpenIddictConstants.Errors.InvalidGrant, result.Error);
        Assert.Contains("租户令牌无效或已超出续期宽限期", result.ErrorDescription);
    }

    [Fact]
    public async Task RenewMissingTenantIdClaimRejected()
    {
        var (user, _, member, _) = await SeedRenewalFixtureAsync();
        var token = CreateTenantJwt(
            [
                new(OpenIddictConstants.Claims.Subject, _obfuscator.Encode(user.Id)),
                new(QlxAuthConstants.Claims.TokenKind, QlxAuthConstants.TokenKinds.Tenant),
                new(QlxAuthConstants.Claims.MemberId, _obfuscator.Encode(member.Id)),
            ]);

        var result = await CreateService().RenewTenantTokenAsync(
            ClientId, token, Ip, TestContext.Current.CancellationToken);

        Assert.Equal("租户令牌声明无效", result.ErrorDescription);
    }

    [Fact]
    public async Task RenewMissingMemberIdClaimRejected()
    {
        var (user, tenant, _, _) = await SeedRenewalFixtureAsync();
        var token = CreateTenantJwt(
            [
                new(OpenIddictConstants.Claims.Subject, _obfuscator.Encode(user.Id)),
                new(QlxAuthConstants.Claims.TokenKind, QlxAuthConstants.TokenKinds.Tenant),
                new(QlxAuthConstants.Claims.TenantId, _obfuscator.Encode(tenant.Id)),
            ]);

        var result = await CreateService().RenewTenantTokenAsync(
            ClientId, token, Ip, TestContext.Current.CancellationToken);

        Assert.Equal("租户令牌声明无效", result.ErrorDescription);
    }

    // ============ Phase 1：密码哈希升级（RehashNeeded）============

    [Fact]
    public async Task AuthenticateRehashNeededUpgradesPasswordHash()
    {
        // 旧算法哈希校验通过但需升级 → 重签新哈希落库
        var user = await SeedUserAsync();
        var tenant = await SeedTenantAsync();
        _ = await SeedMemberAsync(tenant.Id, user.Id);

        var result = await CreateService(hasher: new RehashPasswordHasher()).AuthenticateIdentityAsync(
            ClientId, user.LoginAccount, CorrectPassword, Ip, TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded);
        await using var db = CreateDb();
        var reloaded = await db.PortalUsers.SingleAsync(
            u => u.Id == user.Id, TestContext.Current.CancellationToken);
        Assert.Equal(RehashPasswordHasher.NewHash, reloaded.PasswordHash);
        Assert.NotNull(reloaded.LastLoginAt);
    }

    /// <summary>密码哈希器桩：校验恒返回 SuccessRehashNeeded，重签返回固定新哈希。</summary>
    private sealed class RehashPasswordHasher : IPasswordHasher<PortalUser>
    {
        public const string NewHash = "upgraded-hash";

        public string HashPassword(PortalUser user, string password) => NewHash;

        public PasswordVerificationResult VerifyHashedPassword(
            PortalUser user, string hashedPassword, string providedPassword)
            => PasswordVerificationResult.SuccessRehashNeeded;
    }
}

