/*
 * 所属公司：四川淇利馨集团有限公司
 * 描述：待激活成员首登激活器（X-6 整改）——密码校验通过即视为接受邀请，
 *       将 PendingInvite 成员转为普通成员并逐条留痕 MEMBER_ACTIVATE 审计。
 *       独立成类以便单元测试（PortalAuthenticationService 构造依赖过重）。
 */

using Microsoft.EntityFrameworkCore;
using Qlx.Abstractions.Audit;
using Qlx.Abstractions.Constants;
using Qlx.BuildingBlocks.Ids;
using Qlx.Modules.Portal.Domain.Identity;

namespace Qlx.Modules.Portal.Services;

/// <summary>
/// 待激活成员首登激活器。返回激活条数（0 = 无待激活成员，快速路径）。
/// </summary>
public interface IPendingInviteActivator
{
    /// <summary>激活该用户全部 PendingInvite（且本地状态启用）的成员关系。</summary>
    Task<int> ActivateAsync(PortalUser user, CancellationToken ct);
}

/// <summary>基于 PortalDbContext 的默认实现（Scoped，与登录请求同生命周期）。</summary>
public sealed class PendingInviteActivator(
    PortalDbContext db,
    IAuditService audit,
    ISnowflakeIdGenerator snowflake) : IPendingInviteActivator
{
    /// <inheritdoc />
    public async Task<int> ActivateAsync(PortalUser user, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(user);

        var pendingMemberships = await db.TenantMembers
            .Where(m => m.PortalUserId == user.Id
                && m.Role == TenantMemberRole.PendingInvite
                && m.Status == MemberStatus.Enabled)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        if (pendingMemberships.Count == 0)
        {
            return 0;
        }

        foreach (var member in pendingMemberships)
        {
            member.Activate();
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        // 状态迁移审计：逐成员留痕 MEMBER_ACTIVATE（租户维度）
        foreach (var member in pendingMemberships)
        {
            await audit.CaptureOperationAsync(new OperationLogEntry(
                snowflake.NextId(), QlxAuditConstants.PortalModuleCode, PortalConstants.AuditOperations.MemberActivate,
                $"成员 {user.LoginAccount} 首次登录激活（接受企业邀请）",
                user.LoginAccount, DateTimeOffset.UtcNow, member.TenantId,
                QlxAuditConstants.TargetPortalUser, user.Id), ct).ConfigureAwait(false);
        }

        return pendingMemberships.Count;
    }
}
