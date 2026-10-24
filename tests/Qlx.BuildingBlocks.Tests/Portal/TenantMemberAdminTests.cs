/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-23
 * 描述：TenantMemberAdmin.ResendInviteAsync 状态机测试——仅 PendingInvite 可重发（其余 409）、
 *       成员不存在 404、成功返回关联门户用户 Id（InMemory + InMemoryPortalDbContextFactory）
 */

using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

using Qlx.Abstractions.Exceptions;
using Qlx.BuildingBlocks.Tests.Modules;
using Qlx.Modules.Portal;
using Qlx.Modules.Portal.Auth;
using Qlx.Modules.Portal.Domain.Identity;
using Qlx.Modules.Portal.Services;

namespace Qlx.BuildingBlocks.Tests.Portal;

public sealed class TenantMemberAdminTests
{
    private readonly string _dbName = $"tenant-member-admin-{Guid.NewGuid():N}";

    private TenantMemberAdmin CreateService()
        => new(
            new InMemoryPortalDbContextFactory(_dbName),
            new StubSnowflakeIdGenerator(),
            new PasswordHasher<PortalUser>(),
            Options.Create(new QlxAuthOptions()));

    private async Task<long> SeedMemberAsync(TenantMemberRole role, long portalUserId = 1500)
    {
        var factory = new InMemoryPortalDbContextFactory(_dbName);
        await using var db = factory.CreateDbContext();
        var member = new TenantMember(tenantId: 42, portalUserId, role);
        db.Set<TenantMember>().Add(member);
        _ = await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return member.Id;
    }

    [Fact]
    public async Task ResendInviteAsyncPendingInviteReturnsPortalUserId()
    {
        long memberId = await SeedMemberAsync(TenantMemberRole.PendingInvite, portalUserId: 1500);
        TenantMemberAdmin service = CreateService();

        long portalUserId = await service.ResendInviteAsync(memberId, TestContext.Current.CancellationToken);

        Assert.Equal(1500, portalUserId);
    }

    [Theory]
    [InlineData(TenantMemberRole.OrdinaryMember)]
    [InlineData(TenantMemberRole.TenantAdmin)]
    public async Task ResendInviteAsyncNonPendingInviteThrows409(TenantMemberRole role)
    {
        long memberId = await SeedMemberAsync(role);
        TenantMemberAdmin service = CreateService();

        ConflictException ex = await Assert.ThrowsAsync<ConflictException>(
            () => service.ResendInviteAsync(memberId, TestContext.Current.CancellationToken));
        Assert.Equal(PortalConstants.ErrorCodes.MemberNotPendingInvite, ex.ErrorCode);
    }

    [Fact]
    public async Task ResendInviteAsyncMemberMissingThrows404()
    {
        TenantMemberAdmin service = CreateService();

        _ = await Assert.ThrowsAsync<NotFoundException>(
            () => service.ResendInviteAsync(999999, TestContext.Current.CancellationToken));
    }

    // ============ 邀请（新账号需显式分配门户用户 Id，否则成员外键指向不存在的用户）============

    [Fact]
    public async Task InviteMemberAsyncNewAccountLinksMemberToCreatedPortalUser()
    {
        TenantMemberAdmin service = CreateService();

        (long TenantMemberId, long PortalUserId)? result = await service.InviteMemberAsync(
            42, "13800000002", "冒烟成员", 10, TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        await using var db = new InMemoryPortalDbContextFactory(_dbName).CreateDbContext();
        PortalUser user = await db.Set<PortalUser>().SingleAsync(
            u => u.LoginAccount == "13800000002", TestContext.Current.CancellationToken);
        TenantMember member = await db.Set<TenantMember>().SingleAsync(
            m => m.Id == result.Value.TenantMemberId, TestContext.Current.CancellationToken);
        Assert.Equal(user.Id, result.Value.PortalUserId);
        Assert.Equal(user.Id, member.PortalUserId);
        Assert.Equal(TenantMemberRole.PendingInvite, member.Role);
    }
}
