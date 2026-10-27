# 阶段三第二波（M-3.2）规格：05 商品 + 04 物流

> 所属阶段：阶段三（业务模块）｜波次：第二波｜日期：2026-10-26
> 依据：`docs/下一阶段工作准备与推进计划-20261024.md`（M-3.2）、`docs/模块需求/05-商品管理.md`、`docs/模块需求/04-物流管理.md`、`docs/阶段三第一波-模块01-批次实施记录-20261025.md`
> 强制规范：`.trae/rules/global-cache-layering-standard.md`（C-x/B-x/V-x/N-x/E-x）、`.trae/rules/global-csharp-standard.md`
> 任务清单：见同目录 `tasks.md`（27 项：2A Task 1~13 已完成，2B Task 14~27 待实施）
> 批次编排：2A（Wms 仓库最小切片 → Stk 全量，05 出口）→ 2B（Wms 作业全量，04 出口）

---

## 1. 背景与目标

模块 01 已出口（后端 1258 测试、前端 235 测试全绿）。第二波交付商品与库存本体（05）与仓储作业（04），出口硬指标是**库存台账铁律**：流水只增不改、移动加权平均、负库存拦截、台账只由流水驱动。

两模块为独立程序集、独立表前缀（`stk_` / `wms_`），跨模块一律经 `Qlx.Abstractions` 契约，禁止程序集互引与跨模块 Join。

## 2. 范围

### 2.1 2A（05 出口，含仓库前置）

- `Qlx.Modules.Wms` 骨架 + 仓库/库区/库位主数据 + `IWarehouseQueryService`（2A 最小切片）。
- `Qlx.Modules.Stk` 全量：商品档案、期初建账/过账、库存台账、出入库流水、成本调整、批次库存（后端 + 前端六页）。

### 2.2 2B（04 出口）

- 入库作业、出库作业、物资调拨（含在途台账）、库存盘点（含冻结）、盘盈盘亏、库存预警（后端 + 前端六页）。
- Wms 作业 → Stk 台账的 Outbox 命令通道。

### 2.3 不做（延后阶段四前）

商品报表（ABC/呆滞/周转）、商品影像上传、条码标签打印、配送管理、签收差异、在途独立页面（并入调拨 Tab）、拣货单/盘点表打印、采购/销售自动生成作业单、财务凭证生成（仅发事件）。

## 3. 架构决策

| # | 决策 |
|---|------|
| A-1 | 两模块仅引 `Qlx.Abstractions` + `Qlx.BuildingBlocks`；csproj 复制 Bas（net10.0 / FrameworkReference AspNetCore / EF SqlServer+Design / OpenIddict）。 |
| A-2 | DbContext 由 `AddQlxStk` / `AddQlxWms` 以工厂方式注册（解析 `IInterceptor` 列表），迁移计划 `DependsOn=[Bas]`。 |
| A-3 | 表名 = 前缀 + snake_case(DbSet 属性名)；DbSet 用复数命名（`Items→stk_items`）。 |
| A-4 | 跨模块只存 Id，不建跨模块外键、不做 Join；引用合法性经 Abstractions 查询接口校验。 |
| A-5 | 库存写入唯一通道 `IStockTxnCommandService`（Stk 实现），Wms 只消费；Wms 禁止访问 `stk_` 表。 |
| A-6 | Wms→Stk 命令走 Outbox：`wms_stock_command_outbox` 与作业状态同事务写入，分发任务异步驱动，Stk 幂等去重。 |
| A-7 | 库存流水只 INSERT：服务层无更新/删除路径 + 迁移内 SQL Server 触发器 `trg_stk_stock_txns_no_update_delete`。 |
| A-8 | 并发用 `long Version` 并发令牌（非 rowversion），事务内重算，冲突整单重试 3 次。 |
| A-9 | 台账/流水/批次/作业单/预警不缓存；商品档案与仓库档案读穿缓存（300s±15% 抖动 + NullMarker 30s），写后按前缀失效。 |
| A-10 | 审批只依赖 `IApprovalService`；结果经 public `IIntegrationEventConsumer<ApprovalCompletedEvent>` + 幂等 Applier 应用。 |
| A-11 | 前端挂 `/manage` 单布局；页面按 Manifest 菜单与 `requiredPermissionCode` 裁剪；枚举中文映射、树组装、导出 CSV 均在前端公共层。 |
| A-12 | 偏差登记：静态 `Map` 方法（不引入 Mapperly）、商品/仓库编码不走 `IEncodingGenerator`（商品走分类码 + 序号、仓库走 `WH-` 发号）、库区/库位编码为物理货位人工命名标识（非名称派生，保持前端录入）、期初号段 `SP-QC`。 |

## 4. Abstractions 契约

### 4.1 查询

```csharp
public sealed record WarehouseQueryRow(long Id, long? OrgId, string Code, string Name, int WhType, int Status);

public interface IWarehouseQueryService : IModuleQuery
{
    Task<IReadOnlyList<WarehouseQueryRow>> ListAllAsync(CancellationToken ct);
    Task<IReadOnlyList<WarehouseQueryRow>> ListByOrgAsync(long orgId, CancellationToken ct);
    Task<WarehouseQueryRow?> FindAsync(long id, CancellationToken ct);
    Task<IReadOnlyList<WarehouseQueryRow>> FindByIdsAsync(IReadOnlyList<long> ids, CancellationToken ct);
}

public sealed record StockBalanceRow(long ItemId, long WarehouseId, decimal? QtyOnhand, decimal? AvgCost,
    decimal? Amount, decimal QtyLocked, DateTimeOffset? LastTxnAt);

public sealed record StockBatchRow(long ItemId, long WarehouseId, string BatchNo, DateTimeOffset? ProduceDate,
    DateTimeOffset? ExpiryDate, decimal Qty, decimal AvgCost, decimal Amount);

public interface IStockQueryService : IModuleQuery
{
    Task<StockBalanceRow?> FindBalanceAsync(long itemId, long warehouseId, CancellationToken ct);
    Task<IReadOnlyList<StockBalanceRow>> ListBalancesByItemAsync(long itemId, CancellationToken ct);
    Task<IReadOnlyList<StockBalanceRow>> ListBalancesByWarehouseAsync(long warehouseId, CancellationToken ct);
    Task<IReadOnlyList<StockBatchRow>> ListBatchesAsync(long itemId, long warehouseId, CancellationToken ct); // 效期升序（FEFO 业务规则）
    Task<bool> HasStockInWarehouseAsync(long warehouseId, CancellationToken ct);
}
```

> 成本字段 `AvgCost/Amount` 为 `decimal?`，无 `stk.cost.field` 权限时由实现置 `null`（B-3-1 字段级过滤）。

### 4.2 库存命令

```csharp
public enum StockDirection { In = 1, Out = 2 }

public enum StockSourceType
{
    Opening = 1, PurchaseIn = 2, SaleReturn = 3, TransferIn = 4, Gain = 5,
    PurchaseReturn = 6, SaleOut = 7, TransferOut = 8, Loss = 9, Use = 10, CostAdjust = 11,
}

public sealed record PostStockTxnRequest(
    StockSourceType SourceType, string SourceDocNo, int SourceLineNo, long ItemId, long WarehouseId,
    string? BatchNo, StockDirection Direction, decimal Qty, decimal UnitCost,
    string? OperatorId, string IdempotencyKey, DateTimeOffset OccurredAt);

public sealed record StockPostResult(bool Success, long? TxnId, string? TxnNo,
    decimal BalanceQtyAfter, decimal BalanceAmountAfter, string? ErrorCode, decimal ShortageQty);

public interface IStockTxnCommandService
{
    Task<StockPostResult> PostAsync(PostStockTxnRequest request, CancellationToken ct);
    Task<IReadOnlyList<StockPostResult>> PostBatchAsync(IReadOnlyList<PostStockTxnRequest> requests, CancellationToken ct);
    Task AdjustLockedQtyAsync(long itemId, long warehouseId, decimal deltaQty, string reason, CancellationToken ct);
    Task<StockBalanceRow?> FindBalanceAsync(long itemId, long warehouseId, CancellationToken ct);
}
```

错误码：`STK_INSUFFICIENT`（负库存，附 `ShortageQty`）、`STK_BATCH_REQUIRED`、`STK_BATCH_NOT_FOUND`、`STK_DUPLICATE`（幂等命中，返回成功）。

### 4.3 事件

| 事件 | 发布方 | 载荷 |
|---|---|---|
| `StockChangedEvent` | Stk | ItemId/WarehouseId/BatchNo/Direction/Qty/BalanceQtyAfter |
| `OpeningPostedEvent` | Stk | OpeningBatchId/BatchNo/WarehouseId/TotalAmount |
| `CostAdjustedEvent` | Stk | CostAdjustId/AdjNo/TotalDiffAmount |
| `StockInCompletedEvent` | Wms | TaskId/TaskNo/WarehouseId/SourceDocNo |
| `StockOutCompletedEvent` | Wms | TaskId/TaskNo/WarehouseId/SourceDocNo |
| `TransferShippedEvent` | Wms | TransferId/TransferNo/FromWhId/ToWhId |
| `TransferReceivedEvent` | Wms | TransferId/TransferNo/ToWhId |
| `InventoryWarningRaisedEvent` | Wms | AlertId/AlertType/ItemId/WarehouseId |

## 5. 数据模型

### 5.1 Stk（`stk_`）

| 表 | 关键字段 / 约束 |
|---|---|
| `stk_items` | item_code(24) 租户内唯一；barcode(32) 过滤唯一（`barcode IS NOT NULL AND is_deleted=0`）；category_id/unit_id 仅存 Id；batch_enabled/expiry_enabled；tax_rate(5,4)；ref_cost_price/guide_price(18,4)；status；is_deleted |
| `stk_item_price_logs` | field(1 参考成本/2 指导价/3 税率/4 单位)/old_value/new_value/approved_by/created_at |
| `stk_stock_balances` | item_id+warehouse_id 唯一；qty_onhand(18,4)/avg_cost(18,4)/amount(18,2)/qty_locked(18,4)/last_txn_at/version(并发令牌) |
| `stk_stock_batches` | item_id+warehouse_id+batch_no 唯一；produce_date/expiry_date/qty/avg_cost/amount |
| `stk_stock_txns` | txn_no(32)；source_type/source_doc_no(32)/source_line_no；item_id/warehouse_id/batch_no；direction/qty/unit_cost/amount；balance_qty_after/balance_amount_after；operator_id/created_at；幂等唯一索引（source_type+source_doc_no+source_line_no+item_id+warehouse_id+batch_no+direction） |
| `stk_opening_batches` / `stk_opening_items` | batch_no(32)/warehouse_id/status(1 Draft/2 Pending/3 Posted)/posted_at；明细 qty/unit_cost/amount/batch_no |
| `stk_cost_adjusts` / `stk_cost_adjust_lines` | adj_no(32)/reason_code(1 价格差错/2 盘盈亏联动/3 其他)/total_diff_amount/status；明细 item_id/warehouse_id/old_avg_cost/new_avg_cost/diff_amount |
| `stk_approval_requests` | biz_type/biz_id/approval_instance_id（过滤唯一 `IS NOT NULL`）/payload_json/status |

### 5.2 Wms（`wms_`）

| 表 | 关键字段 / 约束 |
|---|---|
| `wms_warehouses` | warehouse_code(32) 唯一；wh_type(1 自有/2 租赁/3 临时)；org_id/address/keeper_id/status |
| `wms_zones` / `wms_locations` | zone_code；location_code 区内存唯一 + barcode |
| `wms_stock_in_tasks` / `_lines` | task_no(32)；source_type(1 采购/2 销售退货/3 调拨/4 盘盈)；source_doc_no 非空；warehouse_id；status(1 待作业/2 作业中/3 已完成/4 已取消)；明细 expect_qty/count_qty/location_id/batch_no/confirm_qty |
| `wms_stock_out_tasks` / `_lines` | source_type(1 销售/2 采购退货/3 调拨/4 盘亏/5 领用)；picker_id/checker_id/handover_to；明细 apply_qty/pick_qty/check_qty/batch_no/location_id |
| `wms_stock_transfers` / `_lines` / `wms_in_transit_stocks` | transfer_no；from_wh/to_wh 不同；from_org/to_org（跨公司判定）；in_transit_days；status(1 待审批/2 已生效/3 调出已发货/4 调入已收货/5 已作废)；在途表 item/batch/qty/shipped_at/expect_arrive/status |
| `wms_stock_take_plans` / `wms_stock_takes` / `_lines` | plan_no/take_no；scope_type(1 全仓/2 品类/3 库位)；round(1 初盘/2 复盘)；book_qty 快照/actual_qty/diff_qty/diff_reason |
| `wms_inventory_adjust_docs` / `_lines` | adj_no；take_id；type(1 盘盈/2 盘亏)；reason/total_amount/status |
| `wms_inventory_alerts` | alert_type(1 安全库存/2 积压/3 临期/4 在途超期)/item/warehouse/batch/threshold/actual_value/status(1 待处理/2 已处理/3 已忽略)/handled_by/handled_note |
| `wms_stock_command_outbox` | idempotency_key 唯一/state(待分发/已分发/失败)/payload_json/retry_count/next_retry_at |
| `wms_approval_requests` | 同 Stk 挂起请求结构 |

## 6. 台账算法（铁律）

1. 唯一写入口 `StockLedgerService`；任何 Controller 无写台账动作。
2. IN：`newQty=oldQty+q`；`newAmount=oldAmount+Round(q*unitCost,2)`；`avg=newQty==0?0:Round(newAmount/newQty,4)`。
3. OUT：`cost=当前avg`；`newAmount=oldAmount-Round(q*avg,2)`；`newQty=oldQty-q`；`newQty==0 → newAmount=0`，avg 保留；`q > qtyOnhand-qtyLocked` 阻断。
4. 精度：单价 4 位、金额 2 位、数量 4 位，`MidpointRounding.AwayFromZero`。
5. 批次：启用批次商品必填；入库累计、出库按指定批次扣减；批次与余额同事务。
6. 幂等：幂等键命中返回既有流水结果，不重复写。
7. 并发：`Version` 校验失败整单回滚重试 3 次，超限 409。
8. 流水不可变：SQL Server 触发器；红字冲正通过新流水。

## 7. 状态机

| 对象 | 允许流转 |
|---|---|
| Item | Enabled ⇄ Disabled（停用后禁止新增业务，可出库消化） |
| OpeningBatch | Draft → Pending → Posted；Pending → Draft（驳回） |
| CostAdjust | Draft → Pending → Approved/Rejected；仅 Draft 可改删 |
| StockInTask | 待作业 → 作业中 → 已完成；待作业/作业中 → 已取消 |
| StockOutTask | 待作业 → 拣货中 → 待复核 → 已完成；前置态可取消 |
| Transfer | 待审批 → 已生效 → 调出已发货 → 调入已收货；待审批/已生效 → 已作废 |
| StockTake | 待盘点 → 初盘中 → 初盘完成 → 复盘中 → 已完成 |
| InventoryAdjustDoc | Draft → Pending → Approved（驳回回 Draft） |
| InventoryAlert | 待处理 → 已处理/已忽略 |

非法流转统一 409 `STK_INVALID_STATE` / `WMS_INVALID_STATE`。

## 8. 审批矩阵

| definitionCode | bizType | 触发 | 节点 |
|---|---|---|---|
| `flow.stk.item.create` | `stk.item.create` | 商品新建 | 基础数据管理员 |
| `flow.stk.item.keyfield.change` | `stk.item.keyfield.change` | 税率/单位/批次属性变更 | 基础数据管理员 → 财务负责人 |
| `flow.stk.item.disable` | `stk.item.disable` | 有库存停用 | 商品负责人 → 集团运营负责人 |
| `flow.stk.opening.post` | `stk.opening.post` | 期初过账 | 仓库负责人 → 财务负责人 → 集团运营负责人 |
| `flow.stk.cost.adjust` | `stk.cost.adjust` | 调整额 ≤ 阈值 | 仓库/商品负责人 → 财务负责人 |
| `flow.stk.cost.adjust.large` | `stk.cost.adjust.large` | 调整额 > 阈值 | 上述 + 集团分管副总 |
| `flow.wms.outbound.large` | `wms.outbound.large` | 出库金额 > 阈值 | 仓库负责人 → 部门负责人 |
| `flow.wms.transfer.internal` | `wms.transfer` | 同公司调拨 | 调出/调入仓库负责人 |
| `flow.wms.transfer.cross` | `wms.transfer` | 跨公司调拨 | 调出 → 调入 → 集团运营 |
| `flow.wms.inventory.adjust` | `wms.inventory.adjust` | 盘盈盘亏 | 仓库负责人 → 财务负责人 |
| `flow.wms.warehouse.disable` | `wms.warehouse.disable` | 仓库停用 | 仓库负责人 → 集团运营负责人 |

全部节点 `ApprovalApproverTypes.Role`、`role=admin`；定义追加至 `SupportSeedService` 种子表（幂等）。

参数键（`por_parameters`，缺失用默认）：`stk.cost.adjust.threshold`=10000、`wms.outbound.large.threshold`=50000、`wms.alert.stagnant.days`=90、`wms.alert.expiry.days`=30、`wms.transfer.in_transit.days`=7、`wms.alert.safety.stock`。

## 9. 权限点 / 菜单

| 模块 | 2A（已落地） | 2B（新增） |
|---|---|---|
| Stk | 10 权限点 / 6 菜单（`stk.item`、`stk.opening`、`stk.ledger`、`stk.txn`、`stk.costadjust`、`stk.batch` + `stk.cost.field`） | 无新增 |
| Wms | 2 权限点 / 1 菜单（`wms.warehouse.menu`、`wms.warehouse.action`） | 13 权限点 / 6 菜单（见 §14.6），合计 15 权限点 / 7 菜单 |

菜单排序：仓库档案 100、入库作业 110、出库作业 120、物资调拨 130、库存盘点 140、盘盈盘亏 150、库存预警 160。
数据范围（仓管员仅本仓）依赖 `CurrentMemberDataScope`，随 2B 作业查询接入（Task 21 收口核对）。

## 10. 缓存矩阵

| 域 | 策略 |
|---|---|
| 商品档案列表/详情/编码校验 | `GetOrCreateAsync` + 300s±15% + NullMarker 30s；写后 `RemoveByPrefixAsync("tenant:{tid}:stk:")` |
| 仓库档案 + `IWarehouseQueryService` | 同上（前缀 `wms`） |
| 台账/流水/批次/作业/预警 | 不缓存 |
| 清理任务 | `StkCacheCleanupJobHandler`（`0 30 3 * * ? *`）、`WmsCacheCleanupJobHandler`（`0 40 3 * * ? *`），SCAN ≤500/批、幂等 |

## 11. 前端

- 路由（`/manage` 子路由）：2A `wms/warehouses` + `stk/{items,openings,ledger,txns,cost-adjust,batches}`；2B `wms/{inbound,outbound,transfers,stocktakes,adjustments,alerts}`。
- 公共层：`types/{stk,wms}.ts`、`api/{stk,wms}.ts`、`utils/{stk-enums,wms-enums,csv-export}.ts`；分类树/仓库树前端组装；枚举中文与 Tag 映射前端维护；Id 全为 string（Hashids）。
- 表单无编码输入框；成本字段按 `stk.cost.field` 显隐。

## 12. 测试与出口

- 后端：`Stk/{ItemTests,StockLedgerTests,OpeningCostAdjustTests,StkQueryTests}`、`Wms/{WarehouseTests,StockInOutTests,TransferTests,StockTakeTests,AlertScanTests}`；Sqlite 内存 Harness，同时建 Wms/Stk DbContext。
- 前端：`api/__tests__/{stk,wms}.spec.ts`、`utils/__tests__/{stk-enums,wms-enums,csv-export}.spec.ts`。
- 出口：构建 0 警告 0 错误；测试 ≥1258 且只增不减；架构测试全绿；`npm run build`/`npm test` 全绿；Host 冒烟 + 浏览器闭环；文档回写（资产台账、推进计划状态、批次实施记录）。

## 13. 风险与偏差

| # | 风险/偏差 | 应对 |
|---|---|---|
| R-1 | 两 DbContext 无法共享事务 | Outbox + 幂等键（A-6） |
| R-2 | 台账并发写 | Version 令牌 + 重试 |
| R-3 | 模块许可未授权导致 403 | 冒烟前门户授权（D-18） |
| R-4 | 期初/成本调整号段偏离详设 | 登记偏差，批次记录会签 |
| R-5 | 采购/销售未落地，作业单来源需手工录入 | 仅做单号存在性占位校验，第三波接入自动生成 |
| R-6 | 后台 Job 无 HTTP claims，`IStockQueryService` 的成本字段会被裁剪为 null，调拨在途成本快照丢失 | Wms 取成本一律走 `IStockTxnCommandService.FindBalanceAsync`（不裁剪，A-13） |
| R-7 | 采购单价未落地，入库无来源单价则无法按金额记台账 | 入库明细增 `unit_cost`，缺省取商品 `ref_cost_price`，仍空按 0（A-14 偏差登记） |
| R-8 | Outbox 失败终态无自动恢复手段 | `POST /api/wms/stock-commands/{id}/retry`（权限 `wms.command.action`）人工重推 |
| R-9 | 商品档案无安全库存字段，安全库存预警只能全局阈值 | 阈值参数 `wms.alert.safety.stock`（0 = 关闭），商品级阈值延后波次 |

## 14. 2B 详细规格（04 物流作业全量）

### 14.1 单据号段

沿用 `IDocumentNumberGenerator`（文本 = `{prefix}-{orgCode}-{period}-{serial}`，period 取 `DocumentNumberingOptions.PeriodFormat`= `yyyyMM`），前缀常量集中 `Services/WmsDocumentPrefixes.cs`：

| 单据 | 前缀 | 生成文本 |
|---|---|---|
| 入库作业单 | `WL-RK` | `WL-RK-{orgCode}-{yyyyMM}-{0001}` |
| 出库作业单 | `WL-CK` | 同上 |
| 调拨单 | `WL-DB` | 同上 |
| 盘点计划 | `WL-PDJH` | 同上 |
| 盘点单 | `WL-PD` | 同上 |
| 盘盈盘亏单 | `WL-PYK` | 同上 |

组织码取作业仓库归属组织（`IWarehouseQueryService.FindAsync` 的 `OrgId` → `IOrgQueryService.FindAsync` 的 `Code`，缺失回落 `SYS`）。

### 14.2 关键决策（追加）

| # | 决策 |
|---|---|
| A-13 | Wms 需要「可靠成本」一律调 `IStockTxnCommandService.FindBalanceAsync`（返回未裁剪原始行）；`IStockQueryService` 仅供展示类查询（受 `stk.cost.field` 裁剪）。 |
| A-14 | 入库明细 `unit_cost` 为入库成本来源：请求显式值 → 商品 `ref_cost_price` → 0；该字段登记为采购/销售未落地的临时口径（偏差）。 |
| A-15 | 冻结量只经 `AdjustLockedQtyAsync` 调整，Wms 不写 `stk_` 表；出库可用量校验仍由 Stk 单点执行（避免双源判断）。 |
| A-16 | 预警幂等键 = `(alert_type, item_id, warehouse_id, batch_no, alert_date)`，`batch_no` 空值归一为空串参与唯一索引。 |
| A-17 | Outbox 与作业状态同事务：`IStockCommandQueue.EnqueueAsync` 必须在 `ExecuteInTransactionAsync` 回调内、`SaveChangesAsync` 之前调用。 |

### 14.3 Outbox 协议（Wms → Stk）

- 表 `wms_stock_command_outbox`：`id`、`idempotency_key`（租户内唯一）、`payload_json`（`PostStockTxnRequest`）、`state`（1 待分发 / 2 已分发 / 3 失败）、`retry_count`、`next_retry_at`、`last_error`、`dispatched_at`。
- 幂等键格式：`wms:{biz}:{docNo}:{lineNo}`，`biz ∈ rk/ck/db-out/db-in/py`；同一作业行重复完成不会产生第二条命令。
- 分发 Job `wms.stock.command.dispatch`（cron `0 */1 * * * ?`）：
  1. 取 `state ∈ {1,3}` 且 `next_retry_at ≤ now`，按 `id` 升序，单批 ≤100；
  2. 逐条 `IStockTxnCommandService.PostAsync`（每条自带事务，Job 只更新 outbox 行）；
  3. `Success=true`（含 `STK_DUPLICATE` 幂等命中）→ `state=2`、`dispatched_at=now`、`next_retry_at=null`；
  4. `Success=false` → `retry_count+1`、`last_error=ErrorCode`、`next_retry_at=now + backoff(1,2,4,8,16 分钟)`；
  5. `retry_count ≥ 5` → `state=3` 且 `next_retry_at=null`（终态，仅人工重推可恢复）。
- 人工重推：`POST /api/wms/stock-commands/{id}/retry` → `state=1`、`retry_count=0`、`next_retry_at=now`；仅 `state=3` 可重推，重复重推 409 `WMS_INVALID_STATE`。

### 14.4 作业域实体与端点

**入库作业 `/api/wms/stock-in-tasks`**

| 动作 | 路由 | 规则 |
|---|---|---|
| 列表/详情 | `GET` / `GET {id}` | 状态、仓库、来源类型筛选；详情含明细 |
| 新建 | `POST` | `source_type ∈ 1..4`、`source_doc_no` 非空 ≤32（无单禁入 R1）、仓库启用、明细 ≥1 行 |
| 开始作业 | `POST {id}/start` | 1 待作业 → 2 作业中 |
| 完成 | `POST {id}/complete` | 2 作业中 → 3 已完成；每行 `confirm_qty ≤ count_qty`、批次商品 `batch_no` 必填、`location_id` 属同仓库且启用；同事务写 Outbox（`PurchaseIn 2 / SaleReturn 3 / TransferIn 4 / Gain 5`，`Direction=In`，`UnitCost` 取行 `unit_cost` → `ref_cost_price` → 0） |
| 取消 | `POST {id}/cancel` | 1/2 → 4 已取消 |

**出库作业 `/api/wms/stock-out-tasks`**

| 动作 | 路由 | 规则 |
|---|---|---|
| 列表/详情 | `GET` / `GET {id}` | 详情附 `fefoSuggestions`（`IStockQueryService.ListBatchesAsync` 效期升序，FEFO 业务规则） |
| 新建 | `POST` | `source_type ∈ 1..5`、`source_doc_no` 必填、明细 ≥1 行 |
| 拣货 | `POST {id}/pick` | 1 待作业 → 2 拣货中；`pick_qty ≤ apply_qty`，批次/库位按建议或手填 |
| 复核 | `POST {id}/check` | 2 拣货中 → 3 待复核；`check_qty ≠ pick_qty` → 409 `WMS_CHECK_MISMATCH`；`handover_to` 必填 |
| 完成 | `POST {id}/complete` | 3 待复核 → 4 已完成；先算 `total_amount = Σ(pick_qty × 当前均价)`（成本取 `IStockTxnCommandService.FindBalanceAsync`），超 `wms.outbound.large.threshold`（默认 50000）→ 转审批挂起（`wms.outbound.large`），审批通过后由 Applier 完成；否则同事务写 Outbox（`SaleOut 7 / PurchaseReturn 6 / TransferOut 8 / Loss 9 / Use 10`，`Direction=Out`，`UnitCost=0`） |
| 取消 | `POST {id}/cancel` | 1/2/3 → 5 已取消 |

**调拨与在途 `/api/wms/transfers`**

| 动作 | 路由 | 规则 |
|---|---|---|
| 列表/详情 | `GET` / `GET {id}` | 状态、仓库筛选 |
| 新建 | `POST` | `from_wh_id ≠ to_wh_id`；`from_org_id/to_org_id` 取仓库归属；跨公司（org 不同或任一为空）→ `flow.wms.transfer.cross`，否则 `flow.wms.transfer.internal`；状态 1 待审批 → 审批通过 2 已生效 |
| 发货 | `POST {id}/ship` | 2 已生效 → 3 调出已发货；每行 `ship_qty ≤ qty`；成本快照 `unit_cost = IStockTxnCommandService.FindBalanceAsync(item, fromWh)`（无台账 409 `WMS_NO_STOCK`）；同事务写 Outbox（`TransferOut 8`，Out）+ `wms_in_transit_stocks` 行（`shipped_at`、`expect_arrive`、`unit_cost`、`status=1 在途`） |
| 收货 | `POST {id}/receive` | 3 调出已发货 → 4 调入已收货；每行 `receive_qty ≤ ship_qty`；`diff_qty = ship_qty - receive_qty > 0` 必填 `diff_note`；同事务写 Outbox（`TransferIn 4`，In，`UnitCost=在途快照`）+ 在途行 `status=2 已关闭` |
| 作废 | `POST {id}/void` | 1/2 → 5 已作废（3/4 禁止） |
| 在途查询 | `GET /api/wms/transfers/in-transit` | 扁平在途行 + 超期标记由前端按 `expect_arrive` 计算 |

**盘点 `/api/wms/stock-take-plans`、`/api/wms/stock-takes`**

| 动作 | 路由 | 规则 |
|---|---|---|
| 计划列表/新建 | `GET` / `POST /api/wms/stock-take-plans` | `scope_type ∈ 1..3`、`plan_date` 必填、明盘/盲盘 |
| 生成盘点单 | `POST /api/wms/stock-take-plans/{id}/generate` | 按范围快照台账为盘点明细 `book_qty`；对每行 `AdjustLockedQtyAsync(+book_qty)` 冻结；写入 `frozen=true` |
| 盘点单列表/详情 | `GET` / `GET {id}` | 盲盘不返回 `book_qty`（前端隐藏，B-2 不适用：属安全过滤 B-3） |
| 开始 | `POST /api/wms/stock-takes/{id}/start` | 1 待盘点 → 2 初盘中 |
| 保存实盘 | `POST {id}/save-counts` | 2/4 状态可写 `actual_qty`、`diff_reason` |
| 完成 | `POST {id}/finish` | 初盘（round=1）：2 初盘中 → 3 初盘完成，存在差异自动生成 round=2 复盘单；复盘（round=2）：4 复盘中 → 5 已完成，解冻（`AdjustLockedQtyAsync(-book_qty)`）+ 生成盘盈亏草稿 |
| 取消 | `POST {id}/cancel` | 2/4 → 6 已取消，解冻 |

**盘盈盘亏 `/api/wms/inventory-adjusts`**

| 动作 | 路由 | 规则 |
|---|---|---|
| 列表/详情 | `GET` / `GET {id}` | 类型/状态筛选 |
| 新建 | `POST` | 手工建单或由盘点完成自动生成；`type ∈ 1 盘盈/2 盘亏`，明细 qty>0，`total_amount = Σ(qty × 当前均价)` 估算 |
| 提交审批 | `POST {id}/submit` | 1 草稿 → 2 待审批（`flow.wms.inventory.adjust`） |
| 审批通过 | 事件驱动 | Applier：2 → 3 已通过，同事务写 Outbox（`Gain 5` / `Loss 9`，`Direction=In/Out`，盘盈 `UnitCost=当前均价 → ref_cost_price → 0`，盘亏 `0`） |
| 驳回 | 事件驱动 | 3 → 1 草稿（可改后重提） |

**预警 `/api/wms/inventory-alerts`**：`GET`（类型/状态/仓库筛选）、`POST {id}/handle`（`handled_note` 必填，1 待处理 → 2 已处理）、`POST {id}/ignore`（→ 3 已忽略）。Job `wms.alert.scan`（cron `0 0 2 * * ?`）。

状态常量：入库 1..4、出库 1..5、调拨 1..5、盘点 1..6、盘盈亏 1..3、预警 1..3；非法流转统一 409 `WMS_INVALID_STATE`（台账侧 `STK_INVALID_STATE` 由 Wms 原样上抛）。

### 14.5 预警算法

| 类型 | 判定 | 参数（`por_parameters`，缺失取默认） |
|---|---|---|
| 1 安全库存 | `qty_onhand < threshold` 且 `threshold > 0`，且商品/仓库均启用 | `wms.alert.safety.stock`（默认 0 = 关闭，R-9） |
| 2 积压 | `qty_onhand > 0` 且 `last_txn_at < now - days`（无流水按创建时间） | `wms.alert.stagnant.days` = 90 |
| 3 临期 | 批次 `qty > 0` 且 `expiry_date ≤ now + days`，商品 `expiry_enabled` | `wms.alert.expiry.days` = 30 |
| 4 在途超期 | 在途行 `status=在途` 且 `shipped_at < now - days` | `wms.transfer.in_transit.days` = 7 |

- 每类读取 ≤500 行/批，逐批 `SaveChanges` 后继续（禁止一次性全量加载）；
- 幂等：`alert_date`（本地日期）+ A-16 唯一索引，命中跳过；`已处理/已忽略` 当日不再生成；
- 新生成预警经 Outbox 发布 `InventoryWarningRaisedEvent`（通知中心订阅延后，登记偏差）。

### 14.6 权限点 / 菜单（2B 新增）

| 权限码 | 名称 | 类型 |
|---|---|---|
| `wms.inbound.menu` / `wms.inbound.action` | 入库作业查看 / 维护 | Menu / Action |
| `wms.outbound.menu` / `wms.outbound.action` | 出库作业查看 / 维护 | Menu / Action |
| `wms.transfer.menu` / `wms.transfer.action` | 物资调拨查看 / 维护 | Menu / Action |
| `wms.stocktake.menu` / `wms.stocktake.action` | 库存盘点查看 / 维护 | Menu / Action |
| `wms.adjust.menu` / `wms.adjust.action` | 盘盈盘亏查看 / 维护 | Menu / Action |
| `wms.alert.menu` / `wms.alert.action` | 库存预警查看 / 处理 | Menu / Action |
| `wms.command.action` | 库存命令重推 | Action |

菜单：`/manage/wms/{inbound,outbound,transfers,stocktakes,adjustments,alerts}`，排序 110~160；与 2A 仓库档案（100）合计 7 个菜单。

### 14.7 测试矩阵（2B）

| 文件 | 覆盖 |
|---|---|
| `Wms/WmsOutboxTests.cs` | 同事务入队/回滚无残留、幂等键唯一、分发成功/退避/终态、人工重推 |
| `Wms/StockInTests.cs` | 无单禁入、清点上限、批次必填、库位归属、完成写 Outbox、状态机 409 |
| `Wms/StockOutTests.cs` | FEFO 建议、复核不等阻断、大额审批挂起与回放、完成写 Outbox、取消 |
| `Wms/TransferTests.cs` | 同仓阻断、跨公司选流、发货扣减命令 + 在途行、收货入账 + 在途关闭、差异必填 |
| `Wms/StockTakeTests.cs` | 范围快照、冻结幂等、盲盘隐藏 book_qty、初盘→复盘、完成解冻、取消解冻 |
| `Wms/InventoryAdjustTests.cs` | 审批前不改账、Applier 幂等、盘盈成本口径、驳回回草稿 |
| `Wms/AlertScanTests.cs` | 四类阈值、当日不重复、已处理不再生成、分批、事件发布 |
| `Qlx.ArchitectureTests` | 端点零漏配（正向 + 反向）、权限码存在性、分层与表前缀 |

### 14.8 2B 出口条件

1. `dotnet build` 0 警告 0 错误；`dotnet test` 全绿（≥ 2A 的 1305，只增不减）；
2. `npm run build` 0 错误、`npm test` 全绿；
3. Host 全链路冒烟：仓库 → 商品 → 入库 → 出库 → 调拨（发货/收货）→ 盘点（冻结/解冻）→ 盘盈亏（审批）→ 预警 → 台账/流水核对；
4. 台账铁律回归：流水只增、均价/金额吻合、负库存阻断、幂等重投不重复；
5. 浏览器闭环截图留档 `docs/test-shots/wms-2b-20261026/`；
6. 文档回写：批次记录 2B 段、资产台账 §10、推进计划 M-3.2 状态。

## 15. 2A 自查自纠结论（2026-10-26）

> 审查范围：`src/Qlx.Modules.Wms`、`src/Qlx.Modules.Stk`、`src/Qlx.Web` 2A 七页、架构测试、迁移。
> 结论：缓存（C-1~C-7）、前后端边界（B-2/B-3）、编码（N-1 商品/仓库）、端点授权守护（E-1/E-2）**合规**；下列 10 项需修正或登记，全部挂到 2B 任务（Task 25 / Task 21）。

| # | 问题（证据） | 影响 | 修正 |
|---|---|---|---|
| F-1 | `CreateZonePayload.Code` / `CreateLocationPayload.Code` 由前端提交（[WarehouseContracts.cs](file:///e:/ERPv2/eprv3/src/Qlx.Modules.Wms/Dtos/WarehouseContracts.cs#L81)），A-12 仅登记商品/仓库编码偏差 | N-1 未覆盖，评审驳回风险 | 登记偏差：库区/库位编码为物理货位人工命名标识（非名称派生），保持前端录入；写入 A-12 偏差表（Task 25） |
| F-2 | `WarehouseService.RequireName` 无长度上限、`RequireCode` 仅非空、`Normalize` 无长度上限、`SortOrder` 无范围；列长已为 64/32/256（`InitialWms`） | 超长入参落库失败 → 500 而非 400（V-1） | 服务层补齐长度/范围校验（Task 25） |
| F-3 | 商品 `RefCostPrice/GuidePrice` 无非负校验（`ItemService.ValidatePayload`） | 负价格进入档案与成本调整基数 | 补 `≥0` 校验（Task 25） |
| F-4 | `EndpointPolicyCoverageArchitectureTests` 只断言「Action → 规则」（[L40-L92](file:///e:/ERPv2/eprv3/tests/Qlx.ArchitectureTests/EndpointPolicyCoverageArchitectureTests.cs#L40-L92)） | 2B 大量新端点时可能留下「幽灵规则」（规则指向不存在的 Action） | 增反向断言（Task 25） |
| F-5 | `flow.wms.warehouse.disable` 已种子（`SupportSeedService` L484）但 `WarehouseService.ToggleAsync` 未接审批（无 `IApprovalService` 注入） | 审批定义空转；停用无审批留痕 | 停用转审批挂起 + Applier 落地（Task 25 接线、Task 21 Applier 分支） |
| F-6 | 仓库停用前置仅校验库存（[WarehouseService.cs L170-L179](file:///e:/ERPv2/eprv3/src/Qlx.Modules.Wms/Services/WarehouseService.cs#L170-L179)），未校验「无未完成作业单」（需求 04 §2.1） | 停用后仍有在途作业 | 2B 作业表就绪后补作业单前置（Task 25） |
| F-7 | 库区/库位无删除端点，前端以启停替代（2A 已登记） | 与仓库删除能力不一致 | 补 `DELETE zones/{id}`、`DELETE locations/{id}`（子级/库存前置）（Task 25） |
| F-8 | `WarehouseView` 编码列宽 150、`OpeningView` 单号列宽 170，长码截断无悬浮（2A 已登记） | 展示缺陷 | 列宽自适应 + tooltip（Task 25） |
| F-9 | `IStockQueryService.FindBalanceAsync` 成本字段受 `stk.cost.field` 裁剪；后台 Job 无 claims → 成本丢失（已在 R-6 登记） | 调拨在途成本快照丢失 | 约定成本读取走 `IStockTxnCommandService.FindBalanceAsync`（A-13，Task 17） |
| F-10 | 2A 未建 `wms_approval_requests`，Wms 无审批挂起能力 | 2B 大额出库/调拨/盘盈亏/仓库停用无法挂起 | 2B 首个迁移建表 + 挂起框架（Task 14 / Task 21） |
