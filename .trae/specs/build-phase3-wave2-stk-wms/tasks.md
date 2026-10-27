# 阶段三第二波（M-3.2）任务清单：05 商品 + 04 物流

> 规格：`.trae/specs/build-phase3-wave2-stk-wms/spec.md`｜日期：2026-10-26
> 纪律：每任务完成即 `dotnet build` + `dotnet test`（前端任务加 `npm run build` + `npm test`）全绿再推进；构建前停 Host。
> 状态：`[ ]` 未开始 / `[~]` 进行中 / `[x]` 已完成
> 2B 附加约束：① 迁移只在 Task 21 生成（中间任务依赖 Sqlite `EnsureCreated`）；② Manifest 权限点/菜单/Job 只在 Task 21 注册；③ 每个作业任务必须复用 `IStockCommandQueue` 入队，禁止直连 `IStockTxnCommandService` 同步写。

---

## 2A（05 商品 + 仓库最小切片）

### Task 1：公共契约下沉（T2A-01）

- [x] 新建 `src/Qlx.Abstractions/Queries/IWarehouseQueryService.cs`（`WarehouseQueryRow` + 4 方法）
- [x] 新建 `src/Qlx.Abstractions/Queries/IStockQueryService.cs`（`StockBalanceRow`/`StockBatchRow` + 5 方法，成本字段可空）
- [x] 新建 `src/Qlx.Abstractions/Stock/StockContracts.cs`（`StockDirection`/`StockSourceType`/`PostStockTxnRequest`/`StockPostResult`/`IStockTxnCommandService`）
- [x] 新建 `src/Qlx.Abstractions/Events/Stk/{StockChangedEvent,OpeningPostedEvent,CostAdjustedEvent}.cs`
- [x] 新建 `src/Qlx.Abstractions/Events/Wms/{StockInCompletedEvent,StockOutCompletedEvent,TransferShippedEvent,TransferReceivedEvent,InventoryWarningRaisedEvent}.cs`
- **验收**：`dotnet build` 通过；Abstractions 不新增任何模块/BuildingBlocks 引用。

### Task 2：工程装配（T2A-02）

- [x] `Qlx.Erpv3.slnx` 追加 Stk/Wms 项目
- [x] `src/Qlx.Host/Qlx.Host.csproj` 追加两个 ProjectReference
- [x] `src/Qlx.Host/Program.cs`：`.AddQlxBas()` 后追加 `.AddQlxWms().AddQlxStk()`；`AddApplicationPart` 追加两个 DbContext 程序集
- [x] 新建 `config/modules/stk/stk.json`、`config/modules/wms/wms.json`（仅技术参数）
- **验收**：Host 启动日志可见两模块 Manifest 同步；模块定义不重复。

### Task 3：Wms 骨架（T2A-03）

- [x] 新建 `src/Qlx.Modules.Wms/Qlx.Modules.Wms.csproj`（复制 Bas 依赖集）
- [x] 新建 `WmsModule.cs`（ModuleCode=wms、TablePrefix=wms_、仓库权限点/菜单、缓存清理 Job）
- [x] 新建 `WmsServiceCollectionExtensions.cs`（DbContext 工厂 + `WmsTenantMigrationPlan(DependsOn=[Bas])` + 仓储/服务/查询/策略/Job）
- [x] 新建 `WmsTenantDbContext.cs` / `.DbSets.cs` / `WmsTenantDbContextFactory.cs`
- [x] 新建 `WmsEndpointPolicyMap.cs`
- [x] 生成首次迁移 `InitialWms` 并启动期应用
- **验收**：迁移成功；`/api/wms/warehouses` 路由存在；架构测试全绿。

### Task 4：仓库主数据（T2A-04）

- [x] `Domain/Warehouse/{Warehouse,Zone,Location}.cs`（私有 setter + 领域方法 + 索引注解）
- [x] `Dtos/WarehouseContracts.cs`（新建/更新不含 Code）
- [x] `Repositories/{IWmsTenantRepository,WmsTenantRepository}.cs`
- [x] `Services/{IWarehouseService,WarehouseService}.cs`（编码 `WH-` 发号、停用前置校验、失效优先）
- [x] `Services/WarehouseQueryService.cs`（实现 `IWarehouseQueryService`，读穿缓存）
- [x] `Controllers/WarehouseController.cs`（仓库/库区/库位 CRUD，零业务逻辑）
- [x] `tests/Qlx.BuildingBlocks.Tests/Wms/WarehouseTests.cs`
- **验收**：编码唯一、停用前置（库存非 0 拒绝）、缓存写后失效、租户隔离测试全绿。

### Task 5：Stk 骨架（T2A-05）

- [x] 新建 `src/Qlx.Modules.Stk/Qlx.Modules.Stk.csproj`
- [x] 新建 `StkModule.cs`（权限点/六菜单/缓存清理 Job/订阅 `ApprovalCompletedEvent`）
- [x] 新建 `StkServiceCollectionExtensions.cs`（DbContext 工厂 + `StkTenantMigrationPlan(DependsOn=[Bas])` + 各服务/查询/命令/策略/Job）
- [x] 新建 `StkTenantDbContext.cs` / `.DbSets.cs` / `StkTenantDbContextFactory.cs`
- [x] 新建 `StkEndpointPolicyMap.cs`
- [x] 生成首次迁移 `InitialStk`（实体落地后于 T2A-06 生成，含 `stk_items`/`stk_price_logs`/`stk_approval_requests`）
- **验收**：迁移成功；Manifest 权限点/菜单物化；架构测试全绿。

### Task 6：商品档案（T2A-06）

- [x] `Domain/Item/{Item,ItemPriceLog}.cs`、`Domain/Numbering/StkApprovalRequest.cs`
- [x] `Services/Numbering/{IStkItemCodeGenerator,StkItemCodeGenerator}.cs`（`{分类码}{seq:4}` + 唯一冲突重试）
- [x] `Dtos/ItemContracts.cs`（新建不含 `ItemCode`）
- [x] `Services/ItemService.cs`（读穿缓存、价格/关键字段留痕、审批挂起、应用幂等、停用前置）
- [x] `Controllers/ItemController.cs`（含价格历史、关键字段变更提交）
- [x] `tests/Qlx.BuildingBlocks.Tests/Stk/ItemTests.cs`
- **验收**：编码生成、条码唯一、N-1、审批挂起与应用幂等、成本字段裁剪测试全绿。

### Task 7：库存台账铁律（T2A-07）

- [x] `Domain/Inventory/{StockBalance,StockBatch,StockTxn}.cs`（Version 并发令牌、幂等唯一索引）
- [x] `Services/StockLedgerService.cs`（实现 `IStockTxnCommandService`：IN/OUT 公式、批次联动、负库存、幂等、重试）
- [x] 迁移追加 `trg_stk_stock_txns_no_update_delete`（Up/Down）
- [x] `Controllers/InventoryController.cs`（只读：台账/流水/批次）
- [x] `tests/Qlx.BuildingBlocks.Tests/Stk/StockLedgerTests.cs`
- **验收**：入 10@10 + 入 10@20 → 均价 15/金额 300；出 5 → 75；超量返回 `STK_INSUFFICIENT` + 精确缺口且零写入（HTTP 409 由 Wms 侧映射）；幂等重投一条流水；无更新/删除路径（服务层 + 触发器）。

### Task 8：期初建账 / 过账（T2A-08）

- [x] `Domain/Inventory/{OpeningBatch,OpeningItem}.cs`
- [x] `Services/OpeningService.cs`（Draft CRUD、提交过账审批、每仓库唯一已过账、过账写 `SourceType=Opening` 流水、发布事件）
- [x] `Controllers/OpeningController.cs`（含仓库汇总）
- [x] Applier 分支 `stk.opening.post`
- [x] `tests/.../Stk/OpeningCostAdjustTests.cs`（期初部分）
- **验收**：状态机、一次性锁定、过账流水与金额、重复应用幂等。

### Task 9：成本调整（T2A-09）

- [x] `Domain/Inventory/{CostAdjust,CostAdjustLine}.cs`
- [x] `Services/CostAdjustService.cs`（阈值选定义码、按仓库/全部范围展开、红蓝字流水落新均价、发布事件）
- [x] `Controllers/CostAdjustController.cs`
- [x] Applier 分支 `stk.cost.adjust` / `stk.cost.adjust.large`
- [x] `tests/.../Stk/OpeningCostAdjustTests.cs`（成本调整部分）
- **验收**：审批后 `avg_cost` 精确等于新值、差额累计、两仓库范围正确。

### Task 10：查询与事件（T2A-10）

- [x] `Services/StockQueryService.cs`（`IStockQueryService` 实现 + 成本字段裁剪 + FEFO 排序）
- [x] 开启 `StockChangedEvent`/`OpeningPostedEvent`/`CostAdjustedEvent` 持久发布（Outbox）
- [x] `tests/.../Stk/StkQueryTests.cs`
- **验收**：台账/流水/批次查询、权限裁剪、FEFO、事件载荷测试全绿。

### Task 11：审批定义种子（T2A-11）

- [x] `SupportSeedService.cs` 追加 Stk 6 条 + Wms 5 条定义（以 spec §8 矩阵 11 条为准）
- **验收**：启动种子幂等；审批中心可选到新流程；重复启动不重复插入。

### Task 12：前端 2A（7 页）（T2A-12）

- [x] `types/{stk,wms}.ts`、`api/{stk,wms}.ts`、`utils/{stk-enums,wms-enums,csv-export}.ts`、`composables/useLocalTable.ts`
- [x] `views/wms/WarehouseView.vue`
- [x] `views/stk/{ItemMasterView,OpeningView,StockLedgerView,StockTxnView,CostAdjustView,BatchStockView}.vue`
- [x] `router/index.ts` 追加 7 条路由；`data/navigation.ts` 对齐；`AppIcon.vue` 补 `EditPen`
- [x] 前端测试：`api/__tests__/{stk,wms}.spec.ts`、`utils/__tests__/{stk-enums,wms-enums,csv-export}.spec.ts`（新增 5 文件 26 用例）
- **验收**：`npm run build` 0 错误（1979 模块，13.46s）、`npm test` 28 文件 / 272 用例全通过；页面可打开、表单无编码框（`ItemMasterView` 仅只读回显 `itemCode`，N-1 合规）。
- **已知缺口（登记待 2B 补齐）**：库区/库位后端无删除端点，页面暂以「启停」替代删除。

### Task 13：2A 出口（T2A-13）

- [x] 全量 `dotnet build`（0 警告 0 错误）/ `dotnet test`（架构 20 + 单元/集成 1285 = 1305，失败 0）/ `npm run build`（0 错误）/ `npm test`（28 文件 272 用例全通过）
- [x] Host 冒烟（授权许可 → 建仓库 → 建商品 → 期初 → 过账 → 流水 → 台账核对）：22 步全 OK；SQL 核对 `stk_stock_balances` 10/100/10、`stk_stock_txns` direction=1 qty=10 结存 10 source_type=1、`stk_opening_batches.status=3`、`stk_stock_batches` qty=10
- [x] 浏览器闭环截图留档：`docs/test-shots/stk-wms-2a-20261026/`（12 张主证据 + 2 张补充证据，`_scripts/smoke_2a.py` + `smoke-result.json`）
- [x] 文档回写：`docs/项目资产登记台账-20261024.md`、`docs/下一阶段工作准备与推进计划-20261024.md`、新建 `docs/阶段三第二波-05商品-04物流-批次实施记录-20261026.md`（2A 段）
- **验收**：05 十五项交付物齐备；台账铁律用例全过。
- **环境缺口登记**：既有开发租户初始仅授权 `portal/bas/support`，wms/stk 未授权导致 `MODULE_NOT_LICENSED`（404）与菜单缺失；已通过门户模块授权接口补齐（数据授权，非代码缺陷）。
- **展示小问题（登记待 2B 一并处理）**：`WarehouseView.vue` 编码列宽 150、`OpeningView.vue` 单号列宽 170，长编码被截断且无悬浮全文。
- **2A 自查自纠**：见 `spec.md` §15（F-1~F-10），修正统一挂 Task 25 / Task 21。

---

## 2B（04 物流作业全量）

### Task 14：作业公共基建（T2B-01）

- [x] `Domain/Numbering/WmsApprovalRequest.cs`（表 `wms_approval_requests`：biz_type/biz_id/approval_instance_id（过滤唯一）/payload_json/status/created_at，结构对齐 `StkApprovalRequest`）
- [x] `Domain/Outbox/{WmsStockCommandOutbox,WmsStockCommandState}.cs`（`idempotency_key` 租户内唯一；`state` 1 待分发/2 已分发/3 失败；`retry_count`/`next_retry_at`/`last_error`/`dispatched_at`）
- [x] `Services/IStockCommandQueue.cs` + `Services/StockCommandQueue.cs`（同 DbContext 事务内入队；幂等键 `wms:{biz}:{docNo}:{lineNo}`；同键重复入队零新增；`payload_json` = `PostStockTxnRequest`）
- [x] `Jobs/WmsStockCommandDispatchJobHandler.cs`（取 `state ∈ {1,3}` 且 `next_retry_at ≤ now`，按 id 升序批 ≤100；逐条 `PostAsync`；成功/`STK_DUPLICATE` → 已分发；失败 → `retry_count+1` + 退避 1/2/4/8/16 分钟；≥5 次 → 终态 `next_retry_at=null`）
- [x] `Jobs/WmsJobCodes.cs` 追加 `WmsStockCommandDispatch`、`WmsAlertScan`
- [x] `WmsTenantDbContext.DbSets.cs` 追加 `ApprovalRequests`/`StockCommandOutbox`；`OnModelCreating` 补唯一索引与 `(state, next_retry_at)` 索引
- [x] `tests/Qlx.BuildingBlocks.Tests/Wms/WmsOutboxTests.cs`
- **约束**：本任务不注册 Manifest Job、不加端点、不生成迁移（Task 21 收口）。
- **验收**：回滚后无 Outbox 残留；同幂等键二次入队零新增；分发成功置已分发且 `dispatched_at` 非空；失败退避递增；5 次进终态；`STK_INSUFFICIENT` 保留 `last_error`。

### Task 15：入库作业（T2B-02）

- [x] `Domain/Inbound/{StockInTask,StockInTaskLine,StockInTaskStatus,StockInSourceType}.cs`（任务池/明细；`task_no` 由 `WL-RK` 发号）
- [x] `Dtos/InboundContracts.cs`（新建/开始/完成/取消入参；行 `UnitCost` 可空；出参扁平 int 枚举、无展示文案）
- [x] `Services/IStockInService.cs`（public）+ `Services/StockInService.cs`（internal）：无单禁入（`source_doc_no` 非空 ≤32）、仓库启用、明细 ≥1、`source_type ∈ 1..4`、状态机（待作业→作业中→已完成；1/2→已取消）、完成校验（`confirm_qty ≤ count_qty`、批次商品必填 `batch_no`、`location_id` 属同仓库且启用）
- [x] 完成动作在同一事务内 `IStockCommandQueue.EnqueueAsync`（`PurchaseIn 2 / SaleReturn 3 / TransferIn 4 / Gain 5`，`Direction=In`，`UnitCost = 行 unit_cost → 商品 ref_cost_price → 0`）
- [x] 事务提交后 `PublishDurableAsync<StockInCompletedEvent>`
- [x] `Controllers/StockInController.cs`（列表/详情/新建/start/complete/cancel，零业务逻辑）
- [x] `tests/Qlx.BuildingBlocks.Tests/Wms/StockInTests.cs`
- **验收**：空单号 400；`confirm_qty > count_qty` 400；批次商品缺批次 400；库位跨仓 400；完成写 1 条 Outbox/行；重复完成 409 `WMS_INVALID_STATE`；事件载荷含 TaskId/TaskNo/WarehouseId/SourceDocNo。
- **实施结果**：9 用例全绿；全量回归 1324（1304 + 架构 20），失败 0。新增跨模块商品只读契约 `IItemQueryService`（Abstractions）+ Stk `ItemQueryService` 实现（批次属性/参考成本，A-14）。

### Task 16：出库作业（T2B-03）

- [x] `Domain/Outbound/{StockOutTask,StockOutTaskLine,StockOutTaskStatus,StockOutSourceType}.cs`（`WL-CK` 发号）
- [x] `Dtos/OutboundContracts.cs`（新建/拣货/复核/完成/取消；详情出参含 `FefoSuggestions` 扁平行）
- [x] `Services/IStockOutService.cs` + `Services/StockOutService.cs`：`source_type ∈ 1..5`、明细 ≥1、拣货 `pick_qty ≤ apply_qty`、复核 `check_qty ≠ pick_qty` → 409 `WMS_CHECK_MISMATCH`、`handover_to` 必填
- [x] FEFO 建议：`IStockQueryService.ListBatchesAsync` 效期升序（业务规则在后端，B-2 不适用）
- [x] 大额审批：`total_amount = Σ(pick_qty × 均价)`（均价取 `IStockTxnCommandService.FindBalanceAsync`，A-13）；超 `wms.outbound.large.threshold`（默认 50000）→ 写 `WmsApprovalRequest` 挂起 `flow.wms.outbound.large`，审批通过由 Applier 完成（Task 21）
- [x] 普通完成：同事务入队（`SaleOut 7 / PurchaseReturn 6 / TransferOut 8 / Loss 9 / Use 10`，`Direction=Out`，`UnitCost=0`）+ 事务后发 `StockOutCompletedEvent`
- [x] `Controllers/StockOutController.cs`
- [x] `tests/Qlx.BuildingBlocks.Tests/Wms/StockOutTests.cs`
- **验收**：FEFO 排序正确；复核不等 409；大额挂起不写 Outbox/不改台账；审批通过后仅写一次；取消态禁止完成。
- **实施结果**：10 用例全绿；全量回归 1334（1314 + 架构 20），失败 0。大额重复提交返回 409 `WMS_APPROVAL_PENDING`；完成返回 `ApprovalRequired/TotalAmount` 供前端提示。

### Task 17：调拨与在途（T2B-04）

- [x] `Domain/Transfer/{StockTransfer,StockTransferLine,InTransitStock,TransferStatus,InTransitStatus}.cs`（`WL-DB` 发号）
- [x] `Dtos/TransferContracts.cs`（新建/发货/收货/作废/在途行）
- [x] `Services/ITransferService.cs` + `Services/TransferService.cs`：
  - 新建：`from_wh_id ≠ to_wh_id`；`from_org_id/to_org_id` 取仓库归属（`IWarehouseQueryService`）；跨公司（org 不同或任一为空）→ `flow.wms.transfer.cross`，否则 `flow.wms.transfer.internal`，状态 1 待审批
  - 发货：状态 2 已生效 → 3 调出已发货；`ship_qty ≤ qty`；成本快照 `IStockTxnCommandService.FindBalanceAsync`（无台账 409 `WMS_NO_STOCK`）；同事务入队 `TransferOut 8`（Out）+ 写 `wms_in_transit_stocks`（`shipped_at`/`expect_arrive`/`unit_cost`/`status=1`）
  - 收货：3 → 4 调入已收货；`receive_qty ≤ ship_qty`；`diff_qty > 0` 必填 `diff_note`；同事务入队 `TransferIn 4`（In，`UnitCost=在途快照`）+ 在途行 `status=2 已关闭`
  - 作废：1/2 → 5 已作废；3/4 禁止 409
- [x] 事务后发 `TransferShippedEvent` / `TransferReceivedEvent`
- [x] `Controllers/TransferController.cs`（含 `GET /api/wms/transfers/in-transit`）
- [x] `tests/Qlx.BuildingBlocks.Tests/Wms/TransferTests.cs`
- **验收**：同仓 400；跨公司选跨公司流程码；发货生成 1 出库命令 + 1 在途行；收货生成 1 入库命令 + 在途关闭；差异缺备注 400；作废后发货 409。
- **实施结果**：13 用例全绿；全量回归 1347（1327 + 架构 20），失败 0。新建即同事务写 `WmsApprovalRequest`（`biz_type=wms.transfer`）并发起审批；`ApplyApprovalAsync(id, approved)` 供 Task 21 Applier 幂等调用（通过→已生效，驳回→已作废）；发货无台账返回 409 `WMS_NO_STOCK`；在途列表下发扁平原始字段（超期由前端按 `expect_arrive` 计算）。

### Task 18：盘点与冻结（T2B-05）

- [x] `Domain/StockTake/{StockTakePlan,StockTake,StockTakeLine,StockTakeStatus}.cs`（`WL-PDJH` / `WL-PD` 发号）
- [x] `Dtos/StockTakeContracts.cs`（计划、生成、开始、保存实盘、完成、取消）
- [x] `Services/IStockTakeService.cs` + `Services/StockTakeService.cs`：
  - 生成盘点单：按范围（全仓/品类/库位）快照台账明细 `book_qty`；逐行 `IStockTxnCommandService.AdjustLockedQtyAsync(+book_qty)` 冻结并置 `frozen=true`
  - 盲盘（`type=盲盘`）详情不返回 `book_qty`（字段级安全过滤 B-3）
  - 初盘完成（round=1）：2 → 3，存在差异自动生成 round=2 复盘单
  - 复盘完成（round=2）：4 → 5，逐行 `AdjustLockedQtyAsync(-book_qty)` 解冻 + 生成盘盈亏草稿（Task 19 服务）
  - 取消：2/4 → 6，解冻；解冻幂等（`frozen` 置 false 后不重复调用）
- [x] `Controllers/StockTakePlanController.cs`、`Controllers/StockTakeController.cs`
- [x] `tests/Qlx.BuildingBlocks.Tests/Wms/StockTakeTests.cs`
- **验收**：冻结后 `stk_stock_balances.qty_locked` 精确等于快照量；解冻一次后归零；重复完成/取消 409；盲盘详情无 `book_qty`；盘点期间出库可用量不足由 Stk 返回 `STK_INSUFFICIENT`（单点校验）。
- **实施结果**：12 用例全绿；全量回归 1359（1339 + 架构 20），失败 0。冻结/解冻均只经 `AdjustLockedQtyAsync`（A-15），行级 `Frozen` 标记保证重复完成/取消不重复调整；盲盘同时裁剪 `book_qty` 与 `diff_qty`（差异亦泄露账面量）；初盘无差异直接 3 → 5 并解冻，有差异生成 round=2 复盘单（状态 4，仅含差异行）；复盘完成解冻初盘全部冻结行并调用 `IStockTakeAdjustDraftCreator`（Task 18 注册空实现，Task 19 替换为真实落库，避免循环依赖）。偏差登记：台账无库位维度，库位范围仅做归属/启用校验后按整仓快照；`StockItemQueryRow` 追加 `CategoryId`（默认 0，兼容既有调用）以支持品类范围过滤；批次维度盘点细分延后波次（`batch_no` 字段预留）。


### Task 19：盘盈盘亏（T2B-06）

- [x] `Domain/Adjust/{InventoryAdjustDoc,InventoryAdjustLine,AdjustDocStatus}.cs`（`WL-PYK` 发号）
- [x] `Dtos/InventoryAdjustContracts.cs`
- [x] `Services/IInventoryAdjustService.cs` + `Services/InventoryAdjustService.cs`：手工建单 + 盘点完成自动建草稿；`type ∈ 1 盘盈/2 盘亏`、明细 qty>0、`total_amount = Σ(qty × 当前均价)` 估算；提交审批 `flow.wms.inventory.adjust`（1 草稿 → 2 待审批）；审批通过/驳回的应用方法（供 Task 21 Applier 调用，幂等）
- [x] 审批通过：同事务入队（盘盈 `Gain 5` In，`UnitCost=均价 → ref_cost_price → 0`；盘亏 `Loss 9` Out）；驳回回 1 草稿
- [x] `Controllers/InventoryAdjustController.cs`
- [x] `tests/Qlx.BuildingBlocks.Tests/Wms/InventoryAdjustTests.cs`
- **验收**：审批前零命令零流水；审批通过恰好 1 命令/行；重复应用幂等；驳回后可改重提；金额估算与均价一致。
- **实施结果**：9 用例全绿；全量回归 1368（1348 + 架构 20），失败 0。手工建单金额按「当前均价 → 参考成本 → 0」三级回退逐行估算（`total_amount` 两处两位四舍五入）；提交审批同事务写 `WmsApprovalRequest`（`biz_type=wms.inventory.adjust`）并发起 `flow.wms.inventory.adjust`；`IInventoryAdjustApprovalApplier.ApplyAsync(docId, approved)` 仅「2 待审批」生效，通过时同事务入队（幂等键 `wms:py:{AdjustNo}:{LineNo}`，盘盈 In 且单价按审批时点均价回退，盘亏 Out 且单价 0）后置 3，驳回回 1 可改重提，重复应用返回 `false` 且不重复入队。`StockTakeAdjustDraftCreator` 替换 Task 18 空实现：按差异正负拆一张盘盈单（type=1）+ 一张盘亏单（type=2），`qty=|diff_qty|`、`take_id` 回链复盘单，只追加跟踪态实体、由 `StockTakeService` 在盘点完成事务内统一保存。偏差登记：领域表补强 `warehouse_id` 列（spec 表结构未列，明细成本估算与命令入队均需仓库维度）；批次维度沿用盘点 `batch_no` 预留，当前盘点单行批次为空则盘盈亏行批次为空。

### Task 20：库存预警（T2B-07）

- [x] `Domain/Alert/{InventoryAlert,InventoryAlertType,InventoryAlertStatus}.cs`（`alert_date` + A-16 唯一索引，`batch_no` 空值归一空串）
- [x] `Jobs/InventoryAlertScanJobHandler.cs`：四类阈值（安全库存/积压/临期/在途超期），参数缺失取默认（0/90/30/7），每类 ≤500 行/批 + 逐批保存，当日幂等，已处理/已忽略不重复生成
- [x] 新预警经 Outbox 发布 `InventoryWarningRaisedEvent`
- [x] `Controllers/InventoryAlertController.cs`（列表筛选 + handle/ignore 回填）
- [x] `tests/Qlx.BuildingBlocks.Tests/Wms/AlertScanTests.cs`
- **验收**：四类阈值边界；同日二次扫描零新增；已处理不复活；`handled_note` 必填；批量上限生效；事件仅对新生成预警发布。
- **实施结果**：10 用例全绿（`+10`）；全量回归 1378（1358 + 架构 20），失败 0。四类扫描均按雪花 `Id` 游标分页（`Id > afterId` + `OrderBy(Id)` + `Take(500)`），阈值判定在内存完成（SQLite 不支持 `DateTimeOffset`/`decimal` 比较下推）；每类先取当日同类型预警键集合 `(item_id, warehouse_id, batch_no)` 内存去重（含已处理/已忽略），命中即跳过；每批 `SaveChanges` 后仅对本批新增行逐条 `PublishDurableAsync(InventoryWarningRaisedEvent)`，事件载荷 `AlertId/AlertType/ItemId/WarehouseId`。`alert_date` 取本地日期（UTC+8）。处理/忽略要求 `Pending`，否则 409 `WMS_INVALID_STATE`；`handled_note` 必填且 ≤256 字符。
- **偏差登记**：① 类型 2 积压对 `last_txn_at IS NULL` 的行无法按「创建时间」兜底（`StockBalance : Entity` 无 `CreatedAt`，不改 Stk 表结构），当前直接跳过；② 新增 `Qlx.Abstractions.Queries.IStockAlertScanQueryService`（台账/批次分页只读契约）与 `StkTenantRepository` 两个分页方法，避免扩展展示型 `IStockQueryService`（A-13）；③ 在途分页新增 `IWmsTenantRepository.ListInTransitStockPageAsync`（仅 `Status=在途`），预警仓库取调入仓 `ToWarehouseId`；④ 预警扫描 Job 的 Manifest 定义（cron `0 0 2 * * ?`）留 Task 21 收口，本任务仅完成权限 `wms.alert.menu`/`wms.alert.action` 与菜单 `/manage/wms/alerts` 前置注册以过架构测试。

### Task 21：Wms 收口（T2B-08）

- [x] `WmsModule.cs`：权限点 15（含 `wms.command.action`）、菜单 7、Jobs 3（缓存清理 / 命令分发 / 预警扫描）、`SubscribedEvents = ["Qlx.Abstractions.Approvals.ApprovalCompletedEvent"]`
- [x] `WmsEndpointPolicyMap.cs` 全量：Warehouse / StockIn / StockOut / Transfer / StockTakePlan / StockTake / InventoryAdjust / InventoryAlert / StockCommand
- [x] `Controllers/StockCommandController.cs`（`POST /api/wms/stock-commands/{id}/retry`，仅 state=3 可重推，重复 409）
- [x] 迁移 `WmsOperations`：2B 全部实体 + `wms_approval_requests` + 唯一索引（outbox 幂等键、预警四元组+日期、在途唯一）
- [x] `Services/Consumers/ApprovalCompletedEventConsumer.cs`（`public`，按 bizType 分发）+ `Services/WmsApprovalApplier.cs`（`wms.outbound.large` / `wms.transfer` / `wms.inventory.adjust` / `wms.warehouse.disable` 分支，幂等）
- [x] `WmsServiceCollectionExtensions.cs` 注册全部服务/仓储/Job；`AddApplicationPart` 已覆盖
- [x] `tests/Qlx.BuildingBlocks.Tests/Wms/WmsApprovalApplierTests.cs`
- **验收**：架构测试三断言全绿（含权限码存在性）；迁移启动应用成功；消费者 public 可被发现；四类审批回放均落地一次。
- **实施结果**：12 用例全绿（`+12`）；全量回归 1390（1370 + 架构 20），失败 0，0 警告 0 错误。迁移 `20261026114641_WmsOperations` 启动应用成功：Host 启动后 `qlx_dev` 实际存在 18 张 `wms_*` 表（含 `wms_approval_requests`、`wms_stock_command_outbox`、`wms_in_transit_stocks`、`wms_inventory_alerts` 及全部 2B 主从表）。Applier 以 `ApprovalInstanceId` 为幂等键，仅处理 `Pending` 请求；通过与驳回分路径（`MarkApplied` / `MarkRejected` / `MarkWithdrawn`）。四类回放各落地一次：大额出库 → 作业 `Completed` + 1 条 Outbox + 1 条 `StockOutCompletedEvent`；调拨 → `Effective`（驳回/撤回 → `Voided`）；盘盈盘亏 → `Approved` + 1 条 Outbox；仓库停用 → `Disabled`。命令重推仅终态（`State=Failed && NextRetryAt is null`）可恢复为 `Pending`，非终态 409 `WMS_INVALID_STATE`，不存在 404。
- **偏差登记**：① Applier 不包裹外层事务，业务服务各自开启事务并由状态机自身幂等（`PendingCheck → Completed`、`PendingApproval → Effective/Voided`、`PendingApproval → Approved/Draft`），重复事件安全；② 大额出库驳回保持 `PendingCheck` 以便修改后重提，仓库停用驳回为 no-op；③ `wms.warehouse.disable` 的实际接线（`ToggleAsync` 转审批挂起 + 作业单前置校验）留 Task 25，本任务先落地 Applier 分支与回放测试；④ 迁移验证以 Host 启动 + `sys.tables` 实表核对完成，未单独执行 `dotnet ef database update`（避免与运行时按模块历史表机制冲突）。

### Task 22：前端 2B 公共层（T2B-09）

- [x] `types/wms.ts` 扩展（入库/出库/调拨/在途/盘点/盘盈亏/预警/命令；Id 全 string）
- [x] `api/wms.ts` 扩展（全部 2B 端点函数）
- [x] `utils/wms-enums.ts` 扩展（状态、来源类型、预警类型、盘点轮次中文与 Tag 类型映射）
- [x] 复用 `composables/useLocalTable.ts`、`utils/csv-export.ts`；新增 `api/__tests__/wms-2b.spec.ts`、`utils/__tests__/wms-enums-2b.spec.ts`
- **验收**：`npm run build` 0 错误；`npm test` 全绿（用例只增不减）。
- **实施结果**：`types/wms.ts` 追加 14 组数值枚举 + 2B 全部扁平契约（568 行）；`api/wms.ts` 新增 `stockInApi` / `stockOutApi` / `transferApi`（含 `inTransit`）/ `stockTakePlanApi` / `stockTakeApi` / `inventoryAdjustApi` / `inventoryAlertApi` / `stockCommandApi`，全部路径与查询参数对齐后端 Controller，统一 `toParams` 空值不上送、Id 经 `encodeURIComponent`；`utils/wms-enums.ts` 新增 14 组中文字典 + 对应 label/Tone 函数，`WMS_ENUM_OPTION_MAPS` 扩至 16 组。新增 `wms-2b.spec.ts`（12 用例）与 `wms-enums-2b.spec.ts`（5 用例）。前端总计 30 文件 289 用例全绿（原 272 + 17）；`npm run build` 0 错误。
- **偏差登记**：① 出库大额审批提示依赖 `StockOutCompleteResultRow.approvalRequired`，页面在 Task 23 承接；② 在途超期仅由前端按 `expectArrive` 展示判定，后端只下发原始日期（B-2）。

### Task 23：前端作业三页（T2B-10）

- [x] `views/wms/StockInView.vue`（任务池筛选 + 新建 + 开始/完成/取消 + 明细清点/库位/批次/单价）
- [x] `views/wms/StockOutView.vue`（任务池 + 拣货 FEFO 建议 + 复核 + 交接 + 大额审批提示）
- [x] `views/wms/TransferView.vue`（单据列表/表单 + 发货/收货/作废 + 在途 Tab + 差异备注）
- [x] `router/index.ts` 追加 3 条；`data/navigation.ts` 对齐
- **验收**：三页可打开并完成主路径；表单无编码框；CSV 导出可用；`npm run build` / `npm test` 全绿。
- **实施结果**：新增 `StockInView.vue` / `StockOutView.vue` / `TransferView.vue` 三页。入库页：任务池筛选（状态/仓库/来源）+ 新建动态明细 + 开始/完成（清点/确认/库位/批次/单价，`countQty` 联动 `confirmQty`）/取消 + 详情抽屉 + CSV。出库页：新建 + 拣货（`fefoSuggestions` 展示、「取最早效期」按行应用首条建议并取 `min(applyQty, batchQty)`）+ 复核（`handoverTo` 必填，复核量锁定为拣货量）+ 完成（按 `approvalRequired` 提示转大额审批，不前端判定金额）+ 取消 + 详情 + CSV。调拨页：双 Tab（调拨单 / 在途库存），发货（按行 `shipQty`，至少一行 > 0）、收货（`receiveQty < shipQty` 必填差异说明）、作废、在途超期由前端按 `expectArrive < 今日` 标记（B-2）+ 详情 + 两 Tab 各自 CSV。路由追加 `/manage/wms/inbound|outbound|transfers`（权限码 `wms.inbound.menu` / `wms.outbound.menu` / `wms.transfer.menu`）；`navigation.ts` 的 wms 目录对齐后端 7 个菜单（仓库档案/库位管理、入库作业/出库作业/物资调拨、库存盘点/盘盈盘亏/库存预警）。前端 30 文件 289 用例全绿，`npm run build` 0 错误。
- **偏差登记**：① FEFO 单批次约束下，前端「取最早效期」仅应用首条建议（跨批次拆行需后端支持下单行多批次，本期不做）；② 出库拣货时库位必选（后端 `locationId > 0` 强校验），创建时仍可留空。

### Task 24：前端盘点/盘盈亏/预警三页（T2B-11）

- [x] `views/wms/StockTakeView.vue`（计划 + 生成 + 初盘/复盘录入 + 差异 + 盲盘隐藏账面）
- [x] `views/wms/InventoryAdjustView.vue`（单据 + 明细 + 提交审批 + 状态流转）
- [x] `views/wms/InventoryAlertView.vue`（四类卡片看板 + 列表 + 处理/忽略回填）
- [x] `router/index.ts` 追加 3 条；`data/navigation.ts` 对齐
- **验收**：主路径可完成；盲盘不显示账面量；预警处理回填生效；`npm run build` / `npm test` 全绿。
- **实施结果**：新增 `StockTakeView.vue` / `InventoryAdjustView.vue` / `InventoryAlertView.vue` 三页。盘点页：双 Tab（盘点计划 / 盘点单），计划筛选（范围类型/仓库）+ 新建（日期/范围/仓库，品类范围选品类、库位范围懒加载库位；`isBlind` 开关）+ 生成盘点单（confirm → `generate` → 刷新两表并切到盘点单页签）+ 查看已生成盘点单；盘点单筛选（状态/仓库）+ 开始/录入实盘/完成/取消；录入弹窗支持部分保存（`saveCounts` 仅提交已录入行）与「保存并完成」（全量提交后 `finish`，`recheckTakeId` 有值提示已自动生成复盘单，否则提示盘盈亏草稿已生成）；盲盘时隐藏「账面数量」「差异」两列（后端 `bookQty/diffQty=null`），差异原因对有差异行强制填写。盘盈亏页：筛选（类型/状态/仓库）+ 新建（类型/仓库/调整原因 ≤256/备注 + 动态明细：商品/批次号/数量>0/行备注，批次启用商品批次号必填）+ 提交审批（仅草稿）+ 详情抽屉（含明细数量与单位成本）+ CSV。预警页：四类卡片看板（按当前结果统计各类型条数，点击卡片切换类型筛选，再点取消）+ 筛选（类型/状态/仓库）+ 列表（阈值/实际值按类型加「天」单位，处理说明/处理时间回填）+ 处理弹窗（`handledNote` 必填 ≤256）+ 忽略（confirm）+ CSV。路由追加 `/manage/wms/stocktakes|adjustments|alerts`（权限码 `wms.stocktake.menu` / `wms.adjust.menu` / `wms.alert.menu`）；`navigation.ts`「盘点与预警」三菜单保持对齐。前端 30 文件 289 用例全绿，`npm run build` 0 错误（1997 modules）。
- **偏差登记**：① 预警卡片计数按当前筛选结果统计（后端列表无汇总端点，不改后端契约）；② 盘点「完成」入口与「录入实盘」共用同一弹窗，用户需在弹窗内点「保存并完成」（列表页不提供无录入直接完成）；③ 盘盈亏详情「来源」按 `takeId` 是否为空展示「盘点自动生成 / 手工建单」（展示判定在前端，B-4）。

### Task 25：2A 遗留修复（T2B-12）

- [x] F-1 偏差登记：`spec.md` A-12 增补「库区/库位编码为人工命名标识，前端录入」；批次记录偏差表同步
- [x] F-2 `WarehouseService` 补长度/范围校验（名称 ≤64、库区/库位编码 ≤32、条码 ≤64、地址/备注 ≤256、`sort_order ≥ 0`）
- [x] F-3 `ItemService` 补 `RefCostPrice/GuidePrice ≥ 0`
- [x] F-4 `EndpointPolicyCoverageArchitectureTests` 增反向断言（规则声明的 Controller/Action 必须真实存在）
- [x] F-5 `WarehouseService.ToggleAsync`：停用前置通过后转 `flow.wms.warehouse.disable` 审批挂起（审批通过由 Task 21 Applier 落地 `Disable`）
- [x] F-6 停用前置追加「无未完成作业单」（Task 15/16/17 表就绪后）
- [x] F-7 新增 `DELETE /api/wms/warehouses/zones/{id}`、`DELETE /api/wms/warehouses/locations/{id}`（子级/在途前置 + 规则 + 前端入口）
- [x] F-8 `WarehouseView` 编码列宽自适应 + `show-overflow-tooltip`；`OpeningView` 单号列同样处理
- [x] 单测：超长入参返回 400、负价格 400、仓库停用挂起、库区删除前置、规则反向断言
- **验收**：新增用例全绿；架构测试全绿；`npm run build` 0 错误。
- **实施结果**：① 校验统一走 `RequireName`（≤64）/`RequireCode`（≤32）/`NormalizeOptional(value,maxLength,field)`/`ValidateSortOrder(≥0)`，`ItemService` 增 `ValidateNonNegativePrice`（`refCostPrice`/`guidePrice`）。② `ToggleAsync` 返回 `(Row, OldJson, Changed)`：已停用时直接启用（`Changed=true`，无需审批）；启用态依次校验 `HasStockInWarehouseAsync`→`HasUnfinishedOperationsAsync`→已存在 pending 审批，通过后在同一事务内落 `WmsApprovalRequest` + `StartApprovalAsync` + `BindInstance`，响应 `WarehouseRow.ApprovalRequired=true` 且状态仍为启用，不写审计/不失效缓存。③ 新增 `DeleteZoneAsync`（有库位 → `WMS_ZONE_HAS_LOCATIONS`）、`DeleteLocationAsync`（未完结作业引用 → `WMS_LOCATION_HAS_OPEN_TASKS`），均软删 + `SOFT_DELETE` 审计 + 缓存失效；查询层补齐 `!IsDeleted` 过滤。④ 架构断言④捕获并清理 Bas 三条幽灵规则（`Supplier.Toggle`、`Customer.Toggle`、`Customer.AdjustCredit`，对应端点从未实现、前端亦无调用）。⑤ 前端 `WarehouseView` 库区/库位操作列增「删除」（`actions-width` 120→190）、`onToggle` 识别 `approvalRequired` 提示、编码列 `minWidth:190/fixed:left`；`OpeningView` 期初单号列 `width:170→minWidth:190`。⑥ 验证：`dotnet build` 0 警告 0 错误、`dotnet test` 1413/1413 通过；`npm run build` 0 错误、`npm test` 289/289 通过。
- **偏差登记**：① `FieldError` 属性名为 `FieldName`（原计划按 `Field` 断言，已按实际契约修正）；② `StockInTests.CreateRejectsUnknownItemAndDisabledWarehouse` 因停用转审批改为「Toggle → `ApplyDisableApprovalAsync(approved:true)` → 再断言禁入」，属预期行为变更；③ 停用前置「未完成作业」口径为入库 Pending/InProgress、出库 Pending/Picking/PendingCheck、调拨非 Received/Voided；库位「被引用」只看未完结入库/出库明细的 `LocationId`（Stk 台账无库位维度）。

### Task 26：跨模块联调（T2B-13）

- [x] 入库完成 → Outbox 分发 → `stk_stock_txns`（`source_type=2`、`direction=1`）→ 台账金额/均价核对
- [x] 出库完成 → `source_type=7`、`direction=2` → 台账扣减与 `amount` 归零规则
- [x] 调拨发货/收货 → `TransferOut 8` / `TransferIn 4` + 在途台账
- [x] 盘点冻结 ↔ `stk_stock_balances.qty_locked`；冻结期出库返回 `STK_INSUFFICIENT`
- [x] 盘盈亏审批 → `Gain 5` / `Loss 9` 流水
- [x] 预警扫描 → `InventoryWarningRaisedEvent` 持久发布
- [x] 幂等重投：同一作业完成/Outbox 重试不产生第二条流水
- [x] `tests/Qlx.BuildingBlocks.Tests/Wms/WmsIntegrationTests.cs`
- **验收**：联调用例全绿；无跨模块表访问（Wms 代码审查零 `stk_` 表/无 Stk 程序集引用）。
- **实施结果**：新增 `WmsIntegrationTests.cs`（9 用例）：① 入库完成→分发→`PurchaseIn 2/In` 流水与余额；② 两次入库（10@10、10@20）后出库 8 → 按移动加权均价 15 落账（金额 120、余额 12/180）；③ 调拨发货/收货 → `TransferOut 8`/`TransferIn 4` + 在途行 open→closed；④ 盘点冻结后出库分发被台账拦截（Outbox `Failed`、`LastError=STK_INSUFFICIENT`、零新增流水）；⑤ 盘点取消/无差异完成 → `QtyLocked` 归零；⑥ 盘亏审批通过 → `Loss 9/Out`；⑦ 盘盈审批通过 → `Gain 5/In`；⑧ 台账同 8 列唯一键重复提交 + Outbox 换幂等键重投 → 仅 1 条流水、余额不变；⑨ 预警扫描读真实台账（安全库存 10、在库 5）→ 落 `InventoryAlerts` 并持久发布 `InventoryWarningRaisedEvent`。夹具为 Wms / Stk 各一条独立内存 Sqlite 连接（避免 `EnsureCreated` 因库内已有表而跳过建表），分发任务注入真实 `StockLedgerService`。验证：`dotnet build` 0 警告 0 错误；`dotnet test` 1422/1422 通过（1413 基线 + 9 新增）。
- **偏差登记**：① 双库独立连接而非同库（EF `EnsureCreated` 语义限制），跨模块仍只经 `IStockTxnCommandService`/Outbox 桥接，未放宽模块边界；② 未在本文件重复覆盖批次商品与出库大额审批分支（已由 `StockInTests`/`StockOutTests` 单模块用例覆盖）。

### Task 27：2B 出口（T2B-14）

- [x] 全量 `dotnet build`（0 警告 0 错误）/ `dotnet test` 全绿（≥ 1305）
- [x] `npm run build` 0 错误 / `npm test` 全绿
- [x] Host 全链路冒烟：仓库 → 商品 → 入库 → 出库 → 调拨（发货/收货）→ 盘点（冻结/解冻）→ 盘盈亏（审批）→ 预警 → 台账/流水 SQL 核对
- [x] 浏览器闭环截图留档 `docs/test-shots/wms-2b-20261026/`
- [x] 文档回写：`docs/阶段三第二波-05商品-04物流-批次实施记录-20261026.md`（2B 段）、`docs/项目资产登记台账-20261024.md`、`docs/下一阶段工作准备与推进计划-20261024.md`（M-3.2 状态）
- **验收**：04 十五项交付物齐备；spec §14.8 出口条件全部满足。
- **实施结果**：① `dotnet build Qlx.Erpv3.slnx -c Release` 0 警告 0 错误；`dotnet test` **1451/1451**（BuildingBlocks 1430 + Architecture 21，2A 1305 → +146）。② 前端 `npm run build` 0 错误、`vitest` **30 文件 289/289**（2A 272 → +17）。③ Host 冒烟 `smoke_2b.py` 21 步全 STEP_OK：仓库 `WH-SYS-202610-000018/000019` → 商品 `010012` → 入库 `WL-RK-SYS-202610-000007`（台账 100/1000/10）→ 出库 `WL-CK-...-000007`（70/700）→ 调拨 `WL-DB-...-000007`（3 步审批 → 发货 A=50 + 在途 20 → 收货 B=20、在途关闭）→ 盘点 `WL-PD-...-000010/000011`（冻结 50 → 初盘 45 自动复盘 → 解冻 0）→ 盘亏 `WL-PYK-...-000003`（审批 → `Loss 9` → 45/450/10）→ 预警（安全库存 actual=45 → 处理 status=2）→ 台账/流水。SQL 直连 30 项全 true（本轮 Outbox 按单据号圈定后全 `state=2` 无 `last_error`）；14 张截图 + `smoke-result.json`；0 console/page/http 错误。④ 文档回写完成：实施记录 2B 段（七~十二节）、资产台账 §11（含 Wms 88 .cs / Stk 53 .cs、WmsOperations 15 表、审批种子 22 条）、推进计划 M-3.2 标「✅ 已完成」。⑤ 本轮修复：审批显式流程码解析（同 BizType 多流程）、盘点批次快照（`STK_BATCH_REQUIRED`）、跨模块副作用排序（单号冲突可安全重试）。
- **偏差登记**：D-2B-1~5（详见实施记录第十节与资产台账 §11.4）——显式流程码优先解析；跨模块命令独立提交故改「本地落库先行、冻结/解冻随后」；批次商品盘点逐批次成行；复盘单构造即 `Rechecking`；开发环境序号/Outbox 仍为内存实现（生产切持久化，阶段四）。遗留 5 项登记于实施记录第十一节。
