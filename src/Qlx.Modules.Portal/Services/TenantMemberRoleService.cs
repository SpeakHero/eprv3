/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-23 23:30:00 +08:00 中国标准时间 星期四
 * 修改时间：2026-10-23 23:30:00 +08:00 中国标准时间 星期四
 * 描述：选择角色步骤业务服务——身份级 Token 下按企业懒加载当前账号可进入的角色
 *
 * 关键约束：
 * 1. Phase 1 身份级 Token 无租户上下文，BasTenantDbContext 会以 tenantId=0 建库；
 *    因此查询前必须临时写入本次目标租户 Id，查询结束后在 finally 中恢复原值。
 * 2. 成员关系、成员状态、租户状态一律以 Portal 库为准，后端复核，不信任前端参数。
 * 3. 禁止跨库 Join：Bas 角色经 ITenantMemberRoleQuery 契约读取。
 */

using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenIddict.Abstractions;
using Qlx.Abstractions.Constants;
using Qlx.Abstractions.Queries;
using Qlx.Abstractions.Tenancy;
using Qlx.BuildingBlocks.Ids;
using Qlx.Modules.Portal.Auth;
using Qlx.Modules.Portal.Domain.Identity;
using Qlx.Modules.Portal.Domain.Tenancy;

namespace Qlx.Modules.Portal.Services;

/// <summary>选择角色步骤业务契约。</summary>
public interface ITenantMemberRoleService
{
    /// <summary>
    /// 查询当前身份用户在指定企业下可进入的角色列表。
    /// </summary>
    /// <param name="principal">身份级 Token 主体（必须为 identity token）。</param>
    /// <param name="tenantIdRaw">混淆后的租户 Id。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>角色选项列表；无权访问 / 租户不可用 / Token 非法时返回 <c>null</c>。</returns>
    Task<IReadOnlyList<MemberRoleOptionItem>?> GetMemberRolesAsync(
        ClaimsPrincipal principal, string tenantIdRaw, CancellationToken cancellationToken);
}

/// <summary><see cref="ITenantMemberRoleService"/> 实现。</summary>
public sealed partial class TenantMemberRoleService(
    PortalDbContext db,
    IIdObfuscator obfuscator,
    ITenantContext tenantContext,
    IServiceProvider services,
    ILogger<TenantMemberRoleService> logger) : ITenantMemberRoleService
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<MemberRoleOptionItem>?> GetMemberRolesAsync(
        ClaimsPrincipal principal, string tenantIdRaw, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(principal);

        // 选择角色步骤仅接受身份级 Token（此时尚无租户上下文）
        if (principal.FindFirst(QlxAuthConstants.Claims.TokenKind)?.Value != QlxAuthConstants.TokenKinds.Identity)
        {
            return null;
        }

        var subject = principal.FindFirst(OpenIddictConstants.Claims.Subject)?.Value;
        if (subject is null
            || !obfuscator.TryDecode(subject, out var userId)
            || !obfuscator.TryDecode(tenantIdRaw, out var tenantId)
            || tenantId <= 0)
        {
            return null;
        }

        // 成员关系与租户状态校验（Portal 库为准）
        var member = await db.TenantMembers
            .AsNoTracking()
            .Include(m => m.Tenant)
            .SingleOrDefaultAsync(m => m.PortalUserId == userId && m.TenantId == tenantId, cancellationToken)
            .ConfigureAwait(false);

        if (member is null
            || member.Status != MemberStatus.Enabled
            || member.Role == TenantMemberRole.PendingInvite
            || member.Tenant.Status is not (TenantStatus.Active or TenantStatus.GracePeriod))
        {
            return null;
        }

        // 企业管理员走零计算旁路，前端不展示角色步骤
        if (member.Role == TenantMemberRole.TenantAdmin)
        {
            return [];
        }

        // 必须先写入租户上下文再解析查询服务：BasTenantDbContext 为 scoped 工厂，
        // 在构造查询服务时即按当前 ITenantContext.TenantId 决定目标租户库。
        var originalTenantId = tenantContext.TenantId;
        try
        {
            tenantContext.SetTenantId(tenantId);

            var query = services.GetService<ITenantMemberRoleQuery>();
            if (query is null)
            {
                // Bas 模块未注册实现：返回空列表，前端按「无角色」直接进入
                LogRoleQueryNotRegistered(member.Id, tenantId);
                return [];
            }

            var roles = await query.ListMemberRolesAsync(member.Id, tenantId, cancellationToken)
                .ConfigureAwait(false);

            return roles
                .Select(role => new MemberRoleOptionItem(
                    obfuscator.Encode(role.RoleId), role.RoleCode, role.RoleName, role.IsBuiltin))
                .ToList();
        }
        finally
        {
            // 恢复请求原始上下文（身份级请求通常为 null）
            tenantContext.SetTenantId(originalTenantId ?? 0);
        }
    }

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "未注册 ITenantMemberRoleQuery 实现，选择角色步骤返回空列表。memberId={MemberId}, tenantId={TenantId}")]
    private partial void LogRoleQueryNotRegistered(long memberId, long tenantId);
}
