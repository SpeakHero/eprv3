/*
 * 所属公司：四川淇利馨集团有限公司
 * 描述：condition-builder 类型定义——AST JSON 对齐 Qlx.Abstractions/Expressions/ConditionNode.cs 序列化形态
 * 说明：
 *  - 后端经 ConditionExpression.ToJson（QlxJsonDefaults：CamelCase + $type 判别符）序列化，
 *    前端输出的 AST JSON 必须能被 IConditionExpressionEngine.Parse / FromJson 消费。
 *  - 判别符取值：literal / variable / unary / binary / function / list / ternary。
 *  - 字面量仅标量：字符串 / 数值 / 布尔 / null（ConditionValueJsonConverter 约定）。
 */

/** 条件表达式 AST 节点（expr_json 形态，与后端 JSON 双向兼容）。 */
export type ConditionAstNode =
  | { readonly $type: 'literal'; readonly value: string | number | boolean | null }
  | { readonly $type: 'variable'; readonly name: string }
  | { readonly $type: 'unary'; readonly operatorSymbol: string; readonly operand: ConditionAstNode }
  | {
      readonly $type: 'binary'
      readonly left: ConditionAstNode
      readonly operatorSymbol: string
      readonly right: ConditionAstNode
    }
  | { readonly $type: 'function'; readonly name: string; readonly arguments: readonly ConditionAstNode[] }
  | { readonly $type: 'list'; readonly items: readonly ConditionAstNode[] }
  | {
      readonly $type: 'ternary'
      readonly condition: ConditionAstNode
      readonly whenTrue: ConditionAstNode
      readonly whenFalse: ConditionAstNode
    }

/** 字段类型（决定操作符白名单与值控件形态）。 */
export type ConditionFieldType = 'number' | 'string' | 'boolean' | 'date' | 'datetime' | 'dictionary'

/** 字段定义（变量名对齐后端变量解析键）。 */
export interface ConditionFieldDef {
  /** 变量名（bare identifier，如 total_amount）。 */
  name: string
  label: string
  type: ConditionFieldType
  /** dictionary 类型必填：字典项选项。 */
  options?: Array<{ label: string; value: string }>
  /** 数值字段的小数位提示。 */
  decimalHint?: string
}

/** 字段分组（左侧字段树节点）。 */
export interface ConditionFieldGroup {
  key: string
  label: string
  children: ConditionFieldDef[]
}

/** 值来源模式。 */
export type ConditionValueMode = 'const' | 'dict' | 'param'

/** 值形态（由操作符决定）：无值 / 单值 / 列表 / 区间。 */
export type OperatorValueKind = 'none' | 'scalar' | 'list' | 'range'

/** 操作符定义（symbol 对齐 BuiltInOperators 注册符号）。 */
export interface OperatorDef {
  /** 引擎符号：== != > >= < <= IN NOT IN；函数为 Between / Contains 等。 */
  symbol: string
  label: string
  valueKind: OperatorValueKind
  /** AST 形态：binary 节点或 function 节点。 */
  astKind: 'binary' | 'function'
}

/** 规则值（三种来源互斥）。 */
export interface ConditionRuleValue {
  mode: ConditionValueMode
  /** const：常量输入（number 用文本承载，提交时解析）。 */
  constText: string
  /** dict：字典项取值（list 值形态时多选）。 */
  dictValues: string[]
  /** param：参数变量名（bare identifier，展示为 ${name}）。 */
  paramField: string
}

/** 构建器规则节点。 */
export interface ConditionRule {
  id: string
  kind: 'rule'
  field: string
  operatorSymbol: string
  value: ConditionRuleValue
  /** range 形态第二值（Between 上界）。 */
  constTextEnd: string
}

/** 构建器分组节点。 */
export interface ConditionGroup {
  id: string
  kind: 'group'
  logic: 'AND' | 'OR'
  children: BuilderNode[]
}

export type BuilderNode = ConditionRule | ConditionGroup

/** 最大嵌套层数（含根，≤5 层）。 */
export const MAX_GROUP_DEPTH = 5

/** 试算结果。 */
export interface ConditionTrialResult {
  matched: boolean
  detail: string
}
