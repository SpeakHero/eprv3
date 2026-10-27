/*
 * 所属公司：四川淇利馨集团有限公司
 * 描述：门户管理端真实 API 封装（阶段 1D）——契约与 types/admin.ts 完全一致
 * 说明：所有请求使用身份级 Token（门户管理员），后端统一返回 ApiResponse<T>；
 *       此文件替换 mock/portalAdmin.ts 的数据源，页面组件与类型零改动。
 */

import { apiRequest } from '@/api/adapters/request'
import type {
  CreateTenantPayload,
  DatabaseMode,
  LicenseRecord,
  LoginLog,
  PagedQuery,
  PagedResult,
  PortalAdminStat,
  PortalAdminTodo,
  PortalModule,
  PortalTenant,
  PortalTenantDatabaseSummary,
  PortalUser,
  ResetPasswordPayload,
  SaveLicensePayload,
  TenantMembership,
  TenantUpgradeJobItem,
} from '@/types/admin'

/** 分页参数映射（keyword/status/databaseMode 为空时不传）。 */
function pageParams(query: PagedQuery & { status?: string; databaseMode?: DatabaseMode }): Record<string, string | number> {
  const params: Record<string, string | number> = {
    page: query.page,
    pageSize: query.pageSize,
  }
  if (query.keyword?.trim()) {
    params.keyword = query.keyword.trim()
  }
  if (query.status) {
    params.status = query.status
  }
  if (query.databaseMode) {
    params.databaseMode = query.databaseMode
  }
  return params
}

/** ============ 租户 ============ */

/** 租户列表：keyword 匹配名称/编码/税号，status / databaseMode 可选过滤。 */
export function fetchTenants(
  query: PagedQuery & { status?: string; databaseMode?: DatabaseMode },
): Promise<PagedResult<PortalTenant>> {
  return apiRequest<PagedResult<PortalTenant>>({
    method: 'GET',
    url: '/api/portal/admin/tenants',
    params: pageParams(query),
  })
}

/** 新建租户：自动建库 + 默认许可 + 初始租户管理员。 */
export function createTenant(payload: CreateTenantPayload): Promise<PortalTenant> {
  return apiRequest<PortalTenant>({
    method: 'POST',
    url: '/api/portal/admin/tenants',
    data: payload,
  })
}

/** 启用 / 停用租户。 */
export function toggleTenantStatus(tenantId: string): Promise<PortalTenant> {
  return apiRequest<PortalTenant>({
    method: 'POST',
    url: `/api/portal/admin/tenants/${tenantId}/toggle-status`,
  })
}

/** 租户详情（租户基本信息 + 数据库摘要 + 授权变更记录；升级任务另通过 fetchTenantUpgradeJob 获取）。 */
export function fetchTenantDetail(
  tenantId: string,
): Promise<{ tenant: PortalTenant; database: PortalTenantDatabaseSummary | null; records: LicenseRecord[] }> {
  return apiRequest<{ tenant: PortalTenant; database: PortalTenantDatabaseSummary | null; records: LicenseRecord[] }>({
    method: 'GET',
    url: `/api/portal/admin/tenants/${tenantId}`,
  })
}

/** 保存租户模块授权（整单提交，产生授权记录）。 */
export async function saveTenantLicense(payload: SaveLicensePayload): Promise<void> {
  await apiRequest<unknown>({
    method: 'PUT',
    url: `/api/portal/admin/tenants/${payload.tenantId}/modules`,
    data: { moduleCodes: payload.moduleCodes, remark: payload.remark },
  })
}

/** 租户成员列表（详情页只读）。 */
export function fetchTenantMembers(tenantId: string): Promise<TenantMembership[]> {
  return apiRequest<TenantMembership[]>({
    method: 'GET',
    url: `/api/portal/admin/tenants/${tenantId}/members`,
  })
}

/** ============ 租户升级（共享库 → 独立库） ============ */

/** 发起升级：登记任务由后台流水线异步执行，返回初始任务快照。 */
export function promoteTenantToIsolated(tenantId: string): Promise<TenantUpgradeJobItem> {
  return apiRequest<TenantUpgradeJobItem>({
    method: 'POST',
    url: `/api/portal/admin/tenants/${tenantId}/promote-to-isolated`,
  })
}

/** 查询租户最近一次升级任务进度（可空；无任务或已清理则为 null）。 */
export function fetchTenantUpgradeJob(tenantId: string): Promise<TenantUpgradeJobItem | null> {
  return apiRequest<TenantUpgradeJobItem | null>({
    method: 'GET',
    url: `/api/portal/admin/tenants/${tenantId}/upgrade-job`,
  })
}

/** 重试失败的升级任务（断点续跑；非 Failed 状态返回 409 Conflict）。 */
export function retryTenantUpgradeJob(tenantId: string): Promise<TenantUpgradeJobItem> {
  return apiRequest<TenantUpgradeJobItem>({
    method: 'POST',
    url: `/api/portal/admin/tenants/${tenantId}/upgrade-job/retry`,
  })
}

/** 租户预配进度 SSE 流地址（门户管理员身份 Token；仅转发 TenantProvisioningProgress 事件）。 */
export function tenantProvisioningStreamUrl(tenantId: string): string {
  return `/api/portal/admin/tenants/${tenantId}/provisioning-stream`
}

/** ============ 门户用户 ============ */

/** 门户用户列表：keyword 匹配账号/显示名/手机号/邮箱。 */
export function fetchUsers(
  query: PagedQuery & { status?: string },
): Promise<PagedResult<PortalUser>> {
  return apiRequest<PagedResult<PortalUser>>({
    method: 'GET',
    url: '/api/portal/admin/users',
    params: pageParams(query),
  })
}

/** 全局停用 / 启用自然人账号。 */
export function toggleUserStatus(userId: string): Promise<PortalUser> {
  return apiRequest<PortalUser>({
    method: 'POST',
    url: `/api/portal/admin/users/${userId}/toggle-status`,
  })
}

/** 重置密码（token_version 递增，旧会话全部失效）。 */
export async function resetUserPassword(
  userId: string,
  payload: ResetPasswordPayload,
): Promise<void> {
  await apiRequest<unknown>({
    method: 'POST',
    url: `/api/portal/admin/users/${userId}/reset-password`,
    data: payload,
  })
}

/** 用户详情（成员关系 + 跨租户登录日志，日志来自平台日志中心）。 */
export function fetchUserDetail(
  userId: string,
): Promise<{ user: PortalUser; memberships: TenantMembership[]; logs: LoginLog[] }> {
  return apiRequest<{ user: PortalUser; memberships: TenantMembership[]; logs: LoginLog[] }>({
    method: 'GET',
    url: `/api/portal/admin/users/${userId}`,
  })
}

/** ============ 模块目录 / 许可 ============ */

/** 模块目录（含各模块授权租户数与功能点）。 */
export function fetchModules(): Promise<PortalModule[]> {
  return apiRequest<PortalModule[]>({
    method: 'GET',
    url: '/api/portal/admin/modules',
  })
}

/** 全平台许可变更记录（倒序）。 */
export function fetchLicenseRecords(): Promise<LicenseRecord[]> {
  return apiRequest<LicenseRecord[]>({
    method: 'GET',
    url: '/api/portal/admin/modules/license-records',
  })
}

/** ============ 门户管理工作台 ============ */

/** 门户管理工作台概览（指标 + 运营待办）。 */
export interface PortalWorkbenchOverview {
  stats: PortalAdminStat[]
  todos: PortalAdminTodo[]
}

export function fetchPortalWorkbenchOverview(): Promise<PortalWorkbenchOverview> {
  return apiRequest<PortalWorkbenchOverview>({
    method: 'GET',
    url: '/api/portal/admin/workbench/overview',
  })
}
