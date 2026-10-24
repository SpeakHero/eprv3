/*
 * 所属公司：四川淇利馨集团有限公司
 * 描述：菜单 API 单元测试——验证门户/租户两个端点、共享 http 实例、ApiResponse 解包（G-1）与扁平契约（B-2）
 */

import { beforeEach, describe, expect, it, vi } from 'vitest'
import type { AxiosRequestConfig, AxiosResponse } from 'axios'

vi.mock('@/core/http', () => ({
  http: { request: vi.fn() },
}))

import { http } from '@/core/http'
import { fetchMenus, fetchTenantMenus } from '../menus'

const requestMock = vi.mocked(http.request)

function ok<T>(data: T): AxiosResponse {
  return { data: { success: true, code: '0', data } } as AxiosResponse
}

function lastConfig(): AxiosRequestConfig {
  return requestMock.mock.calls.at(-1)![0] as AxiosRequestConfig
}

const flatPayload = {
  navGroups: [{ groupCode: 'workbench', groupName: '工作台', moduleCodes: 'bas', sortOrder: 0 }],
  menus: [
    {
      moduleCode: 'bas',
      menuCode: 'bas.admin.org',
      parentCode: null,
      groupName: '工作台',
      displayName: '组织机构',
      route: '/manage/organizations',
      icon: 'OfficeBuilding',
      requiredPermissionCode: 'bas.admin.org.menu',
      sortOrder: 10,
      openMode: 1,
    },
  ],
}

beforeEach(() => {
  requestMock.mockReset()
})

describe('menus api', () => {
  it('fetchMenus 走 GET /api/portal/menus 并解包扁平数据', async () => {
    requestMock.mockResolvedValueOnce(ok(flatPayload))

    const result = await fetchMenus()

    expect(lastConfig()).toMatchObject({ method: 'GET', url: '/api/portal/menus' })
    expect(result.navGroups).toHaveLength(1)
    expect(result.menus).toHaveLength(1)
    // 契约 B-2：后端仅下发扁平字段，不含 children 嵌套
    expect(result.menus[0]).not.toHaveProperty('children')
    expect(result.menus[0].route).toBe('/manage/organizations')
  })

  it('fetchTenantMenus 走 GET /api/tenant/menus（后端按模块许可裁剪）', async () => {
    requestMock.mockResolvedValueOnce(ok({ navGroups: [], menus: [] }))

    const result = await fetchTenantMenus()

    expect(lastConfig()).toMatchObject({ method: 'GET', url: '/api/tenant/menus' })
    expect(result).toEqual({ navGroups: [], menus: [] })
  })

  it('success=false 时抛出后端 message（G-1 契约：判 success 而非 code）', async () => {
    requestMock.mockResolvedValueOnce({
      data: { success: false, code: 'AUTH_TENANT_REQUIRED', message: '当前会话不包含租户上下文，请先进入租户' },
    } as AxiosResponse)

    await expect(fetchTenantMenus()).rejects.toThrow('当前会话不包含租户上下文，请先进入租户')
  })
})
