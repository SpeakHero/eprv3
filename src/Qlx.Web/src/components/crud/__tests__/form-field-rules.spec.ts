/*
 * 所属公司：四川淇利馨集团有限公司
 * 描述：form-field-rules 单元测试——字段必填判定（required 恒真 / requiredWhen 命中与未命中 / 缺省兜底）
 */

import { describe, expect, it } from 'vitest'
import { isFieldRequired } from '../form-field-rules'
import type { FormField, FormValue } from '../form-field-rules'

/** 最小字段工厂（label / type 取默认值）。 */
function field(partial: Partial<FormField> & { prop: string }): FormField {
  return { label: partial.prop, type: 'input', ...partial }
}

/** 人员表单条件必填：员工（personType=1，缺省按 1）必须指定组织 / 岗位。 */
const employeeOnly = (values: Record<string, FormValue>): boolean =>
  Number(values.personType ?? 1) === 1

describe('isFieldRequired', () => {
  it('required=true 时恒为必填，与表单值无关', () => {
    const target = field({ prop: 'name', required: true })
    expect(isFieldRequired(target, {})).toBe(true)
    expect(isFieldRequired(target, { name: '张三' })).toBe(true)
  })

  it('requiredWhen 命中时为必填', () => {
    const target = field({ prop: 'orgId', requiredWhen: employeeOnly })
    expect(isFieldRequired(target, { personType: 1 })).toBe(true)
    expect(isFieldRequired(target, { personType: '1' })).toBe(true)
  })

  it('requiredWhen 未命中时非必填', () => {
    const target = field({ prop: 'orgId', requiredWhen: employeeOnly })
    expect(isFieldRequired(target, { personType: 2 })).toBe(false)
  })

  it('required 与 requiredWhen 同时存在时按或语义判定', () => {
    const target = field({ prop: 'positionId', required: true, requiredWhen: employeeOnly })
    expect(isFieldRequired(target, { personType: 2 })).toBe(true)
  })

  it('既无 required 也无 requiredWhen 时非必填', () => {
    const target = field({ prop: 'bankCardNo' })
    expect(isFieldRequired(target, { bankCardNo: '' })).toBe(false)
  })

  it('未传表单值时按默认空对象处理（回调缺省兜底生效）', () => {
    const target = field({ prop: 'orgId', requiredWhen: employeeOnly })
    expect(isFieldRequired(target)).toBe(true)
    expect(isFieldRequired(field({ prop: 'positionId' }))).toBe(false)
  })

  it('requiredWhen 读取的字段缺失时不影响其它字段判定', () => {
    const target = field({
      prop: 'positionId',
      requiredWhen: (values) => values.personType === 2,
    })
    expect(isFieldRequired(target, { personType: 1 })).toBe(false)
    expect(isFieldRequired(target, {})).toBe(false)
  })
})
