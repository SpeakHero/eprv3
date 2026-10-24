/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-23 23:30:00 +08:00 中国标准时间 星期四
 * 修改时间：2026-10-23 23:30:00 +08:00 中国标准时间 星期四
 * 描述：ITenantMemberRoleQuery 默认实现——BasTenantDbContext 下
 *       bas_member_roles → RoleId join bas_roles，过滤软删角色并强制 TenantId。
 *
 * 注：BasTenantDbContext 为 scoped 工厂直接注册（未用 AddDbContextFactory），
 * 因此本服务直接注入 BasTenantDbContext 实例即可；ITenantContext 会为当前请求自动注入正确 tenantId。
 */

using Microsoft.EntityFrameworkCore;
using Qlx.Abstractions.Queries;
using Qlx.Modules.Bas.Domain;

namespace Qlx.Modules.Bas.Services;

/// <summary>
/// <see cref="ITenantMemberRoleQuery"/> 实现。
/// <para>强制 <c>TenantId == tenantId</c> 过滤，禁止跨租户数据泄漏。</para>
/// <para>EF 走 <c>AsNoTracking().Select()</c> 投影，无 Include / 无导航属性；排序置于 Select 前且针对实体列。</para>
/// </summary>
internal sealed class BasTenantMemberRoleQuery(
    BasTenantDbContext db) : ITenantMemberRoleQuery
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<TenantMemberRoleItem>> ListMemberRolesAsync(
        long tenantMemberId, long tenantId, CancellationToken cancellationToken)
    {
        var rows = await (
            from mr in db.Set<MemberRole>().AsNoTracking()
            where mr.TenantMemberId == tenantMemberId && mr.TenantId == tenantId
            join r in db.Set<Role>().AsNoTracking()
                on mr.RoleId equals r.Id
            where r.TenantId == tenantId && !r.IsDeleted
            orderby r.Name, r.Id
            select new TenantMemberRoleItem(r.Id, r.Code, r.Name, r.IsBuiltin))
            .ToListAsync(cancellationToken);

        return rows;
    }
}
