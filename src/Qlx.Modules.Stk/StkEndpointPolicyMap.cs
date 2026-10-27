/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-26 14:00:00 +08:00 中国标准时间 星期一
 * 描述：Stk 模块端点授权规则映射（集中式授权，控制器零 [Authorize] 硬编码）
 */

using Qlx.Abstractions.Authorization;

namespace Qlx.Modules.Stk;

/// <summary>Stk 模块端点授权规则提供方。</summary>
public sealed class StkEndpointPolicyMap : IEndpointPolicyMapProvider
{
    /// <inheritdoc />
    public IReadOnlyList<EndpointPolicyRule> GetRules() =>
    [
        // 商品档案（/api/stk/items）
        new() { Controller = "Item", RequireTenantContext = true },
        new() { Controller = "Item", Action = "GetList", PermissionCode = "stk.item.menu" },
        new() { Controller = "Item", Action = "GetDetail", PermissionCode = "stk.item.menu" },
        new() { Controller = "Item", Action = "GetPriceLogs", PermissionCode = "stk.item.menu" },
        new() { Controller = "Item", Action = "Create", PermissionCode = "stk.item.action" },
        new() { Controller = "Item", Action = "Update", PermissionCode = "stk.item.action" },
        new() { Controller = "Item", Action = "Toggle", PermissionCode = "stk.item.action" },
        new() { Controller = "Item", Action = "CreateDisableRequest", PermissionCode = "stk.item.action" },

        // 库存台账 / 出入库流水 / 批次库存（/api/stk/ledger、/api/stk/txns、/api/stk/batches，只读）
        new() { Controller = "Inventory", RequireTenantContext = true },
        new() { Controller = "Inventory", Action = "GetLedger", PermissionCode = "stk.ledger.menu" },
        new() { Controller = "Inventory", Action = "GetTxns", PermissionCode = "stk.txn.menu" },
        new() { Controller = "Inventory", Action = "GetBatches", PermissionCode = "stk.batch.menu" },

        // 期初建账（/api/stk/openings）
        new() { Controller = "Opening", RequireTenantContext = true },
        new() { Controller = "Opening", Action = "GetList", PermissionCode = "stk.opening.menu" },
        new() { Controller = "Opening", Action = "GetDetail", PermissionCode = "stk.opening.menu" },
        new() { Controller = "Opening", Action = "GetSummary", PermissionCode = "stk.opening.menu" },
        new() { Controller = "Opening", Action = "Create", PermissionCode = "stk.opening.action" },
        new() { Controller = "Opening", Action = "Update", PermissionCode = "stk.opening.action" },
        new() { Controller = "Opening", Action = "Delete", PermissionCode = "stk.opening.action" },
        new() { Controller = "Opening", Action = "Submit", PermissionCode = "stk.opening.action" },

        // 成本调整（/api/stk/cost-adjusts）
        new() { Controller = "CostAdjust", RequireTenantContext = true },
        new() { Controller = "CostAdjust", Action = "GetList", PermissionCode = "stk.costadjust.menu" },
        new() { Controller = "CostAdjust", Action = "GetDetail", PermissionCode = "stk.costadjust.menu" },
        new() { Controller = "CostAdjust", Action = "Create", PermissionCode = "stk.costadjust.action" },
        new() { Controller = "CostAdjust", Action = "Update", PermissionCode = "stk.costadjust.action" },
        new() { Controller = "CostAdjust", Action = "Delete", PermissionCode = "stk.costadjust.action" },
        new() { Controller = "CostAdjust", Action = "Submit", PermissionCode = "stk.costadjust.action" },
    ];
}
