/*
 * 所属公司：四川淇利馨集团有限公司
 * 描述：http 客户端 401 拦截器回归测试——续期失败/无续期回调时必须全量清除本地会话
 *       （含 portalAdminMode 等非 token 字段），杜绝 login ↔ workbench 无限跳转
 */

import type { AxiosRequestConfig, AxiosResponse } from 'axios'
import { beforeEach, describe, expect, it, vi } from 'vitest'

/** 内存版 localStorage（node 环境无 DOM）。 */
function stubLocalStorage(): void {
  const store = new Map<string, string>()
  vi.stubGlobal('localStorage', {
    getItem: (key: string) => store.get(key) ?? null,
    setItem: (key: string, value: string) => void store.set(key, value),
    removeItem: (key: string) => void store.delete(key),
    clear: () => store.clear(),
  })
}

const locationStub: { href: string } = { href: '' }

/** 401 拒绝适配器（可切换为「先 401 后 200」验证重试）。 */
function reject401(config: AxiosRequestConfig): Promise<never> {
  return Promise.reject({ config, response: { status: 401, data: {} }, isAxiosError: true })
}

/** 预置一份「门户管理模式」会话（模拟用户正在门户管理端操作）。 */
function seedPortalAdminSession(): void {
  localStorage.setItem(
    'qlx.session.v1',
    JSON.stringify({
      identityToken: 'expired-identity-token',
      refreshToken: 'expired-refresh-token',
      tenantToken: '',
      portalAdminMode: true,
      isPortalAdminAccount: true,
      currentTenant: { tenantId: 'portal', tenantCode: 'portal', displayName: '门户管理' },
    }),
  )
}

describe('http 401 拦截器（会话清除回归）', () => {
  beforeEach(async () => {
    vi.resetModules()
    stubLocalStorage()
    locationStub.href = ''
    vi.stubGlobal('window', { location: locationStub })
  })

  it('续期失败：全量清除会话（localStorage 键被移除）并跳登录页', async () => {
    seedPortalAdminSession()
    const { http, setIdentityRefreshFn } = await import('@/core/http')
    setIdentityRefreshFn(() => Promise.reject(new Error('refresh token 已失效')))
    http.defaults.adapter = reject401

    await expect(http.get('/api/portal/admin/workbench/overview')).rejects.toBeTruthy()

    // 关键断言：会话键整体移除（portalAdminMode 等字段不得残留）
    expect(localStorage.getItem('qlx.session.v1')).toBeNull()
    expect(locationStub.href).toBe('/login')
  })

  it('无续期回调：全量清除会话并跳登录页', async () => {
    seedPortalAdminSession()
    const { http } = await import('@/core/http')
    // 刻意不注册任何续期回调
    http.defaults.adapter = reject401

    await expect(http.get('/api/portal/me')).rejects.toBeTruthy()

    expect(localStorage.getItem('qlx.session.v1')).toBeNull()
    expect(locationStub.href).toBe('/login')
  })

  it('续期成功：携带新令牌重试原请求，会话保留不清除', async () => {
    seedPortalAdminSession()
    const { http, setIdentityRefreshFn } = await import('@/core/http')
    setIdentityRefreshFn(() => Promise.resolve('brand-new-token'))

    let attempts = 0
    let retriedAuth = ''
    http.defaults.adapter = (config: AxiosRequestConfig): Promise<AxiosResponse> => {
      attempts += 1
      if (attempts === 1) {
        return reject401(config)
      }
      retriedAuth = String(config.headers?.Authorization ?? '')
      return Promise.resolve({
        data: { ok: true },
        status: 200,
        statusText: 'OK',
        headers: {},
        config,
      } as AxiosResponse)
    }

    const response = await http.get('/api/portal/admin/workbench/overview')

    expect(attempts).toBe(2)
    expect(retriedAuth).toBe('Bearer brand-new-token')
    expect(response.status).toBe(200)
    // 会话未被清除
    expect(localStorage.getItem('qlx.session.v1')).not.toBeNull()
    expect(locationStub.href).toBe('')
  })
})
