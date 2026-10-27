/*
 * 所属公司：四川淇利馨集团有限公司
 * 描述：Stk 展示层纯函数单元测试——枚举映射完整性 / 未知码兜底 / 数量金额格式化 / 展示计算
 */

import { describe, expect, it } from 'vitest'
import {
  availableQty,
  COST_ADJUST_REASON_LABELS,
  COST_ADJUST_STATUS_LABELS,
  costAdjustReasonLabel,
  costAdjustStatusLabel,
  costAdjustStatusTone,
  formatCost,
  formatMoney,
  formatQty,
  formatTaxRate,
  ITEM_STATUS_LABELS,
  itemStatusLabel,
  itemStatusTone,
  OPENING_STATUS_LABELS,
  openingStatusLabel,
  openingStatusTone,
  PRICE_FIELD_LABELS,
  priceFieldLabel,
  previewAmount,
  previewDiffAmount,
  STOCK_DIRECTION_LABELS,
  STOCK_SOURCE_TYPE_LABELS,
  stockDirectionLabel,
  stockDirectionTone,
  stockSourceTypeLabel,
  toNumberOptions,
  toStringOptions,
} from '../stk-enums'

describe('枚举映射完整性', () => {
  it('商品 / 期初 / 成本调整 / 价格字段 / 方向字典覆盖全部数值码', () => {
    expect(Object.keys(ITEM_STATUS_LABELS)).toHaveLength(2)
    expect(Object.keys(OPENING_STATUS_LABELS)).toHaveLength(3)
    expect(Object.keys(COST_ADJUST_STATUS_LABELS)).toHaveLength(4)
    expect(Object.keys(COST_ADJUST_REASON_LABELS)).toHaveLength(3)
    expect(Object.keys(PRICE_FIELD_LABELS)).toHaveLength(4)
    expect(Object.keys(STOCK_DIRECTION_LABELS)).toHaveLength(2)
    expect(Object.keys(STOCK_SOURCE_TYPE_LABELS)).toHaveLength(11)
    for (const map of [
      ITEM_STATUS_LABELS,
      OPENING_STATUS_LABELS,
      COST_ADJUST_STATUS_LABELS,
      COST_ADJUST_REASON_LABELS,
      PRICE_FIELD_LABELS,
      STOCK_DIRECTION_LABELS,
      STOCK_SOURCE_TYPE_LABELS,
    ]) {
      for (const label of Object.values(map)) {
        expect(label.trim().length).toBeGreaterThan(0)
      }
    }
  })

  it('来源类型 1~11 全部有中文文案', () => {
    const expected = [
      '期初建账',
      '采购入库',
      '销售退货入库',
      '调拨入库',
      '盘盈入库',
      '采购退货出库',
      '销售出库',
      '调拨出库',
      '盘亏出库',
      '领用出库',
      '成本调整',
    ]
    expected.forEach((label, index) => {
      expect(STOCK_SOURCE_TYPE_LABELS[(index + 1) as keyof typeof STOCK_SOURCE_TYPE_LABELS]).toBe(
        label,
      )
    })
  })
})

describe('标签函数与未知码兜底', () => {
  it('已知码返回中文，未知码 / 空值兜底「—」', () => {
    expect(itemStatusLabel(1)).toBe('启用')
    expect(itemStatusLabel(2)).toBe('停用')
    expect(itemStatusLabel(99)).toBe('—')
    expect(itemStatusLabel(null)).toBe('—')

    expect(openingStatusLabel(3)).toBe('已过账')
    expect(openingStatusLabel(99)).toBe('—')

    expect(costAdjustStatusLabel(4)).toBe('已驳回')
    expect(costAdjustStatusLabel(99)).toBe('—')

    expect(costAdjustReasonLabel(1)).toBe('价格差错')
    expect(costAdjustReasonLabel(99)).toBe('—')

    expect(priceFieldLabel(2)).toBe('指导价')
    expect(priceFieldLabel(99)).toBe('—')

    expect(stockDirectionLabel(1)).toBe('入库')
    expect(stockDirectionLabel(99)).toBe('—')

    expect(stockSourceTypeLabel(11)).toBe('成本调整')
    expect(stockSourceTypeLabel(99)).toBe('—')
  })

  it('状态色调按业务语义映射', () => {
    expect(itemStatusTone(1)).toBe('success')
    expect(itemStatusTone(2)).toBe('info')
    expect(openingStatusTone(1)).toBe('info')
    expect(openingStatusTone(2)).toBe('warning')
    expect(openingStatusTone(3)).toBe('success')
    expect(costAdjustStatusTone(1)).toBe('info')
    expect(costAdjustStatusTone(2)).toBe('warning')
    expect(costAdjustStatusTone(3)).toBe('success')
    expect(costAdjustStatusTone(4)).toBe('danger')
    expect(stockDirectionTone(1)).toBe('success')
    expect(stockDirectionTone(2)).toBe('warning')
  })

  it('枚举选项工具保持数值 / 字符串两种 value 形态', () => {
    expect(toNumberOptions(ITEM_STATUS_LABELS)).toEqual([
      { label: '启用', value: 1 },
      { label: '停用', value: 2 },
    ])
    expect(toStringOptions(ITEM_STATUS_LABELS)).toEqual([
      { label: '启用', value: '1' },
      { label: '停用', value: '2' },
    ])
  })
})

describe('数量 / 单价 / 金额 / 税率格式化（前端展示职责，B-4）', () => {
  it('数量最多 4 位小数并去尾零', () => {
    expect(formatQty(12.5)).toBe('12.5')
    expect(formatQty(1234.5)).toBe('1,234.5')
    expect(formatQty(0.1234)).toBe('0.1234')
    expect(formatQty(null)).toBe('—')
    expect(formatQty(undefined)).toBe('—')
  })

  it('单价 2~4 位小数、金额千分位 2 位', () => {
    expect(formatCost(1.5)).toBe('1.50')
    expect(formatCost(3.33333)).toBe('3.3333')
    expect(formatCost(null)).toBe('—')
    expect(formatMoney(1234.5)).toBe('1,234.50')
    expect(formatMoney(null)).toBe('—')
  })

  it('税率 0~1 小数转百分比文案', () => {
    expect(formatTaxRate(0.13)).toBe('13%')
    expect(formatTaxRate(0.135)).toBe('13.5%')
    expect(formatTaxRate(null)).toBe('—')
    expect(formatTaxRate(undefined)).toBe('—')
  })
})

describe('展示计算（可用量 / 金额预览 / 差额预览）', () => {
  it('可用量 = 现有量 − 冻结量', () => {
    expect(availableQty(10, 3)).toBe(7)
    expect(availableQty(0, 0)).toBe(0)
  })

  it('明细金额预览保留 2 位小数', () => {
    expect(previewAmount(2.5, 3.3333)).toBe(8.33)
    expect(previewAmount(10, 5)).toBe(50)
    expect(previewAmount(Number.POSITIVE_INFINITY, 1)).toBe(0)
  })

  it('成本调整差额预览 = (新均价 − 旧均价) × 数量', () => {
    expect(previewDiffAmount(12.2, 10, 5)).toBe(11)
    expect(previewDiffAmount(10, 10, 8)).toBe(0)
    expect(previewDiffAmount(9, 10, 5)).toBe(-5)
    expect(previewDiffAmount(Number.NaN, 10, 5)).toBe(0)
  })
})
