/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-23
 * 修改时间：2026-10-23
 * 描述：IPortalRolePermissionQuery 默认实现——por_user_roles join por_role_permissions。
 *       Portal 认证服务身份级 Token 签发阶段消费，聚合门户角色 → 权限码；
 *       PortalAdminFlag 用户走零计算通道，不进入本查询。
 */

using Microsoft.EntityFrameworkCore;
using Qlx.Abstractions.Queries;
using Qlx.Modules.Portal.Domain.Tenancy;

namespace Qlx.Modules.Portal.Services;

/// <summary>
/// <see cref="IPortalRolePermissionQuery"/> 实现——从门户库聚合指定用户的角色 → 权限码集合。
/// <para>
/// 禁止 Include / 导航属性按需访问：全部用 <c>AsNoTracking().Select()</c> 投影。
/// Join 链：por_user_roles.UserId → por_role_permissions.RoleId → PermissionCode。
/// </para>
/// </summary>
internal sealed class PortalRolePermissionQuery(
    IDbContextFactory<PortalDbContext> dbFactory) : IPortalRolePermissionQuery
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> ListUserPermissionCodesAsync(
        long portalUserId, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

        var codes = await (
            from ur in db.Set<PortalUserRole>().AsNoTracking()
            where ur.UserId == portalUserId
            join rp in db.Set<PortalRolePermission>().AsNoTracking()
                on ur.RoleId equals rp.RoleId
            select rp.PermissionCode)
            .Distinct()
            .OrderBy(c => c)
            .ToListAsync(cancellationToken);

        return codes;
    }
}
