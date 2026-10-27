/*
 * 所属公司：四川淇利馨集团有限公司
 * 描述：物流管理（Wms 2B）API 封装单元测试——入库 / 出库 / 调拨 / 在途 / 盘点 / 盘盈亏 / 预警 / 命令端点与载荷
 */

import { beforeEach, describe, expect, it, vi } from 'vitest'
import type { AxiosRequestConfig, AxiosResponse } from 'axios'

vi.mock('@/core/http', () => ({
  http: { request: vi.fn() },
}))

import { http } from '@/core/http'
import {
  inventoryAdjustApi,
  inventoryAlertApi,
  stockCommandApi,
  stockInApi,
  stockOutApi,
  stockTakeApi,
  stockTakePlanApi,
  transferApi,
} from '../wms'
import type {
  CheckStockOutTaskPayload,
  CompleteStockInTaskPayload,
  CreateInventoryAdjustPayload,
  CreateStockInTaskPayload,
  CreateStockOutTaskPayload,
  CreateStockTakePlanPayload,
  CreateStockTransferPayload,
  PickStockOutTaskPayload,
  ReceiveStockTransferPayload,
  SaveStockTakeCountsPayload,
  ShipStockTransferPayload,
} from '@/types/wms'

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

describe('入库作业端点', () => {
  it('列表仅上送非空筛选参数，详情按 Id 编码', async () => {
    requestMock.mockResolvedValueOnce(ok([]))
    await stockInApi.list({ status: 1, warehouseId: 'w1', sourceType: undefined })
    expect(lastConfig()).toMatchObject({ method: 'get', url: '/api/wms/stock-in-tasks' })
    expect(lastConfig().params).toEqual({ status: 1, warehouseId: 'w1' })

    requestMock.mockResolvedValueOnce(ok({}))
    await stockInApi.get('t 1')
    expect(lastConfig()).toMatchObject({ method: 'get', url: '/api/wms/stock-in-tasks/t%201' })
  })

  it('新建载荷不含单号（N-1），开始 / 取消无载荷', async () => {
    const payload: CreateStockInTaskPayload = {
      sourceType: 1,
      sourceDocNo: 'PO-001',
      warehouseId: 'w1',
      remark: null,
      lines: [{ itemId: 'i1', expectQty: 10, locationId: null, batchNo: null, unitCost: 8 }],
    }
    requestMock.mockResolvedValueOnce(ok({}))
    await stockInApi.create(payload)
    expect(lastConfig()).toMatchObject({ method: 'post', url: '/api/wms/stock-in-tasks' })
    expect(lastConfig().data).toEqual(payload)
    expect(Object.keys(lastConfig().data as object)).not.toContain('taskNo')

    requestMock.mockResolvedValueOnce(ok({}))
    await stockInApi.start('t1')
    expect(lastConfig()).toMatchObject({ method: 'post', url: '/api/wms/stock-in-tasks/t1/start' })

    requestMock.mockResolvedValueOnce(ok({}))
    await stockInApi.cancel('t1')
    expect(lastConfig()).toMatchObject({ method: 'post', url: '/api/wms/stock-in-tasks/t1/cancel' })
  })

  it('完成作业提交清点 / 确认明细', async () => {
    const payload: CompleteStockInTaskPayload = {
      lines: [
        { lineId: 'l1', countQty: 10, confirmQty: 10, locationId: 'loc1', batchNo: 'B1', unitCost: 8 },
      ],
    }
    requestMock.mockResolvedValueOnce(ok({}))
    await stockInApi.complete('t1', payload)
    expect(lastConfig()).toMatchObject({
      method: 'post',
      url: '/api/wms/stock-in-tasks/t1/complete',
    })
    expect(lastConfig().data).toEqual(payload)
  })
})

describe('出库作业端点', () => {
  it('列表 / 详情 / 新建路径对齐', async () => {
    requestMock.mockResolvedValueOnce(ok([]))
    await stockOutApi.list({ status: 2, warehouseId: 'w1', sourceType: 1 })
    expect(lastConfig()).toMatchObject({ method: 'get', url: '/api/wms/stock-out-tasks' })
    expect(lastConfig().params).toEqual({ status: 2, warehouseId: 'w1', sourceType: 1 })

    requestMock.mockResolvedValueOnce(ok({}))
    await stockOutApi.get('t1')
    expect(lastConfig()).toMatchObject({ method: 'get', url: '/api/wms/stock-out-tasks/t1' })

    const payload: CreateStockOutTaskPayload = {
      sourceType: 1,
      sourceDocNo: 'SO-001',
      warehouseId: 'w1',
      remark: null,
      lines: [{ itemId: 'i1', applyQty: 3, locationId: null, batchNo: null }],
    }
    requestMock.mockResolvedValueOnce(ok({}))
    await stockOutApi.create(payload)
    expect(lastConfig()).toMatchObject({ method: 'post', url: '/api/wms/stock-out-tasks' })
    expect(Object.keys(lastConfig().data as object)).not.toContain('taskNo')
  })

  it('拣货 / 复核 / 完成 / 取消路径与载荷', async () => {
    const pick: PickStockOutTaskPayload = {
      lines: [{ lineId: 'l1', pickQty: 3, locationId: 'loc1', batchNo: 'B1' }],
    }
    requestMock.mockResolvedValueOnce(ok({}))
    await stockOutApi.pick('t1', pick)
    expect(lastConfig()).toMatchObject({ method: 'post', url: '/api/wms/stock-out-tasks/t1/pick' })
    expect(lastConfig().data).toEqual(pick)

    const check: CheckStockOutTaskPayload = {
      handoverTo: 'u1',
      lines: [{ lineId: 'l1', checkQty: 3 }],
    }
    requestMock.mockResolvedValueOnce(ok({}))
    await stockOutApi.check('t1', check)
    expect(lastConfig()).toMatchObject({ method: 'post', url: '/api/wms/stock-out-tasks/t1/check' })
    expect(lastConfig().data).toEqual(check)

    requestMock.mockResolvedValueOnce(ok({ approvalRequired: false }))
    await stockOutApi.complete('t1')
    expect(lastConfig()).toMatchObject({
      method: 'post',
      url: '/api/wms/stock-out-tasks/t1/complete',
    })

    requestMock.mockResolvedValueOnce(ok({}))
    await stockOutApi.cancel('t1')
    expect(lastConfig()).toMatchObject({ method: 'post', url: '/api/wms/stock-out-tasks/t1/cancel' })
  })
})

describe('调拨与在途端点', () => {
  it('列表 / 在途 / 详情 / 新建路径', async () => {
    requestMock.mockResolvedValueOnce(ok([]))
    await transferApi.list({ status: 2, warehouseId: 'w1' })
    expect(lastConfig()).toMatchObject({ method: 'get', url: '/api/wms/transfers' })
    expect(lastConfig().params).toEqual({ status: 2, warehouseId: 'w1' })

    requestMock.mockResolvedValueOnce(ok([]))
    await transferApi.inTransit({ status: 1 })
    expect(lastConfig()).toMatchObject({ method: 'get', url: '/api/wms/transfers/in-transit' })
    expect(lastConfig().params).toEqual({ status: 1 })

    requestMock.mockResolvedValueOnce(ok({}))
    await transferApi.get('tf1')
    expect(lastConfig()).toMatchObject({ method: 'get', url: '/api/wms/transfers/tf1' })

    const payload: CreateStockTransferPayload = {
      fromWarehouseId: 'w1',
      toWarehouseId: 'w2',
      inTransitDays: 3,
      remark: null,
      lines: [{ itemId: 'i1', qty: 5, batchNo: null }],
    }
    requestMock.mockResolvedValueOnce(ok({}))
    await transferApi.create(payload)
    expect(lastConfig()).toMatchObject({ method: 'post', url: '/api/wms/transfers' })
    expect(Object.keys(lastConfig().data as object)).not.toContain('transferNo')
  })

  it('发货 / 收货 / 作废路径与载荷', async () => {
    const ship: ShipStockTransferPayload = { lines: [{ lineId: 'l1', shipQty: 5 }] }
    requestMock.mockResolvedValueOnce(ok({}))
    await transferApi.ship('tf1', ship)
    expect(lastConfig()).toMatchObject({ method: 'post', url: '/api/wms/transfers/tf1/ship' })
    expect(lastConfig().data).toEqual(ship)

    const receive: ReceiveStockTransferPayload = {
      lines: [{ lineId: 'l1', receiveQty: 4, diffNote: '少 1 件' }],
    }
    requestMock.mockResolvedValueOnce(ok({}))
    await transferApi.receive('tf1', receive)
    expect(lastConfig()).toMatchObject({ method: 'post', url: '/api/wms/transfers/tf1/receive' })
    expect(lastConfig().data).toEqual(receive)

    requestMock.mockResolvedValueOnce(ok({}))
    await transferApi.void('tf1')
    expect(lastConfig()).toMatchObject({ method: 'post', url: '/api/wms/transfers/tf1/void' })
  })
})

describe('盘点计划与盘点单端点', () => {
  it('计划列表 / 新建 / 生成盘点单', async () => {
    requestMock.mockResolvedValueOnce(ok([]))
    await stockTakePlanApi.list({ scopeType: 1, warehouseId: 'w1' })
    expect(lastConfig()).toMatchObject({ method: 'get', url: '/api/wms/stock-take-plans' })
    expect(lastConfig().params).toEqual({ scopeType: 1, warehouseId: 'w1' })

    const payload: CreateStockTakePlanPayload = {
      planDate: '2026-10-26',
      scopeType: 2,
      warehouseId: 'w1',
      categoryId: 'c1',
      locationId: null,
      isBlind: true,
      remark: null,
    }
    requestMock.mockResolvedValueOnce(ok({}))
    await stockTakePlanApi.create(payload)
    expect(lastConfig()).toMatchObject({ method: 'post', url: '/api/wms/stock-take-plans' })
    expect(Object.keys(lastConfig().data as object)).not.toContain('planNo')

    requestMock.mockResolvedValueOnce(ok({}))
    await stockTakePlanApi.generate('p1')
    expect(lastConfig()).toMatchObject({
      method: 'post',
      url: '/api/wms/stock-take-plans/p1/generate',
    })
  })

  it('盘点单列表 / 详情 / 开始 / 保存实盘 / 完成 / 取消', async () => {
    requestMock.mockResolvedValueOnce(ok([]))
    await stockTakeApi.list({ status: 2, warehouseId: 'w1' })
    expect(lastConfig()).toMatchObject({ method: 'get', url: '/api/wms/stock-takes' })
    expect(lastConfig().params).toEqual({ status: 2, warehouseId: 'w1' })

    requestMock.mockResolvedValueOnce(ok({}))
    await stockTakeApi.get('k1')
    expect(lastConfig()).toMatchObject({ method: 'get', url: '/api/wms/stock-takes/k1' })

    requestMock.mockResolvedValueOnce(ok({}))
    await stockTakeApi.start('k1')
    expect(lastConfig()).toMatchObject({ method: 'post', url: '/api/wms/stock-takes/k1/start' })

    const counts: SaveStockTakeCountsPayload = {
      lines: [{ lineId: 'l1', actualQty: 9, diffReason: null }],
    }
    requestMock.mockResolvedValueOnce(ok({}))
    await stockTakeApi.saveCounts('k1', counts)
    expect(lastConfig()).toMatchObject({
      method: 'post',
      url: '/api/wms/stock-takes/k1/save-counts',
    })
    expect(lastConfig().data).toEqual(counts)

    requestMock.mockResolvedValueOnce(ok({}))
    await stockTakeApi.finish('k1')
    expect(lastConfig()).toMatchObject({ method: 'post', url: '/api/wms/stock-takes/k1/finish' })

    requestMock.mockResolvedValueOnce(ok({}))
    await stockTakeApi.cancel('k1')
    expect(lastConfig()).toMatchObject({ method: 'post', url: '/api/wms/stock-takes/k1/cancel' })
  })
})

describe('盘盈盘亏端点', () => {
  it('列表 / 详情 / 新建 / 提交审批', async () => {
    requestMock.mockResolvedValueOnce(ok([]))
    await inventoryAdjustApi.list({ type: 2, status: 1, warehouseId: 'w1' })
    expect(lastConfig()).toMatchObject({ method: 'get', url: '/api/wms/inventory-adjusts' })
    expect(lastConfig().params).toEqual({ type: 2, status: 1, warehouseId: 'w1' })

    requestMock.mockResolvedValueOnce(ok({}))
    await inventoryAdjustApi.get('a1')
    expect(lastConfig()).toMatchObject({ method: 'get', url: '/api/wms/inventory-adjusts/a1' })

    const payload: CreateInventoryAdjustPayload = {
      type: 1,
      warehouseId: 'w1',
      reason: '盘点差异',
      remark: null,
      lines: [{ itemId: 'i1', batchNo: null, qty: 2, remark: null }],
    }
    requestMock.mockResolvedValueOnce(ok({}))
    await inventoryAdjustApi.create(payload)
    expect(lastConfig()).toMatchObject({ method: 'post', url: '/api/wms/inventory-adjusts' })
    expect(Object.keys(lastConfig().data as object)).not.toContain('adjustNo')

    requestMock.mockResolvedValueOnce(ok({}))
    await inventoryAdjustApi.submit('a1')
    expect(lastConfig()).toMatchObject({
      method: 'post',
      url: '/api/wms/inventory-adjusts/a1/submit',
    })
  })
})

describe('库存预警端点', () => {
  it('列表筛选 / 处理 / 忽略路径', async () => {
    requestMock.mockResolvedValueOnce(ok([]))
    await inventoryAlertApi.list({ alertType: 3, status: 1, warehouseId: 'w1' })
    expect(lastConfig()).toMatchObject({ method: 'get', url: '/api/wms/inventory-alerts' })
    expect(lastConfig().params).toEqual({ alertType: 3, status: 1, warehouseId: 'w1' })

    requestMock.mockResolvedValueOnce(ok({}))
    await inventoryAlertApi.handle('al1', { handledNote: '已补货' })
    expect(lastConfig()).toMatchObject({
      method: 'post',
      url: '/api/wms/inventory-alerts/al1/handle',
    })
    expect(lastConfig().data).toEqual({ handledNote: '已补货' })

    requestMock.mockResolvedValueOnce(ok({}))
    await inventoryAlertApi.ignore('al1')
    expect(lastConfig()).toMatchObject({
      method: 'post',
      url: '/api/wms/inventory-alerts/al1/ignore',
    })
  })
})

describe('库存命令端点', () => {
  it('重推按 Id 编码提交', async () => {
    requestMock.mockResolvedValueOnce(ok({}))
    await stockCommandApi.retry('c 1')
    expect(lastConfig()).toMatchObject({ method: 'post', url: '/api/wms/stock-commands/c%201/retry' })
  })
})
