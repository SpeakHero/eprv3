/*
 * 所属公司：四川淇利馨集团有限公司
 * 描述：Wms 2B 展示层纯函数单元测试——作业 / 调拨 / 盘点 / 盘盈亏 / 预警 / 命令枚举完整性与色调语义
 */

import { describe, expect, it } from 'vitest'
import {
  IN_TRANSIT_STATUS_LABELS,
  INVENTORY_ADJUST_STATUS_LABELS,
  INVENTORY_ADJUST_TYPE_LABELS,
  INVENTORY_ALERT_STATUS_LABELS,
  INVENTORY_ALERT_TYPE_LABELS,
  inventoryAdjustStatusLabel,
  inventoryAdjustStatusTone,
  inventoryAdjustTypeLabel,
  inventoryAdjustTypeTone,
  inventoryAlertStatusLabel,
  inventoryAlertStatusTone,
  inventoryAlertTypeLabel,
  inventoryAlertTypeTone,
  inTransitStatusLabel,
  inTransitStatusTone,
  STOCK_COMMAND_STATE_LABELS,
  STOCK_IN_SOURCE_TYPE_LABELS,
  STOCK_IN_TASK_STATUS_LABELS,
  STOCK_OUT_SOURCE_TYPE_LABELS,
  STOCK_OUT_TASK_STATUS_LABELS,
  STOCK_TAKE_ROUND_LABELS,
  STOCK_TAKE_SCOPE_TYPE_LABELS,
  STOCK_TAKE_STATUS_LABELS,
  STOCK_TRANSFER_STATUS_LABELS,
  stockCommandStateLabel,
  stockCommandStateTone,
  stockInSourceTypeLabel,
  stockInSourceTypeTone,
  stockInTaskStatusLabel,
  stockInTaskStatusTone,
  stockOutSourceTypeLabel,
  stockOutSourceTypeTone,
  stockOutTaskStatusLabel,
  stockOutTaskStatusTone,
  stockTakeRoundLabel,
  stockTakeRoundTone,
  stockTakeScopeTypeLabel,
  stockTakeScopeTypeTone,
  stockTakeStatusLabel,
  stockTakeStatusTone,
  stockTransferStatusLabel,
  stockTransferStatusTone,
  WAREHOUSE_STATUS_LABELS,
  WH_TYPE_LABELS,
  WMS_ENUM_OPTION_MAPS,
} from '../wms-enums'

describe('Wms 2B 枚举字典完整性', () => {
  it('各字典覆盖全部后端枚举值', () => {
    expect(Object.keys(STOCK_IN_SOURCE_TYPE_LABELS)).toHaveLength(4)
    expect(Object.keys(STOCK_IN_TASK_STATUS_LABELS)).toHaveLength(4)
    expect(Object.keys(STOCK_OUT_SOURCE_TYPE_LABELS)).toHaveLength(5)
    expect(Object.keys(STOCK_OUT_TASK_STATUS_LABELS)).toHaveLength(5)
    expect(Object.keys(STOCK_TRANSFER_STATUS_LABELS)).toHaveLength(5)
    expect(Object.keys(IN_TRANSIT_STATUS_LABELS)).toHaveLength(2)
    expect(Object.keys(STOCK_TAKE_SCOPE_TYPE_LABELS)).toHaveLength(3)
    expect(Object.keys(STOCK_TAKE_ROUND_LABELS)).toHaveLength(2)
    expect(Object.keys(STOCK_TAKE_STATUS_LABELS)).toHaveLength(6)
    expect(Object.keys(INVENTORY_ADJUST_TYPE_LABELS)).toHaveLength(2)
    expect(Object.keys(INVENTORY_ADJUST_STATUS_LABELS)).toHaveLength(3)
    expect(Object.keys(INVENTORY_ALERT_TYPE_LABELS)).toHaveLength(4)
    expect(Object.keys(INVENTORY_ALERT_STATUS_LABELS)).toHaveLength(3)
    expect(Object.keys(STOCK_COMMAND_STATE_LABELS)).toHaveLength(3)
  })

  it('关键文案与后端语义一致', () => {
    expect(STOCK_IN_SOURCE_TYPE_LABELS[1]).toBe('采购入库')
    expect(STOCK_IN_SOURCE_TYPE_LABELS[4]).toBe('盘盈入库')
    expect(STOCK_IN_TASK_STATUS_LABELS[2]).toBe('作业中')
    expect(STOCK_OUT_SOURCE_TYPE_LABELS[5]).toBe('领用出库')
    expect(STOCK_OUT_TASK_STATUS_LABELS[3]).toBe('待复核')
    expect(STOCK_TRANSFER_STATUS_LABELS[3]).toBe('调出已发货')
    expect(STOCK_TAKE_SCOPE_TYPE_LABELS[2]).toBe('品类')
    expect(STOCK_TAKE_ROUND_LABELS[2]).toBe('复盘')
    expect(STOCK_TAKE_STATUS_LABELS[4]).toBe('复盘中')
    expect(INVENTORY_ADJUST_TYPE_LABELS[2]).toBe('盘亏')
    expect(INVENTORY_ALERT_TYPE_LABELS[4]).toBe('在途超期')
    expect(STOCK_COMMAND_STATE_LABELS[3]).toBe('失败')
  })

  it('catalog 汇总全部 16 组字典', () => {
    expect(Object.keys(WMS_ENUM_OPTION_MAPS)).toHaveLength(16)
    expect(WMS_ENUM_OPTION_MAPS.whType).toBe(WH_TYPE_LABELS)
    expect(WMS_ENUM_OPTION_MAPS.warehouseStatus).toBe(WAREHOUSE_STATUS_LABELS)
    expect(WMS_ENUM_OPTION_MAPS.stockInSourceType).toBe(STOCK_IN_SOURCE_TYPE_LABELS)
    expect(WMS_ENUM_OPTION_MAPS.stockInTaskStatus).toBe(STOCK_IN_TASK_STATUS_LABELS)
    expect(WMS_ENUM_OPTION_MAPS.stockOutSourceType).toBe(STOCK_OUT_SOURCE_TYPE_LABELS)
    expect(WMS_ENUM_OPTION_MAPS.stockOutTaskStatus).toBe(STOCK_OUT_TASK_STATUS_LABELS)
    expect(WMS_ENUM_OPTION_MAPS.stockTransferStatus).toBe(STOCK_TRANSFER_STATUS_LABELS)
    expect(WMS_ENUM_OPTION_MAPS.inTransitStatus).toBe(IN_TRANSIT_STATUS_LABELS)
    expect(WMS_ENUM_OPTION_MAPS.stockTakeScopeType).toBe(STOCK_TAKE_SCOPE_TYPE_LABELS)
    expect(WMS_ENUM_OPTION_MAPS.stockTakeRound).toBe(STOCK_TAKE_ROUND_LABELS)
    expect(WMS_ENUM_OPTION_MAPS.stockTakeStatus).toBe(STOCK_TAKE_STATUS_LABELS)
    expect(WMS_ENUM_OPTION_MAPS.inventoryAdjustType).toBe(INVENTORY_ADJUST_TYPE_LABELS)
    expect(WMS_ENUM_OPTION_MAPS.inventoryAdjustStatus).toBe(INVENTORY_ADJUST_STATUS_LABELS)
    expect(WMS_ENUM_OPTION_MAPS.inventoryAlertType).toBe(INVENTORY_ALERT_TYPE_LABELS)
    expect(WMS_ENUM_OPTION_MAPS.inventoryAlertStatus).toBe(INVENTORY_ALERT_STATUS_LABELS)
    expect(WMS_ENUM_OPTION_MAPS.stockCommandState).toBe(STOCK_COMMAND_STATE_LABELS)
  })
})

describe('Wms 2B 取标签与兜底', () => {
  it('标签函数未知码 / 空值兜底「—」', () => {
    expect(stockInSourceTypeLabel(1)).toBe('采购入库')
    expect(stockInSourceTypeLabel(99)).toBe('—')
    expect(stockInSourceTypeLabel(null)).toBe('—')
    expect(stockInTaskStatusLabel(2)).toBe('作业中')
    expect(stockOutSourceTypeLabel(5)).toBe('领用出库')
    expect(stockOutTaskStatusLabel(4)).toBe('已完成')
    expect(stockTransferStatusLabel(99)).toBe('—')
    expect(stockTakeStatusLabel(6)).toBe('已取消')
    expect(stockTakeScopeTypeLabel(null)).toBe('—')
    expect(stockTakeRoundLabel(2)).toBe('复盘')
    expect(inventoryAdjustTypeLabel(2)).toBe('盘亏')
    expect(inventoryAdjustStatusLabel(3)).toBe('已通过')
    expect(inventoryAlertTypeLabel(3)).toBe('临期')
    expect(inventoryAlertStatusLabel(3)).toBe('已忽略')
    expect(stockCommandStateLabel(1)).toBe('待分发')
    expect(inTransitStatusLabel(2)).toBe('已关闭')
  })

  it('色调按业务语义映射', () => {
    expect(stockInSourceTypeTone(1)).toBe('primary')
    expect(stockInSourceTypeTone(4)).toBe('success')
    expect(stockInTaskStatusTone(2)).toBe('primary')
    expect(stockInTaskStatusTone(3)).toBe('success')
    expect(stockInTaskStatusTone(4)).toBe('info')
    expect(stockInTaskStatusTone(1)).toBe('warning')
    expect(stockOutSourceTypeTone(4)).toBe('warning')
    expect(stockOutTaskStatusTone(3)).toBe('warning')
    expect(stockOutTaskStatusTone(4)).toBe('success')
    expect(stockTransferStatusTone(4)).toBe('success')
    expect(stockTransferStatusTone(5)).toBe('info')
    expect(inTransitStatusTone(1)).toBe('primary')
    expect(inTransitStatusTone(2)).toBe('success')
    expect(stockTakeScopeTypeTone(1)).toBe('primary')
    expect(stockTakeRoundTone(2)).toBe('warning')
    expect(stockTakeStatusTone(5)).toBe('success')
    expect(stockTakeStatusTone(4)).toBe('warning')
    expect(inventoryAdjustTypeTone(1)).toBe('success')
    expect(inventoryAdjustTypeTone(2)).toBe('danger')
    expect(inventoryAdjustStatusTone(2)).toBe('warning')
    expect(inventoryAlertTypeTone(1)).toBe('danger')
    expect(inventoryAlertTypeTone(2)).toBe('warning')
    expect(inventoryAlertStatusTone(2)).toBe('success')
    expect(stockCommandStateTone(4)).toBe('warning')
    expect(stockCommandStateTone(3)).toBe('danger')
  })
})
