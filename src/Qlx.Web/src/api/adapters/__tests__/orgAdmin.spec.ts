/*
 * 所属公司：四川淇利馨集团有限公司
 * 描述：orgAdmin 适配器单元测试——组织/岗位映射、toggle 端点、分页参数、错误透传
 */

import { beforeEach, describe, expect, it, vi } from 'vitest'
import type { AxiosRequestConfig, AxiosResponse } from 'axios'

vi.mock('@/core/http', () => ({
  http: { request: vi.fn() },
}))

import { http } from '@/core/http'
import { createTenantOrgAdminAdapter } from '../orgAdmin'

const requestMock = vi.mocked(http.request)

function ok<T>(data: T): AxiosResponse {
  return { data: { success: true, data } } as AxiosResponse
}

function lastConfig(): AxiosRequestConfig {
  return requestMock.mock.calls.at(-1)![0] as AxiosRequestConfig
}

const orgRow = {
  id: 'o1', parentId: null, name: '总部', type: 'Company', managerMemberId: null,
  status: 'Enabled', sortOrder: 1, isBuiltin: true, createdAt: '2026-10-23', memberCount: 5,
}

const positionRow = {
  id: 'p1', orgId: 'o1', name: '会计', code: 'acc', headcount: 3, currentCount: 2,
  status: 'Enabled', description: null, updatedAt: '2026-10-23',
}

beforeEach(() => {
  requestMock.mockReset()
})

describe('createTenantOrgAdminAdapter', () => {
  it('listOrgNodes 解包扁平列表（含 memberCount）', async () => {
    requestMock.mockResolvedValueOnce(ok([orgRow]))
    const nodes = await createTenantOrgAdminAdapter().listOrgNodes()
    expect(lastConfig().url).toBe('/api/tenant/organizations/tree')
    expect(nodes).toHaveLength(1)
    expect(nodes[0]).toMatchObject({ id: 'o1', memberCount: 5, isBuiltin: true, parentId: null })
  })

  it('saveOrg 新建走 POST / 更新走 PUT {id}', async () => {
    const adapter = createTenantOrgAdminAdapter()
    const input = { parentId: null, name: '总部', type: 'Company' as const, managerMemberId: null, sortOrder: 1 }

    requestMock.mockResolvedValueOnce(ok(orgRow))
    await adapter.saveOrg(null, input)
    expect(lastConfig().method).toBe('POST')
    expect(lastConfig().url).toBe('/api/tenant/organizations')

    requestMock.mockResolvedValueOnce(ok(orgRow))
    await adapter.saveOrg('o1', input)
    expect(lastConfig().method).toBe('PUT')
    expect(lastConfig().url).toBe('/api/tenant/organizations/o1')
  })

  it('toggleOrg 走 POST {id}/toggle 并返回更新后节点', async () => {
    requestMock.mockResolvedValueOnce(ok({ ...orgRow, status: 'Disabled' }))
    const node = await createTenantOrgAdminAdapter().toggleOrg('o1')
    expect(lastConfig()).toMatchObject({ method: 'POST', url: '/api/tenant/organizations/o1/toggle' })
    expect(node.status).toBe('Disabled')
  })

  it('fetchPositions 组装分页参数（keyword 去空白 + orgId）', async () => {
    requestMock.mockResolvedValueOnce(ok({
      items: [positionRow], totalCount: 1, page: 1, pageSize: 20, totalPages: 1, isLastPage: true,
    }))
    const res = await createTenantOrgAdminAdapter().fetchPositions({ page: 1, pageSize: 20, keyword: ' 会计 ', orgId: 'o1' })
    expect(lastConfig().params).toEqual({ page: 1, pageSize: 20, keyword: '会计', orgId: 'o1' })
    expect(res.items[0]).toMatchObject({ id: 'p1', currentCount: 2, description: '' })
    expect(res.totalCount).toBe(1)
  })

  it('savePosition 走 POST 并归一可空 description', async () => {
    requestMock.mockResolvedValueOnce(ok(positionRow))
    const item = await createTenantOrgAdminAdapter().savePosition({
      name: '会计', orgId: 'o1', headcount: 3, description: '',
    })
    expect(lastConfig()).toMatchObject({ method: 'POST', url: '/api/tenant/positions' })
    expect(item.description).toBe('')
  })

  it('togglePosition 走 POST positions/{id}/toggle（B-5 新端点）', async () => {
    requestMock.mockResolvedValueOnce(ok({ ...positionRow, status: 'Disabled' }))
    const item = await createTenantOrgAdminAdapter().togglePosition('p1')
    expect(lastConfig()).toMatchObject({ method: 'POST', url: '/api/tenant/positions/p1/toggle' })
    expect(item.status).toBe('Disabled')
  })

  it('success=false 时透传后端 message（如根公司保护）', async () => {
    requestMock.mockResolvedValueOnce({
      data: { success: false, code: 'ORG_ROOT_PROTECTED', message: '根公司不可停用' },
    } as AxiosResponse)
    await expect(createTenantOrgAdminAdapter().toggleOrg('o1')).rejects.toThrow('根公司不可停用')
  })

  it('断网归一为可读文案', async () => {
    requestMock.mockRejectedValueOnce(Object.assign(new Error('Network Error'), { isAxiosError: true }))
    await expect(createTenantOrgAdminAdapter().listOrgNodes()).rejects.toThrow('无法连接服务，请确认后端服务已启动')
  })

  it('超时归一为可读文案', async () => {
    requestMock.mockRejectedValueOnce(Object.assign(new Error('timeout'), { isAxiosError: true, code: 'ECONNABORTED' }))
    await expect(createTenantOrgAdminAdapter().listOrgNodes()).rejects.toThrow('请求超时，请稍后重试')
  })
})
