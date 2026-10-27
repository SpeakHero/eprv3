/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-26 10:00:00 +08:00 中国标准时间 星期一
 * 描述：Wms 模块端点授权规则映射（集中式授权，控制器零 [Authorize] 硬编码）
 */

using Qlx.Abstractions.Authorization;

namespace Qlx.Modules.Wms;

/// <summary>Wms 模块端点授权规则提供方。</summary>
public sealed class WmsEndpointPolicyMap : IEndpointPolicyMapProvider
{
    /// <inheritdoc />
    public IReadOnlyList<EndpointPolicyRule> GetRules() =>
    [
        // 仓库档案（/api/wms/warehouses）
        new() { Controller = "Warehouse", RequireTenantContext = true },
        new() { Controller = "Warehouse", Action = "GetList", PermissionCode = "wms.warehouse.menu" },
        new() { Controller = "Warehouse", Action = "GetDetail", PermissionCode = "wms.warehouse.menu" },
        new() { Controller = "Warehouse", Action = "GetZones", PermissionCode = "wms.warehouse.menu" },
        new() { Controller = "Warehouse", Action = "GetLocations", PermissionCode = "wms.warehouse.menu" },
        new() { Controller = "Warehouse", Action = "Create", PermissionCode = "wms.warehouse.action" },
        new() { Controller = "Warehouse", Action = "Update", PermissionCode = "wms.warehouse.action" },
        new() { Controller = "Warehouse", Action = "Toggle", PermissionCode = "wms.warehouse.action" },
        new() { Controller = "Warehouse", Action = "Delete", PermissionCode = "wms.warehouse.action" },
        new() { Controller = "Warehouse", Action = "CreateZone", PermissionCode = "wms.warehouse.action" },
        new() { Controller = "Warehouse", Action = "UpdateZone", PermissionCode = "wms.warehouse.action" },
        new() { Controller = "Warehouse", Action = "ToggleZone", PermissionCode = "wms.warehouse.action" },
        new() { Controller = "Warehouse", Action = "DeleteZone", PermissionCode = "wms.warehouse.action" },
        new() { Controller = "Warehouse", Action = "CreateLocation", PermissionCode = "wms.warehouse.action" },
        new() { Controller = "Warehouse", Action = "UpdateLocation", PermissionCode = "wms.warehouse.action" },
        new() { Controller = "Warehouse", Action = "ToggleLocation", PermissionCode = "wms.warehouse.action" },
        new() { Controller = "Warehouse", Action = "DeleteLocation", PermissionCode = "wms.warehouse.action" },

        // 入库作业（/api/wms/stock-in-tasks）
        new() { Controller = "StockIn", RequireTenantContext = true },
        new() { Controller = "StockIn", Action = "GetList", PermissionCode = "wms.inbound.menu" },
        new() { Controller = "StockIn", Action = "GetDetail", PermissionCode = "wms.inbound.menu" },
        new() { Controller = "StockIn", Action = "Create", PermissionCode = "wms.inbound.action" },
        new() { Controller = "StockIn", Action = "Start", PermissionCode = "wms.inbound.action" },
        new() { Controller = "StockIn", Action = "Complete", PermissionCode = "wms.inbound.action" },
        new() { Controller = "StockIn", Action = "Cancel", PermissionCode = "wms.inbound.action" },

        // 出库作业（/api/wms/stock-out-tasks）
        new() { Controller = "StockOut", RequireTenantContext = true },
        new() { Controller = "StockOut", Action = "GetList", PermissionCode = "wms.outbound.menu" },
        new() { Controller = "StockOut", Action = "GetDetail", PermissionCode = "wms.outbound.menu" },
        new() { Controller = "StockOut", Action = "Create", PermissionCode = "wms.outbound.action" },
        new() { Controller = "StockOut", Action = "Pick", PermissionCode = "wms.outbound.action" },
        new() { Controller = "StockOut", Action = "Check", PermissionCode = "wms.outbound.action" },
        new() { Controller = "StockOut", Action = "Complete", PermissionCode = "wms.outbound.action" },
        new() { Controller = "StockOut", Action = "Cancel", PermissionCode = "wms.outbound.action" },

        // 物资调拨（/api/wms/transfers）
        new() { Controller = "Transfer", RequireTenantContext = true },
        new() { Controller = "Transfer", Action = "GetList", PermissionCode = "wms.transfer.menu" },
        new() { Controller = "Transfer", Action = "GetInTransit", PermissionCode = "wms.transfer.menu" },
        new() { Controller = "Transfer", Action = "GetDetail", PermissionCode = "wms.transfer.menu" },
        new() { Controller = "Transfer", Action = "Create", PermissionCode = "wms.transfer.action" },
        new() { Controller = "Transfer", Action = "Ship", PermissionCode = "wms.transfer.action" },
        new() { Controller = "Transfer", Action = "Receive", PermissionCode = "wms.transfer.action" },
        new() { Controller = "Transfer", Action = "Void", PermissionCode = "wms.transfer.action" },

        // 盘点计划（/api/wms/stock-take-plans）
        new() { Controller = "StockTakePlan", RequireTenantContext = true },
        new() { Controller = "StockTakePlan", Action = "GetList", PermissionCode = "wms.stocktake.menu" },
        new() { Controller = "StockTakePlan", Action = "Create", PermissionCode = "wms.stocktake.action" },
        new() { Controller = "StockTakePlan", Action = "Generate", PermissionCode = "wms.stocktake.action" },

        // 盘点单（/api/wms/stock-takes）
        new() { Controller = "StockTake", RequireTenantContext = true },
        new() { Controller = "StockTake", Action = "GetList", PermissionCode = "wms.stocktake.menu" },
        new() { Controller = "StockTake", Action = "GetDetail", PermissionCode = "wms.stocktake.menu" },
        new() { Controller = "StockTake", Action = "Start", PermissionCode = "wms.stocktake.action" },
        new() { Controller = "StockTake", Action = "SaveCounts", PermissionCode = "wms.stocktake.action" },
        new() { Controller = "StockTake", Action = "Finish", PermissionCode = "wms.stocktake.action" },
        new() { Controller = "StockTake", Action = "Cancel", PermissionCode = "wms.stocktake.action" },

        // 盘盈盘亏（/api/wms/inventory-adjusts）
        new() { Controller = "InventoryAdjust", RequireTenantContext = true },
        new() { Controller = "InventoryAdjust", Action = "GetList", PermissionCode = "wms.adjust.menu" },
        new() { Controller = "InventoryAdjust", Action = "GetDetail", PermissionCode = "wms.adjust.menu" },
        new() { Controller = "InventoryAdjust", Action = "Create", PermissionCode = "wms.adjust.action" },
        new() { Controller = "InventoryAdjust", Action = "Submit", PermissionCode = "wms.adjust.action" },

        // 库存预警（/api/wms/inventory-alerts）
        new() { Controller = "InventoryAlert", RequireTenantContext = true },
        new() { Controller = "InventoryAlert", Action = "GetList", PermissionCode = "wms.alert.menu" },
        new() { Controller = "InventoryAlert", Action = "Handle", PermissionCode = "wms.alert.action" },
        new() { Controller = "InventoryAlert", Action = "Ignore", PermissionCode = "wms.alert.action" },

        // 库存命令 Outbox（/api/wms/stock-commands）
        new() { Controller = "StockCommand", RequireTenantContext = true },
        new() { Controller = "StockCommand", Action = "Retry", PermissionCode = "wms.command.action" },
    ];
}
