/*
 * 所属公司：四川淇利馨集团有限公司
 * 描述：路由守卫纯函数回归测试——重点覆盖 X-4 整改：
 *       门户特权账号在租户上下文（非门户管理模式）不得进入 /portal/*，
 *       且原有五级判定行为保持不变
 */

import { describe, expect, it } from 'vitest'
import { resolveRouteGuard } from '@/router/guard'
import type { GuardAuthSnapshot } from '@/router/guard'

function snapshot(overrides: Partial<GuardAuthSnapshot> = {}): GuardAuthSnapshot {
  return {
    isAuthenticated: false,
    hasTenant: false,
    tenantToken: '',
    isPortalAdminAccount: false,
    portalAdminMode: false,
    ...overrides,
  }
}

/** 门户特权账号 + 租户上下文（X-4 场景）：已进入某企业，但非门户管理模式。 */
const portalAdminInTenant = snapshot({
  isAuthenticated: true,
  hasTenant: true,
  tenantToken: 'tenant-token',
  isPortalAdminAccount: true,
  portalAdminMode: false,
})

/** 门户特权账号 + 门户管理模式。 */
const portalAdminInPortalMode = snapshot({
  isAuthenticated: true,
  hasTenant: true,
  isPortalAdminAccount: true,
  portalAdminMode: true,
})

describe('路由守卫（X-4 门户特权上下文隔离）', () => {
  it('X-4：门户特权账号处于租户上下文访问 /portal/* → 重定向工作台', () => {
    expect(resolveRouteGuard({ meta: { requiresPortalAdmin: true } }, portalAdminInTenant))
      .toBe('/workbench')
  })

  it('X-4：门户特权账号无租户上下文且非门户模式访问 /portal/* → 重定向选企业页', () => {
    const auth = snapshot({ isAuthenticated: true, isPortalAdminAccount: true })
    expect(resolveRouteGuard({ meta: { requiresPortalAdmin: true } }, auth))
      .toBe('/select-tenant')
  })

  it('门户管理模式访问 /portal/* → 放行', () => {
    expect(resolveRouteGuard({ meta: { requiresPortalAdmin: true } }, portalAdminInPortalMode))
      .toBe(true)
  })

  it('非门户特权账号访问 /portal/* → 重定向选企业页（原行为保持）', () => {
    const auth = snapshot({ isAuthenticated: true, hasTenant: true, tenantToken: 't' })
    expect(resolveRouteGuard({ meta: { requiresPortalAdmin: true } }, auth))
      .toBe('/select-tenant')
  })

  it('未登录访问 /portal/* → 重定向登录页（原行为保持）', () => {
    expect(resolveRouteGuard({ meta: { requiresPortalAdmin: true } }, snapshot()))
      .toBe('/login')
  })

  it('租户成员访问工作台 → 放行（原行为保持）', () => {
    expect(resolveRouteGuard({ meta: { requiresTenant: true } }, portalAdminInTenant))
      .toBe(true)
  })

  it('门户管理模式访问租户自管理域（requiresTenantData）→ 重定向选企业页（原行为保持）', () => {
    expect(resolveRouteGuard({ meta: { requiresTenantData: true } }, portalAdminInPortalMode))
      .toBe('/select-tenant')
  })

  it('已登录访问登录页 → 按会话深度前移工作台（原行为保持）', () => {
    expect(resolveRouteGuard({ meta: { publicPage: true } }, portalAdminInTenant))
      .toBe('/workbench')
  })
})
