/*
 * 所属公司：四川淇利馨集团有限公司
 * 描述：HTTP 客户端与 Portal 认证 API 封装
 * 说明：/connect/token 为 OpenIddict 标准端点，必须 application/x-www-form-urlencoded
 */

import axios, { AxiosError } from 'axios'
import type { CurrentUser, MemberRoleOption, TenantSummary, TokenResponse } from '@/types/auth'

/** 前端单壳公共客户端标识（与后端 OpenIddictConfigurator.SpaClientId 一致）。 */
export const CLIENT_ID = 'erp-web'

/** 三阶段登录自定义授权类型（与后端 QlxAuthConstants.GrantTypes 一致）。 */
export const GRANT_PORTAL_IDENTITY = 'urn:qlx:grant:portal_identity'
export const GRANT_TENANT_CONTEXT = 'urn:qlx:grant:tenant_context'
/** 租户级 Token 静默续期授权类型（凭即将过期的租户 access token 换新 token）。 */
export const GRANT_TENANT_SILENT = 'urn:qlx:grant:tenant_silent'

const http = axios.create({
  baseURL: import.meta.env.VITE_API_BASE_URL ?? '',
  timeout: 15000,
  headers: { Accept: 'application/json' },
})

/** 从后端错误响应中提取可读消息（OpenIddict 用 error / error_description）。 */
function toMessage(error: unknown, fallback: string): string {
  if (axios.isAxiosError(error)) {
    const data = (error as AxiosError<Record<string, unknown>>).response?.data
    const description = data?.error_description ?? data?.message ?? data?.code
    if (typeof description === 'string' && description.length > 0) {
      return description
    }
    if (error.code === 'ECONNABORTED') {
      return '请求超时，请检查网络后重试'
    }
    if (!error.response) {
      return '无法连接服务，请确认后端服务已启动'
    }
  }
  return fallback
}

/** 解析 qlx_tenants（字符串化 JSON）为租户摘要数组。 */
function parseTenants(raw?: string): TenantSummary[] {
  if (!raw) {
    return []
  }
  try {
    const parsed = JSON.parse(raw) as TenantSummary[]
    return Array.isArray(parsed) ? parsed : []
  } catch {
    return []
  }
}

/**
 * Phase 1：身份认证（账号密码）。
 * @returns 身份级 Token、refresh token、可进入的租户列表与门户管理员标记
 */
export async function loginIdentity(
  loginAccount: string,
  password: string,
): Promise<{ tokens: TokenResponse; tenants: TenantSummary[]; isPortalAdmin: boolean }> {
  const body = new URLSearchParams({
    grant_type: GRANT_PORTAL_IDENTITY,
    client_id: CLIENT_ID,
    login_account: loginAccount,
    password,
  })

  try {
    const { data } = await http.post<TokenResponse>('/connect/token', body, {
      headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
    })
    const isPortalAdmin = data.qlx_portal_admin === true || data.qlx_portal_admin === 'true'
    return { tokens: data, tenants: parseTenants(data.qlx_tenants), isPortalAdmin }
  } catch (error) {
    throw new Error(toMessage(error, '登录失败，请稍后重试'))
  }
}

/**
 * Phase 3：凭身份级 access token + 混淆 tenant_id 换取租户上下文 Token。
 * @param roleId 所选租户角色 Id（普通成员多角色时必填）
 */
export async function loginTenant(
  identityToken: string,
  tenantId: string,
  roleId?: string,
): Promise<TokenResponse> {
  const body = new URLSearchParams({
    grant_type: GRANT_TENANT_CONTEXT,
    client_id: CLIENT_ID,
    tenant_id: tenantId,
  })
  if (roleId) {
    body.set('member_role_id', roleId)
  }

  try {
    const { data } = await http.post<TokenResponse>('/connect/token', body, {
      headers: {
        'Content-Type': 'application/x-www-form-urlencoded',
        Authorization: `Bearer ${identityToken}`,
      },
    })
    return data
  } catch (error) {
    throw new Error(toMessage(error, '进入企业失败，请重新登录后再试'))
  }
}

/**
 * 选择角色步骤：按企业懒加载当前账号可进入的角色（身份级 Token）。
 */
export async function fetchMemberRoles(
  identityToken: string,
  tenantId: string,
): Promise<MemberRoleOption[]> {
  try {
    const { data } = await http.get<
      { data?: MemberRoleOption[] } | MemberRoleOption[]
    >(`/api/portal/tenants/${encodeURIComponent(tenantId)}/member-roles`, {
      headers: { Authorization: `Bearer ${identityToken}` },
    })

    if (Array.isArray(data)) {
      return data
    }
    return Array.isArray(data?.data) ? data.data : []
  } catch (error) {
    throw new Error(toMessage(error, '获取企业角色失败，请稍后重试'))
  }
}

/** 读取当前 Token 对应的用户信息（租户级 Token 含模块许可）。 */
export async function fetchCurrentUser(accessToken: string): Promise<CurrentUser> {
  try {
    const { data } = await http.get<CurrentUser>('/api/portal/me', {
      headers: { Authorization: `Bearer ${accessToken}` },
    })
    return data
  } catch (error) {
    throw new Error(toMessage(error, '获取用户信息失败'))
  }
}

/**
 * 租户级 Token 静默续期：凭即将过期（或刚过期且在宽限期内）的租户 access token
 * 调用 urn:qlx:grant:tenant_silent 换取新的租户级 access token。
 * 后端会校验签名、iss/aud、有效期（含宽限）、用户/成员/租户状态与 Token 版本。
 */
export async function silentRefreshTenantToken(tenantToken: string): Promise<TokenResponse> {
  const body = new URLSearchParams({
    grant_type: GRANT_TENANT_SILENT,
    client_id: CLIENT_ID,
  })

  try {
    const { data } = await http.post<TokenResponse>('/connect/token', body, {
      headers: {
        'Content-Type': 'application/x-www-form-urlencoded',
        Authorization: `Bearer ${tenantToken}`,
      },
    })
    return data
  } catch (error) {
    throw new Error(toMessage(error, '会话已过期，请重新登录'))
  }
}

/**
 * 身份级 Token 刷新：用 refresh_token 换取新的身份级 access token + 轮换后的 refresh token。
 * 同时重发 qlx_tenants 与 qlx_portal_admin 参数，保持与会话一致。
 */
export async function refreshIdentityToken(refreshToken: string): Promise<TokenResponse> {
  const body = new URLSearchParams({
    grant_type: 'refresh_token',
    client_id: CLIENT_ID,
    refresh_token: refreshToken,
  })

  try {
    const { data } = await http.post<TokenResponse>('/connect/token', body, {
      headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
    })
    return data
  } catch (error) {
    throw new Error(toMessage(error, '登录状态已失效，请重新登录'))
  }
}

/** 登出：将当前 access token 的 jti 写入服务端黑名单。 */
export async function logout(accessToken: string): Promise<void> {
  await http.post(
    '/api/portal/logout',
    undefined,
    { headers: { Authorization: `Bearer ${accessToken}` } },
  )
}

/** 吊销 refresh token（与登出配合形成双保障）。 */
export async function revokeToken(token: string, tokenTypeHint = 'refresh_token'): Promise<void> {
  const body = new URLSearchParams({
    token,
    token_type_hint: tokenTypeHint,
    client_id: CLIENT_ID,
  })
  await http.post('/connect/revoke', body, {
    headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
  })
}
