/*
 * 所属公司：四川淇利馨集团有限公司
 * 描述：当前成员数据范围解析器（X-5 整改）——从租户级 Token claim 解析 member_id / member_role / member_role_id，
 *       按所选角色（未选则全角色并集取最宽）的 DefaultDataScope 产出过滤上下文；
 *       TenantAdmin 零计算旁路返回 null（不过滤）。
 */

using Microsoft.AspNetCore.Http;
using Qlx.Abstractions.Constants;
using Qlx.Abstractions.Queries;
using Qlx.BuildingBlocks.Ids;
using Qlx.Modules.Bas.Domain;
using Qlx.Modules.Bas.Repositories;

namespace Qlx.Modules.Bas.Services;

/// <summary>
/// 当前成员数据范围上下文。
/// </summary>
/// <param name="MemberId">当前成员 Id（por_tenant_members.id）。</param>
/// <param name="Scope">生效数据范围（Custom 已按 Dept 降级）。</param>
/// <param name="OrgId">当前成员主属组织 Id（Self/Dept/DeptAndChildren 过滤锚点）。</param>
internal sealed record MemberDataScopeContext(long MemberId, DataScope Scope, long? OrgId);

/// <summary>
/// 当前成员数据范围解析器。返回 null 表示不做数据范围过滤
/// （TenantAdmin 旁路 / 无成员上下文的门户模式请求）。
/// </summary>
internal interface ICurrentMemberDataScopeResolver
{
    /// <summary>解析当前请求成员的数据范围上下文。</summary>
    Task<MemberDataScopeContext?> ResolveAsync(CancellationToken ct);
}

/// <summary>
/// 基于 HttpContext claims 的解析实现（Scoped，与租户库仓储同生命周期）。
/// <para>解析顺序：TenantAdmin 旁路 → member_id 解码 → member_role_id（本次所选角色）→ 全角色最宽档。</para>
/// </summary>
internal sealed class HttpCurrentMemberDataScopeResolver(
    IHttpContextAccessor httpContextAccessor,
    IIdObfuscator obfuscator,
    IBasTenantRepository repo,
    ITenantMemberQuery memberQuery) : ICurrentMemberDataScopeResolver
{
    /// <inheritdoc />
    public async Task<MemberDataScopeContext?> ResolveAsync(CancellationToken ct)
    {
        var user = httpContextAccessor.HttpContext?.User;
        if (user?.Identity?.IsAuthenticated != true)
        {
            return null;
        }

        // TenantAdmin 零计算旁路：数据范围不过滤
        var memberRole = user.FindFirst(QlxClaimConstants.MemberRole)?.Value;
        if (string.Equals(memberRole, QlxMemberRoleValues.TenantAdmin, StringComparison.Ordinal))
        {
            return null;
        }

        var memberIdRaw = user.FindFirst(QlxClaimConstants.MemberId)?.Value;
        if (memberIdRaw is null || !obfuscator.TryDecode(memberIdRaw, out var memberId))
        {
            // 无成员上下文（如门户模式借用租户路由）：不过滤，保持既有行为
            return null;
        }

        // 本次进入所选角色优先；未选角色则取全角色并集
        IReadOnlyList<Role> roles;
        var memberRoleIdRaw = user.FindFirst(QlxClaimConstants.MemberRoleId)?.Value;
        if (memberRoleIdRaw is not null && obfuscator.TryDecode(memberRoleIdRaw, out var roleId))
        {
            var role = await repo.FindRoleAsync(roleId, ct);
            roles = role is null || role.IsDeleted ? [] : [role];
        }
        else
        {
            roles = await repo.ListMemberRolesAsync(memberId, ct);
        }

        if (roles.Count == 0)
        {
            // 普通成员无任何角色：保守降级为仅本人
            return new MemberDataScopeContext(memberId, DataScope.Self, await ResolveOrgIdAsync(memberId, ct));
        }

        // 多角色取最宽档（枚举值越小范围越宽：All=1 … Self=5）
        // 偏差标注：Custom（自定义组织集）所需的角色-组织映射表尚未就绪，按 Dept 降级
        var scope = roles.Min(r => r.DefaultDataScope);
        if (scope == DataScope.Custom)
        {
            scope = DataScope.Dept;
        }

        return new MemberDataScopeContext(memberId, scope, await ResolveOrgIdAsync(memberId, ct));
    }

    private async Task<long?> ResolveOrgIdAsync(long memberId, CancellationToken ct)
        => (await memberQuery.GetMemberDetailAsync(memberId, ct))?.OrgId;
}
