/*
 * 所属公司：四川淇利馨集团有限公司
 * 描述：condition-builder AST JSON 一致性单测
 * 断言基准：后端 Qlx.Abstractions/Expressions/ConditionNode.cs 的 System.Text.Json 序列化形态——
 *   $type 判别符（literal/variable/unary/binary/function/list/ternary）+ CamelCase 属性
 *   （operatorSymbol / whenTrue / whenFalse / arguments / items），字面量仅标量。
 *   该 JSON 可被 ConditionExpression.FromJson 直接反序列化（引擎求值时按符号经注册表解析，
 *   纯字母符号 AND / OR / IN / NOT IN / Between 不区分大小写）。
 */

import { describe, expect, it } from 'vitest'
import {
  builderToAst,
  collectVariables,
  createGroup,
  createRule,
  evaluateAst,
  astToText,
  validateBuilder,
} from '../ast'
import type { ConditionAstNode, ConditionFieldDef, ConditionGroup, ConditionRule } from '../types'
import { mockConditionFields } from '../mock'

function fieldLookup(): Map<string, ConditionFieldDef> {
  const map = new Map<string, ConditionFieldDef>()
  mockConditionFields.forEach((group) => group.children.forEach((field) => map.set(field.name, field)))
  return map
}

function rule(field: string, op: string, mode: 'const' | 'dict' | 'param', constText = '', dictValues: string[] = [], paramField = '') {
  return {
    id: `r-${field}-${op}-${mode}-${constText}-${dictValues.join('_')}-${paramField}`,
    kind: 'rule' as const,
    field,
    operatorSymbol: op,
    value: { mode, constText, dictValues, paramField },
    constTextEnd: '',
  }
}

function groupOf(logic: 'AND' | 'OR', children: ConditionGroup['children']): ConditionGroup {
  return { id: `g-${logic}-${children.length}`, kind: 'group', logic, children }
}

describe('condition-builder AST JSON 与引擎格式一致性', () => {
  it('比较运算：total_amount > 50000 → binary + variable + 数值 literal（对齐 BinaryNode 序列化）', () => {
    const root = groupOf('AND', [rule('total_amount', '>', 'const', '50000')])
    const ast = builderToAst(root, fieldLookup())
    expect(ast).toEqual({
      $type: 'binary',
      left: { $type: 'variable', name: 'total_amount' },
      operatorSymbol: '>',
      right: { $type: 'literal', value: 50000 },
    } satisfies ConditionAstNode)
  })

  it('AND 逻辑分组折叠为左结合 binary，符号 AND（&& 的别名，引擎按不区分大小写解析）', () => {
    const root = groupOf('AND', [
      rule('total_amount', '>', 'const', '50000'),
      rule('status', '==', 'const', 'Draft'),
    ])
    const ast = builderToAst(root, fieldLookup())
    expect(ast).toEqual({
      $type: 'binary',
      left: {
        $type: 'binary',
        left: { $type: 'variable', name: 'total_amount' },
        operatorSymbol: '>',
        right: { $type: 'literal', value: 50000 },
      },
      operatorSymbol: 'AND',
      right: {
        $type: 'binary',
        left: { $type: 'variable', name: 'status' },
        operatorSymbol: '==',
        right: { $type: 'literal', value: 'Draft' },
      },
    } satisfies ConditionAstNode)
  })

  it('参数变量 ${approved}：AST 内为裸名称 variable（对齐词法 ${name} 去包装语义）', () => {
    const root = groupOf('AND', [rule('settled', '==', 'param', '', [], 'approved')])
    const ast = builderToAst(root, fieldLookup())
    expect(ast).toEqual({
      $type: 'binary',
      left: { $type: 'variable', name: 'settled' },
      operatorSymbol: '==',
      right: { $type: 'variable', name: 'approved' },
    } satisfies ConditionAstNode)
  })

  it('IN 列表：右侧为 list 节点（对齐 ListNode），字符串字面量带引号语义由 JSON 字符串承载', () => {
    const root = groupOf('AND', [rule('status', 'IN', 'const', 'Draft,Submitted')])
    const ast = builderToAst(root, fieldLookup())
    expect(ast).toEqual({
      $type: 'binary',
      left: { $type: 'variable', name: 'status' },
      operatorSymbol: 'IN',
      right: {
        $type: 'list',
        items: [
          { $type: 'literal', value: 'Draft' },
          { $type: 'literal', value: 'Submitted' },
        ],
      },
    } satisfies ConditionAstNode)
  })

  it('字典字段 IN：字典项值作为字符串字面量进入 list', () => {
    const root = groupOf('AND', [rule('department_code', 'IN', 'dict', '', ['FIN', 'PUR'])])
    const ast = builderToAst(root, fieldLookup())
    expect(ast).toEqual({
      $type: 'binary',
      left: { $type: 'variable', name: 'department_code' },
      operatorSymbol: 'IN',
      right: {
        $type: 'list',
        items: [
          { $type: 'literal', value: 'FIN' },
          { $type: 'literal', value: 'PUR' },
        ],
      },
    } satisfies ConditionAstNode)
  })

  it('Between → function 节点（对齐 FunctionNode：name + arguments），数值字面量为 JSON number', () => {
    const node = createRule(mockConditionFields[0].children[0])
    node.operatorSymbol = 'Between'
    node.value.mode = 'const'
    node.value.constText = '1000'
    node.constTextEnd = '50000'
    const root = groupOf('AND', [node])
    const ast = builderToAst(root, fieldLookup())
    expect(ast).toEqual({
      $type: 'function',
      name: 'Between',
      arguments: [
        { $type: 'variable', name: 'total_amount' },
        { $type: 'literal', value: 1000 },
        { $type: 'literal', value: 50000 },
      ],
    } satisfies ConditionAstNode)
  })

  it('布尔 / 字符串字面量形态对齐 ConditionValueJsonConverter（bool → true/false，string → JSON 字符串）', () => {
    const root = groupOf('AND', [
      rule('settled', '==', 'const', 'true'),
      rule('status', '==', 'const', 'Draft'),
    ])
    const ast = builderToAst(root, fieldLookup())
    expect(ast).toMatchObject({
      operatorSymbol: 'AND',
      left: { operatorSymbol: '==', right: { value: true } },
      right: { operatorSymbol: '==', right: { value: 'Draft' } },
    })
    // 可被 JSON 往返（后端 FromJson 消费前提）
    const roundTrip = JSON.parse(JSON.stringify(ast)) as ConditionAstNode
    expect(roundTrip).toEqual(ast)
  })

  it('嵌套分组（AND(OR(a,b),c)）→ 嵌套 binary；变量清单与引擎 CollectVariables 语义一致', () => {
    const inner = groupOf('OR', [
      rule('status', '==', 'const', 'Draft'),
      rule('settled', '==', 'const', 'false'),
    ])
    const root = groupOf('AND', [inner, rule('invoice_count', '>', 'const', '0')])
    const ast = builderToAst(root, fieldLookup())
    expect(ast).toMatchObject({ $type: 'binary', operatorSymbol: 'AND' })
    expect((ast as { left: ConditionAstNode }).left).toMatchObject({ operatorSymbol: 'OR' })
    expect([...collectVariables(ast!)]).toEqual(['status', 'settled', 'invoice_count'])
  })
})

describe('condition-builder 试算与校验', () => {
  const variables = {
    total_amount: 68000,
    status: 'Draft',
    department_code: 'FIN',
    settled: false,
    invoice_count: 3,
  }

  it('AND / OR / 比较 / IN / Between 求值结果与引擎语义一致', () => {
    const root = groupOf('AND', [
      rule('total_amount', '>', 'const', '50000'),
      rule('status', 'IN', 'const', 'Draft,Submitted'),
    ])
    const ast = builderToAst(root, fieldLookup())!
    expect(evaluateAst(ast, variables)).toBe(true)

    const orRoot = groupOf('OR', [
      rule('total_amount', '<', 'const', '1000'),
      rule('department_code', '==', 'const', 'FIN'),
    ])
    expect(evaluateAst(builderToAst(orRoot, fieldLookup())!, variables)).toBe(true)

    const betweenRoot = groupOf('AND', [rule('total_amount', 'Between', 'const', '60000')])
    const betweenNode = betweenRoot.children[0] as ConditionRule
    betweenNode.constTextEnd = '70000'
    expect(evaluateAst(builderToAst(betweenRoot, fieldLookup())!, variables)).toBe(true)
  })

  it('可读文本渲染（字段标签 + 中文连接词）', () => {
    const root = groupOf('AND', [
      rule('total_amount', '>', 'const', '50000'),
      rule('status', '==', 'const', 'Draft'),
    ])
    const text = astToText(builderToAst(root, fieldLookup())!, fieldLookup())
    expect(text).toBe('（订单金额 大于 50000 且 单据状态 等于 \'Draft\'）')
  })

  it('校验器：空分组 / 缺常量 / 缺区间上界可被拦截', () => {
    const empty = createGroup('AND')
    expect(validateBuilder(empty, fieldLookup()).length).toBeGreaterThan(0)

    const missingValue = groupOf('AND', [rule('total_amount', '>', 'const', '')])
    expect(validateBuilder(missingValue, fieldLookup()).some((message) => message.includes('未填写常量值'))).toBe(true)

    const missingEnd = groupOf('AND', [rule('total_amount', 'Between', 'const', '1000')])
    expect(validateBuilder(missingEnd, fieldLookup()).some((message) => message.includes('区间上界'))).toBe(true)

    const ok = groupOf('AND', [rule('total_amount', '>', 'const', '50000')])
    expect(validateBuilder(ok, fieldLookup())).toEqual([])
  })
})
