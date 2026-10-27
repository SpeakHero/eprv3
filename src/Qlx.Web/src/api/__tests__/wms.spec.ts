/*
 * 所属公司：四川淇利馨集团有限公司
 * 描述：物流管理（Wms）API 封装单元测试——仓库 / 库区 / 库位端点路径、查询参数与载荷（仓库无编码字段）
 */

import { beforeEach, describe, expect, it, vi } from 'vitest'
import type { AxiosRequestConfig, AxiosResponse } from 'axios'

vi.mock('@/core/http', () => ({
  http: { request: vi.fn() },
}))

import { http } from '@/core/http'
import { warehouseApi } from '../wms'
import type { CreateWarehousePayload, UpdateWarehousePayload } from '@/types/wms'

const requestMock = vi.mocked(http.request)

function ok<T>(data: T): AxiosResponse {
  return { data: { success: true, data } } as AxiosResponse
}

function lastConfig(): AxiosRequestConfig {
  return requestMock.mock.calls.at(-1)![0] as AxiosRequestConfig
}

beforeEach(() => {
  requestMock.mockReset()
})

describe('仓库端点', () => {
  it('列表 / 详情 / 启停 / 删除方法对齐后端', async () => {
    requestMock.mockResolvedValueOnce(ok([]))
    await warehouseApi.list()
    expect(lastConfig()).toMatchObject({ method: 'get', url: '/api/wms/warehouses' })

    requestMock.mockResolvedValueOnce(ok({ id: 'w1' }))
    await warehouseApi.get('w 1')
    expect(lastConfig()).toMatchObject({ method: 'get', url: '/api/wms/warehouses/w%201' })

    requestMock.mockResolvedValueOnce(ok({ id: 'w1' }))
    await warehouseApi.toggle('w1')
    expect(lastConfig()).toMatchObject({ method: 'post', url: '/api/wms/warehouses/w1/toggle' })

    requestMock.mockResolvedValueOnce(ok(true))
    await warehouseApi.remove('w1')
    expect(lastConfig()).toMatchObject({ method: 'delete', url: '/api/wms/warehouses/w1' })
  })

  it('新建 / 更新载荷不含仓库编码（N-1）', async () => {
    const payload: CreateWarehousePayload = {
      name: '一号仓',
      whType: 1,
      orgId: null,
      address: '成都',
      keeperId: null,
      remark: null,
    }
    requestMock.mockResolvedValueOnce(ok({ id: 'w1' }))
    await warehouseApi.create(payload)
    expect(lastConfig()).toMatchObject({ method: 'post', url: '/api/wms/warehouses' })
    expect(lastConfig().data).toEqual(payload)
    expect(Object.keys(lastConfig().data as object)).not.toContain('code')

    requestMock.mockResolvedValueOnce(ok({ id: 'w1' }))
    await warehouseApi.update('w1', payload as UpdateWarehousePayload)
    expect(lastConfig()).toMatchObject({ method: 'put', url: '/api/wms/warehouses/w1' })
  })
})

describe('库区端点', () => {
  it('列表 / 新建 / 更新 / 启停路径', async () => {
    requestMock.mockResolvedValueOnce(ok([]))
    await warehouseApi.listZones('w1')
    expect(lastConfig()).toMatchObject({ method: 'get', url: '/api/wms/warehouses/w1/zones' })

    requestMock.mockResolvedValueOnce(ok({ id: 'z1' }))
    await warehouseApi.createZone('w1', { code: 'A', name: 'A 区', sortOrder: 0 })
    expect(lastConfig()).toMatchObject({ method: 'post', url: '/api/wms/warehouses/w1/zones' })

    requestMock.mockResolvedValueOnce(ok({ id: 'z1' }))
    await warehouseApi.updateZone('z1', { name: 'A 区', sortOrder: 1 })
    expect(lastConfig()).toMatchObject({ method: 'put', url: '/api/wms/warehouses/zones/z1' })

    requestMock.mockResolvedValueOnce(ok({ id: 'z1' }))
    await warehouseApi.toggleZone('z1')
    expect(lastConfig()).toMatchObject({ method: 'post', url: '/api/wms/warehouses/zones/z1/toggle' })
  })
})

describe('库位端点', () => {
  it('列表 / 新建（zoneId 为查询参数） / 更新 / 启停路径', async () => {
    requestMock.mockResolvedValueOnce(ok([]))
    await warehouseApi.listLocations('w1')
    expect(lastConfig()).toMatchObject({ method: 'get', url: '/api/wms/warehouses/w1/locations' })

    requestMock.mockResolvedValueOnce(ok({ id: 'l1' }))
    await warehouseApi.createLocation('w1', 'z1', { code: 'A-01', barcode: 'B001', sortOrder: 0 })
    expect(lastConfig()).toMatchObject({
      method: 'post',
      url: '/api/wms/warehouses/w1/locations',
    })
    expect(lastConfig().params).toEqual({ zoneId: 'z1' })
    expect(Object.keys(lastConfig().data as object)).not.toContain('zoneId')

    requestMock.mockResolvedValueOnce(ok({ id: 'l1' }))
    await warehouseApi.updateLocation('l1', { barcode: 'B002', sortOrder: 1 })
    expect(lastConfig()).toMatchObject({ method: 'put', url: '/api/wms/warehouses/locations/l1' })

    requestMock.mockResolvedValueOnce(ok({ id: 'l1' }))
    await warehouseApi.toggleLocation('l1')
    expect(lastConfig()).toMatchObject({
      method: 'post',
      url: '/api/wms/warehouses/locations/l1/toggle',
    })
  })
})
