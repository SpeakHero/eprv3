/*
 * 所属公司：四川淇利馨集团有限公司
 * 描述：路由守卫纯函数——公开 / 需身份 / 需租户上下文 / 需真实租户 / 门户管理五级判定。
 *       抽离为纯函数供 vitest 直测（router/index.ts 仅负责注入 auth 与标题设置）。
 */

import type { RouteMeta } from 'vue-router'

/** 守卫决策所需的最小会话快照（与 auth store getter 对齐）。 */
export interface GuardAuthSnapshot {
  isAuthenticated: boolean
  hasTenant: boolean
  tenantToken: string
  isPortalAdminAccount: boolean
  portalAdminMode: boolean
}

/** 守卫判定结果：true 放行；字符串为重定向目标。 */
export type GuardDecision = true | string

/**
 * 路由访问判定（X-4 整改：门户管理端须同时满足账号特权 + 门户管理模式）。
 * @param to 目标路由（仅消费 meta）。
 * @param auth 会话快照。
 */
export function resolveRouteGuard(
  to: { meta: RouteMeta },
  auth: GuardAuthSnapshot,
): GuardDecision {
  // 已登录用户访问登录页：按会话深度前移
  if (to.meta.publicPage) {
    if (auth.hasTenant) {
      return '/workbench'
    }
    if (auth.isAuthenticated) {
      return '/select-tenant'
    }
    return true
  }

  if (to.meta.requiresTenant && !auth.hasTenant) {
    return auth.isAuthenticated ? '/select-tenant' : '/login'
  }

  // 租户自管理域必须有真实租户上下文（门户管理模式不签发租户 Token）
  if (to.meta.requiresTenantData && !auth.tenantToken) {
    return auth.isAuthenticated ? '/select-tenant' : '/login'
  }

  if (to.meta.requiresPortalAdmin) {
    if (!auth.isPortalAdminAccount) {
      return auth.isAuthenticated ? '/select-tenant' : '/login'
    }
    // X-4 整改：门户特权账号处于租户上下文（未显式进入门户管理模式）时不得访问门户管理端
    if (!auth.portalAdminMode) {
      return auth.hasTenant ? '/workbench' : '/select-tenant'
    }
  }

  if (to.meta.requiresIdentity && !auth.isAuthenticated) {
    return '/login'
  }

  return true
}
