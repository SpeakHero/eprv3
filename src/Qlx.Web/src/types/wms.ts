/*
 * 所属公司：四川淇利馨集团有限公司
 * 描述：物流管理（Wms 模块）仓库域契约——后端 DTO 直通类型 + 数值枚举
 * 说明：
 *   1. 字段为扁平原始字段，与后端 camelCase JSON 一致（B-2）；枚举数值对接，中文映射见 utils/wms-enums.ts（B-4）。
 *   2. 业务 Id 一律为 string（Hashids 混淆串）。
 *   3. 仓库新建/更新入参不含仓库编码（N-1）；库区/库位编码由业务方录入（后端契约）。
 */

// ================ 枚举（数值） ================

/** 仓库启停状态（1=启用 / 2=停用）。 */
export type WarehouseStatus = 1 | 2

/** 仓库类型（1=自有 / 2=租赁 / 3=临时）。 */
export type WarehouseType = 1 | 2 | 3

// ================ 仓库 / 库区 / 库位 ================

/** 仓库扁平行（后端 WarehouseRow 直通）。 */
export interface WarehouseRow {
  id: string
  /** 仓库编码（后端生成，只读回显，N-1）。 */
  code: string
  name: string
  whType: WarehouseType
  orgId: string | null
  address: string | null
  /** 仓管员人员 Id。 */
  keeperId: string | null
  remark: string | null
  status: WarehouseStatus
  /** 停用是否已转审批挂起（仅停用请求响应为 true，状态仍为启用）。 */
  approvalRequired?: boolean
}

/** 仓库新建入参（不含仓库编码，N-1）。 */
export interface CreateWarehousePayload {
  name: string
  whType: WarehouseType
  orgId: string | null
  address: string | null
  keeperId: string | null
  remark: string | null
}

/** 仓库更新入参（编码不可修改）。 */
export interface UpdateWarehousePayload {
  name: string
  whType: WarehouseType
  orgId: string | null
  address: string | null
  keeperId: string | null
  remark: string | null
}

/** 库区扁平行。 */
export interface ZoneRow {
  id: string
  warehouseId: string
  code: string
  name: string
  sortOrder: number
  status: WarehouseStatus
}

/** 库区新建入参。 */
export interface CreateZonePayload {
  code: string
  name: string
  sortOrder: number
}

/** 库区更新入参（编码不可修改）。 */
export interface UpdateZonePayload {
  name: string
  sortOrder: number
}

/** 库位扁平行。 */
export interface LocationRow {
  id: string
  warehouseId: string
  zoneId: string
  code: string
  /** 库位条码。 */
  barcode: string | null
  sortOrder: number
  status: WarehouseStatus
}

/** 库位新建入参。 */
export interface CreateLocationPayload {
  code: string
  barcode: string | null
  sortOrder: number
}

/** 库位更新入参（编码不可修改）。 */
export interface UpdateLocationPayload {
  barcode: string | null
  sortOrder: number
}

// ================ 2B 枚举（数值，B-2-5） ================

/** 入库来源类型（1=采购 / 2=销售退货 / 3=调拨 / 4=盘盈）。 */
export type StockInSourceType = 1 | 2 | 3 | 4

/** 入库作业状态（1=待作业 / 2=作业中 / 3=已完成 / 4=已取消）。 */
export type StockInTaskStatus = 1 | 2 | 3 | 4

/** 出库来源类型（1=销售 / 2=采购退货 / 3=调拨 / 4=盘亏 / 5=领用）。 */
export type StockOutSourceType = 1 | 2 | 3 | 4 | 5

/** 出库作业状态（1=待作业 / 2=拣货中 / 3=待复核 / 4=已完成 / 5=已取消）。 */
export type StockOutTaskStatus = 1 | 2 | 3 | 4 | 5

/** 调拨单状态（1=待审批 / 2=已生效 / 3=调出已发货 / 4=调入已收货 / 5=已作废）。 */
export type StockTransferStatus = 1 | 2 | 3 | 4 | 5

/** 在途状态（1=在途 / 2=已关闭）。 */
export type InTransitStatus = 1 | 2

/** 盘点范围类型（1=全仓 / 2=品类 / 3=库位）。 */
export type StockTakeScopeType = 1 | 2 | 3

/** 盘点轮次（1=初盘 / 2=复盘）。 */
export type StockTakeRound = 1 | 2

/** 盘点单状态（1=待盘点 / 2=初盘中 / 3=初盘完成 / 4=复盘中 / 5=已完成 / 6=已取消）。 */
export type StockTakeStatus = 1 | 2 | 3 | 4 | 5 | 6

/** 盘盈盘亏类型（1=盘盈 / 2=盘亏）。 */
export type InventoryAdjustType = 1 | 2

/** 盘盈盘亏单状态（1=草稿 / 2=待审批 / 3=已通过）。 */
export type InventoryAdjustStatus = 1 | 2 | 3

/** 库存预警类型（1=安全库存 / 2=积压 / 3=临期 / 4=在途超期）。 */
export type InventoryAlertType = 1 | 2 | 3 | 4

/** 库存预警状态（1=待处理 / 2=已处理 / 3=已忽略）。 */
export type InventoryAlertStatus = 1 | 2 | 3

/** 库存命令分发状态（1=待分发 / 2=已分发 / 3=失败）。 */
export type StockCommandState = 1 | 2 | 3

// ================ 入库作业 ================

/** 入库作业头扁平行（后端 StockInTaskRow 直通）。 */
export interface StockInTaskRow {
  id: string
  /** 作业单号（后端生成，只读回显，N-1）。 */
  taskNo: string
  sourceType: StockInSourceType
  sourceDocNo: string
  warehouseId: string
  status: StockInTaskStatus
  remark: string | null
  createdAt: string
}

/** 入库作业明细扁平行。 */
export interface StockInTaskLineRow {
  id: string
  lineNo: number
  itemId: string
  expectQty: number
  countQty: number
  confirmQty: number
  locationId: string | null
  batchNo: string | null
  unitCost: number | null
}

/** 入库作业详情（头 + 扁平明细）。 */
export interface StockInTaskDetailRow {
  task: StockInTaskRow
  lines: StockInTaskLineRow[]
}

/** 新建入库作业明细入参。 */
export interface CreateStockInLinePayload {
  itemId: string
  expectQty: number
  locationId: string | null
  batchNo: string | null
  unitCost: number | null
}

/** 新建入库作业入参（不含单号，N-1）。 */
export interface CreateStockInTaskPayload {
  sourceType: StockInSourceType
  sourceDocNo: string
  warehouseId: string
  remark: string | null
  lines: CreateStockInLinePayload[]
}

/** 完成入库明细入参（清点/确认结果）。 */
export interface CompleteStockInLinePayload {
  lineId: string
  countQty: number
  confirmQty: number
  locationId: string | null
  batchNo: string | null
  unitCost: number | null
}

/** 完成入库作业入参。 */
export interface CompleteStockInTaskPayload {
  lines: CompleteStockInLinePayload[]
}

// ================ 出库作业 ================

/** 出库作业头扁平行（后端 StockOutTaskRow 直通）。 */
export interface StockOutTaskRow {
  id: string
  /** 作业单号（后端生成，只读回显，N-1）。 */
  taskNo: string
  sourceType: StockOutSourceType
  sourceDocNo: string
  warehouseId: string
  status: StockOutTaskStatus
  pickerId: string | null
  checkerId: string | null
  handoverTo: string | null
  /** 出库总金额（完成时估算，原始值）。 */
  totalAmount: number
  remark: string | null
  createdAt: string
}

/** 出库作业明细扁平行。 */
export interface StockOutTaskLineRow {
  id: string
  lineNo: number
  itemId: string
  applyQty: number
  pickQty: number
  checkQty: number
  locationId: string | null
  batchNo: string | null
}

/** FEFO 拣货建议扁平行（后端按批次效期升序求值）。 */
export interface StockOutFefoSuggestionRow {
  lineNo: number
  itemId: string
  batchNo: string
  expiryDate: string | null
  qty: number
}

/** 出库作业详情（头 + 扁平明细 + FEFO 建议）。 */
export interface StockOutTaskDetailRow {
  task: StockOutTaskRow
  lines: StockOutTaskLineRow[]
  fefoSuggestions: StockOutFefoSuggestionRow[]
}

/** 新建出库作业明细入参。 */
export interface CreateStockOutLinePayload {
  itemId: string
  applyQty: number
  locationId: string | null
  batchNo: string | null
}

/** 新建出库作业入参（不含单号，N-1）。 */
export interface CreateStockOutTaskPayload {
  sourceType: StockOutSourceType
  sourceDocNo: string
  warehouseId: string
  remark: string | null
  lines: CreateStockOutLinePayload[]
}

/** 拣货明细入参。 */
export interface PickStockOutLinePayload {
  lineId: string
  pickQty: number
  locationId: string | null
  batchNo: string | null
}

/** 拣货入参。 */
export interface PickStockOutTaskPayload {
  lines: PickStockOutLinePayload[]
}

/** 复核明细入参。 */
export interface CheckStockOutLinePayload {
  lineId: string
  checkQty: number
}

/** 复核入参。 */
export interface CheckStockOutTaskPayload {
  handoverTo: string
  lines: CheckStockOutLinePayload[]
}

/** 出库完成结果（approvalRequired=true 表示转大额审批挂起）。 */
export interface StockOutCompleteResultRow {
  task: StockOutTaskRow
  totalAmount: number
  approvalRequired: boolean
}

// ================ 调拨与在途 ================

/** 调拨单头扁平行（后端 StockTransferRow 直通）。 */
export interface StockTransferRow {
  id: string
  /** 调拨单号（后端生成，只读回显，N-1）。 */
  transferNo: string
  fromWarehouseId: string
  toWarehouseId: string
  fromOrgId: string | null
  toOrgId: string | null
  inTransitDays: number
  status: StockTransferStatus
  remark: string | null
  createdAt: string
}

/** 调拨明细扁平行。 */
export interface StockTransferLineRow {
  id: string
  lineNo: number
  itemId: string
  qty: number
  shipQty: number
  receiveQty: number
  batchNo: string | null
  unitCost: number | null
  diffNote: string | null
}

/** 调拨单详情（头 + 扁平明细）。 */
export interface StockTransferDetailRow {
  transfer: StockTransferRow
  lines: StockTransferLineRow[]
}

/** 新建调拨明细入参。 */
export interface CreateStockTransferLinePayload {
  itemId: string
  qty: number
  batchNo: string | null
}

/** 新建调拨单入参（不含单号，N-1）。 */
export interface CreateStockTransferPayload {
  fromWarehouseId: string
  toWarehouseId: string
  inTransitDays: number | null
  remark: string | null
  lines: CreateStockTransferLinePayload[]
}

/** 发货明细入参。 */
export interface ShipStockTransferLinePayload {
  lineId: string
  shipQty: number
}

/** 发货入参。 */
export interface ShipStockTransferPayload {
  lines: ShipStockTransferLinePayload[]
}

/** 收货明细入参。 */
export interface ReceiveStockTransferLinePayload {
  lineId: string
  receiveQty: number
  diffNote: string | null
}

/** 收货入参。 */
export interface ReceiveStockTransferPayload {
  lines: ReceiveStockTransferLinePayload[]
}

/** 在途库存扁平行（超期标记由前端按 expectArrive 计算，B-2）。 */
export interface InTransitStockRow {
  id: string
  transferId: string
  transferNo: string
  transferLineId: string
  itemId: string
  fromWarehouseId: string
  toWarehouseId: string
  batchNo: string | null
  qty: number
  unitCost: number
  shippedAt: string
  expectArrive: string
  status: InTransitStatus
}

// ================ 盘点 ================

/** 盘点计划扁平行（后端 StockTakePlanRow 直通）。 */
export interface StockTakePlanRow {
  id: string
  /** 计划号（后端生成，只读回显，N-1）。 */
  planNo: string
  /** 盘点日期（YYYY-MM-DD）。 */
  planDate: string
  scopeType: StockTakeScopeType
  warehouseId: string
  categoryId: string | null
  locationId: string | null
  isBlind: boolean
  remark: string | null
  /** 已生成的盘点单 Id（未生成为 null）。 */
  takeId: string | null
  createdAt: string
}

/** 新建盘点计划入参（不含计划号，N-1）。 */
export interface CreateStockTakePlanPayload {
  planDate: string
  scopeType: StockTakeScopeType
  warehouseId: string
  categoryId: string | null
  locationId: string | null
  isBlind: boolean
  remark: string | null
}

/** 盘点单头扁平行。 */
export interface StockTakeRow {
  id: string
  /** 盘点单号（后端生成，只读回显，N-1）。 */
  takeNo: string
  planId: string
  warehouseId: string
  round: StockTakeRound
  isBlind: boolean
  sourceTakeId: string | null
  status: StockTakeStatus
  remark: string | null
  createdAt: string
}

/** 盘点明细扁平行（盲盘时 bookQty / diffQty 为 null，字段级安全过滤 B-3）。 */
export interface StockTakeLineRow {
  id: string
  lineNo: number
  itemId: string
  batchNo: string | null
  bookQty: number | null
  actualQty: number | null
  diffQty: number | null
  diffReason: string | null
  frozen: boolean
}

/** 盘点单详情（头 + 扁平明细）。 */
export interface StockTakeDetailRow {
  take: StockTakeRow
  lines: StockTakeLineRow[]
}

/** 保存实盘明细入参。 */
export interface SaveStockTakeCountLinePayload {
  lineId: string
  actualQty: number
  diffReason: string | null
}

/** 保存实盘入参。 */
export interface SaveStockTakeCountsPayload {
  lines: SaveStockTakeCountLinePayload[]
}

/** 盘点完成结果（初盘有差异时回传自动生成的复盘单 Id）。 */
export interface StockTakeFinishResultRow {
  detail: StockTakeDetailRow
  recheckTakeId: string | null
}

// ================ 盘盈盘亏 ================

/** 盘盈盘亏单头扁平行（后端 InventoryAdjustRow 直通）。 */
export interface InventoryAdjustRow {
  id: string
  /** 单据号（后端生成，只读回显，N-1）。 */
  adjustNo: string
  takeId: string | null
  type: InventoryAdjustType
  warehouseId: string
  reason: string
  /** 金额估算（Σ 数量 × 当前均价，原始值）。 */
  totalAmount: number
  remark: string | null
  status: InventoryAdjustStatus
  createdAt: string
}

/** 盘盈盘亏明细扁平行。 */
export interface InventoryAdjustLineRow {
  id: string
  lineNo: number
  itemId: string
  batchNo: string | null
  qty: number
  unitCost: number | null
  remark: string | null
}

/** 盘盈盘亏单详情（头 + 扁平明细）。 */
export interface InventoryAdjustDetailRow {
  adjust: InventoryAdjustRow
  lines: InventoryAdjustLineRow[]
}

/** 新建盘盈盘亏明细入参。 */
export interface CreateInventoryAdjustLinePayload {
  itemId: string
  batchNo: string | null
  qty: number
  remark: string | null
}

/** 新建盘盈盘亏单入参（不含单号，N-1）。 */
export interface CreateInventoryAdjustPayload {
  type: InventoryAdjustType
  warehouseId: string
  reason: string
  remark: string | null
  lines: CreateInventoryAdjustLinePayload[]
}

// ================ 库存预警 ================

/** 库存预警扁平行（后端 InventoryAlertRow 直通）。 */
export interface InventoryAlertRow {
  id: string
  alertType: InventoryAlertType
  itemId: string
  warehouseId: string
  batchNo: string
  /** 判定阈值（数量或天数，原始值）。 */
  threshold: number
  /** 实际值（数量或天数，原始值）。 */
  actualValue: number
  /** 预警日期（YYYY-MM-DD）。 */
  alertDate: string
  status: InventoryAlertStatus
  handledBy: string | null
  handledNote: string | null
  handledAt: string | null
  createdAt: string
}

/** 处理预警入参（处理说明必填）。 */
export interface HandleInventoryAlertPayload {
  handledNote: string
}

// ================ 库存命令 ================

/** 库存命令 Outbox 扁平行。 */
export interface StockCommandRow {
  id: string
  idempotencyKey: string
  state: StockCommandState
  retryCount: number
  nextRetryAt: string | null
  lastError: string | null
  dispatchedAt: string | null
}
