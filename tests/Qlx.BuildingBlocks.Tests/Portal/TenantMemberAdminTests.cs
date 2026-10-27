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

    // ============ X-6：待激活成员激活链路（重置密码放开 409 + 首登激活 + 状态迁移审计）============

    /// <summary>种子：门户用户（含初始密码哈希）+ 指定角色的成员记录。</summary>
    private async Task<(long MemberId, long PortalUserId)> SeedMemberWithUserAsync(
        TenantMemberRole role, string loginAccount, bool disabled = false)
    {
        var factory = new InMemoryPortalDbContextFactory(_dbName);
        await using var db = factory.CreateDbContext();
        var hasher = new PasswordHasher<PortalUser>();
        var user = new PortalUser(loginAccount, "演示成员");
        user.SetPasswordHash(hasher.HashPassword(user, "Old@Passw0rd123"));
        db.Set<PortalUser>().Add(user);
        var member = new TenantMember(42, user.Id, role);
        if (disabled)
        {
            member.Disable();
        }
        db.Set<TenantMember>().Add(member);
        _ = await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return (member.Id, user.Id);
    }

    [Fact]
    public async Task ResetMemberPasswordAsyncPendingInviteSucceeds()
    {
        // X-6 整改：管理员重置密码即发放初始密码，PendingInvite 不再 409
        var (memberId, portalUserId) = await SeedMemberWithUserAsync(TenantMemberRole.PendingInvite, "13800000010");
        TenantMemberAdmin service = CreateService();

        await service.ResetMemberPasswordAsync(memberId, "Qlx@2026Reset!", TestContext.Current.CancellationToken);

        await using var db = new InMemoryPortalDbContextFactory(_dbName).CreateDbContext();
        PortalUser user = await db.Set<PortalUser>().SingleAsync(
            u => u.Id == portalUserId, TestContext.Current.CancellationToken);
        var hasher = new PasswordHasher<PortalUser>();
        Assert.Equal(
            PasswordVerificationResult.Success,
            hasher.VerifyHashedPassword(user, user.PasswordHash!, "Qlx@2026Reset!"));
    }

    [Fact]
    public async Task ResetMemberPasswordAsyncDisabledMemberStillThrows409()
    {
        var (memberId, _) = await SeedMemberWithUserAsync(
            TenantMemberRole.OrdinaryMember, "13800000011", disabled: true);
        TenantMemberAdmin service = CreateService();

        ConflictException ex = await Assert.ThrowsAsync<ConflictException>(
            () => service.ResetMemberPasswordAsync(memberId, "Qlx@2026Reset!", TestContext.Current.CancellationToken));
        Assert.Equal(PortalConstants.ErrorCodes.MemberDisabled, ex.ErrorCode);
    }

    [Fact]
    public async Task PendingInviteActivatorFirstLoginActivatesAndAudits()
    {
        // 编排：两个 PendingInvite(Enabled) + 一个 OrdinaryMember + 一个 PendingInvite(Disabled)
        var factory = new InMemoryPortalDbContextFactory(_dbName);
        PortalUser user;
        await using (var seedDb = factory.CreateDbContext())
        {
            user = new PortalUser("13800000012", "激活成员");
            seedDb.Set<PortalUser>().Add(user);
            seedDb.Set<TenantMember>().Add(new TenantMember(42, user.Id, TenantMemberRole.PendingInvite));
            seedDb.Set<TenantMember>().Add(new TenantMember(43, user.Id, TenantMemberRole.PendingInvite));
            seedDb.Set<TenantMember>().Add(new TenantMember(44, user.Id, TenantMemberRole.OrdinaryMember));
            var disabledPending = new TenantMember(45, user.Id, TenantMemberRole.PendingInvite);
            disabledPending.Disable();
            seedDb.Set<TenantMember>().Add(disabledPending);
            _ = await seedDb.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var audit = new InMemoryAuditService();
        int activated;
        await using (var db = factory.CreateDbContext())
        {
            var activator = new PendingInviteActivator(db, audit, new StubSnowflakeIdGenerator());
            var trackedUser = await db.Set<PortalUser>().SingleAsync(
                u => u.Id == user.Id, TestContext.Current.CancellationToken);
            activated = await activator.ActivateAsync(trackedUser, TestContext.Current.CancellationToken);
        }

        Assert.Equal(2, activated);

        await using var verifyDb = factory.CreateDbContext();
        var memberships = await verifyDb.Set<TenantMember>()
            .Where(m => m.PortalUserId == user.Id)
            .ToListAsync(TestContext.Current.CancellationToken);
        Assert.Equal(2, memberships.Count(m => m.Role == TenantMemberRole.PendingInvite == false && m.JoinAt != null));
        Assert.Equal(TenantMemberRole.OrdinaryMember, memberships.Single(m => m.TenantId == 42).Role);
        Assert.Equal(TenantMemberRole.OrdinaryMember, memberships.Single(m => m.TenantId == 43).Role);
        // 已是普通成员与停用成员不受影响
        Assert.Null(memberships.Single(m => m.TenantId == 44).JoinAt);
        Assert.Equal(TenantMemberRole.PendingInvite, memberships.Single(m => m.TenantId == 45).Role);

        // 状态迁移审计：两条 MEMBER_ACTIVATE，按租户维度留痕
        var operations = audit.GetCapturedOperations();
        Assert.Equal(2, operations.Count(o => o.Operation == PortalConstants.AuditOperations.MemberActivate));
        Assert.Equal([42L, 43L], operations.Select(o => o.TenantId).OrderBy(x => x));
    }

    [Fact]
    public async Task PendingInviteActivatorNoPendingMembershipsReturnsZero()
    {
        // 用户仅含普通成员关系 → 无待激活成员，直接返回 0，不产生审计
        var factory = new InMemoryPortalDbContextFactory(_dbName);
        PortalUser user;
        await using (var seedDb = factory.CreateDbContext())
        {
            user = new PortalUser("13800000013", "无待激活");
            seedDb.Set<PortalUser>().Add(user);
            seedDb.Set<TenantMember>().Add(new TenantMember(46, user.Id, TenantMemberRole.OrdinaryMember));
            _ = await seedDb.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var audit = new InMemoryAuditService();
        int activated;
        await using (var db = factory.CreateDbContext())
        {
            var activator = new PendingInviteActivator(db, audit, new StubSnowflakeIdGenerator());
            var trackedUser = await db.Set<PortalUser>().SingleAsync(
                u => u.Id == user.Id, TestContext.Current.CancellationToken);
            activated = await activator.ActivateAsync(trackedUser, TestContext.Current.CancellationToken);
        }

        Assert.Equal(0, activated);
        Assert.Empty(audit.GetCapturedOperations());
    }
}
