/*
 * 所属公司：四川淇利馨集团有限公司
 * 描述：物流管理（Wms）展示层纯函数——仓库 / 作业 / 调拨 / 盘点 / 盘盈亏 / 预警 / 命令 数值枚举中文与标签色调
 * 说明：后端只下发数值枚举（B-2-5），中文映射唯一来源本文件；通用取标签工具复用基础信息公共层。
 */

import type { TagTone } from '@/components/crud/QlxDataTable.vue'
import type {
  InTransitStatus,
  InventoryAdjustStatus,
  InventoryAdjustType,
  InventoryAlertStatus,
  InventoryAlertType,
  StockCommandState,
  StockInSourceType,
  StockInTaskStatus,
  StockOutSourceType,
  StockOutTaskStatus,
  StockTakeRound,
  StockTakeScopeType,
  StockTakeStatus,
  StockTransferStatus,
  WarehouseStatus,
  WarehouseType,
} from '@/types/wms'
import { labelOfCode, toNumberOptions, toStringOptions } from '@/utils/bas-master-mappers'

/** 仓库类型中文。 */
export const WH_TYPE_LABELS: Record<WarehouseType, string> = {
  1: '自有仓',
  2: '租赁仓',
  3: '临时仓',
}

/** 仓库启停状态中文。 */
export const WAREHOUSE_STATUS_LABELS: Record<WarehouseStatus, string> = {
  1: '启用',
  2: '停用',
}

/* ---------------- 入库 ---------------- */

/** 入库来源类型中文。 */
export const STOCK_IN_SOURCE_TYPE_LABELS: Record<StockInSourceType, string> = {
  1: '采购入库',
  2: '销售退货',
  3: '调拨入库',
  4: '盘盈入库',
}

/** 入库作业状态中文。 */
export const STOCK_IN_TASK_STATUS_LABELS: Record<StockInTaskStatus, string> = {
  1: '待作业',
  2: '作业中',
  3: '已完成',
  4: '已取消',
}

/* ---------------- 出库 ---------------- */

/** 出库来源类型中文。 */
export const STOCK_OUT_SOURCE_TYPE_LABELS: Record<StockOutSourceType, string> = {
  1: '销售出库',
  2: '采购退货',
  3: '调拨出库',
  4: '盘亏出库',
  5: '领用出库',
}

/** 出库作业状态中文。 */
export const STOCK_OUT_TASK_STATUS_LABELS: Record<StockOutTaskStatus, string> = {
  1: '待作业',
  2: '拣货中',
  3: '待复核',
  4: '已完成',
  5: '已取消',
}

/* ---------------- 调拨 / 在途 ---------------- */

/** 调拨单状态中文。 */
export const STOCK_TRANSFER_STATUS_LABELS: Record<StockTransferStatus, string> = {
  1: '待审批',
  2: '已生效',
  3: '调出已发货',
  4: '调入已收货',
  5: '已作废',
}

/** 在途状态中文。 */
export const IN_TRANSIT_STATUS_LABELS: Record<InTransitStatus, string> = {
  1: '在途',
  2: '已关闭',
}

/* ---------------- 盘点 ---------------- */

/** 盘点范围类型中文。 */
export const STOCK_TAKE_SCOPE_TYPE_LABELS: Record<StockTakeScopeType, string> = {
  1: '全仓',
  2: '品类',
  3: '库位',
}

/** 盘点轮次中文。 */
export const STOCK_TAKE_ROUND_LABELS: Record<StockTakeRound, string> = {
  1: '初盘',
  2: '复盘',
}

/** 盘点单状态中文。 */
export const STOCK_TAKE_STATUS_LABELS: Record<StockTakeStatus, string> = {
  1: '待盘点',
  2: '初盘中',
  3: '初盘完成',
  4: '复盘中',
  5: '已完成',
  6: '已取消',
}

/* ---------------- 盘盈盘亏 ---------------- */

/** 盘盈盘亏类型中文。 */
export const INVENTORY_ADJUST_TYPE_LABELS: Record<InventoryAdjustType, string> = {
  1: '盘盈',
  2: '盘亏',
}

/** 盘盈盘亏单状态中文。 */
export const INVENTORY_ADJUST_STATUS_LABELS: Record<InventoryAdjustStatus, string> = {
  1: '草稿',
  2: '待审批',
  3: '已通过',
}

/* ---------------- 库存预警 ---------------- */

/** 库存预警类型中文。 */
export const INVENTORY_ALERT_TYPE_LABELS: Record<InventoryAlertType, string> = {
  1: '安全库存',
  2: '积压',
  3: '临期',
  4: '在途超期',
}

/** 库存预警状态中文。 */
export const INVENTORY_ALERT_STATUS_LABELS: Record<InventoryAlertStatus, string> = {
  1: '待处理',
  2: '已处理',
  3: '已忽略',
}

/* ---------------- 库存命令 ---------------- */

/** 库存命令分发状态中文。 */
export const STOCK_COMMAND_STATE_LABELS: Record<StockCommandState, string> = {
  1: '待分发',
  2: '已分发',
  3: '失败',
}

/** 数值枚举 catalog（页面按需取用）。 */
export const WMS_ENUM_OPTION_MAPS = {
  whType: WH_TYPE_LABELS,
  warehouseStatus: WAREHOUSE_STATUS_LABELS,
  stockInSourceType: STOCK_IN_SOURCE_TYPE_LABELS,
  stockInTaskStatus: STOCK_IN_TASK_STATUS_LABELS,
  stockOutSourceType: STOCK_OUT_SOURCE_TYPE_LABELS,
  stockOutTaskStatus: STOCK_OUT_TASK_STATUS_LABELS,
  stockTransferStatus: STOCK_TRANSFER_STATUS_LABELS,
  inTransitStatus: IN_TRANSIT_STATUS_LABELS,
  stockTakeScopeType: STOCK_TAKE_SCOPE_TYPE_LABELS,
  stockTakeRound: STOCK_TAKE_ROUND_LABELS,
  stockTakeStatus: STOCK_TAKE_STATUS_LABELS,
  inventoryAdjustType: INVENTORY_ADJUST_TYPE_LABELS,
  inventoryAdjustStatus: INVENTORY_ADJUST_STATUS_LABELS,
  inventoryAlertType: INVENTORY_ALERT_TYPE_LABELS,
  inventoryAlertStatus: INVENTORY_ALERT_STATUS_LABELS,
  stockCommandState: STOCK_COMMAND_STATE_LABELS,
} as const

/** 仓库类型中文（未知码兜底「—」）。 */
export const whTypeLabel = (whType: number | null | undefined): string =>
  labelOfCode(WH_TYPE_LABELS, whType)

/** 仓库类型色调。 */
export const whTypeTone = (whType: number | null | undefined): TagTone =>
  whType === 1 ? 'primary' : 'info'

/** 仓库启停状态中文。 */
export const warehouseStatusLabel = (status: number | null | undefined): string =>
  labelOfCode(WAREHOUSE_STATUS_LABELS, status)

/** 仓库启停状态色调。 */
export const warehouseStatusTone = (status: number | null | undefined): TagTone =>
  status === 1 ? 'success' : 'info'

/* ---------------- 入库 / 出库 ---------------- */

/** 入库来源类型中文。 */
export const stockInSourceTypeLabel = (code: number | null | undefined): string =>
  labelOfCode(STOCK_IN_SOURCE_TYPE_LABELS, code)

/** 入库来源类型色调（采购为主色，盘盈为成功色）。 */
export const stockInSourceTypeTone = (code: number | null | undefined): TagTone => {
  if (code === 1) {
    return 'primary'
  }
  return code === 4 ? 'success' : 'info'
}

/** 入库作业状态中文。 */
export const stockInTaskStatusLabel = (code: number | null | undefined): string =>
  labelOfCode(STOCK_IN_TASK_STATUS_LABELS, code)

/** 入库作业状态色调。 */
export const stockInTaskStatusTone = (code: number | null | undefined): TagTone => {
  if (code === 3) {
    return 'success'
  }
  if (code === 1) {
    return 'warning'
  }
  return code === 2 ? 'primary' : 'info'
}

/** 出库来源类型中文。 */
export const stockOutSourceTypeLabel = (code: number | null | undefined): string =>
  labelOfCode(STOCK_OUT_SOURCE_TYPE_LABELS, code)

/** 出库来源类型色调（盘亏为警示色）。 */
export const stockOutSourceTypeTone = (code: number | null | undefined): TagTone => {
  if (code === 1) {
    return 'primary'
  }
  return code === 4 ? 'warning' : 'info'
}

/** 出库作业状态中文。 */
export const stockOutTaskStatusLabel = (code: number | null | undefined): string =>
  labelOfCode(STOCK_OUT_TASK_STATUS_LABELS, code)

/** 出库作业状态色调。 */
export const stockOutTaskStatusTone = (code: number | null | undefined): TagTone => {
  if (code === 4) {
    return 'success'
  }
  if (code === 1 || code === 3) {
    return 'warning'
  }
  return code === 2 ? 'primary' : 'info'
}

/* ---------------- 调拨 / 在途 ---------------- */

/** 调拨单状态中文。 */
export const stockTransferStatusLabel = (code: number | null | undefined): string =>
  labelOfCode(STOCK_TRANSFER_STATUS_LABELS, code)

/** 调拨单状态色调。 */
export const stockTransferStatusTone = (code: number | null | undefined): TagTone => {
  if (code === 4) {
    return 'success'
  }
  if (code === 1) {
    return 'warning'
  }
  return code === 2 || code === 3 ? 'primary' : 'info'
}

/** 在途状态中文。 */
export const inTransitStatusLabel = (code: number | null | undefined): string =>
  labelOfCode(IN_TRANSIT_STATUS_LABELS, code)

/** 在途状态色调。 */
export const inTransitStatusTone = (code: number | null | undefined): TagTone =>
  code === 2 ? 'success' : 'primary'

/* ---------------- 盘点 ---------------- */

/** 盘点范围类型中文。 */
export const stockTakeScopeTypeLabel = (code: number | null | undefined): string =>
  labelOfCode(STOCK_TAKE_SCOPE_TYPE_LABELS, code)

/** 盘点范围类型色调。 */
export const stockTakeScopeTypeTone = (code: number | null | undefined): TagTone =>
  code === 1 ? 'primary' : 'info'

/** 盘点轮次中文。 */
export const stockTakeRoundLabel = (code: number | null | undefined): string =>
  labelOfCode(STOCK_TAKE_ROUND_LABELS, code)

/** 盘点轮次色调（复盘为警示色）。 */
export const stockTakeRoundTone = (code: number | null | undefined): TagTone =>
  code === 2 ? 'warning' : 'primary'

/** 盘点单状态中文。 */
export const stockTakeStatusLabel = (code: number | null | undefined): string =>
  labelOfCode(STOCK_TAKE_STATUS_LABELS, code)

/** 盘点单状态色调。 */
export const stockTakeStatusTone = (code: number | null | undefined): TagTone => {
  if (code === 5) {
    return 'success'
  }
  if (code === 1 || code === 4) {
    return 'warning'
  }
  return code === 2 || code === 3 ? 'primary' : 'info'
}

/* ---------------- 盘盈盘亏 ---------------- */

/** 盘盈盘亏类型中文。 */
export const inventoryAdjustTypeLabel = (code: number | null | undefined): string =>
  labelOfCode(INVENTORY_ADJUST_TYPE_LABELS, code)

/** 盘盈盘亏类型色调（盘亏为危险色）。 */
export const inventoryAdjustTypeTone = (code: number | null | undefined): TagTone =>
  code === 2 ? 'danger' : 'success'

/** 盘盈盘亏单状态中文。 */
export const inventoryAdjustStatusLabel = (code: number | null | undefined): string =>
  labelOfCode(INVENTORY_ADJUST_STATUS_LABELS, code)

/** 盘盈盘亏单状态色调。 */
export const inventoryAdjustStatusTone = (code: number | null | undefined): TagTone => {
  if (code === 3) {
    return 'success'
  }
  return code === 2 ? 'warning' : 'info'
}

/* ---------------- 库存预警 ---------------- */

/** 库存预警类型中文。 */
export const inventoryAlertTypeLabel = (code: number | null | undefined): string =>
  labelOfCode(INVENTORY_ALERT_TYPE_LABELS, code)

/** 库存预警类型色调（安全库存 / 在途超期为危险色）。 */
export const inventoryAlertTypeTone = (code: number | null | undefined): TagTone => {
  if (code === 1 || code === 4) {
    return 'danger'
  }
  return code === 2 || code === 3 ? 'warning' : 'info'
}

/** 库存预警状态中文。 */
export const inventoryAlertStatusLabel = (code: number | null | undefined): string =>
  labelOfCode(INVENTORY_ALERT_STATUS_LABELS, code)

/** 库存预警状态色调。 */
export const inventoryAlertStatusTone = (code: number | null | undefined): TagTone => {
  if (code === 2) {
    return 'success'
  }
  return code === 1 ? 'warning' : 'info'
}

/* ---------------- 库存命令 ---------------- */

/** 库存命令分发状态中文。 */
export const stockCommandStateLabel = (code: number | null | undefined): string =>
  labelOfCode(STOCK_COMMAND_STATE_LABELS, code)

/** 库存命令分发状态色调。 */
export const stockCommandStateTone = (code: number | null | undefined): TagTone => {
  if (code === 2) {
    return 'success'
  }
  return code === 3 ? 'danger' : 'warning'
}

/** 数值枚举 → 表单下拉选项。 */
export { toNumberOptions, toStringOptions }
