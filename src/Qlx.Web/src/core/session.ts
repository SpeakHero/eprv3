/*
 * 所属公司：四川淇利馨集团有限公司
 * 描述：会话持久化与跨标签页同步
 * 说明：Token 存 localStorage 以便「原生多标签页」新开标签沿用登录态；
 *      登录态变化经 BroadcastChannel 同步（登出 / 切换企业即时广播）。
 */

import type { CurrentUser, TenantSummary } from '@/types/auth'

const STORAGE_KEY = 'qlx.session.v1'
const CHANNEL_NAME = 'qlx-session'

/** 持久化会话结构。 */
export interface PersistedSession {
  identityToken: string
  refreshToken: string
  /** 身份级 access token 过期时间戳（ms），用于主动续期判定。 */
  identityExpiresAt: number
  user: CurrentUser | null
  tenants: TenantSummary[]
  tenantToken: string
  /** 租户级 access token 过期时间戳（ms），用于主动续期判定。 */
  tenantExpiresAt: number
  currentTenant: TenantSummary | null
  licensedModules: string[]
  /** 登录账号是否门户管理员（决定选择页是否展示「门户管理」入口）。 */
  isPortalAdminAccount: boolean
  /** 当前会话是否处于门户管理模式（凭身份级 Token 进入，无租户上下文）。 */
  portalAdminMode: boolean
  /** 租户内角色：TenantAdmin / OrdinaryMember；门户管理模式下为空。 */
  memberRole: string
  /** 本次进入所选的租户角色 Id（Hashids 混淆串；企业管理员/门户管理模式下为空）。 */
  memberRoleId: string
  /** 当前会话的权限码列表（TenantAdmin / portalAdminMode 可能为空但不影响判定）。 */
  permissions: string[]
}

/** 广播事件类型。 */
export type SessionEvent = 'session-updated' | 'session-cleared'

const emptySession = (): PersistedSession => ({
  identityToken: '',
  refreshToken: '',
  identityExpiresAt: 0,
  user: null,
  tenants: [],
  tenantToken: '',
  tenantExpiresAt: 0,
  currentTenant: null,
  licensedModules: [],
  isPortalAdminAccount: false,
  portalAdminMode: false,
  memberRole: '',
  memberRoleId: '',
  permissions: [],
})

/** 读取本地会话；解析失败视为无会话。 */
export function loadSession(): PersistedSession {
  try {
    const raw = localStorage.getItem(STORAGE_KEY)
    if (!raw) {
      return emptySession()
    }
    const parsed = JSON.parse(raw) as Partial<PersistedSession>
    return { ...emptySession(), ...parsed }
  } catch {
    return emptySession()
  }
}

/** 写入本地会话。 */
export function saveSession(session: PersistedSession): void {
  localStorage.setItem(STORAGE_KEY, JSON.stringify(session))
}

/** 清除本地会话。 */
export function clearSession(): void {
  localStorage.removeItem(STORAGE_KEY)
}

/** BroadcastChannel 通道（不支持时降级为 storage 事件监听）。 */
const channel: BroadcastChannel | null =
  typeof BroadcastChannel !== 'undefined' ? new BroadcastChannel(CHANNEL_NAME) : null

/** 广播会话变化（发送方自身不会收到）。 */
export function broadcastSession(type: SessionEvent): void {
  channel?.postMessage({ type, at: Date.now() })
}

/**
 * 订阅其他标签页的会话变化。
 * @param handler 事件处理函数
 * @returns 取消订阅函数
 */
export function onSessionEvent(handler: (type: SessionEvent) => void): () => void {
  if (channel) {
    const listener = (event: MessageEvent<{ type?: SessionEvent }>) => {
      if (event.data?.type === 'session-updated' || event.data?.type === 'session-cleared') {
        handler(event.data.type)
      }
    }
    channel.addEventListener('message', listener)
    return () => channel.removeEventListener('message', listener)
  }

  // 降级：storage 事件（同源其他标签页写入时触发）
  const listener = (event: StorageEvent) => {
    if (event.key === STORAGE_KEY) {
      handler(event.newValue ? 'session-updated' : 'session-cleared')
    }
  }
  window.addEventListener('storage', listener)
  return () => window.removeEventListener('storage', listener)
}
