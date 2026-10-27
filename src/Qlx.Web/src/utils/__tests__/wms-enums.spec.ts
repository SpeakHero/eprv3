/*
 * 所属公司：四川淇利馨集团有限公司
 * 描述：Wms 展示层纯函数单元测试——仓库类型 / 启停状态映射完整性与未知码兜底
 */

import { describe, expect, it } from 'vitest'
import {
  toNumberOptions,
  toStringOptions,
  WAREHOUSE_STATUS_LABELS,
  warehouseStatusLabel,
  warehouseStatusTone,
  WH_TYPE_LABELS,
  whTypeLabel,
  whTypeTone,
  WMS_ENUM_OPTION_MAPS,
} from '../wms-enums'

describe('仓库枚举映射', () => {
  it('仓库类型 1~3 / 启停状态 1~2 均有中文文案', () => {
    expect(WH_TYPE_LABELS[1]).toBe('自有仓')
    expect(WH_TYPE_LABELS[2]).toBe('租赁仓')
    expect(WH_TYPE_LABELS[3]).toBe('临时仓')
    expect(Object.keys(WH_TYPE_LABELS)).toHaveLength(3)

    expect(WAREHOUSE_STATUS_LABELS[1]).toBe('启用')
    expect(WAREHOUSE_STATUS_LABELS[2]).toBe('停用')
    expect(Object.keys(WAREHOUSE_STATUS_LABELS)).toHaveLength(2)
  })

  it('枚举 catalog 汇总两个字典', () => {
    expect(WMS_ENUM_OPTION_MAPS.whType).toBe(WH_TYPE_LABELS)
    expect(WMS_ENUM_OPTION_MAPS.warehouseStatus).toBe(WAREHOUSE_STATUS_LABELS)
  })

  it('标签函数对未知码 / 空值兜底「—」', () => {
    expect(whTypeLabel(1)).toBe('自有仓')
    expect(whTypeLabel(99)).toBe('—')
    expect(whTypeLabel(null)).toBe('—')
    expect(warehouseStatusLabel(2)).toBe('停用')
    expect(warehouseStatusLabel(99)).toBe('—')
  })

  it('标签色调按业务语义映射', () => {
    expect(whTypeTone(1)).toBe('primary')
    expect(whTypeTone(2)).toBe('info')
    expect(whTypeTone(3)).toBe('info')
    expect(warehouseStatusTone(1)).toBe('success')
    expect(warehouseStatusTone(2)).toBe('info')
  })

  it('选项工具保持数值 / 字符串两种 value 形态', () => {
    expect(toNumberOptions(WH_TYPE_LABELS)).toEqual([
      { label: '自有仓', value: 1 },
      { label: '租赁仓', value: 2 },
      { label: '临时仓', value: 3 },
    ])
    expect(toStringOptions(WAREHOUSE_STATUS_LABELS)).toEqual([
      { label: '启用', value: '1' },
      { label: '停用', value: '2' },
    ])
  })
})
