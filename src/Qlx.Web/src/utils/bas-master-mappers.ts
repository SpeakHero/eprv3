/*
 * 所属公司：四川淇利馨集团有限公司
 * 描述：基础信息中心（Bas）展示层纯函数——树组装 / 枚举中文与色调 / 金额日期格式化 / 脱敏 / 快照摘要
 * 说明：
 *   1. 全部为无框架依赖纯函数，页面禁止重复实现（B-4）；后端只下发扁平原始字段与数值枚举（B-2）。
 *   2. 中文映射字典唯一来源为 types/bas-master.ts；本文件只做取用、兜底与展示加工。
 *   3. 金额 / 日期 / 脱敏仅用于界面呈现，不参与任何业务判定。
 */

import type { TagTone } from '@/components/crud/QlxDataTable.vue'
import type {
  BasOrgType,
  ChangeLogRow,
  CodeRuleRow,
  CustomerCreditGrade,
  CertStatus,
  EmployStatus,
  ItemCategoryRow,
  PartnerStatus,
  SupplierGrade,
  BasStatus,
  DictOwnerLevel,
  ApprovalRequestStatus,
} from '@/types/bas-master'
import {
  APPROVAL_REQUEST_STATUS_LABELS,
  BAS_STATUS_LABELS,
  CERT_STATUS_LABELS,
  CHANGE_LOG_OPERATION_LABELS,
  CODE_RULE_BIZ_LABELS,
  CUSTOMER_CREDIT_GRADE_LABELS,
  DICT_OWNER_LEVEL_LABELS,
  EMPLOY_STATUS_LABELS,
  ID_TYPE_LABELS,
  INVOICE_TYPE_LABELS,
  PARTNER_STATUS_LABELS,
  PARTNER_TYPE_LABELS,
  PERSON_TYPE_LABELS,
  SUPPLIER_GRADE_LABELS,
} from '@/types/bas-master'

/* ============================================================
 * 一、树组装（商品分类；B-2-1 后端只下发扁平列表）
 * ============================================================ */

/** 商品分类树节点。 */
export type CategoryTreeNode = ItemCategoryRow & { children: CategoryTreeNode[] }

/**
 * 扁平分类列表 → 嵌套树。
 * 同级按编码升序（后端层级连续编码，字典序即层级展示顺序）；父缺失的孤儿节点兜底为根。
 */
export function buildCategoryTree(flat: ItemCategoryRow[]): CategoryTreeNode[] {
  const byId = new Map<string, CategoryTreeNode>()
  for (const row of flat) {
    byId.set(row.id, { ...row, children: [] })
  }
  const roots: CategoryTreeNode[] = []
  for (const node of byId.values()) {
    const parent = node.parentId ? byId.get(node.parentId) : undefined
    if (parent) {
      parent.children.push(node)
    } else {
      roots.push(node)
    }
  }
  const sortRecursive = (nodes: CategoryTreeNode[]): void => {
    nodes.sort((a, b) => a.code.localeCompare(b.code))
    for (const node of nodes) {
      sortRecursive(node.children)
    }
  }
  sortRecursive(roots)
  return roots
}

/** 按 Id 深度优先查找分类节点。 */
export function findCategoryNode(
  nodes: CategoryTreeNode[],
  id: string,
): CategoryTreeNode | null {
  for (const node of nodes) {
    if (node.id === id) {
      return node
    }
    const found = findCategoryNode(node.children, id)
    if (found) {
      return found
    }
  }
  return null
}

/** 收集某分类及其全部下级 Id（新建子分类时上级选项排除自身子树）。 */
export function collectCategoryIds(node: CategoryTreeNode): Set<string> {
  const result = new Set<string>([node.id])
  for (const child of node.children) {
    for (const id of collectCategoryIds(child)) {
      result.add(id)
    }
  }
  return result
}

/** 分类树 → 层级下拉选项（「　」× depth + 「└ 」缩进）。 */
export function buildCategoryOptions(
  nodes: CategoryTreeNode[],
  exclude?: Set<string>,
): Array<{ label: string; value: string }> {
  const options: Array<{ label: string; value: string }> = []
  const walk = (list: CategoryTreeNode[], depth: number): void => {
    for (const node of list) {
      if (!exclude?.has(node.id)) {
        options.push({
          label: `${depth ? `${'　'.repeat(depth)}└ ` : ''}${node.name}`,
          value: node.id,
        })
      }
      walk(node.children, depth + 1)
    }
  }
  walk(nodes, 0)
  return options
}

/* ============================================================
 * 二、枚举中文 / 色调 / 选项（数值对接，中文在前端维护）
 * ============================================================ */

/** 状态码 → 中文（未知码兜底「—」）。 */
export function labelOfCode(
  map: Record<number, string>,
  code: number | null | undefined,
  fallback = '—',
): string {
  if (code === null || code === undefined) {
    return fallback
  }
  return map[code] ?? fallback
}

/** 数值枚举 → 表单下拉选项（value 为数值）。 */
export function toNumberOptions(
  map: Record<number, string>,
): Array<{ label: string; value: number }> {
  return Object.entries(map).map(([code, label]) => ({ label, value: Number(code) }))
}

/** 数值枚举 → 搜索下拉选项（value 为字符串，配合 SearchField）。 */
export function toStringOptions(
  map: Record<number, string>,
): Array<{ label: string; value: string }> {
  return Object.entries(map).map(([code, label]) => ({ label, value: code }))
}

/** 启停状态中文。 */
export const basStatusLabel = (status: number | null | undefined): string =>
  labelOfCode(BAS_STATUS_LABELS, status)

/** 启停状态色调。 */
export const basStatusTone = (status: number | null | undefined): TagTone =>
  status === 1 ? 'success' : 'info'

/** 供应商类型中文。 */
export const partnerTypeLabel = (type: number | null | undefined): string =>
  labelOfCode(PARTNER_TYPE_LABELS, type)

/** 供应商评级中文。 */
export const supplierGradeLabel = (grade: number | null | undefined): string =>
  labelOfCode(SUPPLIER_GRADE_LABELS, grade)

/** 供应商评级色调（黑名单红 / C 橙 / B 蓝 / A 绿）。 */
export function supplierGradeTone(grade: number | null | undefined): TagTone {
  switch (grade as SupplierGrade) {
    case 1:
      return 'success'
    case 2:
      return 'primary'
    case 3:
      return 'warning'
    case 4:
      return 'danger'
    default:
      return 'info'
  }
}

/** 客户信用等级中文。 */
export const creditGradeLabel = (grade: number | null | undefined): string =>
  labelOfCode(CUSTOMER_CREDIT_GRADE_LABELS, grade)

/** 客户信用等级色调（D 级红）。 */
export function creditGradeTone(grade: number | null | undefined): TagTone {
  switch (grade as CustomerCreditGrade) {
    case 1:
      return 'success'
    case 2:
      return 'primary'
    case 3:
      return 'warning'
    case 4:
      return 'danger'
    default:
      return 'info'
  }
}

/** 合作状态中文。 */
export const partnerStatusLabel = (status: number | null | undefined): string =>
  labelOfCode(PARTNER_STATUS_LABELS, status)

/** 合作状态色调（待准入灰 / 合作中绿 / 冻结橙 / 终止红）。 */
export function partnerStatusTone(status: number | null | undefined): TagTone {
  switch (status as PartnerStatus) {
    case 1:
      return 'info'
    case 2:
      return 'success'
    case 3:
      return 'warning'
    case 4:
      return 'danger'
    default:
      return 'info'
  }
}

/** 开票类型中文。 */
export const invoiceTypeLabel = (type: number | null | undefined): string =>
  labelOfCode(INVOICE_TYPE_LABELS, type)

/** 资质证照状态中文。 */
export const certStatusLabel = (status: number | null | undefined): string =>
  labelOfCode(CERT_STATUS_LABELS, status)

/** 资质证照状态色调。 */
export function certStatusTone(status: number | null | undefined): TagTone {
  switch (status as CertStatus) {
    case 1:
      return 'success'
    case 2:
      return 'warning'
    case 3:
      return 'info'
    default:
      return 'info'
  }
}

/** 人员类型中文。 */
export const personTypeLabel = (type: number | null | undefined): string =>
  labelOfCode(PERSON_TYPE_LABELS, type)

/** 证件类型中文。 */
export const idTypeLabel = (type: number | null | undefined): string =>
  labelOfCode(ID_TYPE_LABELS, type)

/** 在职状态中文。 */
export const employStatusLabel = (status: number | null | undefined): string =>
  labelOfCode(EMPLOY_STATUS_LABELS, status)

/** 在职状态色调（试用蓝 / 在职绿 / 停职橙 / 离职灰）。 */
export function employStatusTone(status: number | null | undefined): TagTone {
  switch (status as EmployStatus) {
    case 1:
      return 'primary'
    case 2:
      return 'success'
    case 3:
      return 'warning'
    case 4:
      return 'info'
    default:
      return 'info'
  }
}

/** 字典归属层级中文。 */
export const dictOwnerLevelLabel = (level: number | null | undefined): string =>
  labelOfCode(DICT_OWNER_LEVEL_LABELS, level)

/** 审批挂起请求状态中文。 */
export const approvalRequestStatusLabel = (status: number | null | undefined): string =>
  labelOfCode(APPROVAL_REQUEST_STATUS_LABELS, status)

/** 审批挂起请求状态色调。 */
export function approvalRequestStatusTone(status: number | null | undefined): TagTone {
  switch (status as ApprovalRequestStatus) {
    case 1:
      return 'warning'
    case 2:
      return 'success'
    case 3:
      return 'danger'
    case 4:
      return 'info'
    default:
      return 'info'
  }
}

/** 编码规则业务类型中文。 */
export function codeRuleBizTypeLabel(bizType: string | null | undefined): string {
  return bizType ? (CODE_RULE_BIZ_LABELS[bizType] ?? bizType) : '—'
}

/** 变更日志操作中文。 */
export function changeLogOperationLabel(operation: string | null | undefined): string {
  return operation ? (CHANGE_LOG_OPERATION_LABELS[operation] ?? operation) : '—'
}

/** 变更日志对象类型中文（未登记类型原样回显）。 */
export const CHANGE_LOG_ENTITY_LABELS: Record<string, string> = {
  Organization: '组织机构',
  Position: '岗位',
  ItemCategory: '商品分类',
  UnitGroup: '计量单位组',
  Unit: '计量单位',
  UnitConversion: '单位换算',
  Supplier: '供应商',
  Customer: '客户',
  Person: '人员',
  DictType: '字典类型',
  DictItem: '字典项',
  CodeRule: '编码规则',
}

export function changeLogEntityLabel(entityType: string | null | undefined): string {
  return entityType ? (CHANGE_LOG_ENTITY_LABELS[entityType] ?? entityType) : '—'
}

/** 数值枚举 catalog（页面按需取用，避免各自硬编码顺序）。 */
export const ENUM_OPTION_MAPS = {
  basStatus: BAS_STATUS_LABELS,
  partnerType: PARTNER_TYPE_LABELS,
  supplierGrade: SUPPLIER_GRADE_LABELS,
  creditGrade: CUSTOMER_CREDIT_GRADE_LABELS,
  partnerStatus: PARTNER_STATUS_LABELS,
  invoiceType: INVOICE_TYPE_LABELS,
  certStatus: CERT_STATUS_LABELS,
  personType: PERSON_TYPE_LABELS,
  idType: ID_TYPE_LABELS,
  employStatus: EMPLOY_STATUS_LABELS,
  dictOwnerLevel: DICT_OWNER_LEVEL_LABELS,
  approvalRequestStatus: APPROVAL_REQUEST_STATUS_LABELS,
} as const

/** 供页面校验使用的已声明枚举集合（不改变后端契约，仅前端展示字典）。 */
export type KnownEnumKey = keyof typeof ENUM_OPTION_MAPS

/** 类型守卫：值是否为合法的 Bas 组织类型数值（1~4）。 */
export function isBasOrgType(value: unknown): value is BasOrgType {
  return value === 1 || value === 2 || value === 3 || value === 4
}

/** 类型守卫：值是否为合法启停状态（1~2）。 */
export function isBasStatus(value: unknown): value is BasStatus {
  return value === 1 || value === 2
}

/** 类型守卫：值是否为合法归属层级（1~2）。 */
export function isDictOwnerLevel(value: unknown): value is DictOwnerLevel {
  return value === 1 || value === 2
}

/* ============================================================
 * 三、金额 / 日期 / 数字格式化（仅展示，不改业务值）
 * ============================================================ */

/** 金额 → 千分位 + 固定小数位（空值占位）。 */
export function formatAmount(
  value: number | null | undefined,
  digits = 2,
  placeholder = '—',
): string {
  if (value === null || value === undefined || Number.isNaN(value)) {
    return placeholder
  }
  return new Intl.NumberFormat('zh-CN', {
    minimumFractionDigits: digits,
    maximumFractionDigits: digits,
  }).format(value)
}

/** 百分比 → 「12.5%」。 */
export function formatPercent(
  value: number | null | undefined,
  digits = 1,
  placeholder = '—',
): string {
  if (value === null || value === undefined || Number.isNaN(value)) {
    return placeholder
  }
  return `${formatAmount(value, digits, placeholder)}%`
}

/** 换算率 → 最多 6 位小数并去尾零（1 源单位 = Rate 目标单位）。 */
export function formatRate(
  value: number | null | undefined,
  placeholder = '—',
): string {
  if (value === null || value === undefined || Number.isNaN(value)) {
    return placeholder
  }
  return String(Number(value.toFixed(6)))
}

/**
 * DateOnly（'YYYY-MM-DD'）或 ISO 时间 → 'YYYY-MM-DD'。
 * 仅做展示裁剪，不改变后端已序列化的日期值。
 */
export function formatDateOnly(
  value: string | null | undefined,
  placeholder = '—',
): string {
  if (!value) {
    return placeholder
  }
  const matched = /^\d{4}-\d{2}-\d{2}/.exec(value)
  return matched ? matched[0] : value
}

/** 生效期展示（失效日期为空视为长期有效）。 */
export function formatEffectPeriod(
  effectDate: string | null | undefined,
  expiryDate: string | null | undefined,
): string {
  const from = formatDateOnly(effectDate)
  const to = expiryDate ? formatDateOnly(expiryDate) : '长期有效'
  return `${from} ~ ${to}`
}

/* ============================================================
 * 四、脱敏（明文查看弹窗展示；后端列表已脱敏）
 * ============================================================ */

/** 手机号脱敏（前 3 后 4）。 */
export function maskMobile(mobile: string | null | undefined, placeholder = '—'): string {
  const value = (mobile ?? '').trim()
  if (!value) {
    return placeholder
  }
  if (value.length < 8) {
    return `${value.slice(0, 1)}****`
  }
  return `${value.slice(0, 3)}****${value.slice(-4)}`
}

/** 证件号脱敏（前 6 后 4）。 */
export function maskIdNo(idNo: string | null | undefined, placeholder = '—'): string {
  const value = (idNo ?? '').trim()
  if (!value) {
    return placeholder
  }
  if (value.length <= 10) {
    return `${value.slice(0, 2)}****${value.slice(-2)}`
  }
  return `${value.slice(0, 6)}********${value.slice(-4)}`
}

/** 银行卡号脱敏（仅末 4 位）。 */
export function maskBankCard(cardNo: string | null | undefined, placeholder = '—'): string {
  const value = (cardNo ?? '').trim()
  if (!value) {
    return placeholder
  }
  return `**** **** ${value.slice(-4)}`
}

/* ============================================================
 * 五、JSON 快照展示（组织版本历史 / 变更日志）
 * ============================================================ */

/** JSON 文本美化（非法 JSON 原样回显，空值占位）。 */
export function prettyJson(json: string | null | undefined, placeholder = '—'): string {
  if (!json) {
    return placeholder
  }
  try {
    return JSON.stringify(JSON.parse(json), null, 2)
  } catch {
    return json
  }
}

/** 组织版本快照摘要（仅取展示所需关键字段）。 */
export interface OrgSnapshotSummary {
  name: string
  type: number | null
  status: number | null
  parentId: string | null
  version: number | null
}

/** 解析组织版本快照 JSON → 关键字段摘要（解析失败返回 null）。 */
export function summarizeOrgSnapshot(
  snapshotJson: string | null | undefined,
): OrgSnapshotSummary | null {
  if (!snapshotJson) {
    return null
  }
  try {
    const raw = JSON.parse(snapshotJson) as Record<string, unknown>
    return {
      name: typeof raw.name === 'string' ? raw.name : '',
      type: typeof raw.type === 'number' ? raw.type : null,
      status: typeof raw.status === 'number' ? raw.status : null,
      parentId: typeof raw.parentId === 'string' ? raw.parentId : null,
      version: typeof raw.version === 'number' ? raw.version : null,
    }
  } catch {
    return null
  }
}

/** 编码规则分段模板 → 可读说明（如「类型代码 · - · 2 位流水号」）。 */
export function formatSegments(segmentsJson: string | null | undefined): string {
  if (!segmentsJson) {
    return '—'
  }
  let segments: unknown
  try {
    segments = JSON.parse(segmentsJson)
  } catch {
    return segmentsJson
  }
  if (!Array.isArray(segments)) {
    return segmentsJson
  }
  const describe = (segment: unknown): string => {
    const token = String(segment ?? '')
    if (token === '{type}') return '类型代码'
    if (token === '{parent}') return '上级编码'
    if (token === '{yyyyMM}') return '年月(yyyyMM)'
    const seq = /^\{seq:(\d+)\}$/.exec(token)
    if (seq) return `${seq[1]} 位流水号`
    return token
  }
  return segments.map(describe).join(' · ')
}

/* ============================================================
 * 六、表格行键补齐（CodeRuleRow / ChangeLogRow 无 id 字段）
 * ============================================================ */

/** 编码规则行补 id（bizType 唯一）。 */
export function toCodeRuleRows(rows: CodeRuleRow[]): Array<CodeRuleRow & { id: string }> {
  return rows.map((row) => ({ ...row, id: row.bizType }))
}

/** 变更日志行补 id（eventId 唯一）。 */
export function toChangeLogRows(rows: ChangeLogRow[]): Array<ChangeLogRow & { id: string }> {
  return rows.map((row) => ({ ...row, id: row.eventId }))
}

/* ============================================================
 * 七、文件下载（导入模板 / 导出结果）
 * ============================================================ */

/** 触发浏览器下载 Blob（导入模板 / 导出文件共用）。 */
export function triggerDownload(blob: Blob, fileName: string): void {
  const url = URL.createObjectURL(blob)
  const anchor = document.createElement('a')
  anchor.href = url
  anchor.download = fileName
  document.body.appendChild(anchor)
  anchor.click()
  anchor.remove()
  URL.revokeObjectURL(url)
}

/* ============================================================
 * 八、审批实例归并（组织调整审批状态；一次加载、禁 N+1）
 * ============================================================ */

/** 组织调整审批的 bizType（与后端提交调整申请时写入的值一致）。 */
export const ORG_ADJUST_BIZ_TYPE = 'bas.org.adjust'

/** 审批实例最小字段（列表接口直出；status 为整数 ApprovalStatusCode）。 */
export interface ApprovalInstanceLite {
  instanceId: string
  bizType: string
  bizId: string
  /** 整数审批状态：0 草稿 / 1 审批中 / 2 通过 / 3 驳回 / 4 撤回 / 5 终止 / 6 取消。 */
  status: number
  startedAt: string
}

/** 发起时间比较（不可解析时回退 ISO 字符串字典序，即时间序）。 */
function compareStartedAt(left: string, right: string): number {
  const leftTime = Date.parse(left)
  const rightTime = Date.parse(right)
  if (Number.isNaN(leftTime) || Number.isNaN(rightTime)) {
    return left.localeCompare(right)
  }
  return leftTime - rightTime
}

/**
 * 审批实例列表 → Map<bizId, 该业务对象最近一条实例>。
 * 同 bizId 取 startedAt 最新（并列取列表中靠后者）；其他 bizType 与无 bizId 的实例一律忽略。
 */
export function mapOrgAdjustApprovals<T extends ApprovalInstanceLite>(
  instances: readonly T[],
  bizType: string = ORG_ADJUST_BIZ_TYPE,
): Map<string, T> {
  const result = new Map<string, T>()
  for (const instance of instances) {
    if (instance.bizType !== bizType || !instance.bizId) {
      continue
    }
    const current = result.get(instance.bizId)
    if (!current || compareStartedAt(instance.startedAt, current.startedAt) >= 0) {
      result.set(instance.bizId, instance)
    }
  }
  return result
}
