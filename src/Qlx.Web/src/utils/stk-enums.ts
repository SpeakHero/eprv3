/*
 * 所属公司：四川淇利馨集团有限公司
 * 描述：商品与库存（Stk）展示层纯函数——数值枚举中文 / 标签色调 / 数量金额格式化 / 可用量与差额预览
 * 说明：
 *   1. 全部为无框架依赖纯函数，页面禁止重复实现（B-4）；后端只下发数值枚举与原始数值（B-2）。
 *   2. 中文映射字典唯一来源为本文件；金额 / 数量 / 差额仅用于界面呈现，不参与业务判定。
 *   3. 通用取标签与选项工具复用基础信息公共层（labelOfCode / toNumberOptions / toStringOptions）。
 */

import type { TagTone } from '@/components/crud/QlxDataTable.vue'
import type {
  CostAdjustReason,
  CostAdjustStatus,
  ItemStatus,
  OpeningStatus,
  PriceField,
  StockDirection,
  StockSourceType,
} from '@/types/stk'
import {
  formatAmount,
  labelOfCode,
  toNumberOptions,
  toStringOptions,
} from '@/utils/bas-master-mappers'

/* ============================================================
 * 一、中文映射字典（数值对接，唯一来源）
 * ============================================================ */

/** 商品启停状态中文。 */
export const ITEM_STATUS_LABELS: Record<ItemStatus, string> = {
  1: '启用',
  2: '停用',
}

/** 期初建账状态中文。 */
export const OPENING_STATUS_LABELS: Record<OpeningStatus, string> = {
  1: '草稿',
  2: '待审批',
  3: '已过账',
}

/** 成本调整状态中文。 */
export const COST_ADJUST_STATUS_LABELS: Record<CostAdjustStatus, string> = {
  1: '草稿',
  2: '待审批',
  3: '已通过',
  4: '已驳回',
}

/** 成本调整原因中文。 */
export const COST_ADJUST_REASON_LABELS: Record<CostAdjustReason, string> = {
  1: '价格差错',
  2: '盘盈亏联动',
  3: '其他',
}

/** 价格类变更字段中文。 */
export const PRICE_FIELD_LABELS: Record<PriceField, string> = {
  1: '参考成本',
  2: '指导价',
  3: '税率',
  4: '单位',
}

/** 库存变动方向中文。 */
export const STOCK_DIRECTION_LABELS: Record<StockDirection, string> = {
  1: '入库',
  2: '出库',
}

/** 库存流水来源类型中文（数值一经发布不可变更，新增类型只追加）。 */
export const STOCK_SOURCE_TYPE_LABELS: Record<StockSourceType, string> = {
  1: '期初建账',
  2: '采购入库',
  3: '销售退货入库',
  4: '调拨入库',
  5: '盘盈入库',
  6: '采购退货出库',
  7: '销售出库',
  8: '调拨出库',
  9: '盘亏出库',
  10: '领用出库',
  11: '成本调整',
}

/** 数值枚举 catalog（页面按需取用，避免各自硬编码顺序）。 */
export const STK_ENUM_OPTION_MAPS = {
  itemStatus: ITEM_STATUS_LABELS,
  openingStatus: OPENING_STATUS_LABELS,
  costAdjustStatus: COST_ADJUST_STATUS_LABELS,
  costAdjustReason: COST_ADJUST_REASON_LABELS,
  priceField: PRICE_FIELD_LABELS,
  stockDirection: STOCK_DIRECTION_LABELS,
  stockSourceType: STOCK_SOURCE_TYPE_LABELS,
} as const

/* ============================================================
 * 二、标签 / 色调（未知码兜底「—」）
 * ============================================================ */

/** 商品状态中文。 */
export const itemStatusLabel = (status: number | null | undefined): string =>
  labelOfCode(ITEM_STATUS_LABELS, status)

/** 商品状态色调。 */
export const itemStatusTone = (status: number | null | undefined): TagTone =>
  status === 1 ? 'success' : 'info'

/** 期初状态中文。 */
export const openingStatusLabel = (status: number | null | undefined): string =>
  labelOfCode(OPENING_STATUS_LABELS, status)

/** 期初状态色调（草稿灰 / 待审批橙 / 已过账绿）。 */
export function openingStatusTone(status: number | null | undefined): TagTone {
  switch (status as OpeningStatus) {
    case 1:
      return 'info'
    case 2:
      return 'warning'
    case 3:
      return 'success'
    default:
      return 'info'
  }
}

/** 成本调整状态中文。 */
export const costAdjustStatusLabel = (status: number | null | undefined): string =>
  labelOfCode(COST_ADJUST_STATUS_LABELS, status)

/** 成本调整状态色调。 */
export function costAdjustStatusTone(status: number | null | undefined): TagTone {
  switch (status as CostAdjustStatus) {
    case 1:
      return 'info'
    case 2:
      return 'warning'
    case 3:
      return 'success'
    case 4:
      return 'danger'
    default:
      return 'info'
  }
}

/** 成本调整原因中文。 */
export const costAdjustReasonLabel = (reason: number | null | undefined): string =>
  labelOfCode(COST_ADJUST_REASON_LABELS, reason)

/** 价格类变更字段中文。 */
export const priceFieldLabel = (field: number | null | undefined): string =>
  labelOfCode(PRICE_FIELD_LABELS, field)

/** 库存方向中文。 */
export const stockDirectionLabel = (direction: number | null | undefined): string =>
  labelOfCode(STOCK_DIRECTION_LABELS, direction)

/** 库存方向色调（入库绿 / 出库橙）。 */
export const stockDirectionTone = (direction: number | null | undefined): TagTone =>
  direction === 1 ? 'success' : 'warning'

/** 库存来源类型中文。 */
export const stockSourceTypeLabel = (sourceType: number | null | undefined): string =>
  labelOfCode(STOCK_SOURCE_TYPE_LABELS, sourceType)

/** 数值枚举 → 表单下拉选项。 */
export { toNumberOptions, toStringOptions }

/* ============================================================
 * 三、格式化（数量 / 单价 / 金额 / 税率）
 * ============================================================ */

/** 数量 → 最多 4 位小数并去尾零（后端 18,4）。 */
export function formatQty(value: number | null | undefined, placeholder = '—'): string {
  if (value === null || value === undefined || Number.isNaN(value)) {
    return placeholder
  }
  return new Intl.NumberFormat('zh-CN', {
    minimumFractionDigits: 0,
    maximumFractionDigits: 4,
  }).format(value)
}

/** 单价 → 2~4 位小数（后端 18,4）。 */
export function formatCost(value: number | null | undefined, placeholder = '—'): string {
  if (value === null || value === undefined || Number.isNaN(value)) {
    return placeholder
  }
  return new Intl.NumberFormat('zh-CN', {
    minimumFractionDigits: 2,
    maximumFractionDigits: 4,
  }).format(value)
}

/** 金额 → 千分位 + 2 位小数。 */
export function formatMoney(value: number | null | undefined, placeholder = '—'): string {
  return formatAmount(value, 2, placeholder)
}

/** 税率（0~1 小数）→ 百分比文案（如 0.13 → 13%）。 */
export function formatTaxRate(value: number | null | undefined, placeholder = '—'): string {
  if (value === null || value === undefined || Number.isNaN(value)) {
    return placeholder
  }
  return `${new Intl.NumberFormat('zh-CN', { maximumFractionDigits: 4 }).format(value * 100)}%`
}

/* ============================================================
 * 四、展示计算（可用量 / 明细金额 / 成本差额预览）
 * ============================================================ */

/** 可用量 = 现有量 − 冻结量（仅展示，真实校验以服务端为准，B-4）。 */
export function availableQty(qtyOnhand: number, qtyLocked: number): number {
  return qtyOnhand - qtyLocked
}

/** 明细金额预览 = 数量 × 单价（仅展示，最终金额以服务端计算为准，V-1）。 */
export function previewAmount(qty: number, unitCost: number): number {
  if (!Number.isFinite(qty) || !Number.isFinite(unitCost)) {
    return 0
  }
  return Math.round(qty * unitCost * 100) / 100
}

/** 成本调整差额预览 = (新均价 − 旧均价) × 台账数量，保留 2 位小数（服务端复核，V-1）。 */
export function previewDiffAmount(newAvgCost: number, oldAvgCost: number, qty: number): number {
  if (!Number.isFinite(newAvgCost) || !Number.isFinite(oldAvgCost) || !Number.isFinite(qty)) {
    return 0
  }
  return Math.round((newAvgCost - oldAvgCost) * qty * 100) / 100
}
