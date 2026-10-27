/*
 * 所属公司：四川淇利馨集团有限公司
 * 描述：PermissionAuthorizationHandler 单元测试
 *       覆盖：未认证 Fail、租户上下文 TenantAdmin 零计算旁路、permissions claim 包含/不包含、
 *             门户上下文 is_portal_admin 旁路、门户上下文 permissions 匹配、无任何匹配 Fail
 */

using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Qlx.Abstractions.Authorization;
using Qlx.Abstractions.Constants;
using Qlx.BuildingBlocks.Authorization;

namespace Qlx.BuildingBlocks.Tests.Authorization;

public sealed class PermissionAuthorizationHandlerTests
{
    private const string RequiredCode = "bas.admin.org.action";

    private static ClaimsPrincipal CreateUser(params Claim[] claims)
        => new(new ClaimsIdentity(claims, authenticationType: "Test"));

    private static async Task<AuthorizationHandlerContext> EvaluateAsync(ClaimsPrincipal user)
    {
        var requirement = new PermissionRequirement(RequiredCode);
        var context = new AuthorizationHandlerContext([requirement], user, resource: null);
        await new PermissionAuthorizationHandler().HandleAsync(context);
        return context;
    }

    [Fact]
    public async Task UnauthenticatedUserFails()
    {
        var context = await EvaluateAsync(new ClaimsPrincipal(new ClaimsIdentity()));

        Assert.False(context.HasSucceeded);
    }

    [Fact]
    public async Task TenantAdminBypassesInTenantContext()
    {
        var user = CreateUser(
            new Claim(QlxClaimConstants.TenantId, "tenant-1"),
            new Claim(QlxClaimConstants.MemberRole, QlxMemberRoleValues.TenantAdmin));

        var context = await EvaluateAsync(user);

        Assert.True(context.HasSucceeded);
    }

    [Fact]
    public async Task TenantMemberWithMatchingPermissionSucceeds()
    {
        var user = CreateUser(
            new Claim(QlxClaimConstants.TenantId, "tenant-1"),
            new Claim(QlxClaimConstants.MemberRole, QlxMemberRoleValues.OrdinaryMember),
            new Claim(QlxClaimConstants.Permissions, $"bas.admin.org.menu;{RequiredCode}"));

        var context = await EvaluateAsync(user);

        Assert.True(context.HasSucceeded);
    }

    [Fact]
    public async Task TenantMemberWithoutMatchingPermissionFails()
    {
        var user = CreateUser(
            new Claim(QlxClaimConstants.TenantId, "tenant-1"),
            new Claim(QlxClaimConstants.MemberRole, QlxMemberRoleValues.OrdinaryMember),
            new Claim(QlxClaimConstants.Permissions, "bas.admin.org.menu;bas.admin.member.menu"));

        var context = await EvaluateAsync(user);

        Assert.False(context.HasSucceeded);
        Assert.True(context.HasFailed);
    }

    [Fact]
    public async Task PortalAdminBypassesInPortalContext()
    {
        var user = CreateUser(new Claim(QlxClaimConstants.PortalAdminFlag, "1"));

        var context = await EvaluateAsync(user);

        Assert.True(context.HasSucceeded);
    }

    [Fact]
    public async Task PortalUserWithMatchingPermissionSucceeds()
    {
        var user = CreateUser(new Claim(QlxClaimConstants.Permissions, RequiredCode));

        var context = await EvaluateAsync(user);

        Assert.True(context.HasSucceeded);
    }

    [Fact]
    public async Task PortalUserWithoutAnyMatchFails()
    {
        var user = CreateUser(new Claim(QlxClaimConstants.Permissions, "portal.other.code"));

        var context = await EvaluateAsync(user);

        Assert.False(context.HasSucceeded);
        Assert.True(context.HasFailed);
    }

    [Fact]
    public async Task PermissionMatchingIsExactNotPrefix()
    {
        // bas.admin.org.actionx / bas.admin.org.act 均不得误判为包含
        var user = CreateUser(
            new Claim(QlxClaimConstants.TenantId, "tenant-1"),
            new Claim(QlxClaimConstants.MemberRole, QlxMemberRoleValues.OrdinaryMember),
            new Claim(QlxClaimConstants.Permissions, "bas.admin.org.actionx bas.admin.org.act"));

        var context = await EvaluateAsync(user);

        Assert.False(context.HasSucceeded);
    }
}
