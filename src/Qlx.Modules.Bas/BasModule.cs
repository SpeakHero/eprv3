/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-23
 * 描述：Bas（基础信息中心）模块装配入口：仅承担 Manifest 声明职责；
 *      BasTenantDbContext / 仓储 / 服务的注册由宿主直接调用 AddQlxBas 完成，ConfigureServices 为空实现。
 */

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Qlx.Abstractions.Constants;
using Qlx.Abstractions.Modules;

namespace Qlx.Modules.Bas;

/// <summary>
/// Bas 模块（基础信息中心）。宿主经程序集扫描发现本类型并装配 Manifest；
/// DI 注册由宿主 Program.cs 先于 <c>AddQlxModules</c> 直接调用 <see cref="BasServiceCollectionExtensions.AddQlxBas"/> 完成，
/// 本类仅承担 <see cref="ModuleManifest"/> 声明职责（与 PortalModule 同约定）。
/// </summary>
public sealed class BasModule : IModule
{
    /// <inheritdoc />
    public ModuleManifest Manifest { get; } = new()
    {
        ModuleCode = QlxModuleCodes.Bas,
        DisplayName = "基础信息中心",
        TablePrefix = QlxModuleCodes.BasTablePrefix,
        Permissions =
        [
            // 租户自管理域：组织机构
            new PermissionDefinition("bas.admin.org.menu", "组织机构管理", PermissionKind.Menu),
            new PermissionDefinition("bas.admin.org.action", "组织机构维护操作", PermissionKind.Action),
            // 租户自管理域：成员管理
            new PermissionDefinition("bas.admin.member.menu", "成员管理", PermissionKind.Menu),
            new PermissionDefinition("bas.admin.member.action", "成员维护操作", PermissionKind.Action),
            // 租户自管理域：角色权限
            new PermissionDefinition("bas.admin.role.menu", "角色权限管理", PermissionKind.Menu),
            new PermissionDefinition("bas.admin.role.action", "角色权限维护操作", PermissionKind.Action),
        ],
        Menus = [],
        Jobs = [],
        ServiceJobs = [],
        SubscribedEvents = [],
    };

    /// <inheritdoc />
    /// <remarks>空实现：Bas 的 DI 注册由宿主直接调用 <c>AddQlxBas</c> 完成，避免二次注册。</remarks>
    public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        // 空实现：Bas 的 DI 注册由宿主 Program.cs 直接调用 AddQlxBas 完成。
    }
}
