/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-21
 * 描述：跨模块租户成员写入契约实现——覆盖邀请/启停/移除/重置密码/组织归属。
 */

using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Qlx.Abstractions.Exceptions;
using Qlx.Abstractions.Tenancy;
using Qlx.BuildingBlocks.Ids;
using Qlx.Modules.Portal.Auth;
using Qlx.Modules.Portal.Domain.Identity;

namespace Qlx.Modules.Portal.Services;

/// <summary>
/// <see cref="ITenantMemberAdmin"/> 实现。操作 Portal 库 por_tenant_members / por_users /
/// por_user_password_histories；重置密码走 PasswordPolicyValidator + IPasswordHasher + 历史禁重。
/// </summary>
internal sealed class TenantMemberAdmin(
    IDbContextFactory<PortalDbContext> dbFactory,
    ISnowflakeIdGenerator idGen,
    IPasswordHasher<PortalUser> hasher,
    IOptions<QlxAuthOptions> authOptions) : ITenantMemberAdmin
{
    public async Task<(long TenantMemberId, long PortalUserId)?> InviteMemberAsync(
        long tenantId, string account, string displayName, long orgId, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        // 已是租户成员 → 拒绝
        bool exists = await (
            from m in db.Set<TenantMember>()
            join u in db.Set<PortalUser>() on m.PortalUserId equals u.Id
            where m.TenantId == tenantId && u.LoginAccount == account
            select m.Id).AnyAsync(ct);
        if (exists) return null;

        var user = await db.Set<PortalUser>().FirstOrDefaultAsync(u => u.LoginAccount == account, ct);

        long portalUserId;
        if (user is null)
        {
            user = new PortalUser(account, displayName);
            // 显式分配雪花 Id：TenantMember.PortalUserId 为外键，未赋值时 EF 会先插入成员行，
            // 与外键约束冲突（FK_por_tenant_members_por_users_PortalUserId）
            portalUserId = idGen.NextId();
            user.AssignId(portalUserId);
            db.Set<PortalUser>().Add(user);
        }
        else
        {
            portalUserId = user.Id;
        }

        var member = new TenantMember(tenantId, portalUserId, TenantMemberRole.PendingInvite)
        {
            DisplayNameInTenant = displayName,
            OrgId = orgId,
        };
        member.AssignId(idGen.NextId());
        db.Set<TenantMember>().Add(member);

        await db.SaveChangesAsync(ct);
        return (member.Id, portalUserId);
    }

    public async Task ToggleMemberStatusAsync(long tenantMemberId, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var member = await db.Set<TenantMember>().FindAsync([tenantMemberId], ct) ?? throw new NotFoundException(PortalConstants.ResourceNames.TenantMember, $"{tenantMemberId}");
        if (member.Role == TenantMemberRole.TenantAdmin)
            throw new ConflictException("企业管理员不可在租户内停用", PortalConstants.ErrorCodes.TenantAdminRoleProtected);

        if (member.Status == MemberStatus.Enabled) member.Disable();
        else member.Enable();
        await db.SaveChangesAsync(ct);
    }

    public async Task<long> ResendInviteAsync(long tenantMemberId, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var member = await db.Set<TenantMember>().FindAsync([tenantMemberId], ct) ?? throw new NotFoundException(PortalConstants.ResourceNames.TenantMember, $"{tenantMemberId}");
        if (member.Role != TenantMemberRole.PendingInvite)
            throw new ConflictException("仅待激活成员可重发邀请", PortalConstants.ErrorCodes.MemberNotPendingInvite);

        // 无可重置的实体字段（JoinAt 待接受时才写入），邀请状态即 Role=PendingInvite；通知由调用方经 IMessageService 发送
        return member.PortalUserId;
    }

    public async Task RemoveMemberFromTenantAsync(long tenantMemberId, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var member = await db.Set<TenantMember>().FindAsync([tenantMemberId], ct) ?? throw new NotFoundException(PortalConstants.ResourceNames.TenantMember, $"{tenantMemberId}");
        if (member.Role == TenantMemberRole.TenantAdmin)
            throw new ConflictException("企业管理员不可从租户移除", PortalConstants.ErrorCodes.TenantAdminRoleProtected);

        db.Set<TenantMember>().Remove(member);
        await db.SaveChangesAsync(ct);
    }

    public async Task ResetMemberPasswordAsync(
        long tenantMemberId, string newPassword, CancellationToken ct)
    {
        PasswordPolicyValidator.ValidateComplexity(newPassword, authOptions.Value);

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var member = await db.Set<TenantMember>().FindAsync([tenantMemberId], ct) ?? throw new NotFoundException(PortalConstants.ResourceNames.TenantMember, $"{tenantMemberId}");

        var user = await db.Set<PortalUser>().FindAsync([member.PortalUserId], ct) ?? throw new NotFoundException(PortalConstants.ResourceNames.PortalUserEntity, $"{member.PortalUserId}");

        if (member.Status == MemberStatus.Disabled)
            throw new ConflictException("成员已停用，请先启用后再重置密码", PortalConstants.ErrorCodes.MemberDisabled);

        // 历史禁重
        var histories = await db.Set<PortalUserPasswordHistory>()
            .Where(h => h.PortalUserId == user.Id)
            .OrderByDescending(h => h.CreatedAt)
            .Take(authOptions.Value.PasswordNeverRepeatLastN)
            .ToListAsync(ct);
        PasswordPolicyValidator.ValidateNotReused(newPassword, user, histories, hasher, authOptions.Value.PasswordNeverRepeatLastN);

        // 写新哈希
        var hash = hasher.HashPassword(user, newPassword);
        user.SetPasswordHash(hash);
        user.BumpTokenVersion();

        // 写历史（构造函数接收 id + portalUserId + passwordHash）
        db.Set<PortalUserPasswordHistory>().Add(new PortalUserPasswordHistory(idGen.NextId(), user.Id, hash));

        await db.SaveChangesAsync(ct);
    }

    public async Task UpdateMemberOrgAsync(long tenantMemberId, long? orgId, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var member = await db.Set<TenantMember>().FindAsync([tenantMemberId], ct) ?? throw new NotFoundException(PortalConstants.ResourceNames.TenantMember, $"{tenantMemberId}");
        member.OrgId = orgId;
        await db.SaveChangesAsync(ct);
    }
}
