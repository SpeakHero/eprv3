/*
 * 所属公司：四川淇利馨集团有限公司
 * 描述：认证状态管理——三阶段登录、企业切换、登出、跨标签页同步
 * v1.1 扩展：memberRole / permissions 字段 + JWT payload 自动解码（TenantAdmin 零计算旁路）
 */

import { defineStore } from 'pinia'
import type { Router } from 'vue-router'
import {
  fetchMemberRoles,
  fetchCurrentUser,
  loginIdentity,
  loginTenant,
  logout,
  refreshIdentityToken,
  revokeToken,
  silentRefreshTenantToken,
} from '@/api/portal'
import { setIdentityRefreshFn, setTenantRefreshFn } from '@/core/http'
import {
  broadcastSession,
  clearSession,
  loadSession,
  onSessionEvent,
  saveSession,
} from '@/core/session'
import { decodeJwtPayload } from '@/utils/jwt'
import { PORTAL_ADMIN_TENANT_ID } from '@/types/auth'
import type {
  CurrentUser,
  MemberRoleOption,
  SignInResult,
  TenantSummary,
  TokenResponse,
} from '@/types/auth'

/** 主动续期提前量：Token 到期前 N 秒触发续期（避免临期请求失败）。 */
const REFRESH_LEAD_SECONDS = 60
/** 续期定时器最小间隔（防止高频调用）。 */
const REFRESH_MIN_INTERVAL_MS = 30_000

/** 跨标签页订阅句柄（模块级，避免进入响应式状态）。 */
let stopSync: (() => void) | null = null
/** 主动续期定时器句柄。 */
let refreshTimer: ReturnType<typeof setTimeout> | null = null
/** 角色懒加载并发去重（同一企业复用同一请求）。 */
const roleRequests = new Map<string, Promise<MemberRoleOption[]>>()

export const useAuthStore = defineStore('auth', {
  state: () => ({
    identityToken: '',
    refreshToken: '',
    identityExpiresAt: 0,
    user: null as CurrentUser | null,
    tenants: [] as TenantSummary[],
    tenantToken: '',
    tenantExpiresAt: 0,
    currentTenant: null as TenantSummary | null,
    licensedModules: [] as string[],
    /** 登录账号是否门户管理员（后端 qlx_portal_admin 下发）。 */
    isPortalAdminAccount: false,
    /** 当前会话是否处于门户管理模式（凭身份级 Token 进入，无租户上下文）。 */
    portalAdminMode: false,
    restored: false,
    /** v1.1：租户内角色 TenantAdmin / OrdinaryMember；门户管理模式下为空。 */
    memberRole: '',
    /** 本次进入所选的租户角色 Id；企业管理员/门户管理模式下为空。 */
    memberRoleId: '',
    /** v1.1：当前会话的权限码列表（TenantAdmin / portalAdminMode 可能为空但不影响判定）。 */
    permissions: [] as string[],
    /** 企业角色懒加载缓存（仅内存，不落会话）。 */
    tenantRoles: {} as Record<string, MemberRoleOption[]>,
    /** 登录后单企业多角色：选择页直接落在「选择角色」步骤。 */
    pendingRoleTenantId: '',
  }),

  getters: {
    isAuthenticated: (state) => Boolean(state.identityToken),
    // 门户管理模式无租户 Token，但可进入工作台（平台运营视图）；
    // 必须以身份级 Token 为前提——身份令牌缺失时 portalAdminMode/tenantToken 均为无效残留，
    // 否则 401 清会话后守卫会误判已登录，造成 login ↔ workbench 无限跳转
    hasTenant: (state) =>
      Boolean(state.identityToken) && (Boolean(state.tenantToken) || state.portalAdminMode),
    loginAccount: (state) => state.user?.loginAccount ?? '',
    displayName: (state) => state.user?.displayName ?? '',
    tenantName: (state) => state.currentTenant?.displayName ?? '',
    /** 模块许可判定（后端 /me 下发的 modules 为准）。 */
    isModuleLicensed: (state) => (moduleCode: string) =>
      state.licensedModules.includes(moduleCode),
    /** v1.1：TenantAdmin 零计算旁路（不依赖 permissions 列表）。 */
    isTenantAdmin: (state) => state.memberRole === 'TenantAdmin',
    /** v1.1：权限判定：TenantAdmin / portalAdminMode 全通，否则 permissions 精确匹配。 */
    hasPermission: (state) => (code: string): boolean =>
      state.portalAdminMode || state.memberRole === 'TenantAdmin' || state.permissions.includes(code),
  },

  actions: {
    /** 从本地存储恢复会话（幂等，仅首次生效）。 */
    restore(): void {
      if (this.restored) {
        return
      }
      const session = loadSession()
      this.$patch({ ...session, restored: true })
      // v1.2：历史会话（JWE Token 无法解出 claims）以企业摘要中的角色兜底，
      // 避免 TenantAdmin 因 memberRole 缺失被 v-permission 移除全部操作按钮
      if (!this.memberRole && this.currentTenant?.memberRole) {
        this.memberRole = this.currentTenant.memberRole
      }
    },

    /** 写入本地存储（供其他标签页读取）。 */
    persist(): void {
      saveSession({
        identityToken: this.identityToken,
        refreshToken: this.refreshToken,
        identityExpiresAt: this.identityExpiresAt,
        user: this.user,
        tenants: this.tenants,
        tenantToken: this.tenantToken,
        tenantExpiresAt: this.tenantExpiresAt,
        currentTenant: this.currentTenant,
        licensedModules: this.licensedModules,
        isPortalAdminAccount: this.isPortalAdminAccount,
        portalAdminMode: this.portalAdminMode,
        memberRole: this.memberRole,
        memberRoleId: this.memberRoleId,
        permissions: this.permissions,
      })
    },

    /** 清空内存中的会话状态。 */
    reset(): void {
      this.identityToken = ''
      this.refreshToken = ''
      this.identityExpiresAt = 0
      this.user = null
      this.tenants = []
      this.tenantToken = ''
      this.tenantExpiresAt = 0
      this.currentTenant = null
      this.licensedModules = []
      this.isPortalAdminAccount = false
      this.portalAdminMode = false
      this.memberRole = ''
      this.memberRoleId = ''
      this.permissions = []
      this.tenantRoles = {}
      this.pendingRoleTenantId = ''
      roleRequests.clear()
    },

    /**
     * 账号密码登录（Phase 1）。
     * 企业标识选填（企业名称 / 税号）：填写则校验归属并直接进入；
     * 门户管理员恒进选择页（置顶展示「门户管理」入口）；
     * 普通用户单企业自动进入，多企业进选择页。
     */
    async signIn(loginAccount: string, password: string, tenantCode?: string): Promise<SignInResult> {
      const { tokens, tenants, isPortalAdmin } = await loginIdentity(loginAccount, password)

      this.identityToken = tokens.access_token
      this.refreshToken = tokens.refresh_token ?? ''
      this.identityExpiresAt = this.computeExpiresAt(tokens.expires_in)
      this.tenants = tenants
      this.tenantToken = ''
      this.tenantExpiresAt = 0
      this.currentTenant = null
      this.licensedModules = []
      this.isPortalAdminAccount = isPortalAdmin
      this.portalAdminMode = false
      this.memberRoleId = ''
      this.tenantRoles = {}
      this.pendingRoleTenantId = ''

      // v1.1：decode 身份级 Token（PortalAdmin 身份级 Token 的 permissions 可能不写）
      this.applyIdentityTokenClaims(tokens.access_token)

      if (tenants.length === 0 && !isPortalAdmin) {
        // 无企业且非门户管理员：清态后抛错（会话不保留）
        this.persist()
        throw new Error('该登录账号尚未关联任何企业，请联系集团管理员')
      }
      this.persist()
      this.scheduleRefresh()

      const wanted = tenantCode?.trim() ?? ''
      if (wanted) {
        const matched = tenants.find(
          (item) =>
            item.displayName.includes(wanted) || (item.taxNo ?? '').includes(wanted),
        )
        if (!matched) {
          broadcastSession('session-updated')
          return {
            path: '/select-tenant',
            notice: `企业标识"${wanted}"与当前账号关联的企业不匹配，请重新选择`,
          }
        }
        try {
          const roles = await this.loadMemberRoles(matched.tenantId)
          // 单角色 / 无角色：直接进入；多角色：进选择页的角色步骤
          if (roles.length <= 1) {
            await this.enterTenant(matched, roles[0]?.roleId)
            return { path: '/workbench' }
          }
          this.pendingRoleTenantId = matched.tenantId
        } catch {
          // 角色加载失败：回选择页重试，避免按未知角色签发权限
        }
        broadcastSession('session-updated')
        return { path: '/select-tenant' }
      }

      // 门户管理员始终经选择页（门户管理置顶）；普通用户单企业直进（多角色除外）
      if (!isPortalAdmin && tenants.length === 1) {
        const tenant = tenants[0]
        try {
          const roles = await this.loadMemberRoles(tenant.tenantId)
          if (roles.length <= 1) {
            await this.enterTenant(tenant, roles[0]?.roleId)
            return { path: '/workbench' }
          }
          this.pendingRoleTenantId = tenant.tenantId
        } catch {
          // 角色加载失败：回选择页重试
        }
      }

      broadcastSession('session-updated')
      return { path: '/select-tenant' }
    },

    /**
     * 选择角色步骤：按企业懒加载当前账号可进入的角色（命中缓存 / 并发复用）。
     * 失败不写缓存，由调用方提示重试。
     */
    async loadMemberRoles(tenantId: string): Promise<MemberRoleOption[]> {
      const cached = this.tenantRoles[tenantId]
      if (cached) {
        return cached
      }
      const pending = roleRequests.get(tenantId)
      if (pending) {
        return pending
      }

      const request = fetchMemberRoles(this.identityToken, tenantId)
        .then((roles) => {
          this.tenantRoles = { ...this.tenantRoles, [tenantId]: roles }
          return roles
        })
        .finally(() => {
          roleRequests.delete(tenantId)
        })

      roleRequests.set(tenantId, request)
      return request
    },

    /**
     * 进入企业（Phase 3）：身份级 Token + 混淆 tenant_id + 所选角色 → 租户级 Token → 拉取用户与模块许可。
     * @param roleId 所选租户角色 Id（单角色/多角色选择后传入；企业管理员可不传）
     */
    async enterTenant(tenant: TenantSummary, roleId?: string): Promise<void> {
      const token = await loginTenant(this.identityToken, tenant.tenantId, roleId)
      const user = await fetchCurrentUser(token.access_token)

      this.tenantToken = token.access_token
      this.tenantExpiresAt = this.computeExpiresAt(token.expires_in)
      this.currentTenant = tenant
      this.user = user
      this.licensedModules = user.modules ?? []
      this.portalAdminMode = false
      this.memberRoleId = roleId ?? ''
      this.pendingRoleTenantId = ''

      // v1.1：decode 租户级 Token（qlx_member_role + qlx_permissions）
      this.applyTenantTokenClaims(token.access_token)
      // v1.2：租户 Token 为 JWE（RSA-OAEP 加密，5 段）时前端无法解出 claims，
      // 以 /me 权威响应补齐（TenantAdmin 零计算旁路依赖 memberRole，缺失会导致 v-permission 移除全部按钮）
      if (!this.memberRole && user.memberRole) {
        this.memberRole = user.memberRole
      }
      if (this.permissions.length === 0 && Array.isArray(user.permissions)) {
        this.permissions = user.permissions
      }

      this.persist()
      this.scheduleRefresh()
      broadcastSession('session-updated')
    },

    /**
     * 进入门户管理（门户管理员专享）：不换取租户上下文，
     * 凭身份级 Token 调用门户管理端 API（/api/portal/me 返回 isPortalAdmin）。
     */
    async enterPortalAdmin(): Promise<void> {
      const user = await fetchCurrentUser(this.identityToken)

      this.tenantToken = ''
      this.currentTenant = {
        tenantId: PORTAL_ADMIN_TENANT_ID,
        tenantCode: 'portal',
        displayName: '门户管理',
        memberRole: 'PortalAdmin',
        lastLoginAt: null,
      }
      this.user = user
      this.licensedModules = user.modules ?? []
      this.portalAdminMode = true

      // v1.1：身份级 Token 已经在 signIn() 时 decode 过，portalAdminMode 本身就是全权限旁路
      // 这里无需再覆盖 memberRole / permissions

      this.persist()
      broadcastSession('session-updated')
    },

    // =============== Token claims 解码（v1.1） ===============

    /** decode 身份级 Token（signIn / 身份续期后调用）。 */
    applyIdentityTokenClaims(token: string): void {
      const claims = decodeJwtPayload(token)
      if (!claims) {
        return
      }
      // 门户管理员身份 Token 的 qlx_permissions 可能不写 → 保持空数组即可（portalAdminMode getter 全通）
      if (claims.permissions) {
        this.permissions = claims.permissions
      }
      // 身份级 Token 无 memberRole（那是租户级 claim）
      // portalAdminMode 由 action 手动设置，不由 claims 推断
    },

    /** decode 租户级 Token（enterTenant / 租户续期后调用）。 */
    applyTenantTokenClaims(token: string): void {
      const claims = decodeJwtPayload(token)
      if (!claims) {
        return
      }
      this.memberRole = claims.memberRole ?? ''
      if (claims.memberRoleId) {
        this.memberRoleId = claims.memberRoleId
      }
      // TenantAdmin 后端不写 qlx_permissions → 空数组；getter 里 memberRole=TenantAdmin 已旁路
      if (claims.permissions) {
        this.permissions = claims.permissions
      }
    },

    /** 登出：先清本地（保证 UI 立即失效），再尽力通知服务端拉黑 / 吊销。 */
    async signOut(): Promise<void> {
      const tenantToken = this.tenantToken
      const refreshToken = this.refreshToken

      this.reset()
      clearSession()
      this.stopRefresh()
      broadcastSession('session-cleared')

      if (tenantToken) {
        try {
          await logout(tenantToken)
        } catch {
          // 服务端不可达不阻断前端登出
        }
      }
      if (refreshToken) {
        try {
          await revokeToken(refreshToken)
        } catch {
          // 同上：refresh token 已随本地清除失效
        }
      }
    },

    /** 订阅其他标签页的会话变化 + 注册 HTTP 客户端续期回调 + 启动主动续期定时器。 */
    startSync(router: Router): void {
      if (stopSync) {
        return
      }
      // 注入续期回调（core/http 的 401 拦截器调用）
      setIdentityRefreshFn(() => this.refreshIdentity())
      setTenantRefreshFn(() => this.refreshTenant())

      // 恢复会话后启动续期定时器
      this.scheduleRefresh()

      stopSync = onSessionEvent((type) => {
        if (type === 'session-cleared') {
          this.reset()
          this.stopRefresh()
          this.restored = true
          void router.replace('/login')
          return
        }

        // 会话更新：重新读取存储；若本页需要租户上下文且已切换，整页重载保证数据一致
        const session = loadSession()
        this.$patch(session)
        this.scheduleRefresh()
        if (!session.tenantToken && !session.portalAdminMode) {
          void router.replace('/select-tenant')
          return
        }
        window.location.reload()
      })
    },

    // =============== Token 续期 ===============

    /** 由 expires_in（秒）计算到期时间戳（ms）。 */
    computeExpiresAt(expiresIn: number): number {
      const sec = typeof expiresIn === 'number' && expiresIn > 0 ? expiresIn : 0
      return sec > 0 ? Date.now() + sec * 1000 : 0
    },

    /**
     * 主动续期定时器：取身份/租户 Token 中较早到期者，
     * 提前 REFRESH_LEAD_SECONDS 秒触发续期；最小间隔 REFRESH_MIN_INTERVAL_MS。
     */
    scheduleRefresh(): void {
      this.stopRefresh()
      const now = Date.now()
      const candidates: number[] = []
      if (this.identityToken && this.identityExpiresAt > now) {
        candidates.push(this.identityExpiresAt - REFRESH_LEAD_SECONDS * 1000)
      }
      if (this.tenantToken && this.tenantExpiresAt > now) {
        candidates.push(this.tenantExpiresAt - REFRESH_LEAD_SECONDS * 1000)
      }
      if (candidates.length === 0) {
        return
      }
      const target = Math.min(...candidates)
      const delay = Math.max(target - now, REFRESH_MIN_INTERVAL_MS)
      refreshTimer = setTimeout(() => {
        void this.runScheduledRefresh()
      }, delay)
    },

    /** 停止主动续期定时器。 */
    stopRefresh(): void {
      if (refreshTimer) {
        clearTimeout(refreshTimer)
        refreshTimer = null
      }
    },

    /** 定时器触发：按到期顺序续期身份 / 租户 Token。 */
    async runScheduledRefresh(): Promise<void> {
      const now = Date.now()
      try {
        if (this.identityToken && this.identityExpiresAt > 0 && this.identityExpiresAt - now < REFRESH_LEAD_SECONDS * 1000) {
          await this.refreshIdentity()
        }
        if (this.tenantToken && this.tenantExpiresAt > 0 && this.tenantExpiresAt - now < REFRESH_LEAD_SECONDS * 1000) {
          await this.refreshTenant()
        }
      } catch {
        // 续期失败不阻断，下次定时器或 401 拦截器会重试；持续失败最终跳登录
      } finally {
        this.scheduleRefresh()
      }
    },

    /**
     * 身份级 Token 续期：refresh_token grant → 新 access + 轮换 refresh。
     * @returns 新的身份级 access token
     */
    async refreshIdentity(): Promise<string> {
      if (!this.refreshToken) {
        throw new Error('无 refresh token，无法续期身份令牌')
      }
      const tokens: TokenResponse = await refreshIdentityToken(this.refreshToken)
      this.identityToken = tokens.access_token
      if (tokens.refresh_token) {
        this.refreshToken = tokens.refresh_token
      }
      this.identityExpiresAt = this.computeExpiresAt(tokens.expires_in)
      // v1.1：decode 新身份 Token
      this.applyIdentityTokenClaims(tokens.access_token)
      this.persist()
      return tokens.access_token
    },

    /**
     * 租户级 Token 静默续期：tenant_silent grant → 新租户 access token。
     * @returns 新的租户级 access token
     */
    async refreshTenant(): Promise<string> {
      if (!this.tenantToken) {
        throw new Error('无租户令牌，无法续期')
      }
      const tokens: TokenResponse = await silentRefreshTenantToken(this.tenantToken)
      this.tenantToken = tokens.access_token
      this.tenantExpiresAt = this.computeExpiresAt(tokens.expires_in)
      // v1.1：decode 新租户 Token
      this.applyTenantTokenClaims(tokens.access_token)
      this.persist()
      return tokens.access_token
    },
  },
})
