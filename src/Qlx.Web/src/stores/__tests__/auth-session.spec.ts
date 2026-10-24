/*
 * 所属公司：四川淇利馨集团有限公司
 * 描述：auth store 会话判定回归测试——hasTenant 必须以身份级 Token 为前提，
 *       防止 401 清会话残留（portalAdminMode/tenantToken）导致 login ↔ workbench 无限跳转
 */

import { createPinia, setActivePinia } from 'pinia'
import { beforeEach, describe, expect, it } from 'vitest'
import { useAuthStore } from '@/stores/auth'

describe('auth store 会话判定（hasTenant 回归）', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
  })

  it('门户管理模式 + 身份令牌：hasTenant 为 true（原行为保持）', () => {
    const auth = useAuthStore()
    auth.$patch({ identityToken: 'id-token', portalAdminMode: true })
    expect(auth.isAuthenticated).toBe(true)
    expect(auth.hasTenant).toBe(true)
  })

  it('租户令牌 + 身份令牌：hasTenant 为 true（原行为保持）', () => {
    const auth = useAuthStore()
    auth.$patch({ identityToken: 'id-token', tenantToken: 'tenant-token' })
    expect(auth.hasTenant).toBe(true)
  })

  it('仅身份令牌（登录 Phase 1 完成、未选企业）：hasTenant 为 false', () => {
    const auth = useAuthStore()
    auth.$patch({ identityToken: 'id-token' })
    expect(auth.isAuthenticated).toBe(true)
    expect(auth.hasTenant).toBe(false)
  })

  it('回归：portalAdminMode 残留但身份令牌已清除 → hasTenant 必须为 false（守卫不得弹回工作台）', () => {
    const auth = useAuthStore()
    // 模拟 401 清会话后的残留状态：token 全空，portalAdminMode 仍为 true
    auth.$patch({ identityToken: '', tenantToken: '', portalAdminMode: true })
    expect(auth.isAuthenticated).toBe(false)
    expect(auth.hasTenant).toBe(false)
  })

  it('回归：tenantToken 残留但身份令牌已清除 → hasTenant 必须为 false', () => {
    const auth = useAuthStore()
    auth.$patch({ identityToken: '', tenantToken: 'stale-tenant-token' })
    expect(auth.isAuthenticated).toBe(false)
    expect(auth.hasTenant).toBe(false)
  })
})
