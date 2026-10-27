/*
 * 所属公司：四川淇利馨集团有限公司
 * 描述：基础信息中心（Bas 模块）主数据契约——后端 DTO 直通类型 + 数值枚举中文映射字典
 * 说明：
 *   1. 所有字段为扁平原始字段，字段名与后端 camelCase JSON 一致（B-2）。
 *   2. 枚举一律以数值对接，中文文案只在本文件字典维护；展示映射函数见 utils/bas-master-mappers.ts（B-4）。
 *   3. 业务 Id 一律为 string（Hashids 混淆串）；DateOnly 字段为 'YYYY-MM-DD' 字符串。
 *   4. 新建/更新入参一律不含 code（编码由后端发号器生成，N-1），编码仅作只读回显。
 */

import type { PagedQuery, PagedResult } from './admin'

export type { PagedQuery, PagedResult }

// ================ 枚举（数值） ================

/** 启停状态（1=启用 / 2=停用）。 */
export type BasStatus = 1 | 2

/** 组织节点类型（1=集团 / 2=子公司 / 3=部门 / 4=项目部）。 */
export type BasOrgType = 1 | 2 | 3 | 4

/** 供应商 / 客户类型（1=生产厂家 / 2=经销商 / 3=劳务分包商 / 4=服务商 / 5=其他）。 */
export type PartnerType = 1 | 2 | 3 | 4 | 5

/** 供应商评级（1=A / 2=B / 3=C / 4=黑名单）。 */
export type SupplierGrade = 1 | 2 | 3 | 4

/** 客户信用等级（1=A / 2=B / 3=C / 4=D）。 */
export type CustomerCreditGrade = 1 | 2 | 3 | 4

/** 合作状态（1=待准入 / 2=合作中 / 3=冻结 / 4=终止）。 */
export type PartnerStatus = 1 | 2 | 3 | 4

/** 开票类型（1=专票 / 2=普票 / 3=数电票）。 */
export type InvoiceType = 1 | 2 | 3

/** 供应商资质证照状态（1=有效 / 2=已过期 / 3=已作废）。 */
export type CertStatus = 1 | 2 | 3

/** 人员类型（1=员工 / 2=劳务人员）。 */
export type PersonType = 1 | 2

/** 证件类型（1=身份证 / 2=护照 / 3=其他）。 */
export type IdType = 1 | 2 | 3

/** 在职状态（1=试用 / 2=在职 / 3=停职 / 4=离职）。 */
export type EmployStatus = 1 | 2 | 3 | 4

/** 字典归属层级（1=集团级 / 2=子公司级）。 */
export type DictOwnerLevel = 1 | 2

/** 审批挂起请求状态（1=待审批 / 2=通过 / 3=驳回 / 4=撤回）。 */
export type ApprovalRequestStatus = 1 | 2 | 3 | 4

// ================ 字典（前端公共层唯一来源） ================

/** 启停状态中文映射。 */
export const BAS_STATUS_LABELS: Record<BasStatus, string> = {
  1: '启用',
  2: '停用',
}

/** 组织类型中文映射（D2：四级）。 */
export const BAS_ORG_TYPE_LABELS: Record<BasOrgType, string> = {
  1: '集团',
  2: '子公司',
  3: '部门',
  4: '项目部',
}

/** 组织类型图标映射（图标名取自 AppIcon 既有图标集，禁止新增图标依赖）。 */
export const BAS_ORG_TYPE_ICONS: Record<BasOrgType, string> = {
  1: 'OfficeBuilding',
  2: 'Shop',
  3: 'House',
  4: 'Management',
}

/** 供应商 / 客户类型中文映射。 */
export const PARTNER_TYPE_LABELS: Record<PartnerType, string> = {
  1: '生产厂家',
  2: '经销商',
  3: '劳务分包商',
  4: '服务商',
  5: '其他',
}

/** 供应商评级中文映射。 */
export const SUPPLIER_GRADE_LABELS: Record<SupplierGrade, string> = {
  1: 'A 级',
  2: 'B 级',
  3: 'C 级',
  4: '黑名单',
}

/** 客户信用等级中文映射。 */
export const CUSTOMER_CREDIT_GRADE_LABELS: Record<CustomerCreditGrade, string> = {
  1: 'A 级',
  2: 'B 级',
  3: 'C 级',
  4: 'D 级',
}

/** 合作状态中文映射。 */
export const PARTNER_STATUS_LABELS: Record<PartnerStatus, string> = {
  1: '待准入',
  2: '合作中',
  3: '冻结',
  4: '终止',
}

/** 开票类型中文映射。 */
export const INVOICE_TYPE_LABELS: Record<InvoiceType, string> = {
  1: '增值税专用发票',
  2: '增值税普通发票',
  3: '数电票',
}

/** 资质证照状态中文映射。 */
export const CERT_STATUS_LABELS: Record<CertStatus, string> = {
  1: '有效',
  2: '已过期',
  3: '已作废',
}

/** 人员类型中文映射。 */
export const PERSON_TYPE_LABELS: Record<PersonType, string> = {
  1: '员工',
  2: '劳务人员',
}

/** 证件类型中文映射。 */
export const ID_TYPE_LABELS: Record<IdType, string> = {
  1: '身份证',
  2: '护照',
  3: '其他',
}

/** 在职状态中文映射。 */
export const EMPLOY_STATUS_LABELS: Record<EmployStatus, string> = {
  1: '试用',
  2: '在职',
  3: '停职',
  4: '离职',
}

/** 字典归属层级中文映射。 */
export const DICT_OWNER_LEVEL_LABELS: Record<DictOwnerLevel, string> = {
  1: '集团级',
  2: '子公司级',
}

/** 审批挂起请求状态中文映射。 */
export const APPROVAL_REQUEST_STATUS_LABELS: Record<ApprovalRequestStatus, string> = {
  1: '待审批',
  2: '已通过',
  3: '已驳回',
  4: '已撤回',
}

/** 编码规则业务类型中文映射（rules 以 bizType 字符串对接）。 */
export const CODE_RULE_BIZ_LABELS: Record<string, string> = {
  org: '组织机构',
  supplier: '供应商',
  customer: '客户',
  employee: '员工',
  labour: '劳务人员',
  category: '商品分类',
}

/** 变更日志操作类型中文映射。 */
export const CHANGE_LOG_OPERATION_LABELS: Record<string, string> = {
  INSERT: '新增',
  UPDATE: '修改',
  SOFT_DELETE: '软删除',
  DELETE: '删除',
  VIEW_PLAINTEXT: '查看敏感明文',
}

// ================ 组织 ================

/** 组织扁平行（后端 OrgRow 直通）。 */
export interface BasOrgRow {
  id: string
  parentId: string | null
  /** 组织编码（后端生成，只读回显，N-1）。 */
  code: string
  name: string
  type: BasOrgType
  /** 负责人自然人 Id（人员主数据）。 */
  leaderPersonId: string | null
  /** 生效日期（YYYY-MM-DD）。 */
  effectDate: string
  /** 失效日期（null 表示长期有效）。 */
  expiryDate: string | null
  /** 数据版本号（每次结构调整 +1）。 */
  version: number
  status: BasStatus
  sortOrder: number
  /** 系统内置根节点。 */
  isBuiltin: boolean
  createdAt: string
  updatedAt: string
}

/** 组织新建 / 更新入参（无编码字段，N-1）。 */
export interface SaveBasOrgPayload {
  parentId: string | null
  name: string
  type: BasOrgType
  leaderPersonId: string | null
  sortOrder: number
  effectDate?: string | null
  expiryDate?: string | null
}

/** 组织版本历史行。 */
export interface OrgHistoryRow {
  id: string
  orgId: string
  version: number
  /** 版本快照 JSON（扁平原始字段，仅原样展示关键字段）。 */
  snapshotJson: string
  effectiveFrom: string
  changedBy: string
}

/** 组织合并入参。 */
export interface MergeOrgPayload {
  targetOrgId: string
}

/** 组织调整审批申请入参（create / update / disable / merge）。 */
export interface SubmitOrgAdjustPayload {
  action: 'create' | 'update' | 'disable' | 'merge'
  orgId?: string | null
  name?: string | null
  parentId?: string | null
  type?: BasOrgType | null
  leaderPersonId?: string | null
  sortOrder?: number | null
  targetOrgId?: string | null
  reason?: string | null
}

// ================ 岗位 ================

/** 岗位扁平行（后端 PositionMasterRow 直通）。 */
export interface BasPositionRow {
  id: string
  orgId: string
  name: string
  /** 岗位编码（后端生成，只读）。 */
  code: string
  headcount: number
  currentCount: number
  status: BasStatus
  description: string | null
  duty: string | null
  updatedAt: string
}

/** 岗位新建 / 更新入参（无编码字段）。 */
export interface SaveBasPositionPayload {
  id?: string | null
  name: string
  orgId: string
  headcount: number
  description?: string | null
  duty?: string | null
}

/** 超编审批申请入参。 */
export interface SubmitOverstaffPayload {
  headcount: number
  reason?: string | null
}

// ================ 审批挂起请求 ================

/** 审批挂起请求行。 */
export interface ApprovalRequestRow {
  id: string
  approvalInstanceId: string | null
  bizType: string
  bizId: string
  payloadJson: string | null
  status: ApprovalRequestStatus
  appliedAt: string | null
  createdAt: string
}

/** 人员候选项（组织负责人等选择场景共用；后端人员列表脱敏字段不参与）。 */
export interface PersonOption {
  id: string
  name: string
  /** 人员编码（只读回显）。 */
  code: string
}

// ================ 商品分类 ================

/** 商品分类扁平行（分类树由前端组装，B-2-1）。 */
export interface ItemCategoryRow {
  id: string
  parentId: string | null
  /** 分类编码（层级连续编码，只读）。 */
  code: string
  name: string
  levelNo: number
  /** 默认计量单位组 Id。 */
  unitGroupId: string | null
  status: BasStatus
  updatedAt: string
}

/** 新建商品分类入参（无编码字段）。 */
export interface CreateItemCategoryPayload {
  name: string
  parentId: string | null
  unitGroupId: string | null
}

/** 更新商品分类入参（编码与父级不可变更）。 */
export interface UpdateItemCategoryPayload {
  name: string
  unitGroupId: string | null
}

// ================ 计量单位 ================

/** 计量单位组扁平行。 */
export interface UnitGroupRow {
  id: string
  /** 单位组编码（后端生成，只读）。 */
  code: string
  name: string
  status: BasStatus
  updatedAt: string
}

/** 计量单位组新建 / 更新入参。 */
export interface SaveUnitGroupPayload {
  id?: string | null
  name: string
}

/** 计量单位扁平行。 */
export interface UnitRow {
  id: string
  unitGroupId: string
  /** 单位编码（后端生成，只读）。 */
  code: string
  name: string
  /** 数量精度（小数位 0~8）。 */
  precision: number
  status: BasStatus
  updatedAt: string
}

/** 计量单位新建 / 更新入参。 */
export interface SaveUnitPayload {
  id?: string | null
  name: string
  unitGroupId: string
  precision: number
}

/** 单位换算关系扁平行。 */
export interface UnitConversionRow {
  id: string
  unitGroupId: string
  fromUnitId: string
  toUnitId: string
  /** 换算率（1 源单位 = Rate 目标单位）。 */
  rate: number
  status: BasStatus
  updatedAt: string
}

/** 单位换算关系新建 / 更新入参。 */
export interface SaveUnitConversionPayload {
  id?: string | null
  unitGroupId: string
  fromUnitId: string
  toUnitId: string
  rate: number
}

// ================ 供应商 ================

/** 供应商扁平行。 */
export interface SupplierRow {
  id: string
  /** 供应商编码（后端生成，只读）。 */
  code: string
  name: string
  uscc: string
  supplierType: PartnerType
  contact: string | null
  phone: string | null
  grade: SupplierGrade
  status: PartnerStatus
  updatedAt: string
}

/** 供应商详情（主档 + 子表扁平集合）。 */
export interface SupplierDetailRow {
  master: SupplierRow
  certs: SupplierCertRow[]
  banks: SupplierBankRow[]
  invoices: SupplierInvoiceRow[]
  gradeLogs: SupplierGradeLogRow[]
}

/** 供应商新建 / 更新入参（无编码字段）。 */
export interface SaveSupplierPayload {
  name: string
  uscc: string
  supplierType: PartnerType
  contact?: string | null
  phone?: string | null
}

/** 供应商评级变更入参。 */
export interface ChangeSupplierGradePayload {
  grade: SupplierGrade
  reason?: string | null
}

/** 供应商资质证照扁平行。 */
export interface SupplierCertRow {
  id: string
  supplierId: string
  certType: string
  certNo: string
  issueOrg: string | null
  effectDate: string | null
  expiryDate: string | null
  fileId: string | null
  status: CertStatus
}

/** 供应商银行账户扁平行（账号明文仅本接口下发，展示需脱敏）。 */
export interface SupplierBankRow {
  id: string
  supplierId: string
  bankName: string
  accountName: string
  accountNo: string
  isDefault: boolean
  status: BasStatus
}

/** 供应商开票信息扁平行。 */
export interface SupplierInvoiceRow {
  id: string
  supplierId: string
  title: string
  taxNo: string | null
  bankInfo: string | null
  addrPhone: string | null
  invoiceType: InvoiceType
  status: BasStatus
}

/** 供应商评级历史扁平行。 */
export interface SupplierGradeLogRow {
  id: string
  supplierId: string
  oldGrade: SupplierGrade
  newGrade: SupplierGrade
  reason: string | null
  approvedBy: string | null
  createdAt: string
}

/** 供应商资质证照登记入参。 */
export interface SaveSupplierCertPayload {
  certType: string
  certNo: string
  issueOrg?: string | null
  effectDate?: string | null
  expiryDate?: string | null
  fileId?: string | null
}

/** 供应商银行账户登记入参。 */
export interface SaveSupplierBankPayload {
  bankName: string
  accountName: string
  accountNo: string
  isDefault: boolean
}

/** 供应商开票信息登记入参。 */
export interface SaveSupplierInvoicePayload {
  title: string
  taxNo?: string | null
  bankInfo?: string | null
  addrPhone?: string | null
  invoiceType: InvoiceType
}

// ================ 客户 ================

/** 客户扁平行（金额为原始数值，不做显示格式化，B-2-2）。 */
export interface CustomerRow {
  id: string
  /** 客户编码（后端生成，只读）。 */
  code: string
  name: string
  uscc: string
  customerType: PartnerType
  contact: string | null
  phone: string | null
  creditGrade: CustomerCreditGrade
  creditLimit: number
  creditUsed: number
  paymentTerms: string | null
  status: PartnerStatus
  updatedAt: string
}

/** 客户详情（主档 + 子表扁平集合）。 */
export interface CustomerDetailRow {
  master: CustomerRow
  banks: CustomerBankRow[]
  invoices: CustomerInvoiceRow[]
  creditLogs: CustomerCreditLogRow[]
}

/** 客户新建 / 更新入参（无编码字段）。 */
export interface SaveCustomerPayload {
  name: string
  uscc: string
  customerType: PartnerType
  contact?: string | null
  phone?: string | null
  paymentTerms?: string | null
}

/** 客户信用额度调整申请入参。 */
export interface CustomerCreditAdjustPayload {
  newLimit: number
  newCreditGrade: CustomerCreditGrade
  reason?: string | null
}

/** 客户信用额度视图（原始金额）。 */
export interface CustomerCreditViewRow {
  customerId: string
  creditLimit: number
  creditUsed: number
  /** 可用额度（额度 - 占用，后端业务规则值）。 */
  available: number
  creditGrade: CustomerCreditGrade
  status: PartnerStatus
}

/** 客户回款账户扁平行。 */
export interface CustomerBankRow {
  id: string
  customerId: string
  bankName: string
  accountName: string
  accountNo: string
  isDefault: boolean
  status: BasStatus
}

/** 客户开票信息扁平行。 */
export interface CustomerInvoiceRow {
  id: string
  customerId: string
  title: string
  taxNo: string | null
  bankInfo: string | null
  addrPhone: string | null
  invoiceType: InvoiceType
  status: BasStatus
}

/** 客户信用变更历史扁平行。 */
export interface CustomerCreditLogRow {
  id: string
  customerId: string
  oldCreditGrade: CustomerCreditGrade
  newCreditGrade: CustomerCreditGrade
  oldLimit: number
  newLimit: number
  reason: string | null
  approvedBy: string | null
  createdAt: string
}

/** 客户回款账户登记入参。 */
export interface SaveCustomerBankPayload {
  bankName: string
  accountName: string
  accountNo: string
  isDefault: boolean
}

/** 客户开票信息登记入参。 */
export interface SaveCustomerInvoicePayload {
  title: string
  taxNo?: string | null
  bankInfo?: string | null
  addrPhone?: string | null
  invoiceType: InvoiceType
}

// ================ 人员 ================

/** 人员扁平行（证件 / 手机 / 银行卡均为脱敏值）。 */
export interface PersonRow {
  id: string
  /** 人员编码 / 工号（后端生成，只读）。 */
  code: string
  name: string
  personType: PersonType
  idType: IdType
  /** 脱敏证件号（前 6 后 4）。 */
  idNoMasked: string | null
  /** 脱敏手机号（前 3 后 4）。 */
  mobileMasked: string | null
  emergencyContact: string | null
  emergencyPhone: string | null
  orgId: string | null
  positionId: string | null
  employStatus: EmployStatus
  entryDate: string
  leaveDate: string | null
  /** 脱敏银行卡号（后 4 位）。 */
  bankCardNoMasked: string | null
  updatedAt: string
}

/** 人员详情（主档 + 当前任职 + 任职历史 + 证件列表）。 */
export interface PersonDetailRow {
  master: PersonRow
  currentAssignment: PersonAssignmentRow | null
  assignments: PersonAssignmentRow[]
  certs: PersonCertRow[]
}

/** 人员任职记录扁平行。 */
export interface PersonAssignmentRow {
  id: string
  personId: string
  orgId: string | null
  positionId: string | null
  startDate: string
  endDate: string | null
  changeReason: string | null
  isCurrent: boolean
}

/** 人员证件档案扁平行。 */
export interface PersonCertRow {
  id: string
  personId: string
  certType: string
  certNo: string | null
  issuedOn: string | null
  expiresOn: string | null
  remark: string | null
}

/** 人员新建入参（证件 / 手机 / 银行卡为明文入参，后端加密落库）。 */
export interface SavePersonPayload {
  name: string
  personType: PersonType
  idType: IdType
  idNo: string
  mobile: string
  emergencyContact?: string | null
  emergencyPhone?: string | null
  orgId?: string | null
  positionId?: string | null
  employStatus: EmployStatus
  entryDate?: string | null
  bankCardNo?: string | null
}

/** 人员信息更新入参（不含编码与证件号）。 */
export interface UpdatePersonPayload {
  name: string
  personType: PersonType
  emergencyContact?: string | null
  emergencyPhone?: string | null
  orgId?: string | null
  positionId?: string | null
  effectiveDate?: string | null
  changeReason?: string | null
}

/** 人员离职入参。 */
export interface ResignPersonPayload {
  leaveDate?: string | null
  reason?: string | null
}

/** 人员再入职入参。 */
export interface RehirePersonPayload {
  entryDate?: string | null
  orgId?: string | null
  positionId?: string | null
  employStatus?: EmployStatus | null
}

/** 人员敏感字段明文行（字段级权限 + 审计）。 */
export interface PersonPlaintextRow {
  id: string
  code: string
  idNo: string
  mobile: string
  bankCardNo: string | null
}

/** 人员证件档案新增 / 更新入参。 */
export interface SavePersonCertPayload {
  certType: string
  certNo?: string | null
  issuedOn?: string | null
  expiresOn?: string | null
  remark?: string | null
}

// ================ 数据字典 ================

/** 字典类型扁平行。 */
export interface DictTypeRow {
  id: string
  typeCode: string
  typeName: string
  ownerLevel: DictOwnerLevel
  /** 所属组织 Id（子公司级有值，集团级为空）。 */
  orgId: string | null
  status: BasStatus
  /** 系统内置（不可删除，仅可启停）。 */
  isSystem: boolean
  updatedAt: string
}

/** 字典类型新建入参（类型编码由用户指定，创建后不可修改）。 */
export interface SaveDictTypePayload {
  typeCode: string
  typeName: string
  ownerLevel: DictOwnerLevel
  orgId?: string | null
}

/** 字典类型更新入参（编码与归属层级不可修改）。 */
export interface UpdateDictTypePayload {
  typeName: string
}

/** 字典项扁平行。 */
export interface DictItemRow {
  id: string
  typeCode: string
  itemCode: string
  itemName: string
  sortNo: number
  status: BasStatus
  isSystem: boolean
  updatedAt: string
}

/** 字典项新建入参（项编码创建后不可修改）。 */
export interface SaveDictItemPayload {
  itemCode: string
  itemName: string
  sortNo: number
}

/** 字典项更新入参（项编码不可修改）。 */
export interface UpdateDictItemPayload {
  itemName: string
  sortNo: number
}

// ================ 编码规则 ================

/** 编码规则扁平行（规则内容只读，仅可启停）。 */
export interface CodeRuleRow {
  bizType: string
  prefix: string
  /** 分段模板 JSON 数组（只读展示）。 */
  segmentsJson: string
  status: BasStatus
  updatedAt: string
}

// ================ 变更日志 ================

/** 变更日志扁平行（快照 JSON 已脱敏）。 */
export interface ChangeLogRow {
  eventId: string
  entityType: string
  entityId: string
  /** 操作（INSERT / UPDATE / SOFT_DELETE / DELETE / VIEW_PLAINTEXT）。 */
  operation: string
  oldValues: string | null
  newValues: string | null
  changedBy: string
  changedAt: string
}

/** 变更日志查询条件。 */
export interface ChangeLogQuery {
  entityType?: string
  entityId?: string
  changedBy?: string
  from?: string
  to?: string
  page: number
  pageSize: number
}

// ================ 导入导出 ================

/** 导入模板列元数据。 */
export interface ImportTemplateColumn {
  columnName: string
  displayName: string
  isRequired: boolean
  dataType: string | null
  description: string | null
}

/** 导入模板信息。 */
export interface ImportTemplateInfo {
  templateCode: string
  name: string
  moduleCode: string
  columns: ImportTemplateColumn[]
}

/** 行级导入错误。 */
export interface ImportRowError {
  rowNumber: number
  columnName: string
  errorMessage: string
}

/** 导入结果。 */
export interface ImportResult {
  totalRows: number
  successRows: number
  failedRows: number
  errors: ImportRowError[]
  duration: string
}

/** 下载 / 导出文件结果（Blob + 文件名）。 */
export interface MasterFileResult {
  blob: Blob
  fileName: string
}

/** 列表分页查询条件（主数据通用）。 */
export interface MasterPagedQuery extends PagedQuery {
  [key: string]: string | number | undefined
}
