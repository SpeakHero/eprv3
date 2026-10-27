/*
 * 所属公司：四川淇利馨集团有限公司
 * 作者：SpeakHero（491960352@qq.com）
 * 版本：1.0.0 beta
 * 创建时间：2026-10-26
 * 修改时间：2026-10-26 20:00:00 +08:00 中国标准时间 星期一
 * 描述：StkTenantDbContext 的 DbSet 集合（商品档案 / 价格变更日志 / 审批挂起请求 / 库存台账 / 批次库存 /
 *       库存流水 / 期初建账 / 成本调整）
 */

using Microsoft.EntityFrameworkCore;

using Qlx.Modules.Stk.Domain.Inventory;
using Qlx.Modules.Stk.Domain.Item;
using Qlx.Modules.Stk.Domain.Numbering;

namespace Qlx.Modules.Stk;

public sealed partial class StkTenantDbContext
{
    // ======= Item 域（商品档案 / 价格变更日志）=======
    public DbSet<Item> Items => Set<Item>();

    public DbSet<ItemPriceLog> PriceLogs => Set<ItemPriceLog>();

    // ======= Numbering 域（审批挂起请求）=======
    public DbSet<StkApprovalRequest> ApprovalRequests => Set<StkApprovalRequest>();

    // ======= Inventory 域（台账余额 / 批次库存 / 库存流水）=======
    public DbSet<StockBalance> StockBalances => Set<StockBalance>();

    public DbSet<StockBatch> StockBatches => Set<StockBatch>();

    public DbSet<StockTxn> StockTxns => Set<StockTxn>();

    // ======= Inventory 域（期初建账 / 成本调整）=======
    public DbSet<OpeningBatch> OpeningBatches => Set<OpeningBatch>();

    public DbSet<OpeningItem> OpeningItems => Set<OpeningItem>();

    public DbSet<CostAdjust> CostAdjusts => Set<CostAdjust>();

    public DbSet<CostAdjustLine> CostAdjustLines => Set<CostAdjustLine>();
}
