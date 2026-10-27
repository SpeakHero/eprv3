/*
 * 所属公司：四川淇利馨集团有限公司
 * 描述：基础信息中心（Bas 模块）主数据统一 API 封装——组织 / 岗位 / 分类 / 单位 / 供应商 / 客户 /
 *       人员 / 字典 / 编码规则 / 变更日志 / 导入导出
 * 说明：
 *   1. 全部使用 core/http.ts 共享客户端（自动注入租户 Token + 401 静默续期）；
 *   2. 响应统一解包 ApiResponse<T>；业务失败 / 网络异常抛出可读（后端 message 原样透传，422/409/403 可见）；
 *   3. 端点严格对齐 Qlx.Modules.Bas 各 Controller（路径 / 方法 / 查询参数）；
 *   4. 所有 Id 参数声明为 string（Hashids 混淆串）；新建/更新入参不含 code（N-1）。
 */

import { apiRequest, apiRequestBlob } from '@/api/adapters/request'
import type { PagedResult } from '@/types/admin'
import type {
  ApprovalRequestRow,
  BasOrgRow,
  BasPositionRow,
  ChangeLogQuery,
  ChangeLogRow,
  CodeRuleRow,
  CreateItemCategoryPayload,
  CustomerBankRow,
  CustomerCreditAdjustPayload,
  CustomerCreditViewRow,
  CustomerDetailRow,
  CustomerInvoiceRow,
  CustomerRow,
  DictItemRow,
  DictTypeRow,
  ItemCategoryRow,
  ImportResult,
  ImportTemplateInfo,
  MasterFileResult,
  MergeOrgPayload,
  OrgHistoryRow,
  PersonCertRow,
  PersonDetailRow,
  PersonPlaintextRow,
  PersonRow,
  RehirePersonPayload,
  ResignPersonPayload,
  SaveBasOrgPayload,
  SaveBasPositionPayload,
  SaveCustomerBankPayload,
  SaveCustomerInvoicePayload,
  SaveCustomerPayload,
  SaveDictItemPayload,
  SaveDictTypePayload,
  SavePersonCertPayload,
  SavePersonPayload,
  SaveSupplierBankPayload,
  SaveSupplierCertPayload,
  SaveSupplierInvoicePayload,
  SaveSupplierPayload,
  SaveUnitConversionPayload,
  SaveUnitGroupPayload,
  SaveUnitPayload,
  SubmitOrgAdjustPayload,
  SubmitOverstaffPayload,
  SupplierBankRow,
  SupplierCertRow,
  SupplierDetailRow,
  SupplierInvoiceRow,
  SupplierRow,
  UnitConversionRow,
  UnitGroupRow,
  UnitRow,
  UpdateDictItemPayload,
  UpdateDictTypePayload,
  UpdateItemCategoryPayload,
  UpdatePersonPayload,
} from '@/types/bas-master'

/* ============================================================
 * 组织机构（/api/bas/orgs）
 * ============================================================ */

const ORGS = '/api/bas/orgs'

export const orgMasterApi = {
  /** 组织扁平列表（可选回看某日期生效版本）。 */
  list(params: { asOfDate?: string } = {}): Promise<BasOrgRow[]> {
    const query: Record<string, unknown> = {}
    if (params.asOfDate) query.asOfDate = params.asOfDate
    return apiRequest<BasOrgRow[]>({ method: 'get', url: ORGS, params: query })
  },
  /** 组织详情。 */
  get(id: string): Promise<BasOrgRow> {
    return apiRequest<BasOrgRow>({ method: 'get', url: `${ORGS}/${encodeURIComponent(id)}` })
  },
  /** 组织版本历史。 */
  history(id: string): Promise<OrgHistoryRow[]> {
    return apiRequest<OrgHistoryRow[]>({
      method: 'get',
      url: `${ORGS}/${encodeURIComponent(id)}/history`,
    })
  },
  /** 创建组织（无编码字段，N-1）。 */
  create(payload: SaveBasOrgPayload): Promise<BasOrgRow> {
    return apiRequest<BasOrgRow>({ method: 'post', url: ORGS, data: payload })
  },
  /** 更新组织。 */
  update(id: string, payload: SaveBasOrgPayload): Promise<BasOrgRow> {
    return apiRequest<BasOrgRow>({
      method: 'put',
      url: `${ORGS}/${encodeURIComponent(id)}`,
      data: payload,
    })
  },
  /** 停用 / 启用组织。 */
  toggle(id: string): Promise<BasOrgRow> {
    return apiRequest<BasOrgRow>({
      method: 'post',
      url: `${ORGS}/${encodeURIComponent(id)}/toggle`,
    })
  },
  /** 合并组织（源组织归档）。 */
  merge(id: string, payload: MergeOrgPayload): Promise<BasOrgRow> {
    return apiRequest<BasOrgRow>({
      method: 'post',
      url: `${ORGS}/${encodeURIComponent(id)}/merge`,
      data: payload,
    })
  },
  /** 提交组织调整审批申请（挂起，审批通过后生效）。 */
  submitAdjust(payload: SubmitOrgAdjustPayload): Promise<ApprovalRequestRow> {
    return apiRequest<ApprovalRequestRow>({ method: 'post', url: `${ORGS}/requests`, data: payload })
  },
}

/* ============================================================
 * 岗位（/api/bas/positions）
 * ============================================================ */

const POSITIONS = '/api/bas/positions'

export const positionApi = {
  /** 岗位分页列表（orgId 含全部下级）。 */
  list(params: {
    orgId?: string
    keyword?: string
    page: number
    pageSize: number
  }): Promise<PagedResult<BasPositionRow>> {
    const query: Record<string, unknown> = { page: params.page, pageSize: params.pageSize }
    if (params.orgId) query.orgId = params.orgId
    if (params.keyword) query.keyword = params.keyword
    return apiRequest<PagedResult<BasPositionRow>>({ method: 'get', url: POSITIONS, params: query })
  },
  /** 新建 / 更新岗位。 */
  save(payload: SaveBasPositionPayload): Promise<BasPositionRow> {
    return apiRequest<BasPositionRow>({ method: 'post', url: POSITIONS, data: payload })
  },
  /** 停用 / 启用岗位。 */
  toggle(id: string): Promise<BasPositionRow> {
    return apiRequest<BasPositionRow>({
      method: 'post',
      url: `${POSITIONS}/${encodeURIComponent(id)}/toggle`,
    })
  },
  /** 提交超编审批申请。 */
  submitOverstaff(id: string, payload: SubmitOverstaffPayload): Promise<ApprovalRequestRow> {
    return apiRequest<ApprovalRequestRow>({
      method: 'post',
      url: `${POSITIONS}/${encodeURIComponent(id)}/overstaff-requests`,
      data: payload,
    })
  },
}

/* ============================================================
 * 商品分类（/api/bas/categories）
 * ============================================================ */

const CATEGORIES = '/api/bas/categories'

export const categoryApi = {
  /** 分类扁平列表（树由前端组装，B-2-1）。 */
  list(): Promise<ItemCategoryRow[]> {
    return apiRequest<ItemCategoryRow[]>({ method: 'get', url: CATEGORIES })
  },
  /** 分类详情。 */
  get(id: string): Promise<ItemCategoryRow> {
    return apiRequest<ItemCategoryRow>({ method: 'get', url: `${CATEGORIES}/${encodeURIComponent(id)}` })
  },
  /** 新建分类。 */
  create(payload: CreateItemCategoryPayload): Promise<ItemCategoryRow> {
    return apiRequest<ItemCategoryRow>({ method: 'post', url: CATEGORIES, data: payload })
  },
  /** 更新分类（编码与层级不可变更）。 */
  update(id: string, payload: UpdateItemCategoryPayload): Promise<ItemCategoryRow> {
    return apiRequest<ItemCategoryRow>({
      method: 'put',
      url: `${CATEGORIES}/${encodeURIComponent(id)}`,
      data: payload,
    })
  },
  /** 停用 / 启用分类。 */
  toggle(id: string): Promise<ItemCategoryRow> {
    return apiRequest<ItemCategoryRow>({
      method: 'post',
      url: `${CATEGORIES}/${encodeURIComponent(id)}/toggle`,
    })
  },
  /** 提交分类停用审批申请。 */
  submitDisable(id: string): Promise<ApprovalRequestRow> {
    return apiRequest<ApprovalRequestRow>({
      method: 'post',
      url: `${CATEGORIES}/${encodeURIComponent(id)}/disable-requests`,
    })
  },
}

/* ============================================================
 * 计量单位（/api/bas/unit-groups、/api/bas/units、/api/bas/units/conversions）
 * ============================================================ */

const UNIT_GROUPS = '/api/bas/unit-groups'
const UNITS = '/api/bas/units'

export const unitApi = {
  /** 单位组扁平列表。 */
  listGroups(): Promise<UnitGroupRow[]> {
    return apiRequest<UnitGroupRow[]>({ method: 'get', url: UNIT_GROUPS })
  },
  /** 新建 / 更新单位组。 */
  saveGroup(payload: SaveUnitGroupPayload): Promise<UnitGroupRow> {
    return apiRequest<UnitGroupRow>({ method: 'post', url: UNIT_GROUPS, data: payload })
  },
  /** 停用 / 启用单位组。 */
  toggleGroup(id: string): Promise<UnitGroupRow> {
    return apiRequest<UnitGroupRow>({
      method: 'post',
      url: `${UNIT_GROUPS}/${encodeURIComponent(id)}/toggle`,
    })
  },
  /** 计量单位扁平列表。 */
  list(): Promise<UnitRow[]> {
    return apiRequest<UnitRow[]>({ method: 'get', url: UNITS })
  },
  /** 新建 / 更新计量单位。 */
  save(payload: SaveUnitPayload): Promise<UnitRow> {
    return apiRequest<UnitRow>({ method: 'post', url: UNITS, data: payload })
  },
  /** 停用 / 启用计量单位。 */
  toggle(id: string): Promise<UnitRow> {
    return apiRequest<UnitRow>({ method: 'post', url: `${UNITS}/${encodeURIComponent(id)}/toggle` })
  },
  /** 换算关系列表（可按单位组过滤）。 */
  listConversions(unitGroupId?: string): Promise<UnitConversionRow[]> {
    const query: Record<string, unknown> = {}
    if (unitGroupId) query.unitGroupId = unitGroupId
    return apiRequest<UnitConversionRow[]>({ method: 'get', url: `${UNITS}/conversions`, params: query })
  },
  /** 新建 / 更新换算关系（换算率变更写审计）。 */
  saveConversion(payload: SaveUnitConversionPayload): Promise<UnitConversionRow> {
    return apiRequest<UnitConversionRow>({ method: 'post', url: `${UNITS}/conversions`, data: payload })
  },
}

/* ============================================================
 * 供应商（/api/bas/suppliers）
 * ============================================================ */

const SUPPLIERS = '/api/bas/suppliers'

export const supplierApi = {
  /** 供应商分页列表。 */
  list(params: {
    grade?: string
    status?: string
    keyword?: string
    page: number
    pageSize: number
  }): Promise<PagedResult<SupplierRow>> {
    const query: Record<string, unknown> = { page: params.page, pageSize: params.pageSize }
    if (params.grade) query.grade = params.grade
    if (params.status) query.status = params.status
    if (params.keyword) query.keyword = params.keyword
    return apiRequest<PagedResult<SupplierRow>>({ method: 'get', url: SUPPLIERS, params: query })
  },
  /** 供应商详情（主档 + 子表扁平集合）。 */
  get(id: string): Promise<SupplierDetailRow> {
    return apiRequest<SupplierDetailRow>({ method: 'get', url: `${SUPPLIERS}/${encodeURIComponent(id)}` })
  },
  /** 新建供应商。 */
  create(payload: SaveSupplierPayload): Promise<SupplierRow> {
    return apiRequest<SupplierRow>({ method: 'post', url: SUPPLIERS, data: payload })
  },
  /** 更新供应商基础信息。 */
  update(id: string, payload: SaveSupplierPayload): Promise<SupplierRow> {
    return apiRequest<SupplierRow>({
      method: 'put',
      url: `${SUPPLIERS}/${encodeURIComponent(id)}`,
      data: payload,
    })
  },
  /** 变更供应商评级。 */
  changeGrade(
    id: string,
    payload: { grade: number; reason?: string | null },
  ): Promise<SupplierRow> {
    return apiRequest<SupplierRow>({
      method: 'post',
      url: `${SUPPLIERS}/${encodeURIComponent(id)}/grade`,
      data: payload,
    })
  },
  /** 提交供应商准入审批申请。 */
  submitAdmission(id: string, reason?: string | null): Promise<ApprovalRequestRow> {
    return apiRequest<ApprovalRequestRow>({
      method: 'post',
      url: `${SUPPLIERS}/${encodeURIComponent(id)}/admission-requests`,
      data: { supplierId: id, reason: reason ?? null },
    })
  },
  /** 资质证照扁平集合。 */
  listCerts(id: string): Promise<SupplierCertRow[]> {
    return apiRequest<SupplierCertRow[]>({
      method: 'get',
      url: `${SUPPLIERS}/${encodeURIComponent(id)}/certs`,
    })
  },
  /** 登记资质证照。 */
  saveCert(id: string, payload: SaveSupplierCertPayload): Promise<SupplierCertRow> {
    return apiRequest<SupplierCertRow>({
      method: 'post',
      url: `${SUPPLIERS}/${encodeURIComponent(id)}/certs`,
      data: payload,
    })
  },
  /** 银行账户扁平集合。 */
  listBanks(id: string): Promise<SupplierBankRow[]> {
    return apiRequest<SupplierBankRow[]>({
      method: 'get',
      url: `${SUPPLIERS}/${encodeURIComponent(id)}/banks`,
    })
  },
  /** 登记银行账户（默认账户唯一）。 */
  saveBank(id: string, payload: SaveSupplierBankPayload): Promise<SupplierBankRow> {
    return apiRequest<SupplierBankRow>({
      method: 'post',
      url: `${SUPPLIERS}/${encodeURIComponent(id)}/banks`,
      data: payload,
    })
  },
  /** 提交银行账户变更审批申请。 */
  submitBankChange(id: string, payload: SaveSupplierBankPayload): Promise<ApprovalRequestRow> {
    return apiRequest<ApprovalRequestRow>({
      method: 'post',
      url: `${SUPPLIERS}/${encodeURIComponent(id)}/bank-change-requests`,
      data: { supplierId: id, ...payload },
    })
  },
  /** 开票信息扁平集合。 */
  listInvoices(id: string): Promise<SupplierInvoiceRow[]> {
    return apiRequest<SupplierInvoiceRow[]>({
      method: 'get',
      url: `${SUPPLIERS}/${encodeURIComponent(id)}/invoices`,
    })
  },
  /** 登记开票信息。 */
  saveInvoice(id: string, payload: SaveSupplierInvoicePayload): Promise<SupplierInvoiceRow> {
    return apiRequest<SupplierInvoiceRow>({
      method: 'post',
      url: `${SUPPLIERS}/${encodeURIComponent(id)}/invoices`,
      data: payload,
    })
  },
}

/* ============================================================
 * 客户（/api/bas/customers）
 * ============================================================ */

const CUSTOMERS = '/api/bas/customers'

export const customerApi = {
  /** 客户分页列表。 */
  list(params: {
    creditGrade?: string
    status?: string
    keyword?: string
    page: number
    pageSize: number
  }): Promise<PagedResult<CustomerRow>> {
    const query: Record<string, unknown> = { page: params.page, pageSize: params.pageSize }
    if (params.creditGrade) query.creditGrade = params.creditGrade
    if (params.status) query.status = params.status
    if (params.keyword) query.keyword = params.keyword
    return apiRequest<PagedResult<CustomerRow>>({ method: 'get', url: CUSTOMERS, params: query })
  },
  /** 客户详情（主档 + 子表扁平集合）。 */
  get(id: string): Promise<CustomerDetailRow> {
    return apiRequest<CustomerDetailRow>({ method: 'get', url: `${CUSTOMERS}/${encodeURIComponent(id)}` })
  },
  /** 新建客户。 */
  create(payload: SaveCustomerPayload): Promise<CustomerRow> {
    return apiRequest<CustomerRow>({ method: 'post', url: CUSTOMERS, data: payload })
  },
  /** 更新客户基础信息。 */
  update(id: string, payload: SaveCustomerPayload): Promise<CustomerRow> {
    return apiRequest<CustomerRow>({
      method: 'put',
      url: `${CUSTOMERS}/${encodeURIComponent(id)}`,
      data: payload,
    })
  },
  /** 提交客户准入审批申请。 */
  submitAdmission(id: string, reason?: string | null): Promise<ApprovalRequestRow> {
    return apiRequest<ApprovalRequestRow>({
      method: 'post',
      url: `${CUSTOMERS}/${encodeURIComponent(id)}/admission-requests`,
      data: { customerId: id, reason: reason ?? null },
    })
  },
  /** 提交信用额度调整审批申请（载荷 customerId 由服务端按路由 Id 归一）。 */
  submitCreditAdjust(id: string, payload: CustomerCreditAdjustPayload): Promise<ApprovalRequestRow> {
    return apiRequest<ApprovalRequestRow>({
      method: 'post',
      url: `${CUSTOMERS}/${encodeURIComponent(id)}/credit-adjust-requests`,
      data: { customerId: id, ...payload },
    })
  },
  /** 信用额度视图（原始金额）。 */
  getCredit(id: string): Promise<CustomerCreditViewRow> {
    return apiRequest<CustomerCreditViewRow>({
      method: 'get',
      url: `${CUSTOMERS}/${encodeURIComponent(id)}/credit`,
    })
  },
  /** 回款账户扁平集合。 */
  listBanks(id: string): Promise<CustomerBankRow[]> {
    return apiRequest<CustomerBankRow[]>({
      method: 'get',
      url: `${CUSTOMERS}/${encodeURIComponent(id)}/banks`,
    })
  },
  /** 登记回款账户（默认账户唯一）。 */
  saveBank(id: string, payload: SaveCustomerBankPayload): Promise<CustomerBankRow> {
    return apiRequest<CustomerBankRow>({
      method: 'post',
      url: `${CUSTOMERS}/${encodeURIComponent(id)}/banks`,
      data: payload,
    })
  },
  /** 开票信息扁平集合。 */
  listInvoices(id: string): Promise<CustomerInvoiceRow[]> {
    return apiRequest<CustomerInvoiceRow[]>({
      method: 'get',
      url: `${CUSTOMERS}/${encodeURIComponent(id)}/invoices`,
    })
  },
  /** 登记开票信息。 */
  saveInvoice(id: string, payload: SaveCustomerInvoicePayload): Promise<CustomerInvoiceRow> {
    return apiRequest<CustomerInvoiceRow>({
      method: 'post',
      url: `${CUSTOMERS}/${encodeURIComponent(id)}/invoices`,
      data: payload,
    })
  },
}

/* ============================================================
 * 人员（/api/bas/persons）
 * ============================================================ */

const PERSONS = '/api/bas/persons'

export const personApi = {
  /** 人员分页列表（敏感字段已脱敏）。 */
  list(params: {
    personType?: string
    employStatus?: string
    orgId?: string
    positionId?: string
    keyword?: string
    page: number
    pageSize: number
  }): Promise<PagedResult<PersonRow>> {
    const query: Record<string, unknown> = { page: params.page, pageSize: params.pageSize }
    if (params.personType) query.personType = params.personType
    if (params.employStatus) query.employStatus = params.employStatus
    if (params.orgId) query.orgId = params.orgId
    if (params.positionId) query.positionId = params.positionId
    if (params.keyword) query.keyword = params.keyword
    return apiRequest<PagedResult<PersonRow>>({ method: 'get', url: PERSONS, params: query })
  },
  /** 人员详情。 */
  get(id: string): Promise<PersonDetailRow> {
    return apiRequest<PersonDetailRow>({ method: 'get', url: `${PERSONS}/${encodeURIComponent(id)}` })
  },
  /** 新建人员（证件 / 手机 / 银行卡为明文入参，后端加密落库）。 */
  create(payload: SavePersonPayload): Promise<PersonRow> {
    return apiRequest<PersonRow>({ method: 'post', url: PERSONS, data: payload })
  },
  /** 更新人员基础信息与任职。 */
  update(id: string, payload: UpdatePersonPayload): Promise<PersonRow> {
    return apiRequest<PersonRow>({
      method: 'put',
      url: `${PERSONS}/${encodeURIComponent(id)}`,
      data: payload,
    })
  },
  /** 办理离职。 */
  leave(id: string, payload: ResignPersonPayload): Promise<PersonRow> {
    return apiRequest<PersonRow>({
      method: 'post',
      url: `${PERSONS}/${encodeURIComponent(id)}/leave`,
      data: payload,
    })
  },
  /** 再入职。 */
  rehire(id: string, payload: RehirePersonPayload): Promise<PersonRow> {
    return apiRequest<PersonRow>({
      method: 'post',
      url: `${PERSONS}/${encodeURIComponent(id)}/rehire`,
      data: payload,
    })
  },
  /** 敏感字段明文（字段级权限 + 审计）。 */
  plaintext(id: string): Promise<PersonPlaintextRow> {
    return apiRequest<PersonPlaintextRow>({
      method: 'get',
      url: `${PERSONS}/${encodeURIComponent(id)}/plaintext`,
    })
  },
  /** 证件档案扁平集合。 */
  listCerts(id: string): Promise<PersonCertRow[]> {
    return apiRequest<PersonCertRow[]>({
      method: 'get',
      url: `${PERSONS}/${encodeURIComponent(id)}/certs`,
    })
  },
  /** 登记证件档案。 */
  saveCert(id: string, payload: SavePersonCertPayload): Promise<PersonCertRow> {
    return apiRequest<PersonCertRow>({
      method: 'post',
      url: `${PERSONS}/${encodeURIComponent(id)}/certs`,
      data: payload,
    })
  },
  /** 更新证件档案。 */
  updateCert(id: string, certId: string, payload: SavePersonCertPayload): Promise<PersonCertRow> {
    return apiRequest<PersonCertRow>({
      method: 'put',
      url: `${PERSONS}/${encodeURIComponent(id)}/certs/${encodeURIComponent(certId)}`,
      data: payload,
    })
  },
  /** 删除证件档案。 */
  deleteCert(id: string, certId: string): Promise<boolean> {
    return apiRequest<boolean>({
      method: 'delete',
      url: `${PERSONS}/${encodeURIComponent(id)}/certs/${encodeURIComponent(certId)}`,
    })
  },
}

/* ============================================================
 * 数据字典（/api/bas/dictionaries）
 * ============================================================ */

const DICTIONARIES = '/api/bas/dictionaries'

export const dictApi = {
  /** 字典类型扁平列表。 */
  listTypes(params: { ownerLevel?: string; status?: string } = {}): Promise<DictTypeRow[]> {
    const query: Record<string, unknown> = {}
    if (params.ownerLevel) query.ownerLevel = params.ownerLevel
    if (params.status) query.status = params.status
    return apiRequest<DictTypeRow[]>({ method: 'get', url: DICTIONARIES, params: query })
  },
  /** 字典类型详情。 */
  getType(id: string): Promise<DictTypeRow> {
    return apiRequest<DictTypeRow>({ method: 'get', url: `${DICTIONARIES}/${encodeURIComponent(id)}` })
  },
  /** 新建字典类型（类型编码创建后不可修改）。 */
  createType(payload: SaveDictTypePayload): Promise<DictTypeRow> {
    return apiRequest<DictTypeRow>({ method: 'post', url: DICTIONARIES, data: payload })
  },
  /** 重命名字典类型。 */
  updateType(id: string, payload: UpdateDictTypePayload): Promise<DictTypeRow> {
    return apiRequest<DictTypeRow>({
      method: 'put',
      url: `${DICTIONARIES}/${encodeURIComponent(id)}`,
      data: payload,
    })
  },
  /** 启停字典类型。 */
  toggleType(id: string): Promise<DictTypeRow> {
    return apiRequest<DictTypeRow>({
      method: 'post',
      url: `${DICTIONARIES}/${encodeURIComponent(id)}/toggle`,
    })
  },
  /** 指定类型下的字典项列表。 */
  listItems(typeCode: string, params: { status?: string } = {}): Promise<DictItemRow[]> {
    const query: Record<string, unknown> = {}
    if (params.status) query.status = params.status
    return apiRequest<DictItemRow[]>({
      method: 'get',
      url: `${DICTIONARIES}/${encodeURIComponent(typeCode)}/items`,
      params: query,
    })
  },
  /** 新增字典项。 */
  createItem(typeCode: string, payload: SaveDictItemPayload): Promise<DictItemRow> {
    return apiRequest<DictItemRow>({
      method: 'post',
      url: `${DICTIONARIES}/${encodeURIComponent(typeCode)}/items`,
      data: payload,
    })
  },
  /** 更新字典项名称与排序号。 */
  updateItem(id: string, payload: UpdateDictItemPayload): Promise<DictItemRow> {
    return apiRequest<DictItemRow>({
      method: 'put',
      url: `${DICTIONARIES}/items/${encodeURIComponent(id)}`,
      data: payload,
    })
  },
  /** 启停字典项。 */
  toggleItem(id: string): Promise<DictItemRow> {
    return apiRequest<DictItemRow>({
      method: 'post',
      url: `${DICTIONARIES}/items/${encodeURIComponent(id)}/toggle`,
    })
  },
  /** 删除字典项（系统内置 / 被引用时后端拒绝）。 */
  deleteItem(id: string): Promise<unknown> {
    return apiRequest<unknown>({
      method: 'delete',
      url: `${DICTIONARIES}/items/${encodeURIComponent(id)}`,
    })
  },
}

/* ============================================================
 * 编码规则（/api/bas/code-rules）——仅查询与启停
 * ============================================================ */

const CODE_RULES = '/api/bas/code-rules'

export const codeRuleApi = {
  /** 编码规则扁平列表。 */
  list(params: { bizType?: string; status?: string } = {}): Promise<CodeRuleRow[]> {
    const query: Record<string, unknown> = {}
    if (params.bizType) query.bizType = params.bizType
    if (params.status) query.status = params.status
    return apiRequest<CodeRuleRow[]>({ method: 'get', url: CODE_RULES, params: query })
  },
  /** 启用指定业务类型的编码规则。 */
  enable(bizType: string): Promise<CodeRuleRow> {
    return apiRequest<CodeRuleRow>({
      method: 'post',
      url: `${CODE_RULES}/${encodeURIComponent(bizType)}/enable`,
    })
  },
  /** 停用指定业务类型的编码规则。 */
  disable(bizType: string): Promise<CodeRuleRow> {
    return apiRequest<CodeRuleRow>({
      method: 'post',
      url: `${CODE_RULES}/${encodeURIComponent(bizType)}/disable`,
    })
  },
}

/* ============================================================
 * 变更日志（/api/bas/changelogs）
 * ============================================================ */

const CHANGE_LOGS = '/api/bas/changelogs'

export const changeLogApi = {
  /** 变更日志分页列表。 */
  list(query: ChangeLogQuery): Promise<PagedResult<ChangeLogRow>> {
    const params: Record<string, unknown> = { page: query.page, pageSize: query.pageSize }
    if (query.entityType) params.entityType = query.entityType
    if (query.entityId) params.entityId = query.entityId
    if (query.changedBy) params.changedBy = query.changedBy
    if (query.from) params.from = query.from
    if (query.to) params.to = query.to
    return apiRequest<PagedResult<ChangeLogRow>>({ method: 'get', url: CHANGE_LOGS, params })
  },
}

/* ============================================================
 * 导入导出（/api/bas/import-export）
 * ============================================================ */

const IMPORT_EXPORT = '/api/bas/import-export'

/** 导入模板编码约定（与后端 BasImportTemplateDefinitionProvider 一致；取清单校验后再用）。 */
export const IMPORT_TEMPLATE_CODES = {
  supplier: 'bas.supplier.import',
  customer: 'bas.customer.import',
  person: 'bas.person.import',
  category: 'bas.category.import',
} as const

export const importExportApi = {
  /** 导入模板清单（后端仅返回导入模板）。 */
  listTemplates(): Promise<ImportTemplateInfo[]> {
    return apiRequest<ImportTemplateInfo[]>({ method: 'get', url: `${IMPORT_EXPORT}/templates` })
  },
  /** 下载标准导入模板（xlsx）。 */
  downloadTemplate(templateCode: string): Promise<MasterFileResult> {
    return apiRequestBlob({
      method: 'get',
      url: `${IMPORT_EXPORT}/templates/${encodeURIComponent(templateCode)}/download`,
    })
  },
  /** 按模板导入（xlsx multipart）。 */
  importData(templateCode: string, file: File): Promise<ImportResult> {
    const formData = new FormData()
    formData.append('templateCode', templateCode)
    formData.append('file', file)
    return apiRequest<ImportResult>({
      method: 'post',
      url: `${IMPORT_EXPORT}/import`,
      data: formData,
    })
  },
  /** 按导出模板导出数据（敏感字段已脱敏）。 */
  exportData(params: {
    templateCode: string
    status?: string
    personType?: string
    employStatus?: string
    keyword?: string
  }): Promise<MasterFileResult> {
    const query: Record<string, unknown> = { templateCode: params.templateCode }
    if (params.status) query.status = params.status
    if (params.personType) query.personType = params.personType
    if (params.employStatus) query.employStatus = params.employStatus
    if (params.keyword) query.keyword = params.keyword
    return apiRequestBlob({ method: 'get', url: `${IMPORT_EXPORT}/export`, params: query })
  },
  /**
   * 校验导入模板编码（先取实际清单，避免硬编码失效）；
   * 清单中不存在时抛错并提示可用模板。
   */
  async resolveImportTemplateCode(code: string): Promise<string> {
    const templates = await importExportApi.listTemplates()
    const matched = templates.find((item) => item.templateCode === code)
    if (!matched) {
      const available = templates.map((item) => item.templateCode).join(' / ') || '无'
      throw new Error(`未找到导入模板：${code}（可用：${available}）`)
    }
    return matched.templateCode
  },
  /** 由导入模板编码推导导出模板编码（后端清单只含 .import，.export 为同前缀导出模板）。 */
  resolveExportTemplateCode(importTemplateCode: string): string {
    return importTemplateCode.endsWith('.import')
      ? `${importTemplateCode.slice(0, -'.import'.length)}.export`
      : `${importTemplateCode}.export`
  },
}
