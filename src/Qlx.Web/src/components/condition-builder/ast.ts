/*
 * 所属公司：四川淇利馨集团有限公司
 * 描述：构建器模型 ↔ AST JSON 转换 + 可读文本渲染 + 样例数据试算（纯函数，无框架依赖）
 * 说明：AST JSON 输出与 ConditionExpression.ToJson（CamelCase + $type 判别符）完全一致，
 *       可被 IConditionExpressionEngine.Parse → FromJson 直接消费；单测做结构一致性断言。
 */

import type {
  ConditionAstNode,
  ConditionFieldDef,
  ConditionGroup,
  ConditionRule,
} from './types'
import { findOperator, operatorLabel } from './operators'

/** 深度受限的 id 发号器（演示构建用，非业务雪花 Id）。 */
let nodeSeq = 0

export function nextNodeId(): string {
  nodeSeq += 1
  return `node-${nodeSeq}`
}

/** 新建规则节点（默认取字段类型白名单首个操作符）。 */
export function createRule(field: ConditionFieldDef): ConditionRule {
  const firstOp = findOperatorByType(field)
  return {
    id: nextNodeId(),
    kind: 'rule',
    field: field.name,
    operatorSymbol: firstOp,
    value: { mode: field.type === 'dictionary' ? 'dict' : 'const', constText: '', dictValues: [], paramField: '' },
    constTextEnd: '',
  }
}

function findOperatorByType(field: ConditionFieldDef): string {
  // 延迟引用避免循环依赖：白名单逻辑与 operatorsForType 一致
  const order: Record<string, string[]> = {
    number: ['>', '==', '>=', '<', '<=', '!=', 'Between', 'IN', 'NOT IN'],
    string: ['==', 'Contains', '!=', 'StartsWith', 'EndsWith', 'IN', 'NOT IN', 'IsNull', 'IsNotNull'],
    boolean: ['==', '!='],
    date: ['>=', '>', '==', '<=', '<', '!=', 'Between'],
    datetime: ['>=', '>', '==', '<=', '<', '!=', 'Between'],
    dictionary: ['==', 'IN', '!=', 'NOT IN'],
  }
  const allowed = order[field.type] ?? ['==']
  return allowed[0]
}

export function createGroup(logic: 'AND' | 'OR' = 'AND'): ConditionGroup {
  return { id: nextNodeId(), kind: 'group', logic, children: [] }
}

// ============ 字面量构造（对齐 ConditionValueJsonConverter：仅标量） ============

/** 数值字段常量 → literal（非法输入退回 null 字面量，由校验拦截）。 */
function numberLiteral(text: string): ConditionAstNode {
  const parsed = Number(text.trim())
  return { $type: 'literal', value: text.trim() !== '' && Number.isFinite(parsed) ? parsed : null }
}

function booleanLiteral(text: string): ConditionAstNode {
  return { $type: 'literal', value: text === 'true' }
}

/** 按字段类型把常量文本转为字面量节点。 */
export function literalFor(field: ConditionFieldDef, text: string): ConditionAstNode {
  switch (field.type) {
    case 'number':
      return numberLiteral(text)
    case 'boolean':
      return booleanLiteral(text)
    default:
      return { $type: 'literal', value: text }
  }
}

/** 取规则值节点（const / dict / param → literal / literal / variable）。 */
function valueNodeOf(rule: ConditionRule, field: ConditionFieldDef, end: boolean): ConditionAstNode {
  switch (rule.value.mode) {
    case 'param':
      // 后端词法：${name} 的 VariableNode.Name 即裸名称
      return { $type: 'variable', name: rule.value.paramField.trim() }
    case 'dict': {
      if (findOperator(rule.operatorSymbol)?.valueKind === 'list') {
        const index = end ? 1 : 0
        return { $type: 'literal', value: rule.value.dictValues[index] ?? '' }
      }
      return { $type: 'literal', value: rule.value.dictValues[0] ?? '' }
    }
    default:
      return literalFor(field, end ? rule.constTextEnd : rule.value.constText)
  }
}

/** 规则节点 → AST（非法配置抛错，由组件校验层先行拦截）。 */
export function ruleToAst(rule: ConditionRule, fieldLookup: Map<string, ConditionFieldDef>): ConditionAstNode {
  const field = fieldLookup.get(rule.field)
  if (!field) {
    throw new Error(`未知字段：${rule.field}`)
  }
  const op = findOperator(rule.operatorSymbol)
  if (!op) {
    throw new Error(`未知操作符：${rule.operatorSymbol}`)
  }
  const variable: ConditionAstNode = { $type: 'variable', name: rule.field }

  if (op.astKind === 'function') {
    switch (op.valueKind) {
      case 'none':
        return { $type: 'function', name: op.symbol, arguments: [variable] }
      case 'range':
        return {
          $type: 'function',
          name: op.symbol,
          arguments: [variable, valueNodeOf(rule, field, false), valueNodeOf(rule, field, true)],
        }
      default:
        return { $type: 'function', name: op.symbol, arguments: [variable, valueNodeOf(rule, field, false)] }
    }
  }

  if (op.valueKind === 'list') {
    const items = listItemsOf(rule, field)
    return { $type: 'binary', left: variable, operatorSymbol: op.symbol, right: { $type: 'list', items } }
  }

  return { $type: 'binary', left: variable, operatorSymbol: op.symbol, right: valueNodeOf(rule, field, false) }
}

function listItemsOf(rule: ConditionRule, field: ConditionFieldDef): ConditionAstNode[] {
  if (rule.value.mode === 'dict') {
    return rule.value.dictValues.map((value) => ({ $type: 'literal' as const, value }))
  }
  if (rule.value.mode === 'param') {
    return [{ $type: 'variable' as const, name: rule.value.paramField.trim() }]
  }
  return rule.value.constText
    .split(/[，,]/)
    .map((part) => part.trim())
    .filter((part) => part !== '')
    .map((part) => literalFor(field, part))
}

/** 构建器树 → AST 根节点（空树返回 null；单子节点折叠避免冗余括号）。 */
export function builderToAst(root: ConditionGroup, fieldLookup: Map<string, ConditionFieldDef>): ConditionAstNode | null {
  return groupToAst(root, fieldLookup)
}

function groupToAst(group: ConditionGroup, fieldLookup: Map<string, ConditionFieldDef>): ConditionAstNode | null {
  const childAsts = group.children
    .map((child) => (child.kind === 'rule' ? ruleToAst(child, fieldLookup) : groupToAst(child, fieldLookup)))
    .filter((ast): ast is ConditionAstNode => ast !== null)

  if (childAsts.length === 0) {
    return null
  }
  return childAsts.reduce((left, right) => ({
    $type: 'binary' as const,
    left,
    operatorSymbol: group.logic,
    right,
  }))
}

// ============ 可读文本渲染 ============

/** 字符串字面量加引号（可读文本）。 */
function quote(text: string): string {
  return `'${text}'`
}

/** 变量显示名（参数变量带 ${}，普通变量用字段标签）。 */
function variableDisplay(name: string, fieldLookup: Map<string, ConditionFieldDef>): string {
  return fieldLookup.get(name)?.label ?? `\${${name}}`
}

function literalDisplay(node: ConditionAstNode): string {
  if (node.$type !== 'literal') {
    return ''
  }
  if (node.value === null) {
    return 'null'
  }
  return typeof node.value === 'string' ? quote(node.value) : String(node.value)
}

/** AST → 可读文本（如「订单金额 大于 50000 且 审批状态 属于 'Draft','Submitted'」）。 */
export function astToText(node: ConditionAstNode, fieldLookup: Map<string, ConditionFieldDef>): string {
  switch (node.$type) {
    case 'literal':
      return literalDisplay(node)
    case 'variable':
      return variableDisplay(node.name, fieldLookup)
    case 'unary':
      return `${node.operatorSymbol === '!' ? '非' : node.operatorSymbol} ${astToText(node.operand, fieldLookup)}`
    case 'binary': {
      const left = astToText(node.left, fieldLookup)
      const right = astToText(node.right, fieldLookup)
      switch (node.operatorSymbol) {
        case 'AND':
          return `（${left} 且 ${right}）`
        case 'OR':
          return `（${left} 或 ${right}）`
        case 'IN':
        case 'NOT IN': {
          const items = node.right.$type === 'list' ? node.right.items.map(literalDisplay).join('、') : right
          const label = node.operatorSymbol === 'IN' ? '属于其中' : '不属于其中'
          return `${left} ${label} ${items}`
        }
        default:
          return `${left} ${operatorLabel(node.operatorSymbol)} ${right}`
      }
    }
    case 'function': {
      const args = node.arguments
      const target = args.length > 0 ? astToText(args[0], fieldLookup) : ''
      switch (node.name) {
        case 'Between':
          return `${target} 介于 ${args[1] ? literalDisplay(args[1]) : ''} 与 ${args[2] ? literalDisplay(args[2]) : ''}`
        case 'IsNull':
          return `${target} 为空`
        case 'IsNotNull':
          return `${target} 不为空`
        default:
          return `${target} ${operatorLabel(node.name)} ${args[1] ? literalDisplay(args[1]) : ''}`
      }
    }
    case 'list':
      return node.items.map(literalDisplay).join('、')
    case 'ternary':
      return `（${astToText(node.condition, fieldLookup)} ? ${astToText(node.whenTrue, fieldLookup)} : ${astToText(node.whenFalse, fieldLookup)}）`
  }
}

// ============ 样例数据试算（mock 服务内核，语义对齐引擎求值） ============

type VariableBag = Record<string, unknown>

function toComparable(value: unknown): string | number | boolean | null {
  if (value === null || value === undefined) {
    return null
  }
  if (typeof value === 'number' || typeof value === 'boolean') {
    return value
  }
  if (typeof value === 'string') {
    return value
  }
  return String(value)
}

function compare(op: string, left: unknown, right: unknown): boolean {
  const a = toComparable(left)
  const b = toComparable(right)
  if (typeof a === 'number' && typeof b === 'number') {
    switch (op) {
      case '==':
        return a === b
      case '!=':
        return a !== b
      case '>':
        return a > b
      case '>=':
        return a >= b
      case '<':
        return a < b
      case '<=':
        return a <= b
      default:
        return false
    }
  }
  const sa = a === null ? '' : String(a)
  const sb = b === null ? '' : String(b)
  switch (op) {
    case '==':
      return sa === sb
    case '!=':
      return sa !== sb
    case '>':
      return sa > sb
    case '>=':
      return sa >= sb
    case '<':
      return sa < sb
    case '<=':
      return sa <= sb
    default:
      return false
  }
}

/** AST 求值（变量缺失视为 null，与引擎宽松模式一致）。 */
export function evaluateAst(node: ConditionAstNode, variables: VariableBag): unknown {
  switch (node.$type) {
    case 'literal':
      return node.value
    case 'variable':
      return variables[node.name] ?? null
    case 'unary': {
      const operand = evaluateAst(node.operand, variables)
      if (node.operatorSymbol === '!' || node.operatorSymbol === 'NOT') {
        return !operand
      }
      return typeof operand === 'number' ? -operand : operand
    }
    case 'binary': {
      if (node.operatorSymbol === 'AND') {
        return Boolean(evaluateAst(node.left, variables)) && Boolean(evaluateAst(node.right, variables))
      }
      if (node.operatorSymbol === 'OR') {
        return Boolean(evaluateAst(node.left, variables)) || Boolean(evaluateAst(node.right, variables))
      }
      if (node.operatorSymbol === 'IN' || node.operatorSymbol === 'NOT IN') {
        const items = node.right.$type === 'list' ? node.right.items : []
        const values = items.map((item) => toComparable(evaluateAst(item, variables)))
        const left = toComparable(evaluateAst(node.left, variables))
        const hit = values.some((item) =>
          typeof left === 'number' ? item === left : String(item) === String(left ?? ''),
        )
        return node.operatorSymbol === 'IN' ? hit : !hit
      }
      return compare(node.operatorSymbol, evaluateAst(node.left, variables), evaluateAst(node.right, variables))
    }
    case 'function': {
      const args = node.arguments.map((arg) => evaluateAst(arg, variables))
      const target = toComparable(args[0])
      switch (node.name.toLowerCase()) {
        case 'between': {
          const low = toComparable(args[1])
          const high = toComparable(args[2])
          return compare('>=', target, low) && compare('<=', target, high)
        }
        case 'contains':
          return String(target ?? '').includes(String(args[1] ?? ''))
        case 'startswith':
          return String(target ?? '').startsWith(String(args[1] ?? ''))
        case 'endswith':
          return String(target ?? '').endsWith(String(args[1] ?? ''))
        case 'isnull':
          return target === null
        case 'isnotnull':
          return target !== null
        case 'length':
          return String(target ?? '').length
        case 'abs':
          return Math.abs(Number(target) || 0)
        case 'upper':
          return String(target ?? '').toUpperCase()
        case 'lower':
          return String(target ?? '').toLowerCase()
        default:
          throw new Error(`试算暂不支持函数：${node.name}`)
      }
    }
    case 'list':
      return node.items.map((item) => evaluateAst(item, variables))
    case 'ternary':
      return Boolean(evaluateAst(node.condition, variables))
        ? evaluateAst(node.whenTrue, variables)
        : evaluateAst(node.whenFalse, variables)
  }
}

/** 收集 AST 引用的全部变量名（对齐 ConditionExpression.CollectVariables，不区分大小写去重）。 */
export function collectVariables(node: ConditionAstNode, into: Set<string> = new Set()): Set<string> {
  switch (node.$type) {
    case 'variable':
      into.add(node.name)
      break
    case 'unary':
      collectVariables(node.operand, into)
      break
    case 'binary':
      collectVariables(node.left, into)
      collectVariables(node.right, into)
      break
    case 'function':
      node.arguments.forEach((arg) => collectVariables(arg, into))
      break
    case 'list':
      node.items.forEach((item) => collectVariables(item, into))
      break
    case 'ternary':
      collectVariables(node.condition, into)
      collectVariables(node.whenTrue, into)
      collectVariables(node.whenFalse, into)
      break
    default:
      break
  }
  return into
}

/** 树校验：空规则 / 缺值 / 嵌套超限，返回错误描述（空数组即合法）。 */
export function validateBuilder(root: ConditionGroup, fieldLookup: Map<string, ConditionFieldDef>): string[] {
  const errors: string[] = []
  walk(root, 1)
  return errors

  function walk(group: ConditionGroup, depth: number): void {
    if (depth > 5) {
      errors.push('分组嵌套超过 5 层上限')
    }
    if (group.children.length === 0) {
      errors.push('存在空分组，请补充条件或删除该分组')
    }
    group.children.forEach((child) => {
      if (child.kind === 'group') {
        walk(child, depth + 1)
        return
      }
      const rule = child as ConditionRule
      const field = fieldLookup.get(rule.field)
      const op = findOperator(rule.operatorSymbol)
      if (!field || !op) {
        errors.push('存在未完成的条件行')
        return
      }
      const v = rule.value
      if (op.valueKind !== 'none') {
        if (v.mode === 'param' && !v.paramField.trim()) {
          errors.push(`条件「${field.label}」未填写参数变量`)
        } else if (v.mode === 'dict' && v.dictValues.length === 0) {
          errors.push(`条件「${field.label}」未选择字典项`)
        } else if (v.mode === 'const') {
          if (op.valueKind === 'list' && listItemsOf(rule, field).length === 0) {
            errors.push(`条件「${field.label}」未填写列表值`)
          } else if (op.valueKind !== 'list' && !v.constText.trim()) {
            errors.push(`条件「${field.label}」未填写常量值`)
          } else if (op.valueKind === 'range' && !rule.constTextEnd.trim()) {
            errors.push(`条件「${field.label}」未填写区间上界`)
          }
        }
      }
    })
  }
}

