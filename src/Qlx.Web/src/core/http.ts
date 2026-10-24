/*
 * 所属公司：四川淇利馨集团有限公司
 * 描述：共享 HTTP 客户端——统一 BaseURL、Token 注入、401 静默续期与重试
 * 说明：所有业务 API 必须使用本客户端，禁止各自 new axios；Token 续期由 auth store
 *       注入 refreshIdentity/refreshTenant 回调，避免循环依赖。
 */

import axios, { type AxiosInstance, type AxiosRequestConfig, type AxiosResponse, type InternalAxiosRequestConfig } from 'axios'
import { broadcastSession, clearSession, loadSession } from '@/core/session'

/** 会话彻底失效（401 且续期失败/无续期回调）：全量清除本地会话并广播，再跳登录页。 */
function forceReLogin(): void {
  // 必须全量清除（含 portalAdminMode/user/currentTenant 等）：仅清 token 会让
  // 路由守卫 hasTenant 仍判定为已登录，造成 login ↔ workbench 无限跳转
  clearSession()
  broadcastSession('session-cleared')
  window.location.href = '/login'
}

/** 续期回调：身份级用 refresh_token grant；租户级用 tenant_silent grant。 */
export type TokenRefreshFn = () => Promise<string>

/** 续期回调注册表（由 auth store 在启动时注入）。 */
let refreshIdentity: TokenRefreshFn | null = null
let refreshTenant: TokenRefreshFn | null = null

/** 注册身份级 Token 续期回调（auth store 启动时调用）。 */
export function setIdentityRefreshFn(fn: TokenRefreshFn): void {
  refreshIdentity = fn
}

/** 注册租户级 Token 续期回调（auth store 启动时调用）。 */
export function setTenantRefreshFn(fn: TokenRefreshFn): void {
  refreshTenant = fn
}

/** 判定 URL 是否为门户管理端 API（使用身份级 Token）。 */
function isPortalAdminApi(url: string | undefined): boolean {
  return !!url && url.startsWith('/api/portal/admin')
}

/** 从本地会话取对应 Token。 */
function pickToken(url: string | undefined): string {
  const session = loadSession()
  return isPortalAdminApi(url) ? session.identityToken : session.tenantToken
}

/** 共享 axios 实例（所有业务 API 复用）。 */
export const http: AxiosInstance = axios.create({
  baseURL: import.meta.env.VITE_API_BASE_URL ?? '',
  timeout: 15000,
  headers: { Accept: 'application/json' },
})

// ---- 请求拦截：自动注入 Authorization 头 ----
http.interceptors.request.use((config: InternalAxiosRequestConfig) => {
  const token = pickToken(config.url)
  if (token && !config.headers.Authorization) {
    config.headers.Authorization = `Bearer ${token}`
  }
  return config
})

// ---- 响应拦截：401 触发静默续期并重试（单次，防循环）----
let isRefreshing = false
let pendingQueue: Array<(token: string) => void> = []

http.interceptors.response.use(
  (response) => response,
  async (error) => {
    const original = error.config as AxiosRequestConfig & { _retried?: boolean }
    if (error.response?.status !== 401 || original._retried) {
      return Promise.reject(error)
    }
    original._retried = true

    const useIdentity = isPortalAdminApi(original.url)
    const refreshFn = useIdentity ? refreshIdentity : refreshTenant

    if (!refreshFn) {
      // 未注入续期回调：会话已不可用，全量清除后跳登录
      forceReLogin()
      return Promise.reject(error)
    }

    // 并发请求合并：续期期间的 401 排队等待同一结果
    if (isRefreshing) {
      return new Promise<AxiosResponse>((resolve, reject) => {
        pendingQueue.push((token) => {
          original.headers = { ...original.headers, Authorization: `Bearer ${token}` }
          http(original).then(resolve).catch(reject)
        })
      })
    }

    isRefreshing = true
    try {
      const newToken = await refreshFn()
      // 唤醒排队请求
      pendingQueue.forEach((cb) => cb(newToken))
      pendingQueue = []

      original.headers = { ...original.headers, Authorization: `Bearer ${newToken}` }
      return http(original)
    } catch {
      pendingQueue = []
      // 续期失败：会话彻底失效，全量清除（含 portalAdminMode 等非 token 字段）后跳登录
      forceReLogin()
      return Promise.reject(error)
    } finally {
      isRefreshing = false
    }
  },
)
