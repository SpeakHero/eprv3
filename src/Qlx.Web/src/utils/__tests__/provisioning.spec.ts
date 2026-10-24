/*
 * 所属公司：四川淇利馨集团有限公司
 * 描述：utils/provisioning.ts 单元测试——覆盖步骤标签映射 / 百分比夹紧 / 预配完成判定
 */

import { describe, expect, it } from 'vitest'
import {
  clampProvisioningPercent,
  isProvisioningFinished,
  provisioningStepLabel,
} from '../provisioning'

describe('provisioningStepLabel', () => {
  it('已知步骤返回中文标签', () => {
    expect(provisioningStepLabel('Migrating')).toBe('数据库迁移中')
    expect(provisioningStepLabel('Completed')).toBe('预配完成')
    expect(provisioningStepLabel('Failed')).toBe('预配失败')
    expect(provisioningStepLabel('MaxRetryReached')).toBe('已达重试上限')
  })

  it('未知步骤返回「等待预配进度」', () => {
    expect(provisioningStepLabel('Unknown')).toBe('等待预配进度')
  })

  it('空值返回「等待预配进度」', () => {
    expect(provisioningStepLabel(undefined)).toBe('等待预配进度')
    expect(provisioningStepLabel('')).toBe('等待预配进度')
  })
})

describe('clampProvisioningPercent', () => {
  it('负数夹紧到 0', () => {
    expect(clampProvisioningPercent(-5)).toBe(0)
  })

  it('区间内四舍五入保留', () => {
    expect(clampProvisioningPercent(50)).toBe(50)
  })

  it('超上限夹紧到 100', () => {
    expect(clampProvisioningPercent(150)).toBe(100)
  })

  it('NaN 返回 0', () => {
    expect(clampProvisioningPercent(Number.NaN)).toBe(0)
  })

  it('undefined 返回 0', () => {
    expect(clampProvisioningPercent(undefined)).toBe(0)
  })
})

describe('isProvisioningFinished', () => {
  it('Active 视为已结束', () => {
    expect(isProvisioningFinished('Active')).toBe(true)
  })

  it('Failed 视为已结束', () => {
    expect(isProvisioningFinished('Failed')).toBe(true)
  })

  it('Provisioning 视为未结束', () => {
    expect(isProvisioningFinished('Provisioning')).toBe(false)
  })

  it('undefined 视为未结束', () => {
    expect(isProvisioningFinished(undefined)).toBe(false)
  })
})
