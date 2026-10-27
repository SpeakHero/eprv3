/*
 * 所属公司：四川淇利馨集团有限公司
 * 描述：商品与库存（Stk 模块）契约——后端 DTO 直通类型 + 数值枚举
 * 说明：
 *   1. 字段为扁平原始字段，与后端 camelCase JSON 一致（B-2）；枚举一律数值对接，中文映射见 utils/stk-enums.ts（B-4）。
 *   2. 业务 Id 一律为 string（Hashids 混淆串）；时间字段为 ISO 字符串，日期字段为 'YYYY-MM-DD' 字符串。
 *   3. 新建/更新入参一律不含编码/单号（编码由后端生成，N-1），编码仅作只读回显。
 *   4. 成本字段在无 stk.cost.field 权限时后端置 null（B-3-1）。
 */

// ================ 枚举（数值） ================

/** 商品启停状态（1=启用 / 2=停用）。 */
export type ItemStatus = 1 | 2

/** 期初建账状态（1=草稿 / 2=待审批 / 3=已过账）。 */
export type OpeningStatus = 1 | 2 | 3

/** 成本调整状态（1=草稿 / 2=待审批 / 3=已通过 / 4=已驳回）。 */
export type CostAdjustStatus = 1 | 2 | 3 | 4

/** 成本调整原因（1=价格差错 / 2=盘盈亏联动 / 3=其他）。 */
export type CostAdjustReason = 1 | 2 | 3

/** 商品价格类变更字段（1=参考成本 / 2=指导价 / 3=税率 / 4=单位）。 */
export type PriceField = 1 | 2 | 3 | 4

/** 库存变动方向（1=入库 / 2=出库）。 */
export type StockDirection = 1 | 2

/** 库存流水来源类型（1~11，数值一经发布不可变更）。 */
export type StockSourceType = 1 | 2 | 3 | 4 | 5 | 6 | 7 | 8 | 9 | 10 | 11

// ================ 商品档案 ================

/** 商品扁平行（后端 ItemRow 直通）。 */
export interface ItemRow {
  id: string
  /** 商品编码（后端生成，只读回显，N-1）。 */
  itemCode: string
  name: string
  spec: string | null
  barcode: string | null
  categoryId: string
  unitId: string
  /** 批次管理。 */
  batchEnabled: boolean
  /** 有效期管理。 */
  expiryEnabled: boolean
  /** 税率（原始数值，展示格式化为前端职责）。 */
  taxRate: number
  /** 参考成本价（无成本字段权限时为 null）。 */
  refCostPrice: number | null
  /** 指导价（无成本字段权限时为 null）。 */
  guidePrice: number | null
  status: ItemStatus
}

/** 新建商品入参（不含商品编码，N-1）。 */
export interface CreateItemPayload {
  name: string
  spec: string | null
  barcode: string | null
  categoryId: string
  unitId: string
  batchEnabled: boolean
  expiryEnabled: boolean
  taxRate: number
  refCostPrice: number | null
  guidePrice: number | null
}

/** 更新商品入参（不含商品编码，编码不可修改）。 */
export interface UpdateItemPayload {
  name: string
  spec: string | null
  barcode: string | null
  categoryId: string
  unitId: string
  batchEnabled: boolean
  expiryEnabled: boolean
  taxRate: number
  refCostPrice: number | null
  guidePrice: number | null
}

/** 审批挂起请求行（新建 / 关键属性变更 / 停用申请共用）。 */
export interface StkApprovalRequestRow {
  id: string
  /** 审批实例 Id（发起审批前为 null）。 */
  approvalInstanceId: string | null
  bizType: string
  bizId: string
  /** 请求状态数值（1=待审批 / 2=通过 / 3=驳回 / 4=撤回）。 */
  status: number
}

/** 商品更新结果：直接生效返回扁平行，需审批返回挂起请求。 */
export interface UpdateItemResult {
  pendingApproval: boolean
  row: ItemRow | null
  request: StkApprovalRequestRow | null
}

/** 商品价格类变更日志扁平行。 */
export interface ItemPriceLogRow {
  id: string
  itemId: string
  /** 变更字段（见 PriceField 数值枚举）。 */
  field: PriceField
  oldValue: string | null
  newValue: string | null
  approvedBy: string | null
  createdAt: string
}

// ================ 期初建账 ================

/** 期初建账单头扁平行。 */
export interface OpeningBatchRow {
  id: string
  /** 期初单号（后端生成，只读）。 */
  batchNo: string
  warehouseId: string
  status: OpeningStatus
  postedAt: string | null
  remark: string | null
}

/** 期初建账明细扁平行（金额由后端计算）。 */
export interface OpeningItemRow {
  id: string
  itemId: string
  /** 批次号（非批次商品为 null）。 */
  batchNo: string | null
  qty: number
  unitCost: number
  amount: number
}

/** 期初建账详情（表头 + 明细）。 */
export interface OpeningBatchDetailRow {
  header: OpeningBatchRow
  items: OpeningItemRow[]
}

/** 期初明细入参（不含金额，金额由后端计算，V-1）。 */
export interface OpeningItemPayload {
  itemId: string
  batchNo: string | null
  qty: number
  unitCost: number
}

/** 新建 / 更新期初草稿入参（不含期初单号，N-1）。 */
export interface SaveOpeningPayload {
  warehouseId: string
  remark: string | null
  lines: OpeningItemPayload[]
}

/** 期初仓库汇总扁平行（仅已过账明细，原始数值）。 */
export interface OpeningWarehouseSummaryRow {
  warehouseId: string
  totalQty: number
  totalAmount: number
}

// ================ 成本调整 ================

/** 成本调整单头扁平行。 */
export interface CostAdjustRow {
  id: string
  /** 成本调整单号（后端生成，只读）。 */
  adjNo: string
  reasonCode: CostAdjustReason
  /** 仓库 Id（null 表示全部仓库）。 */
  warehouseId: string | null
  /** 差额合计（后端计算）。 */
  totalDiffAmount: number
  status: CostAdjustStatus
  appliedAt: string | null
  remark: string | null
}

/** 成本调整明细扁平行（旧均价 / 差额由后端取值计算）。 */
export interface CostAdjustLineRow {
  id: string
  itemId: string
  warehouseId: string
  oldAvgCost: number
  newAvgCost: number
  diffAmount: number
}

/** 成本调整详情（表头 + 明细）。 */
export interface CostAdjustDetailRow {
  header: CostAdjustRow
  lines: CostAdjustLineRow[]
}

/** 成本调整明细入参（提交只传商品、仓库与目标均价，V-1）。 */
export interface CostAdjustLinePayload {
  itemId: string
  warehouseId: string
  newAvgCost: number
}

/** 新建 / 更新成本调整草稿入参（不含单号，N-1）。 */
export interface SaveCostAdjustPayload {
  reasonCode: CostAdjustReason
  warehouseId: string | null
  lines: CostAdjustLinePayload[]
  remark: string | null
}

// ================ 库存台账 / 流水 / 批次 ================

/** 库存台账余额扁平行（无成本权限时 avgCost / amount 为 null）。 */
export interface StockBalanceRow {
  itemId: string
  warehouseId: string
  qtyOnhand: number
  avgCost: number | null
  amount: number | null
  qtyLocked: number
  lastTxnAt: string | null
}

/** 库存台账表格行（前端补行键 itemId-warehouseId，QlxDataTable 需要 id）。 */
export type StockBalanceTableRow = StockBalanceRow & { id: string }

/** 库存流水扁平行（只读，流水不可变）。 */
export interface StockTxnRow {
  id: string
  txnNo: string
  sourceType: StockSourceType
  sourceDocNo: string
  sourceLineNo: number
  itemId: string
  warehouseId: string
  batchNo: string | null
  direction: StockDirection
  qty: number
  unitCost: number | null
  amount: number | null
  balanceQtyAfter: number
  balanceAmountAfter: number | null
  operatorId: string | null
  occurredAt: string
}

/** 批次库存扁平行（FEFO 顺序由后端返回，前端不重排）。 */
export interface StockBatchRow {
  itemId: string
  warehouseId: string
  batchNo: string
  produceDate: string | null
  expiryDate: string | null
  qty: number
  /** 批次均价（无成本权限时为 null）。 */
  avgCost: number | null
  /** 批次金额（无成本权限时为 null）。 */
  amount: number | null
}

/** 批次库存表格行（前端补行键，QlxDataTable 需要 id）。 */
export type StockBatchTableRow = StockBatchRow & { id: string }

/** 库存流水查询条件（时间区间为 ISO 字符串）。 */
export interface StockTxnQuery {
  itemId?: string
  warehouseId?: string
  sourceType?: number
  from?: string
  to?: string
  take?: number
}
