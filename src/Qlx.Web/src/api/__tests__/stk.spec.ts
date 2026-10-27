/*
 * 所属公司：四川淇利馨集团有限公司
 * 描述：商品与库存（Stk）API 封装单元测试——端点路径 / 查询参数组装 / 请求载荷（无编码、无后端计算金额）
 */

import { beforeEach, describe, expect, it, vi } from 'vitest'
import type { AxiosRequestConfig, AxiosResponse } from 'axios'

vi.mock('@/core/http', () => ({
  http: { request: vi.fn() },
}))

import { http } from '@/core/http'
import { costAdjustApi, itemApi, openingApi, stockApi } from '../stk'
import type { CreateItemPayload, SaveCostAdjustPayload, SaveOpeningPayload } from '@/types/stk'

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

describe('商品档案端点', () => {
  it('列表 / 详情 / 价格历史走 GET 且 Id 编码', async () => {
    requestMock.mockResolvedValueOnce(ok([]))
    await itemApi.list()
    expect(lastConfig()).toMatchObject({ method: 'get', url: '/api/stk/items' })

    requestMock.mockResolvedValueOnce(ok({ id: 'i1' }))
    await itemApi.get('i 1')
    expect(lastConfig()).toMatchObject({ method: 'get', url: '/api/stk/items/i%201' })

    requestMock.mockResolvedValueOnce(ok([]))
    await itemApi.priceLogs('i1')
    expect(lastConfig()).toMatchObject({ method: 'get', url: '/api/stk/items/i1/price-logs' })
  })

  it('新建载荷不含商品编码（N-1）', async () => {
    const payload: CreateItemPayload = {
      name: '螺栓',
      spec: 'M8',
      barcode: null,
      categoryId: 'c1',
      unitId: 'u1',
      batchEnabled: true,
      expiryEnabled: false,
      taxRate: 0.13,
      refCostPrice: 12.5,
      guidePrice: null,
    }
    requestMock.mockResolvedValueOnce(ok({ id: 'req1' }))
    await itemApi.create(payload)
    const config = lastConfig()
    expect(config).toMatchObject({ method: 'post', url: '/api/stk/items' })
    expect(config.data).toEqual(payload)
    expect(Object.keys(config.data as object)).not.toContain('itemCode')
  })

  it('更新 / 启停 / 停用申请方法对齐后端端点', async () => {
    requestMock.mockResolvedValueOnce(ok({ pendingApproval: false, row: null, request: null }))
    await itemApi.update('i1', {
      name: '螺栓',
      spec: null,
      barcode: null,
      categoryId: 'c1',
      unitId: 'u1',
      batchEnabled: false,
      expiryEnabled: false,
      taxRate: 0,
      refCostPrice: null,
      guidePrice: null,
    })
    expect(lastConfig()).toMatchObject({ method: 'put', url: '/api/stk/items/i1' })

    requestMock.mockResolvedValueOnce(ok({ id: 'i1' }))
    await itemApi.toggle('i1')
    expect(lastConfig()).toMatchObject({ method: 'post', url: '/api/stk/items/i1/toggle' })

    requestMock.mockResolvedValueOnce(ok({ id: 'req2' }))
    await itemApi.submitDisable('i1')
    expect(lastConfig()).toMatchObject({ method: 'post', url: '/api/stk/items/i1/disable-request' })
  })
})

describe('期初建账端点', () => {
  it('列表组装 status / warehouseId，空值不上送', async () => {
    requestMock.mockResolvedValueOnce(ok([]))
    await openingApi.list()
    expect(lastConfig()).toMatchObject({ method: 'get', url: '/api/stk/openings' })
    expect(lastConfig().params).toEqual({})

    requestMock.mockResolvedValueOnce(ok([]))
    await openingApi.list({ status: 2, warehouseId: 'w1' })
    expect(lastConfig().params).toEqual({ status: 2, warehouseId: 'w1' })
  })

  it('汇总 / 详情 / 提交审批端点', async () => {
    requestMock.mockResolvedValueOnce(ok([]))
    await openingApi.summary()
    expect(lastConfig()).toMatchObject({ method: 'get', url: '/api/stk/openings/summary' })

    requestMock.mockResolvedValueOnce(ok({}))
    await openingApi.get('o1')
    expect(lastConfig()).toMatchObject({ method: 'get', url: '/api/stk/openings/o1' })

    requestMock.mockResolvedValueOnce(ok({ id: 'req3' }))
    await openingApi.submit('o1')
    expect(lastConfig()).toMatchObject({ method: 'post', url: '/api/stk/openings/o1/submit' })
  })

  it('新建 / 更新 / 删除载荷不含期初单号与明细金额（N-1 / V-1）', async () => {
    const payload: SaveOpeningPayload = {
      warehouseId: 'w1',
      remark: '期初',
      lines: [{ itemId: 'i1', batchNo: null, qty: 10, unitCost: 5.5 }],
    }
    requestMock.mockResolvedValueOnce(ok({}))
    await openingApi.create(payload)
    const config = lastConfig()
    expect(config).toMatchObject({ method: 'post', url: '/api/stk/openings' })
    expect(config.data).toEqual(payload)
    expect(Object.keys(config.data as object)).toEqual(['warehouseId', 'remark', 'lines'])
    const line = (config.data as SaveOpeningPayload).lines[0]
    expect(Object.keys(line)).toEqual(['itemId', 'batchNo', 'qty', 'unitCost'])

    requestMock.mockResolvedValueOnce(ok({}))
    await openingApi.update('o1', payload)
    expect(lastConfig()).toMatchObject({ method: 'put', url: '/api/stk/openings/o1' })

    requestMock.mockResolvedValueOnce(ok(true))
    await openingApi.remove('o1')
    expect(lastConfig()).toMatchObject({ method: 'delete', url: '/api/stk/openings/o1' })
  })
})

describe('成本调整端点', () => {
  it('列表 status 过滤 / 详情 / 提交审批', async () => {
    requestMock.mockResolvedValueOnce(ok([]))
    await costAdjustApi.list({ status: 1 })
    expect(lastConfig()).toMatchObject({ method: 'get', url: '/api/stk/cost-adjusts' })
    expect(lastConfig().params).toEqual({ status: 1 })

    requestMock.mockResolvedValueOnce(ok({}))
    await costAdjustApi.get('a1')
    expect(lastConfig()).toMatchObject({ method: 'get', url: '/api/stk/cost-adjusts/a1' })

    requestMock.mockResolvedValueOnce(ok({ id: 'req4' }))
    await costAdjustApi.submit('a1')
    expect(lastConfig()).toMatchObject({ method: 'post', url: '/api/stk/cost-adjusts/a1/submit' })
  })

  it('新建 / 更新 / 删除载荷只传目标均价，不传旧均价与差额', async () => {
    const payload: SaveCostAdjustPayload = {
      reasonCode: 1,
      warehouseId: null,
      remark: null,
      lines: [{ itemId: 'i1', warehouseId: 'w1', newAvgCost: 9.8 }],
    }
    requestMock.mockResolvedValueOnce(ok({}))
    await costAdjustApi.create(payload)
    expect(lastConfig()).toMatchObject({ method: 'post', url: '/api/stk/cost-adjusts' })
    expect(lastConfig().data).toEqual(payload)
    const line = (lastConfig().data as SaveCostAdjustPayload).lines[0]
    expect(Object.keys(line)).toEqual(['itemId', 'warehouseId', 'newAvgCost'])

    requestMock.mockResolvedValueOnce(ok({}))
    await costAdjustApi.update('a1', payload)
    expect(lastConfig()).toMatchObject({ method: 'put', url: '/api/stk/cost-adjusts/a1' })

    requestMock.mockResolvedValueOnce(ok(true))
    await costAdjustApi.remove('a1')
    expect(lastConfig()).toMatchObject({ method: 'delete', url: '/api/stk/cost-adjusts/a1' })
  })
})

describe('库存台账 / 流水 / 批次端点', () => {
  it('台账按商品 / 仓库过滤，空值不上送', async () => {
    requestMock.mockResolvedValueOnce(ok([]))
    await stockApi.ledger()
    expect(lastConfig()).toMatchObject({ method: 'get', url: '/api/stk/ledger' })
    expect(lastConfig().params).toEqual({})

    requestMock.mockResolvedValueOnce(ok([]))
    await stockApi.ledger({ itemId: 'i1' })
    expect(lastConfig().params).toEqual({ itemId: 'i1' })
  })

  it('流水组装来源类型 / 时间区间 / take', async () => {
    requestMock.mockResolvedValueOnce(ok([]))
    await stockApi.txns({
      itemId: 'i1',
      warehouseId: 'w1',
      sourceType: 7,
      from: '2026-10-01T00:00:00.000Z',
      to: '2026-10-31T23:59:59.000Z',
      take: 200,
    })
    expect(lastConfig()).toMatchObject({ method: 'get', url: '/api/stk/txns' })
    expect(lastConfig().params).toEqual({
      itemId: 'i1',
      warehouseId: 'w1',
      sourceType: 7,
      from: '2026-10-01T00:00:00.000Z',
      to: '2026-10-31T23:59:59.000Z',
      take: 200,
    })
  })

  it('批次库存必须携带商品与仓库', async () => {
    requestMock.mockResolvedValueOnce(ok([]))
    await stockApi.batches({ itemId: 'i1', warehouseId: 'w1' })
    expect(lastConfig()).toMatchObject({ method: 'get', url: '/api/stk/batches' })
    expect(lastConfig().params).toEqual({ itemId: 'i1', warehouseId: 'w1' })
  })
})
