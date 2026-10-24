/*
 * 所属公司：四川淇利馨集团有限公司
 * 描述：condition-builder mock 服务——字段树 / 样例数据试算（契约形态对齐后端条件预览接口）
 * 说明：可切换真实实现（POST /api/support/conditions/preview，入参 ast_json + sample_data），
 *       本文件仅以内存实现模拟，字段结构与响应形态保持一致。
 */

import type {
  ConditionAstNode,
  ConditionFieldGroup,
  ConditionTrialResult,
} from './types'
import { evaluateAst } from './ast'

/** 示例字段树（订单审批场景；真实环境由业务模块按审批定义下发）。 */
export const mockConditionFields: ConditionFieldGroup[] = [
  {
    key: 'order',
    label: '订单信息',
    children: [
      { name: 'total_amount', label: '订单金额', type: 'number', decimalHint: '保留 2 位小数' },
      { name: 'status', label: '单据状态', type: 'string' },
      { name: 'order_date', label: '下单日期', type: 'date' },
      { name: 'department_code', label: '申请部门', type: 'dictionary', options: [
        { label: '财务部', value: 'FIN' },
        { label: '采购部', value: 'PUR' },
        { label: '销售部', value: 'SAL' },
      ] },
    ],
  },
  {
    key: 'finance',
    label: '财务信息',
    children: [
      { name: 'invoice_count', label: '发票数量', type: 'number' },
      { name: 'settled', label: '是否已结清', type: 'boolean' },
      { name: 'submitted_at', label: '提交时间', type: 'datetime' },
      { name: 'remark', label: '备注', type: 'string' },
    ],
  },
]

/** 样例数据（试算默认值；键为变量名，值形态与后端 IVariableResolver 一致）。 */
export const mockSampleData: Record<string, unknown> = {
  total_amount: 68000,
  status: 'Draft',
  order_date: '2026-10-01',
  department_code: 'FIN',
  invoice_count: 3,
  settled: false,
  submitted_at: '2026-10-20T09:30:00',
  remark: '年度采购框架协议项下订单',
}

/** 模拟网络延迟（ms）。 */
const MOCK_DELAY_MS = 120

function delay<T>(value: T): Promise<T> {
  return new Promise((resolve) => window.setTimeout(() => resolve(value), MOCK_DELAY_MS))
}

/** 拉取字段树（真实环境：GET /api/support/conditions/fields?definitionCode=...）。 */
export function fetchConditionFields(): Promise<ConditionFieldGroup[]> {
  return delay(mockConditionFields.map((group) => ({ ...group, children: [...group.children] })))
}

/** 试算请求（对齐后端预览接口入参形态）。 */
export interface ConditionPreviewRequest {
  /** AST JSON（expr_json 形态）。 */
  astJson: ConditionAstNode
  /** 样例数据（变量键值）。 */
  sampleData: Record<string, unknown>
}

/** 试算：mock 直接本地求值；真实环境 POST /api/support/conditions/preview。 */
export function previewCondition(request: ConditionPreviewRequest): Promise<ConditionTrialResult> {
  try {
    const value = evaluateAst(request.astJson, request.sampleData)
    const matched = typeof value === 'boolean' ? value : Boolean(value)
    return delay({
      matched,
      detail: `引擎求值结果：${String(value)}`,
    })
  } catch (error) {
    return Promise.resolve({
      matched: false,
      detail: error instanceof Error ? error.message : '试算失败',
    })
  }
}
