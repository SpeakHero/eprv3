/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-22 20:40:00 +08:00 中国标准时间 星期四
 * 描述：BasTenantDbContext 的 DbSet 集合
 */

using Microsoft.EntityFrameworkCore;
using Qlx.Modules.Bas.Domain;

namespace Qlx.Modules.Bas;

public sealed partial class BasTenantDbContext
{
    public DbSet<Organization> Organizations => Set<Organization>();

    public DbSet<Position> Positions => Set<Position>();

    public DbSet<Role> Roles => Set<Role>();

    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();

    public DbSet<MemberRole> MemberRoles => Set<MemberRole>();
}
