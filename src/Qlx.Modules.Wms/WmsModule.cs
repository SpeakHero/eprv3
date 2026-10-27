/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-26 10:00:00 +08:00 中国标准时间 星期一
 * 描述：Wms（物流管理）模块装配入口——Manifest 声明仓库域权限点/菜单/缓存清理任务
 */

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using Qlx.Abstractions.Constants;
using Qlx.Abstractions.Jobs;
using Qlx.Abstractions.Modules;
using Qlx.Modules.Wms.Jobs;

namespace Qlx.Modules.Wms;

/// <summary>
/// Wms 模块（物流管理）。宿主经程序集扫描发现本类型并装配 Manifest；
/// DI 注册由宿主 Program.cs 先于 <c>AddQlxModules</c> 直接调用 <see cref="WmsServiceCollectionExtensions.AddQlxWms"/> 完成。
/// </summary>
public sealed class WmsModule : IModule
{
    /// <inheritdoc />
    public ModuleManifest Manifest { get; } = new()
    {
        ModuleCode = QlxModuleCodes.Wms,
        DisplayName = "物流管理",
        TablePrefix = QlxModuleCodes.TablePrefix(QlxModuleCodes.Wms),
        Permissions =
        [
            new PermissionDefinition("wms.warehouse.menu", "仓库档案查看", PermissionKind.Menu),
            new PermissionDefinition("wms.warehouse.action", "仓库档案维护", PermissionKind.Action),
            new PermissionDefinition("wms.inbound.menu", "入库作业查看", PermissionKind.Menu),
            new PermissionDefinition("wms.inbound.action", "入库作业维护", PermissionKind.Action),
            new PermissionDefinition("wms.outbound.menu", "出库作业查看", PermissionKind.Menu),
            new PermissionDefinition("wms.outbound.action", "出库作业维护", PermissionKind.Action),
            new PermissionDefinition("wms.transfer.menu", "物资调拨查看", PermissionKind.Menu),
            new PermissionDefinition("wms.transfer.action", "物资调拨维护", PermissionKind.Action),
            new PermissionDefinition("wms.stocktake.menu", "库存盘点查看", PermissionKind.Menu),
            new PermissionDefinition("wms.stocktake.action", "库存盘点维护", PermissionKind.Action),
            new PermissionDefinition("wms.adjust.menu", "盘盈盘亏查看", PermissionKind.Menu),
            new PermissionDefinition("wms.adjust.action", "盘盈盘亏维护", PermissionKind.Action),
            new PermissionDefinition("wms.alert.menu", "库存预警查看", PermissionKind.Menu),
            new PermissionDefinition("wms.alert.action", "库存预警处理", PermissionKind.Action),
            new PermissionDefinition("wms.command.action", "库存命令重推", PermissionKind.Action),
        ],
        Menus =
        [
            new MenuDefinition("wms.warehouse", "仓库档案", "/manage/wms/warehouses", "OfficeBuilding", null, 100, "wms.warehouse.menu"),
            new MenuDefinition("wms.inbound", "入库作业", "/manage/wms/inbound", "Download", null, 110, "wms.inbound.menu"),
            new MenuDefinition("wms.outbound", "出库作业", "/manage/wms/outbound", "Upload", null, 120, "wms.outbound.menu"),
            new MenuDefinition("wms.transfer", "物资调拨", "/manage/wms/transfers", "Switch", null, 130, "wms.transfer.menu"),
            new MenuDefinition("wms.stocktake", "库存盘点", "/manage/wms/stocktakes", "Document", null, 140, "wms.stocktake.menu"),
            new MenuDefinition("wms.adjust", "盘盈盘亏", "/manage/wms/adjustments", "EditPen", null, 150, "wms.adjust.menu"),
            new MenuDefinition("wms.alert", "库存预警", "/manage/wms/alerts", "Warning", null, 160, "wms.alert.menu"),
        ],
        Jobs =
        [
            new JobDefinition(WmsJobCodes.CacheCleanup, "Wms 缓存清理", "0 40 3 * * ? *",
                typeof(WmsCacheCleanupJobHandler).FullName!),
            new JobDefinition(WmsJobCodes.StockCommandDispatch, "Wms 库存命令分发", "0 */1 * * * ? *",
                typeof(WmsStockCommandDispatchJobHandler).FullName!),
            new JobDefinition(WmsJobCodes.AlertScan, "Wms 库存预警扫描", "0 0 2 * * ? *",
                typeof(InventoryAlertScanJobHandler).FullName!),
        ],
        ServiceJobs = [],
        SubscribedEvents = ["Qlx.Abstractions.Approvals.ApprovalCompletedEvent"],
    };

    /// <inheritdoc />
    public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        // 空实现：Wms 的 DI 注册由宿主 Program.cs 直接调用 AddQlxWms 完成。
    }
}
