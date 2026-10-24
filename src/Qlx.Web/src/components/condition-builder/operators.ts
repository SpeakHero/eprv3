/*
 * 所属公司：四川淇利馨集团有限公司
 * 描述：操作符白名单（按字段类型）——符号严格对齐 Qlx.BuildingBlocks/Expressions/BuiltInOperators.cs 注册符号
 */

import type { ConditionFieldType, OperatorDef } from './types'

/** 全量操作符定义（astKind=function 的以引擎内置函数实现）。 */
const OPERATORS: Record<string, OperatorDef> = {
  eq: { symbol: '==', label: '等于', valueKind: 'scalar', astKind: 'binary' },
  ne: { symbol: '!=', label: '不等于', valueKind: 'scalar', astKind: 'binary' },
  gt: { symbol: '>', label: '大于', valueKind: 'scalar', astKind: 'binary' },
  ge: { symbol: '>=', label: '大于等于', valueKind: 'scalar', astKind: 'binary' },
  lt: { symbol: '<', label: '小于', valueKind: 'scalar', astKind: 'binary' },
  le: { symbol: '<=', label: '小于等于', valueKind: 'scalar', astKind: 'binary' },
  in: { symbol: 'IN', label: '属于其中', valueKind: 'list', astKind: 'binary' },
  notIn: { symbol: 'NOT IN', label: '不属于其中', valueKind: 'list', astKind: 'binary' },
  between: { symbol: 'Between', label: '介于', valueKind: 'range', astKind: 'function' },
  contains: { symbol: 'Contains', label: '包含', valueKind: 'scalar', astKind: 'function' },
  startsWith: { symbol: 'StartsWith', label: '前缀为', valueKind: 'scalar', astKind: 'function' },
  endsWith: { symbol: 'EndsWith', label: '后缀为', valueKind: 'scalar', astKind: 'function' },
  isNull: { symbol: 'IsNull', label: '为空', valueKind: 'none', astKind: 'function' },
  isNotNull: { symbol: 'IsNotNull', label: '不为空', valueKind: 'none', astKind: 'function' },
}

/** 各字段类型的操作符白名单（key 为 OPERATORS 键）。 */
const TYPE_OPERATOR_KEYS: Record<ConditionFieldType, string[]> = {
  number: ['eq', 'ne', 'gt', 'ge', 'lt', 'le', 'between', 'in', 'notIn'],
  string: ['eq', 'ne', 'contains', 'startsWith', 'endsWith', 'in', 'notIn', 'isNull', 'isNotNull'],
  boolean: ['eq', 'ne'],
  date: ['eq', 'ne', 'gt', 'ge', 'lt', 'le', 'between'],
  datetime: ['eq', 'ne', 'gt', 'ge', 'lt', 'le', 'between'],
  dictionary: ['eq', 'ne', 'in', 'notIn'],
}

/** 取字段类型的操作符白名单（保持展示顺序）。 */
export function operatorsForType(type: ConditionFieldType): OperatorDef[] {
  return TYPE_OPERATOR_KEYS[type].map((key) => OPERATORS[key])
}

/** 按 symbol 查操作符定义。 */
export function findOperator(symbol: string): OperatorDef | null {
  return Object.values(OPERATORS).find((op) => op.symbol === symbol) ?? null
}

/** 引擎符号 → 可读文本（可读文本渲染用）。 */
export function operatorLabel(symbol: string): string {
  return findOperator(symbol)?.label ?? symbol
}
