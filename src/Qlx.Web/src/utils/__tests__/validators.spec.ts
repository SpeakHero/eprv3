/*
 * 所属公司：四川淇利馨集团有限公司
 * 描述：utils/validators.ts 单元测试——信用代码 / 手机号 / 邮箱 / 管理员账号（手机号或邮箱）/ 空值
 */

import { describe, expect, it } from 'vitest'
import {
  ADMIN_ACCOUNT_PATTERN,
  CREDIT_CODE_PATTERN,
  EMAIL_PATTERN,
  MOBILE_PATTERN,
  isAdminAccount,
  isCreditCode,
} from '../validators'

describe('CREDIT_CODE_PATTERN', () => {
  it('合法：18 位数字与大写字母（排除 I/O/S/V/Z）', () => {
    expect(CREDIT_CODE_PATTERN.test('91510100MA6C0H9X2W')).toBe(true)
    expect(isCreditCode('91510100MA6C0H9X2W')).toBe(true)
  })

  it('非法：长度不足 18 位', () => {
    expect(isCreditCode('91510100MA6C0H9X2')).toBe(false)
  })

  it('非法：含排除字符 I/O/S/V/Z', () => {
    expect(isCreditCode('91510100MA6C0H9X2I')).toBe(false)
    expect(isCreditCode('91510100MA6C0H9X2Z')).toBe(false)
  })

  it('非法：小写字母', () => {
    expect(isCreditCode('91510100ma6c0h9x2w')).toBe(false)
  })

  it('空值：由 required 规则处理，格式判定返回 false', () => {
    expect(isCreditCode('')).toBe(false)
  })
})

describe('ADMIN_ACCOUNT_PATTERN', () => {
  it('合法：中国大陆手机号', () => {
    expect(MOBILE_PATTERN.test('13800138000')).toBe(true)
    expect(ADMIN_ACCOUNT_PATTERN.test('13800138000')).toBe(true)
    expect(isAdminAccount('13800138000')).toBe(true)
  })

  it('非法：手机号位数不足 / 第二位为 2', () => {
    expect(isAdminAccount('1380013800')).toBe(false)
    expect(isAdminAccount('12800138000')).toBe(false)
  })

  it('合法：邮箱', () => {
    expect(EMAIL_PATTERN.test('admin@qlx.com')).toBe(true)
    expect(isAdminAccount('admin@qlx.com')).toBe(true)
  })

  it('非法：缺少域名后缀 / 含空白', () => {
    expect(isAdminAccount('admin@qlx')).toBe(false)
    expect(isAdminAccount('admin @qlx.com')).toBe(false)
  })

  it('空值：格式判定返回 false', () => {
    expect(isAdminAccount('')).toBe(false)
  })
})
