/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-26 10:00:00 +08:00 中国标准时间 星期一
 * 描述：WmsTenantDbContext 的 DbSet 集合（2A：仓库 / 库区 / 库位；2B：审批挂起 / 库存命令 Outbox）
 */

using Microsoft.EntityFrameworkCore;

using Qlx.Modules.Wms.Domain.Adjust;
using Qlx.Modules.Wms.Domain.Alert;
using Qlx.Modules.Wms.Domain.Inbound;
using Qlx.Modules.Wms.Domain.Numbering;
using Qlx.Modules.Wms.Domain.Outbound;
using Qlx.Modules.Wms.Domain.Outbox;
using Qlx.Modules.Wms.Domain.StockTake;
using Qlx.Modules.Wms.Domain.Transfer;
using Qlx.Modules.Wms.Domain.Warehouse;

namespace Qlx.Modules.Wms;

public sealed partial class WmsTenantDbContext
{
    /// <summary>仓库（wms_warehouses）。</summary>
    public DbSet<Warehouse> Warehouses => Set<Warehouse>();

    /// <summary>库区（wms_zones）。</summary>
    public DbSet<WarehouseZone> Zones => Set<WarehouseZone>();

    /// <summary>库位（wms_locations）。</summary>
    public DbSet<WarehouseLocation> Locations => Set<WarehouseLocation>();

    /// <summary>审批挂起请求（wms_approval_requests）。</summary>
    public DbSet<WmsApprovalRequest> ApprovalRequests => Set<WmsApprovalRequest>();

    /// <summary>库存命令 Outbox（wms_stock_command_outbox）。</summary>
    public DbSet<WmsStockCommandOutbox> StockCommandOutbox => Set<WmsStockCommandOutbox>();

    /// <summary>入库作业单（wms_stock_in_tasks）。</summary>
    public DbSet<StockInTask> StockInTasks => Set<StockInTask>();

    /// <summary>入库作业明细（wms_stock_in_task_lines）。</summary>
    public DbSet<StockInTaskLine> StockInTaskLines => Set<StockInTaskLine>();

    /// <summary>出库作业单（wms_stock_out_tasks）。</summary>
    public DbSet<StockOutTask> StockOutTasks => Set<StockOutTask>();

    /// <summary>出库作业明细（wms_stock_out_task_lines）。</summary>
    public DbSet<StockOutTaskLine> StockOutTaskLines => Set<StockOutTaskLine>();

    /// <summary>调拨单（wms_stock_transfers）。</summary>
    public DbSet<StockTransfer> StockTransfers => Set<StockTransfer>();

    /// <summary>调拨明细（wms_stock_transfer_lines）。</summary>
    public DbSet<StockTransferLine> StockTransferLines => Set<StockTransferLine>();

    /// <summary>在途库存（wms_in_transit_stocks）。</summary>
    public DbSet<InTransitStock> InTransitStocks => Set<InTransitStock>();

    /// <summary>盘点计划（wms_stock_take_plans）。</summary>
    public DbSet<StockTakePlan> StockTakePlans => Set<StockTakePlan>();

    /// <summary>盘点单（wms_stock_takes）。</summary>
    public DbSet<StockTake> StockTakes => Set<StockTake>();

    /// <summary>盘点明细（wms_stock_take_lines）。</summary>
    public DbSet<StockTakeLine> StockTakeLines => Set<StockTakeLine>();

    /// <summary>盘盈盘亏单（wms_inventory_adjust_docs）。</summary>
    public DbSet<InventoryAdjustDoc> InventoryAdjustDocs => Set<InventoryAdjustDoc>();

    /// <summary>盘盈盘亏明细（wms_inventory_adjust_lines）。</summary>
    public DbSet<InventoryAdjustLine> InventoryAdjustLines => Set<InventoryAdjustLine>();

    /// <summary>库存预警（wms_inventory_alerts）。</summary>
    public DbSet<InventoryAlert> InventoryAlerts => Set<InventoryAlert>();
}
