/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-25
 * 描述：CurrentUserService 白盒测试——sub 解析失败/用户缺失/停用返回 null；身份级与租户级 Token 分支；
 *       租户级成员缺失显示名回落、modules claim 枚举、门户管理员标记透传
 */

using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Qlx.Modules.Portal;
using Qlx.Modules.Portal.Auth;
using Qlx.Modules.Portal.Domain.Identity;
using Qlx.Modules.Portal.Domain.Tenancy;
using Qlx.Modules.Portal.Services;

namespace Qlx.BuildingBlocks.Tests.Portal.Auth;

public sealed class CurrentUserServiceTests
{
    private readonly string _dbName = $"current-user-{Guid.NewGuid():N}";
    private readonly StubIdObfuscator _obfuscator = new();

    private PortalDbContext CreateDb()
        => new(new DbContextOptionsBuilder<PortalDbContext>()
            .UseInMemoryDatabase(_dbName)
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options);

    private CurrentUserService CreateService() => new(CreateDb(), _obfuscator);

    private static ClaimsPrincipal PrincipalOf(params Claim[] claims)
        => new(new ClaimsIdentity(claims));

    private async Task<PortalUser> SeedUserAsync(bool disabled = false, bool isPortalAdmin = false)
    {
        await using var db = CreateDb();
        var user = new PortalUser("13800000001", "显示名", isPortalAdmin);
        if (disabled)
        {
            user.Disable();
        }

        db.PortalUsers.Add(user);
        _ = await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return user;
    }

    [Fact]
    public async Task GetCurrentUserAsyncNullPrincipalThrows()
    {
        _ = await Assert.ThrowsAsync<ArgumentNullException>(
            () => CreateService().GetCurrentUserAsync(null!, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GetCurrentUserAsyncMissingSubjectReturnsNull()
    {
        var result = await CreateService().GetCurrentUserAsync(
            PrincipalOf(), TestContext.Current.CancellationToken);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetCurrentUserAsyncUndecodableSubjectReturnsNull()
    {
        var result = await CreateService().GetCurrentUserAsync(
            PrincipalOf(new Claim(OpenIddict.Abstractions.OpenIddictConstants.Claims.Subject, "not-obfuscated")),
            TestContext.Current.CancellationToken);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetCurrentUserAsyncUserNotFoundReturnsNull()
    {
        var result = await CreateService().GetCurrentUserAsync(
            PrincipalOf(new Claim(OpenIddict.Abstractions.OpenIddictConstants.Claims.Subject, _obfuscator.Encode(999))),
            TestContext.Current.CancellationToken);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetCurrentUserAsyncDisabledUserReturnsNull()
    {
        var user = await SeedUserAsync(disabled: true);

        var result = await CreateService().GetCurrentUserAsync(
            PrincipalOf(new Claim(OpenIddict.Abstractions.OpenIddictConstants.Claims.Subject, _obfuscator.Encode(user.Id))),
            TestContext.Current.CancellationToken);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetCurrentUserAsyncIdentityTokenReturnsBaseInfo()
    {
        var user = await SeedUserAsync(isPortalAdmin: true);

        var result = await CreateService().GetCurrentUserAsync(
            PrincipalOf(
                new Claim(OpenIddict.Abstractions.OpenIddictConstants.Claims.Subject, _obfuscator.Encode(user.Id)),
                new Claim(QlxAuthConstants.Claims.TokenKind, QlxAuthConstants.TokenKinds.Identity)),
            TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.Equal(user.LoginAccount, result.LoginAccount);
        Assert.Equal(user.DisplayName, result.DisplayName);
        Assert.True(result.IsPortalAdmin);
        Assert.Null(result.TenantId);
        Assert.Empty(result.Modules);
    }

    [Fact]
    public async Task GetCurrentUserAsyncTenantTokenReturnsTenantContext()
    {
        var user = await SeedUserAsync();
        long tenantId;
        long memberId;
        await using (var db = CreateDb())
        {
            var tenant = new Tenant("t001", "租户甲");
            tenant.Activate();
            db.Tenants.Add(tenant);
            _ = await db.SaveChangesAsync(TestContext.Current.CancellationToken);
            tenantId = tenant.Id;

            var member = new TenantMember(tenantId, user.Id) { DisplayNameInTenant = "租户内名字" };
            db.TenantMembers.Add(member);
            _ = await db.SaveChangesAsync(TestContext.Current.CancellationToken);
            memberId = member.Id;
        }

        var result = await CreateService().GetCurrentUserAsync(
            PrincipalOf(
                new Claim(OpenIddict.Abstractions.OpenIddictConstants.Claims.Subject, _obfuscator.Encode(user.Id)),
                new Claim(QlxAuthConstants.Claims.TokenKind, QlxAuthConstants.TokenKinds.Tenant),
                new Claim(QlxAuthConstants.Claims.TenantId, _obfuscator.Encode(tenantId)),
                new Claim(QlxAuthConstants.Claims.TenantCode, "t001"),
                new Claim(QlxAuthConstants.Claims.MemberId, _obfuscator.Encode(memberId)),
                new Claim(QlxAuthConstants.Claims.MemberRole, "OrdinaryMember"),
                new Claim(QlxAuthConstants.Claims.Modules, "bas"),
                new Claim(QlxAuthConstants.Claims.Modules, "support")),
            TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.Equal("租户内名字", result.DisplayName);       // 租户内显示名优先
        Assert.Equal(_obfuscator.Encode(tenantId), result.TenantId);
        Assert.Equal("t001", result.TenantCode);
        Assert.Equal("OrdinaryMember", result.MemberRole);
        Assert.Equal(["bas", "support"], result.Modules);
    }

    [Fact]
    public async Task GetCurrentUserAsyncTenantTokenMemberMissingFallsBackToPortalDisplayName()
    {
        var user = await SeedUserAsync();

        var result = await CreateService().GetCurrentUserAsync(
            PrincipalOf(
                new Claim(OpenIddict.Abstractions.OpenIddictConstants.Claims.Subject, _obfuscator.Encode(user.Id)),
                new Claim(QlxAuthConstants.Claims.TokenKind, QlxAuthConstants.TokenKinds.Tenant),
                new Claim(QlxAuthConstants.Claims.TenantId, _obfuscator.Encode(42)),
                new Claim(QlxAuthConstants.Claims.MemberId, _obfuscator.Encode(99999))), // 成员不存在
            TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.Equal("显示名", result.DisplayName); // member 为 null → 回落门户 DisplayName
    }

    [Theory]
    [InlineData("missing-tenant-id")]
    [InlineData("undecodable-tenant-id")]
    [InlineData("missing-member-id")]
    [InlineData("undecodable-member-id")]
    public async Task GetCurrentUserAsyncTenantTokenWithBrokenClaimsFallsBackToIdentityBranch(string scenario)
    {
        var user = await SeedUserAsync();
        var claims = new List<Claim>
        {
            new(OpenIddict.Abstractions.OpenIddictConstants.Claims.Subject, _obfuscator.Encode(user.Id)),
            new(QlxAuthConstants.Claims.TokenKind, QlxAuthConstants.TokenKinds.Tenant),
        };
        switch (scenario)
        {
            case "undecodable-tenant-id":
                claims.Add(new Claim(QlxAuthConstants.Claims.TenantId, "bad-value"));
                claims.Add(new Claim(QlxAuthConstants.Claims.MemberId, _obfuscator.Encode(1)));
                break;
            case "missing-member-id":
                claims.Add(new Claim(QlxAuthConstants.Claims.TenantId, _obfuscator.Encode(42)));
                break;
            case "undecodable-member-id":
                claims.Add(new Claim(QlxAuthConstants.Claims.TenantId, _obfuscator.Encode(42)));
                claims.Add(new Claim(QlxAuthConstants.Claims.MemberId, "bad-value"));
                break;
            // missing-tenant-id：不再追加
        }

        var result = await CreateService().GetCurrentUserAsync(
            PrincipalOf([.. claims]), TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.Null(result.TenantId);        // 身份级分支无租户上下文
        Assert.Equal("显示名", result.DisplayName);
    }
}
