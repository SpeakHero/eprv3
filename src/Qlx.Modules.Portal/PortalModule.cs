/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-23 22:30:00 +08:00 中国标准时间 星期四
 * 修改时间：2026-10-23 22:30:00 +08:00 中国标准时间 星期四
 * 描述：Portal（门户管理）模块装配入口：清单声明 + 跨模块 TopNavigationGroups 提供
 *      PortalDbContext / 身份体系 / OpenIddict 的注册由宿主直接调用 AddQlxPortal 完成，
 *      本模块类仅承担 Manifest 声明职责，ConfigureServices 为空实现
 */

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Qlx.Abstractions.Constants;
using Qlx.Abstractions.Modules;

namespace Qlx.Modules.Portal;

/// <summary>
/// Portal 模块（门户管理中心）。宿主经程序集扫描发现本类型并装配；
/// PortalDbContext / 身份体系 / OpenIddict 的注册由宿主在 Program.cs 中
/// 先于 AddQlxModules 直接调用 <see cref="PortalServiceCollectionExtensions.AddQlxPortal"/> 完成，
/// 本类仅承担 <see cref="ModuleManifest"/> 声明职责。
/// 许可停用时（Portal 为必备模块，永不触发）：路由 404、菜单不下发、Manifest 任务暂停。
/// </summary>
public sealed class PortalModule : IModule
{
    /// <inheritdoc />
    public ModuleManifest Manifest { get; } = new()
    {
        ModuleCode = QlxModuleCodes.Portal,
        DisplayName = "门户管理中心",
        TablePrefix = QlxModuleCodes.PortalTablePrefix,
        Permissions =
        [
            // 租户管理域
            new PermissionDefinition("portal.tenant.menu", "租户管理", PermissionKind.Menu),
            new PermissionDefinition("portal.tenant.view", "租户列表与详情查看", PermissionKind.Action),
            new PermissionDefinition("portal.tenant.create", "租户新建", PermissionKind.Action),
            new PermissionDefinition("portal.tenant.edit", "租户基本信息修改", PermissionKind.Action),
            new PermissionDefinition("portal.tenant.enable", "租户启用/停用", PermissionKind.Action),
            new PermissionDefinition("portal.tenant.module", "租户模块许可变更", PermissionKind.Action),
            new PermissionDefinition("portal.tenant.database.upgrade", "租户独立库升级", PermissionKind.Action),
            // 用户与角色域
            new PermissionDefinition("portal.user.menu", "门户用户管理", PermissionKind.Menu),
            new PermissionDefinition("portal.user.view", "用户列表查看", PermissionKind.Action),
            new PermissionDefinition("portal.user.create", "用户新建", PermissionKind.Action),
            new PermissionDefinition("portal.user.edit", "用户资料修改", PermissionKind.Action),
            new PermissionDefinition("portal.user.disable", "用户启用/禁用", PermissionKind.Action),
            new PermissionDefinition("portal.user.role", "用户角色分配", PermissionKind.Action),
            // 角色管理域（细粒度动作点，前端 v-permission 按此校验）
            new PermissionDefinition("portal.role.menu", "角色管理", PermissionKind.Menu),
            new PermissionDefinition("portal.role.action", "角色分配", PermissionKind.Action),
            new PermissionDefinition("portal.role.create", "角色新建", PermissionKind.Action),
            new PermissionDefinition("portal.role.update", "角色编辑", PermissionKind.Action),
            new PermissionDefinition("portal.role.delete", "角色删除", PermissionKind.Action),
            new PermissionDefinition("portal.role.assign", "角色权限/成员分配", PermissionKind.Action),
            // 模块与许可域
            new PermissionDefinition("portal.module.menu", "模块与许可管理", PermissionKind.Menu),
            new PermissionDefinition("portal.module.view", "模块定义与版本查看", PermissionKind.Action),
            new PermissionDefinition("portal.module.license", "模块许可变更与分配", PermissionKind.Action),
            new PermissionDefinition("portal.module.upgrade", "模块版本升级", PermissionKind.Action),
            // 系统参数域
            new PermissionDefinition("portal.system.menu", "系统参数管理", PermissionKind.Menu),
            new PermissionDefinition("portal.system.view", "系统参数查看", PermissionKind.Action),
            new PermissionDefinition("portal.system.edit", "系统参数修改", PermissionKind.Action),
            // 审计与安全域
            new PermissionDefinition("portal.audit.menu", "审计与安全中心", PermissionKind.Menu),
            new PermissionDefinition("portal.audit.login", "登录日志查询", PermissionKind.Action),
            new PermissionDefinition("portal.audit.operation", "操作审计查询", PermissionKind.Action),
            new PermissionDefinition("portal.audit.isolation", "租户隔离巡检", PermissionKind.Action),
            new PermissionDefinition("portal.audit.session", "会话管理", PermissionKind.Action),
            // 门户自助域（身份级 Token 所有者）
            new PermissionDefinition("portal.self.menu", "门户自助", PermissionKind.Menu),
            new PermissionDefinition("portal.self.profile", "个人资料维护", PermissionKind.Action),
            new PermissionDefinition("portal.self.password", "密码修改", PermissionKind.Action),
        ],
        Menus = [],
        Jobs = [],
        ServiceJobs = [],
        SubscribedEvents = [],
        // ======= Portal 模块专有：跨模块顶级导航分组 =======
        // 前端侧边栏折叠展示：一个导航分组可聚合多个模块的菜单树；
        // 非 Portal 模块保持默认空数组即可，不要声明 TopNavigationGroups
        TopNavigationGroups =
        [
            new NavigationGroupDefinition("workbench", "工作台", "portal,bas", 0),
            new NavigationGroupDefinition("business", "业务管理", "pur,sal,wms,stk,ctr,prj,eqp", 1),
            new NavigationGroupDefinition("finance", "财务管理", "fin,inv", 2),
            new NavigationGroupDefinition("hr", "人员管理", "hr", 3),
            new NavigationGroupDefinition("support", "支撑服务中心", "support", 4),
            new NavigationGroupDefinition("system", "系统与门户", "portal", 99),
        ],
    };

    /// <inheritdoc />
    /// <remarks>
    /// PortalDbContext / OpenIddict / 身份体系 / 种子服务的注册由宿主 Program.cs
    /// 在 <see cref="PortalServiceCollectionExtensions.AddQlxPortal"/> 内完成，
    /// 必须先于 <c>AddQlxModules</c> 调用；此处为空实现，避免二次注册导致异常。
    /// </remarks>
    public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        // 空实现：Portal 的 DI 注册由宿主直接调用 AddQlxPortal 完成。
    }
}
