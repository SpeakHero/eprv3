/*
 * 所属公司：四川淇利馨集团有限公司
 * 描述：认证相关类型定义（与后端 TenantSummaryItem / CurrentUserResponse 保持一致）
 */

/** 登录响应中的租户摘要项（TenantId 为 Hashids 混淆串）。 */
export interface TenantSummary {
  tenantId: string
  tenantCode: string
  displayName: string
  /** 统一社会信用代码 / 税号（登录页企业标识匹配用）。 */
  taxNo?: string | null
  memberRole: string
  lastLoginAt: string | null
}

/** 企业下当前账号可进入的角色项（roleId 为 Hashids 混淆串）。 */
export interface MemberRoleOption {
  roleId: string
  roleCode: string
  roleName: string
  isBuiltin: boolean
}

/** 门户管理虚拟主体 Id（不与真实 Hashids 串冲突；仅存在于前端会话态）。 */
export const PORTAL_ADMIN_TENANT_ID = '__portal__'

/** /api/portal/me 响应体。 */
export interface CurrentUser {
  userId: string
  loginAccount: string
  displayName: string
  tenantId?: string | null
  tenantCode?: string | null
  memberRole?: string | null
  modules: string[]
  permissions: string[]
  isPortalAdmin: boolean
}

/** OpenIddict /connect/token 成功响应。 */
export interface TokenResponse {
  access_token: string
  token_type: string
  expires_in: number
  refresh_token?: string
  /** 身份级登录专有：租户列表 JSON 字符串（需二次 JSON.parse）。 */
  qlx_tenants?: string
  /** 身份级登录专有：门户管理员标记（"true" / true）。 */
  qlx_portal_admin?: string | boolean
}

/** 登录后进入流程的结果。 */
export interface SignInResult {
  /** 目标路由：工作台或企业选择页。 */
  path: '/workbench' | '/select-tenant'
  /** 企业标识校验提示（不阻断流程，仅提示）。 */
  notice?: string
}

/** 角色编码 → 中文标签。 */
export const memberRoleLabel = (role?: string | null): string => {
  switch (role) {
    case 'TenantAdmin':
      return '企业管理员'
    case 'OrdinaryMember':
      return '普通成员'
    case 'PendingInvite':
      return '待激活'
    case 'PortalAdmin':
      return '平台管理员'
    default:
      return role ?? '成员'
  }
}
