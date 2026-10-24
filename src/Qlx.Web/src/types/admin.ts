/*
 * 所属公司：四川淇利馨集团有限公司
 * 描述：门户管理域类型定义（对齐 saas-architecture-spec.md 4.3 Portal Database 核心数据模型）
 * 说明：Id 一律为 Hashids 混淆字符串（与后端收发边界约定一致）；当前为前端 mock 契约，
 *       阶段 1D 后端落地后字段命名保持不变，仅将 mock 数据源替换为真实 REST 调用。
 */

/** 通用分页查询参数（mock 与真实接口共用形态）。 */
export interface PagedQuery {
  keyword?: string
  page: number
  pageSize: number
}

/** 通用分页结果（对齐后端 BuildingBlocks PagedResult）。 */
export interface PagedResult<T> {
  items: T[]
  totalCount: number
  page: number
  pageSize: number
  totalPages: number
  isLastPage: boolean
}

/** ============ 租户（tenants） ============ */

export type TenantStatus = 'Enabled' | 'Disabled' | 'Pending' | 'ProvisioningFailed' | 'Expired'

/** 租户数据库模式。 */
export type DatabaseMode = 'Shared' | 'Isolated'

/** 会员等级（预留体系，4.2.4）。 */
export type MemberLevel = 'Trial' | 'Standard' | 'Professional' | 'Flagship'

export interface PortalTenant {
  id: string
  name: string
  /** 租户编码（唯一）。 */
  code: string
  /** 统一社会信用代码（唯一）。 */
  taxNo: string
  status: TenantStatus
  memberLevel: MemberLevel
  /** 会员到期时间（预留）。 */
  memberExpireAt: string | null
  /** 数据库模式（Shared = 租户共享库，Isolated = 独立库）。 */
  databaseMode: DatabaseMode
  /** 数据库名称（共享租户为租户共享库名；无路由行时为 null）。 */
  dbName?: string | null
  /** 独立数据库状态。 */
  dbStatus: 'Ready' | 'Provisioning' | 'Failed'
  /** 预配错误追踪（P1-1）。 */
  dbError?: string | null
  licensedModuleCodes: string[]
  createdAt: string
  lastLoginAt: string | null
}

/** 租户数据库摘要（详情页数据库卡片，凭据脱敏）。 */
export interface PortalTenantDatabaseSummary {
  /** 数据库模式。 */
  mode: DatabaseMode
  /** 数据库名称。 */
  dbName: string
  /** 数据库主机。 */
  dbHost: string
  /** 数据库端口。 */
  dbPort: number
  /** 数据库状态。 */
  status: string
  /** 共享池编码（独立库为 default 占位）。 */
  poolCode: string
}

/** 租户升级任务（共享库 → 独立库）进度。 */
export interface TenantUpgradeJobItem {
  /** 任务 Id（雪花直出字符串，跨进程传递）。 */
  id: string
  /** 所属租户 Hashids。 */
  tenantId: string
  /** 任务状态枚举字面量。 */
  status: string
  /** 当前模块编码（Preparing/Copying 阶段非空）。 */
  moduleCode: string | null
  /** 当前表名（Copying 阶段非空）。 */
  currentTable: string | null
  /** 已累计复制行数。 */
  rowsCopied: number
  /** 失败原因（Failed 状态非空）。 */
  errorMessage: string | null
  /** 重试次数。 */
  retryCount: number
  /** 任务创建时间。 */
  startedAt: string | null
  /** 任务完成时间（Done/Failed 状态非空）。 */
  completedAt: string | null
}

/** 租户详情响应：租户基本信息 + 数据库摘要 + 模块授权变更记录。 */
export interface PortalTenantDetail {
  tenant: PortalTenant
  database: PortalTenantDatabaseSummary | null
  records: LicenseRecord[]
  /** 最近一次升级任务（可空；无任务或已清理则为 null）。 */
  upgradeJob: TenantUpgradeJobItem | null
}

/** 新建租户入参（门户自动建库 + 初始租户管理员）。 */
export interface CreateTenantPayload {
  name: string
  taxNo: string
  memberLevel: MemberLevel
  /** 初始租户管理员登录账号（手机号/邮箱，PortalUser 自动匹配或创建）。 */
  adminAccount: string
  /**
   * 数据库模式（Shared / Isolated）。
   * 不传时由后端读取 Qlx:Portal:Provisioning:DefaultDatabaseMode（默认 Shared）。
   */
  databaseMode?: DatabaseMode
}

/** ============ 门户用户（portal_users — 自然人全局身份） ============ */

export type AccountStatus = 'Enabled' | 'Disabled'

export interface PortalUser {
  id: string
  /** 登录账号（全局唯一，手机号/身份证号/自定义账号）。 */
  loginAccount: string
  /** 显示名（非唯一）。 */
  displayName: string
  phone: string
  email: string
  status: AccountStatus
  realNameVerified: boolean
  isPortalAdmin: boolean
  lastLoginAt: string | null
  createdAt: string
}

/** 重置密码入参（门户统一密码）。 */
export interface ResetPasswordPayload {
  newPassword: string
}

/** ============ 租户成员关系（tenant_members — 多对多核心表） ============ */

export interface TenantMembership {
  id: string
  /** 所属自然人（portal_users.id）。 */
  portalUserId: string
  /** 自然人登录账号（详情页展示用冗余字段）。 */
  loginAccount: string
  tenantId: string
  tenantName: string
  tenantCode: string
  /** 本租户内展示名（默认取自然人显示名，可单独修改）。 */
  displayNameInTenant: string
  memberRole: 'TenantAdmin' | 'OrdinaryMember' | 'PendingInvite'
  memberStatus: AccountStatus
  joinAt: string
  lastLoginAt: string | null
}

/** ============ 模块与许可（tenant_modules） ============ */

export interface PortalModule {
  code: string
  name: string
  version: string
  description: string
  /** 授权功能点清单（features JSON）。 */
  features: string[]
  licensedTenantCount: number
}

/** 许可授权变更记录（授权记录 4.2.3）。 */
export interface LicenseRecord {
  id: string
  tenantId: string
  tenantName: string
  moduleCode: string
  moduleName: string
  action: 'Granted' | 'Revoked' | 'Adjusted'
  operatorName: string
  at: string
  remark: string
}

/** 保存租户模块授权入参（整单提交）。 */
export interface SaveLicensePayload {
  tenantId: string
  moduleCodes: string[]
  remark: string
}

/** ============ 登录日志（登录审计 4.2.5） ============ */

export interface LoginLog {
  id: string
  loginAccount: string
  /** 归属租户（门户登录为 —）。 */
  tenantName: string
  result: 'Success' | 'Failure'
  ipAddress: string
  at: string
}

/** ============ 门户管理工作台（门户管理员工作台视图） ============ */

export interface PortalAdminStat {
  key: string
  label: string
  value: number
  unit: string
  hint: string
  icon: string
  tone: 'brand' | 'gold' | 'success' | 'warning'
}

export interface PortalAdminTodo {
  id: string
  type: string
  title: string
  at: string
  level: 'warning' | 'danger' | 'info'
  href: string
}

/** ============ 枚举标签映射（各页面共用，禁止散落硬编码） ============ */

export const tenantStatusLabels: Record<TenantStatus, string> = {
  Enabled: '启用中',
  Disabled: '已停用',
  Pending: '预配中',
  ProvisioningFailed: '预配失败',
  Expired: '已到期',
}

export const tenantStatusTones: Record<TenantStatus, 'success' | 'danger' | 'warning' | 'info'> = {
  Enabled: 'success',
  Disabled: 'danger',
  Pending: 'warning',
  ProvisioningFailed: 'danger',
  Expired: 'info',
}

export const memberLevelLabels: Record<MemberLevel, string> = {
  Trial: '试用版',
  Standard: '标准版',
  Professional: '专业版',
  Flagship: '旗舰版',
}

export const dbStatusLabels: Record<PortalTenant['dbStatus'], string> = {
  Ready: '就绪',
  Provisioning: '初始化中',
  Failed: '异常',
}

export const databaseModeLabels: Record<DatabaseMode, string> = {
  Shared: '租户共享库',
  Isolated: '独立库',
}

export const databaseModeTones: Record<DatabaseMode, 'primary' | 'success'> = {
  Shared: 'primary',
  Isolated: 'success',
}

export const accountStatusLabels: Record<AccountStatus, string> = {
  Enabled: '启用',
  Disabled: '已停用',
}

export const memberRoleLabels: Record<TenantMembership['memberRole'], string> = {
  TenantAdmin: '企业管理员',
  OrdinaryMember: '普通成员',
  PendingInvite: '待激活',
}

export const licenseActionLabels: Record<LicenseRecord['action'], string> = {
  Granted: '开通',
  Revoked: '停用',
  Adjusted: '调整',
}
