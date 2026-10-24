/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-22 20:40:00 +08:00 中国标准时间 星期四
 * 描述：PortalDbContext 的 DbSet 集合（按聚合拆分管理，主文件不承载集合声明）
 */

using Microsoft.EntityFrameworkCore;
using Qlx.Modules.Portal.Domain.Configuration;
using Qlx.Modules.Portal.Domain.Identity;
using Qlx.Modules.Portal.Domain.Tenancy;

namespace Qlx.Modules.Portal;

public sealed partial class PortalDbContext
{
    /// <summary>租户注册集合。</summary>
    public DbSet<Tenant> Tenants { get; private set; } = default!;

    /// <summary>租户数据库连接信息集合。</summary>
    public DbSet<TenantDatabase> TenantDatabases { get; private set; } = default!;

    /// <summary>租户模块许可集合。</summary>
    public DbSet<TenantModule> TenantModules { get; private set; } = default!;

    /// <summary>模块许可变更记录集合。</summary>
    public DbSet<LicenseRecord> LicenseRecords { get; private set; } = default!;

    /// <summary>系统模块定义集合。</summary>
    public DbSet<ModuleDefinition> ModuleDefinitions { get; private set; } = default!;

    /// <summary>权限点定义集合。</summary>
    public DbSet<PermissionDefinition> PermissionDefinitions { get; private set; } = default!;

    /// <summary>模块菜单定义集合（v1.6 新增，ModuleManifest 同步来源）。</summary>
    public DbSet<ModuleMenu> ModuleMenus { get; private set; } = default!;

    /// <summary>顶级导航分组集合（v1.6 新增，Portal Manifest 同步来源）。</summary>
    public DbSet<NavigationGroup> NavigationGroups { get; private set; } = default!;

    /// <summary>门户角色集合（v1.6 新增）。</summary>
    public DbSet<PortalRole> PortalRoles { get; private set; } = default!;

    /// <summary>门户角色-权限分配集合（v1.6 新增）。</summary>
    public DbSet<PortalRolePermission> PortalRolePermissions { get; private set; } = default!;

    /// <summary>门户用户-角色关联集合（v1.6 新增）。</summary>
    public DbSet<PortalUserRole> PortalUserRoles { get; private set; } = default!;

    /// <summary>门户用户集合（全局登录身份）。</summary>
    public DbSet<PortalUser> PortalUsers { get; private set; } = default!;

    /// <summary>门户用户密码历史集合（用于禁止与最近 N 次相同）。</summary>
    public DbSet<PortalUserPasswordHistory> PortalUserPasswordHistories { get; private set; } = default!;

    /// <summary>租户成员集合（一人多租户成员关系）。</summary>
    public DbSet<TenantMember> TenantMembers { get; private set; } = default!;

    /// <summary>门户系统参数集合（业务运行参数在线维护）。</summary>
    public DbSet<SystemParameter> SystemParameters { get; private set; } = default!;

    /// <summary>租户升级任务集合（共享库 → 独立库）。</summary>
    public DbSet<TenantUpgradeJob> TenantUpgradeJobs { get; private set; } = default!;

    /// <summary>租户升级表级复制断点集合。</summary>
    public DbSet<TenantUpgradeCheckpoint> TenantUpgradeCheckpoints { get; private set; } = default!;

    /// <summary>租户隔离巡检记录集合。</summary>
    public DbSet<TenantIsolationAuditRecord> TenantIsolationAuditRecords { get; private set; } = default!;
}
