/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-26 14:00:00 +08:00 中国标准时间 星期一
 * 描述：Stk（商品/库存）模块装配入口——Manifest 声明六菜单 + 权限点（含成本字段权限）+ 缓存清理任务
 */

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using Qlx.Abstractions.Constants;
using Qlx.Abstractions.Jobs;
using Qlx.Abstractions.Modules;
using Qlx.Modules.Stk.Jobs;

namespace Qlx.Modules.Stk;

/// <summary>
/// Stk 模块（商品管理）。宿主经程序集扫描发现本类型并装配 Manifest；
/// DI 注册由宿主 Program.cs 先于 <c>AddQlxModules</c> 直接调用 <see cref="StkServiceCollectionExtensions.AddQlxStk"/> 完成。
/// </summary>
public sealed class StkModule : IModule
{
    /// <inheritdoc />
    public ModuleManifest Manifest { get; } = new()
    {
        ModuleCode = QlxModuleCodes.Stk,
        DisplayName = "商品管理",
        TablePrefix = QlxModuleCodes.TablePrefix(QlxModuleCodes.Stk),
        Permissions =
        [
            new PermissionDefinition("stk.item.menu", "商品档案查看", PermissionKind.Menu),
            new PermissionDefinition("stk.item.action", "商品档案维护", PermissionKind.Action),
            new PermissionDefinition("stk.opening.menu", "期初建账查看", PermissionKind.Menu),
            new PermissionDefinition("stk.opening.action", "期初建账维护", PermissionKind.Action),
            new PermissionDefinition("stk.ledger.menu", "库存台账查看", PermissionKind.Menu),
            new PermissionDefinition("stk.txn.menu", "出入库流水查看", PermissionKind.Menu),
            new PermissionDefinition("stk.costadjust.menu", "成本调整查看", PermissionKind.Menu),
            new PermissionDefinition("stk.costadjust.action", "成本调整维护", PermissionKind.Action),
            new PermissionDefinition("stk.batch.menu", "批次库存查看", PermissionKind.Menu),
            new PermissionDefinition("stk.cost.field", "成本字段查看", PermissionKind.Field),
        ],
        Menus =
        [
            new MenuDefinition("stk.item", "商品档案", "/manage/stk/items", "Goods", null, 100, "stk.item.menu"),
            new MenuDefinition("stk.opening", "期初建账", "/manage/stk/openings", "Money", null, 110, "stk.opening.menu"),
            new MenuDefinition("stk.ledger", "库存台账", "/manage/stk/ledger", "Coin", null, 120, "stk.ledger.menu"),
            new MenuDefinition("stk.txn", "出入库流水", "/manage/stk/txns", "Tickets", null, 130, "stk.txn.menu"),
            new MenuDefinition("stk.costadjust", "成本调整", "/manage/stk/cost-adjust", "EditPen", null, 140, "stk.costadjust.menu"),
            new MenuDefinition("stk.batch", "批次库存", "/manage/stk/batches", "Box", null, 150, "stk.batch.menu"),
        ],
        Jobs =
        [
            new JobDefinition(StkJobCodes.CacheCleanup, "Stk 缓存清理", "0 30 3 * * ? *",
                typeof(StkCacheCleanupJobHandler).FullName!),
        ],
        ServiceJobs = [],
        SubscribedEvents = ["Qlx.Abstractions.Approvals.ApprovalCompletedEvent"],
    };

    /// <inheritdoc />
    public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        // 空实现：Stk 的 DI 注册由宿主 Program.cs 直接调用 AddQlxStk 完成。
    }
}
