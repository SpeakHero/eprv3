/*
 * 所属公司：四川淇利馨集团有限公司
 * 描述：enterTenant 角色状态收敛回归测试（X-3 修复）——
 *       切换企业/角色时 memberRole 与 permissions 必须以 /me 权威响应无条件覆盖，
 *       禁止上一上下文残留（管理员 → 普通成员不得保留 TenantAdmin；普通 → 管理员必须恢复）
 */

import { createPinia, setActivePinia } from 'pinia'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { fetchCurrentUser, loginTenant } from '@/api/portal'
import { useAuthStore } from '@/stores/auth'
import type { TenantSummary } from '@/types/auth'

vi.mock('@/api/portal', () => ({
  fetchMemberRoles: vi.fn(),
  fetchCurrentUser: vi.fn(),
  loginIdentity: vi.fn(),
  loginTenant: vi.fn(),
  logout: vi.fn(),
  refreshIdentityToken: vi.fn(),
  revokeToken: vi.fn(),
  silentRefreshTenantToken: vi.fn(),
}))

vi.mock('@/core/http', () => ({
  setIdentityRefreshFn: vi.fn(),
  setTenantRefreshFn: vi.fn(),
}))

vi.mock('@/core/session', () => ({
  broadcastSession: vi.fn(),
  clearSession: vi.fn(),
  loadSession: vi.fn(() => null),
  onSessionEvent: vi.fn(),
  saveSession: vi.fn(),
}))

const loginTenantMock = vi.mocked(loginTenant)
const fetchCurrentUserMock = vi.mocked(fetchCurrentUser)

/** 5 段 JWE 结构占位（前端无法解 claims，只能依赖 /me）。 */
const JWE_TOKEN = 'h1.h2.h3.h4.h5'

const tenantA: TenantSummary = {
  tenantId: 'tenant-a',
  tenantCode: 'a',
  displayName: '企业A',
  memberRole: 'TenantAdmin',
  lastLoginAt: null,
}

const tenantB: TenantSummary = {
  tenantId: 'tenant-b',
  tenantCode: 'b',
  displayName: '企业B',
  memberRole: 'OrdinaryMember',
  lastLoginAt: null,
}

describe('enterTenant 角色状态收敛（X-3 回归）', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.clearAllMocks()
    loginTenantMock.mockResolvedValue({
      access_token: JWE_TOKEN,
      expires_in: 3600,
      refresh_token: 'r',
    } as never)
  })

  it('管理员 → 普通成员：memberRole/permissions 被 /me 覆盖，不残留 TenantAdmin', async () => {
    const auth = useAuthStore()
    auth.$patch({ identityToken: 'id-token', memberRole: 'TenantAdmin', permissions: [] })
    fetchCurrentUserMock.mockResolvedValue({
      memberRole: 'OrdinaryMember',
      permissions: ['bas.admin.org.menu'],
      modules: [],
    } as never)

    await auth.enterTenant(tenantB)

    expect(auth.memberRole).toBe('OrdinaryMember')
    expect(auth.permissions).toEqual(['bas.admin.org.menu'])
  })

  it('普通成员 → 管理员：memberRole 恢复 TenantAdmin，permissions 被清空（不残留旧权限码）', async () => {
    const auth = useAuthStore()
    auth.$patch({
      identityToken: 'id-token',
      memberRole: 'OrdinaryMember',
      permissions: ['bas.admin.org.menu'],
    })
    fetchCurrentUserMock.mockResolvedValue({
      memberRole: 'TenantAdmin',
      permissions: [],
      modules: [],
    } as never)

    await auth.enterTenant(tenantA)

    expect(auth.memberRole).toBe('TenantAdmin')
    expect(auth.permissions).toEqual([])
  })

  it('JWE 场景：/me 兜底补齐 memberRole（TenantAdmin 零计算旁路依赖）', async () => {
    const auth = useAuthStore()
    auth.$patch({ identityToken: 'id-token', memberRole: '', permissions: [] })
    fetchCurrentUserMock.mockResolvedValue({
      memberRole: 'TenantAdmin',
      permissions: [],
      modules: [],
    } as never)

    await auth.enterTenant(tenantA)

    expect(auth.memberRole).toBe('TenantAdmin')
  })

  it('进入门户管理：显式清空租户角色态（memberRole/permissions 不得残留）', async () => {
    const auth = useAuthStore()
    auth.$patch({
      identityToken: 'id-token',
      memberRole: 'TenantAdmin',
      memberRoleId: 'role-1',
      permissions: ['portal.tenant.view'],
    })
    fetchCurrentUserMock.mockResolvedValue({ modules: [] } as never)

    await auth.enterPortalAdmin()

    expect(auth.portalAdminMode).toBe(true)
    expect(auth.memberRole).toBe('')
    expect(auth.memberRoleId).toBe('')
    expect(auth.permissions).toEqual([])
  })
})
