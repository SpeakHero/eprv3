/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.1.0 beta
 * 创建时间：2026-10-23
 * 修改时间：2026-10-25 11:00:00 +08:00 中国标准时间 星期日
 * 描述：Bas（基础信息中心）模块装配入口：仅承担 Manifest 声明职责（阶段三新增主数据权限点/菜单/
 *      资质扫描与缓存清理任务/审批完成事件订阅）；DI 注册由宿主直接调用 AddQlxBas 完成。
 */

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Qlx.Abstractions.Constants;
using Qlx.Abstractions.Jobs;
using Qlx.Abstractions.Modules;
using Qlx.Modules.Bas.Jobs;

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
            // 主数据域（阶段三）：供应商
            new PermissionDefinition("bas.supplier.menu", "供应商管理", PermissionKind.Menu),
            new PermissionDefinition("bas.supplier.action", "供应商维护操作", PermissionKind.Action),
            // 主数据域：客户
            new PermissionDefinition("bas.customer.menu", "客户管理", PermissionKind.Menu),
            new PermissionDefinition("bas.customer.action", "客户维护操作", PermissionKind.Action),
            // 主数据域：人员（含敏感字段明文查看，D5）
            new PermissionDefinition("bas.person.menu", "人员主数据管理", PermissionKind.Menu),
            new PermissionDefinition("bas.person.action", "人员主数据维护操作", PermissionKind.Action),
            new PermissionDefinition("bas.person.field.plaintext", "人员敏感字段明文查看", PermissionKind.Field),
            // 主数据域：商品分类
            new PermissionDefinition("bas.category.menu", "商品分类管理", PermissionKind.Menu),
            new PermissionDefinition("bas.category.action", "商品分类维护操作", PermissionKind.Action),
            // 主数据域：计量单位
            new PermissionDefinition("bas.unit.menu", "计量单位管理", PermissionKind.Menu),
            new PermissionDefinition("bas.unit.action", "计量单位维护操作", PermissionKind.Action),
            // 主数据域：数据字典
            new PermissionDefinition("bas.dict.menu", "数据字典管理", PermissionKind.Menu),
            new PermissionDefinition("bas.dict.action", "数据字典维护操作", PermissionKind.Action),
            // 主数据域：编码规则
            new PermissionDefinition("bas.coderule.menu", "编码规则查看", PermissionKind.Menu),
            new PermissionDefinition("bas.coderule.action", "编码规则启停维护", PermissionKind.Action),
            // 主数据域：变更日志（查询复用审计库）
            new PermissionDefinition("bas.changelog.menu", "变更日志查询", PermissionKind.Menu),
        ],
        // 租户自管理域菜单（R-3/L-13）：路由与前端 /manage/* 约定一致，前端按 requiredPermissionCode 裁剪
        Menus =
        [
            new MenuDefinition("bas.admin.org", "组织机构", "/manage/organizations", "OfficeBuilding", null, 10, "bas.admin.org.menu"),
            new MenuDefinition("bas.admin.member", "成员管理", "/manage/members", "User", null, 20, "bas.admin.member.menu"),
            new MenuDefinition("bas.admin.role", "角色权限", "/manage/roles", "Lock", null, 30, "bas.admin.role.menu"),
            // 主数据域菜单（阶段三，排序号在既有 10/20/30 之后递增）
            new MenuDefinition("bas.category", "商品分类", "/manage/master/categories", "Grid", null, 40, "bas.category.menu"),
            new MenuDefinition("bas.unit", "计量单位", "/manage/master/units", "ScaleToOriginal", null, 50, "bas.unit.menu"),
            new MenuDefinition("bas.supplier", "供应商管理", "/manage/master/suppliers", "Shop", null, 60, "bas.supplier.menu"),
            new MenuDefinition("bas.customer", "客户管理", "/manage/master/customers", "UserFilled", null, 70, "bas.customer.menu"),
            new MenuDefinition("bas.person", "人员主数据", "/manage/master/persons", "Avatar", null, 80, "bas.person.menu"),
            new MenuDefinition("bas.dict", "数据字典", "/manage/master/dictionaries", "Notebook", null, 90, "bas.dict.menu"),
            new MenuDefinition("bas.coderule", "编码规则", "/manage/master/code-rules", "Stamp", null, 100, "bas.coderule.menu"),
            new MenuDefinition("bas.changelog", "变更日志", "/manage/changelogs", "Document", null, 110, "bas.changelog.menu"),
        ],
        Jobs =
        [
            // 供应商资质到期扫描（Task 5 补齐扫描逻辑，TaskCode 与调度不变）
            new JobDefinition(BasJobCodes.SupplierCertExpiry, "供应商资质到期扫描", "0 0 2 * * ? *",
                typeof(SupplierCertExpiryJobHandler).FullName!),
            // Bas 缓存清理（C-6：按当前租户基 bas 前缀清理）
            new JobDefinition(BasJobCodes.CacheCleanup, "Bas 缓存清理", "0 30 3 * * ? *",
                typeof(BasCacheCleanupJobHandler).FullName!),
        ],
        ServiceJobs = [],
        SubscribedEvents = ["Qlx.Abstractions.Approvals.ApprovalCompletedEvent"],
    };

    /// <inheritdoc />
    /// <remarks>空实现：Bas 的 DI 注册由宿主直接调用 <c>AddQlxBas</c> 完成，避免二次注册。</remarks>
    public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        // 空实现：Bas 的 DI 注册由宿主 Program.cs 直接调用 AddQlxBas 完成。
    }
}
