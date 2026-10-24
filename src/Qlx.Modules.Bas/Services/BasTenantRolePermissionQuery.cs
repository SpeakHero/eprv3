/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-23
 * 修改时间：2026-10-23
 * 描述：ITenantRolePermissionQuery 默认实现——BasTenantDbContext 下
 *       bas_member_roles → RoleId join bas_role_permissions → PermissionCode。
 *       强制 TenantId 过滤，禁止跨租户查询（RolePermission + MemberRole 表均携带 TenantId 字段）。
 *
 * 注：BasTenantDbContext 为 scoped 工厂直接注册（未用 AddDbContextFactory），
 * 因此本服务直接注入 BasTenantDbContext 实例即可；ITenantContext 会为当前请求自动注入正确 tenantId。
 */

using Microsoft.EntityFrameworkCore;
using Qlx.Abstractions.Queries;
using Qlx.Modules.Bas.Domain;

namespace Qlx.Modules.Bas.Services;

/// <summary>
/// <see cref="ITenantRolePermissionQuery"/> 实现。
/// <para>
/// 所有查询强制 <c>TenantId == tenantId</c> 过滤（Role 表有 TenantId，RolePermission / MemberRole 也有 TenantId），
/// 禁止跨租户数据泄漏。
/// </para>
/// <para>EF 全部走 <c>AsNoTracking().Select()</c> 投影，无 Include / 无导航属性。</para>
/// </summary>
internal sealed class BasTenantRolePermissionQuery(
    BasTenantDbContext db) : ITenantRolePermissionQuery
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> ListMemberPermissionCodesAsync(
        long tenantMemberId, long tenantId, CancellationToken cancellationToken)
    {
        var codes = await (
            from mr in db.Set<MemberRole>().AsNoTracking()
            where mr.TenantMemberId == tenantMemberId && mr.TenantId == tenantId
            join rp in db.Set<RolePermission>().AsNoTracking()
                on mr.RoleId equals rp.RoleId
            where rp.TenantId == tenantId
            select rp.PermissionCode)
            .Distinct()
            .OrderBy(c => c)
            .ToListAsync(cancellationToken);

        return codes;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> ListRolePermissionCodesAsync(
        long roleId, long tenantId, CancellationToken cancellationToken)
    {
        var codes = await db.Set<RolePermission>()
            .AsNoTracking()
            .Where(rp => rp.RoleId == roleId && rp.TenantId == tenantId)
            .Select(rp => rp.PermissionCode)
            .Distinct()
            .OrderBy(c => c)
            .ToListAsync(cancellationToken);

        return codes;
    }
}
