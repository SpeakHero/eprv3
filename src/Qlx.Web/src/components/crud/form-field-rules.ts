/*
 * 所属公司：四川淇利馨集团有限公司
 * 描述：通用表单字段契约与必填判定纯函数（含条件必填）
 * 说明：
 *   1. 字段类型定义从 QlxFormDialog.vue 迁出，避免 `<script setup>` 无法导出函数（QlxFormDialog 保留类型 re-export 兼容旧引用）；
 *   2. 必填判定抽为纯函数便于单测：required 恒真；requiredWhen 命中即必填（V-1 前端 UX 层校验）；
 *   3. 条件必填随表单值响应式重算由调用方（QlxFormDialog 的 rules computed）保证。
 */

/** 下拉选项。 */
export interface FormFieldOption {
  label: string
  value: string | number
}

/** 字段值类型（多选下拉为 string[]）。 */
export type FormValue = string | number | boolean | string[]

/** 通用表单字段配置。 */
export interface FormField {
  prop: string
  label: string
  type: 'input' | 'textarea' | 'select' | 'number' | 'switch' | 'date'
  placeholder?: string
  required?: boolean
  /** 条件必填：入参为当前表单值快照，返回 true 时该字段必填。 */
  requiredWhen?: (values: Record<string, FormValue>) => boolean
  options?: FormFieldOption[]
  /** 下拉是否多选（值为 string[]）。 */
  multiple?: boolean
  /** 下拉是否可搜索。 */
  filterable?: boolean
  /** 输入提示（表单项下方灰字）。 */
  hint?: string
  /** 打开时的初始值。 */
  defaultValue?: FormValue
  /** 正则校验（input 型）。 */
  pattern?: RegExp
  patternMessage?: string
  maxlength?: number
  disabled?: boolean
  /** 是否独占整行（默认单字段半宽）。 */
  full?: boolean
}

/**
 * 判定字段是否必填：required 恒真，requiredWhen 命中即必填，两者皆无则非必填。
 * values 省略时按空表单处理（条件必填的默认值判定由回调自行兜底）。
 */
export function isFieldRequired(
  field: FormField,
  values: Record<string, FormValue> = {},
): boolean {
  return Boolean(field.required) || Boolean(field.requiredWhen?.(values))
}
