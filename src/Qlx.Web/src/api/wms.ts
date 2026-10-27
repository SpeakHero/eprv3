/*
 * 所属公司：四川淇利馨集团有限公司
 * 描述：物流管理（Wms 模块）API 封装——仓库 / 库区 / 库位 + 入库 / 出库 / 调拨 / 在途 / 盘点 / 盘盈亏 / 预警 / 命令
 * 说明：
 *   1. 全部经 api/adapters/request.ts 统一客户端（共享 Token 注入 + ApiResponse 解包 + 错误透传）；
 *   2. 端点严格对齐 Qlx.Modules.Wms.Controllers 各 Controller（路径 / 方法 / 查询参数）；
 *   3. 所有 Id 参数声明为 string（Hashids 混淆串）；新建入参不含编码/单号（N-1）。
 */

import { apiRequest } from '@/api/adapters/request'
import type {
  CheckStockOutTaskPayload,
  CompleteStockInTaskPayload,
  CreateInventoryAdjustPayload,
  CreateLocationPayload,
  CreateStockInTaskPayload,
  CreateStockOutTaskPayload,
  CreateStockTakePlanPayload,
  CreateStockTransferPayload,
  CreateWarehousePayload,
  CreateZonePayload,
  HandleInventoryAlertPayload,
  InTransitStockRow,
  InventoryAdjustDetailRow,
  InventoryAdjustRow,
  InventoryAlertRow,
  LocationRow,
  PickStockOutTaskPayload,
  ReceiveStockTransferPayload,
  SaveStockTakeCountsPayload,
  ShipStockTransferPayload,
  StockCommandRow,
  StockInTaskDetailRow,
  StockInTaskRow,
  StockOutCompleteResultRow,
  StockOutTaskDetailRow,
  StockOutTaskRow,
  StockTakeDetailRow,
  StockTakeFinishResultRow,
  StockTakePlanRow,
  StockTakeRow,
  StockTransferDetailRow,
  StockTransferRow,
  UpdateLocationPayload,
  UpdateWarehousePayload,
  UpdateZonePayload,
  WarehouseRow,
  ZoneRow,
} from '@/types/wms'

/** 按查询对象构建参数（空值不上送）。 */
function toParams(query: Record<string, unknown>): Record<string, unknown> {
  const params: Record<string, unknown> = {}
  for (const [key, value] of Object.entries(query)) {
    if (value === undefined || value === null || value === '') {
      continue
    }
    params[key] = value
  }
  return params
}

const WAREHOUSES = '/api/wms/warehouses'

export const warehouseApi = {
  /** 仓库扁平列表。 */
  list(): Promise<WarehouseRow[]> {
    return apiRequest<WarehouseRow[]>({ method: 'get', url: WAREHOUSES })
  },
  /** 仓库详情。 */
  get(id: string): Promise<WarehouseRow> {
    return apiRequest<WarehouseRow>({
      method: 'get',
      url: `${WAREHOUSES}/${encodeURIComponent(id)}`,
    })
  },
  /** 新建仓库（不含编码，N-1）。 */
  create(payload: CreateWarehousePayload): Promise<WarehouseRow> {
    return apiRequest<WarehouseRow>({ method: 'post', url: WAREHOUSES, data: payload })
  },
  /** 更新仓库（编码不可修改）。 */
  update(id: string, payload: UpdateWarehousePayload): Promise<WarehouseRow> {
    return apiRequest<WarehouseRow>({
      method: 'put',
      url: `${WAREHOUSES}/${encodeURIComponent(id)}`,
      data: payload,
    })
  },
  /** 启用 / 停用仓库。 */
  toggle(id: string): Promise<WarehouseRow> {
    return apiRequest<WarehouseRow>({
      method: 'post',
      url: `${WAREHOUSES}/${encodeURIComponent(id)}/toggle`,
    })
  },
  /** 删除仓库（软删）。 */
  remove(id: string): Promise<unknown> {
    return apiRequest<unknown>({
      method: 'delete',
      url: `${WAREHOUSES}/${encodeURIComponent(id)}`,
    })
  },

  /* ---- 库区 ---- */

  /** 按仓库查询库区列表。 */
  listZones(id: string): Promise<ZoneRow[]> {
    return apiRequest<ZoneRow[]>({
      method: 'get',
      url: `${WAREHOUSES}/${encodeURIComponent(id)}/zones`,
    })
  },
  /** 新建库区。 */
  createZone(id: string, payload: CreateZonePayload): Promise<ZoneRow> {
    return apiRequest<ZoneRow>({
      method: 'post',
      url: `${WAREHOUSES}/${encodeURIComponent(id)}/zones`,
      data: payload,
    })
  },
  /** 更新库区（编码不可修改）。 */
  updateZone(zoneId: string, payload: UpdateZonePayload): Promise<ZoneRow> {
    return apiRequest<ZoneRow>({
      method: 'put',
      url: `${WAREHOUSES}/zones/${encodeURIComponent(zoneId)}`,
      data: payload,
    })
  },
  /** 启用 / 停用库区。 */
  toggleZone(zoneId: string): Promise<ZoneRow> {
    return apiRequest<ZoneRow>({
      method: 'post',
      url: `${WAREHOUSES}/zones/${encodeURIComponent(zoneId)}/toggle`,
    })
  },
  /** 删除库区（存在库位时后端拒绝）。 */
  removeZone(zoneId: string): Promise<null> {
    return apiRequest<null>({
      method: 'delete',
      url: `${WAREHOUSES}/zones/${encodeURIComponent(zoneId)}`,
    })
  },

  /* ---- 库位 ---- */

  /** 按仓库查询库位列表。 */
  listLocations(id: string): Promise<LocationRow[]> {
    return apiRequest<LocationRow[]>({
      method: 'get',
      url: `${WAREHOUSES}/${encodeURIComponent(id)}/locations`,
    })
  },
  /** 新建库位（库区 Id 为查询参数，后端契约）。 */
  createLocation(id: string, zoneId: string, payload: CreateLocationPayload): Promise<LocationRow> {
    return apiRequest<LocationRow>({
      method: 'post',
      url: `${WAREHOUSES}/${encodeURIComponent(id)}/locations`,
      params: { zoneId },
      data: payload,
    })
  },
  /** 更新库位（编码不可修改）。 */
  updateLocation(locationId: string, payload: UpdateLocationPayload): Promise<LocationRow> {
    return apiRequest<LocationRow>({
      method: 'put',
      url: `${WAREHOUSES}/locations/${encodeURIComponent(locationId)}`,
      data: payload,
    })
  },
  /** 启用 / 停用库位。 */
  toggleLocation(locationId: string): Promise<LocationRow> {
    return apiRequest<LocationRow>({
      method: 'post',
      url: `${WAREHOUSES}/locations/${encodeURIComponent(locationId)}/toggle`,
    })
  },
  /** 删除库位（存在未完成作业单引用时后端拒绝）。 */
  removeLocation(locationId: string): Promise<null> {
    return apiRequest<null>({
      method: 'delete',
      url: `${WAREHOUSES}/locations/${encodeURIComponent(locationId)}`,
    })
  },
}

/* ============================================================
 * 入库作业（/api/wms/stock-in-tasks）
 * ============================================================ */

const STOCK_IN_TASKS = '/api/wms/stock-in-tasks'

export const stockInApi = {
  /** 入库作业扁平列表（状态 / 仓库 / 来源类型筛选）。 */
  list(
    params: { status?: number; warehouseId?: string; sourceType?: number } = {},
  ): Promise<StockInTaskRow[]> {
    return apiRequest<StockInTaskRow[]>({
      method: 'get',
      url: STOCK_IN_TASKS,
      params: toParams({
        status: params.status,
        warehouseId: params.warehouseId,
        sourceType: params.sourceType,
      }),
    })
  },
  /** 入库作业详情（头 + 明细）。 */
  get(id: string): Promise<StockInTaskDetailRow> {
    return apiRequest<StockInTaskDetailRow>({
      method: 'get',
      url: `${STOCK_IN_TASKS}/${encodeURIComponent(id)}`,
    })
  },
  /** 新建入库作业（不含单号，N-1）。 */
  create(payload: CreateStockInTaskPayload): Promise<StockInTaskDetailRow> {
    return apiRequest<StockInTaskDetailRow>({
      method: 'post',
      url: STOCK_IN_TASKS,
      data: payload,
    })
  },
  /** 开始作业（待作业 → 作业中）。 */
  start(id: string): Promise<StockInTaskRow> {
    return apiRequest<StockInTaskRow>({
      method: 'post',
      url: `${STOCK_IN_TASKS}/${encodeURIComponent(id)}/start`,
    })
  },
  /** 完成作业（提交清点 / 确认数量，后端写台账）。 */
  complete(id: string, payload: CompleteStockInTaskPayload): Promise<StockInTaskDetailRow> {
    return apiRequest<StockInTaskDetailRow>({
      method: 'post',
      url: `${STOCK_IN_TASKS}/${encodeURIComponent(id)}/complete`,
      data: payload,
    })
  },
  /** 取消作业。 */
  cancel(id: string): Promise<StockInTaskRow> {
    return apiRequest<StockInTaskRow>({
      method: 'post',
      url: `${STOCK_IN_TASKS}/${encodeURIComponent(id)}/cancel`,
    })
  },
}

/* ============================================================
 * 出库作业（/api/wms/stock-out-tasks）
 * ============================================================ */

const STOCK_OUT_TASKS = '/api/wms/stock-out-tasks'

export const stockOutApi = {
  /** 出库作业扁平列表（状态 / 仓库 / 来源类型筛选）。 */
  list(
    params: { status?: number; warehouseId?: string; sourceType?: number } = {},
  ): Promise<StockOutTaskRow[]> {
    return apiRequest<StockOutTaskRow[]>({
      method: 'get',
      url: STOCK_OUT_TASKS,
      params: toParams({
        status: params.status,
        warehouseId: params.warehouseId,
        sourceType: params.sourceType,
      }),
    })
  },
  /** 出库作业详情（头 + 明细 + FEFO 拣货建议）。 */
  get(id: string): Promise<StockOutTaskDetailRow> {
    return apiRequest<StockOutTaskDetailRow>({
      method: 'get',
      url: `${STOCK_OUT_TASKS}/${encodeURIComponent(id)}`,
    })
  },
  /** 新建出库作业（不含单号，N-1）。 */
  create(payload: CreateStockOutTaskPayload): Promise<StockOutTaskDetailRow> {
    return apiRequest<StockOutTaskDetailRow>({
      method: 'post',
      url: STOCK_OUT_TASKS,
      data: payload,
    })
  },
  /** 提交拣货（待作业 → 拣货中）。 */
  pick(id: string, payload: PickStockOutTaskPayload): Promise<StockOutTaskDetailRow> {
    return apiRequest<StockOutTaskDetailRow>({
      method: 'post',
      url: `${STOCK_OUT_TASKS}/${encodeURIComponent(id)}/pick`,
      data: payload,
    })
  },
  /** 提交复核（拣货中 → 待复核）。 */
  check(id: string, payload: CheckStockOutTaskPayload): Promise<StockOutTaskDetailRow> {
    return apiRequest<StockOutTaskDetailRow>({
      method: 'post',
      url: `${STOCK_OUT_TASKS}/${encodeURIComponent(id)}/check`,
      data: payload,
    })
  },
  /** 完成出库（大额返回 approvalRequired=true 转审批挂起）。 */
  complete(id: string): Promise<StockOutCompleteResultRow> {
    return apiRequest<StockOutCompleteResultRow>({
      method: 'post',
      url: `${STOCK_OUT_TASKS}/${encodeURIComponent(id)}/complete`,
    })
  },
  /** 取消作业。 */
  cancel(id: string): Promise<StockOutTaskRow> {
    return apiRequest<StockOutTaskRow>({
      method: 'post',
      url: `${STOCK_OUT_TASKS}/${encodeURIComponent(id)}/cancel`,
    })
  },
}

/* ============================================================
 * 调拨与在途（/api/wms/transfers）
 * ============================================================ */

const TRANSFERS = '/api/wms/transfers'

export const transferApi = {
  /** 调拨单扁平列表（状态 / 仓库筛选）。 */
  list(params: { status?: number; warehouseId?: string } = {}): Promise<StockTransferRow[]> {
    return apiRequest<StockTransferRow[]>({
      method: 'get',
      url: TRANSFERS,
      params: toParams({ status: params.status, warehouseId: params.warehouseId }),
    })
  },
  /** 在途库存扁平列表（超期标记由前端按 expectArrive 计算，B-2）。 */
  inTransit(params: { status?: number; warehouseId?: string } = {}): Promise<InTransitStockRow[]> {
    return apiRequest<InTransitStockRow[]>({
      method: 'get',
      url: `${TRANSFERS}/in-transit`,
      params: toParams({ status: params.status, warehouseId: params.warehouseId }),
    })
  },
  /** 调拨单详情（头 + 明细）。 */
  get(id: string): Promise<StockTransferDetailRow> {
    return apiRequest<StockTransferDetailRow>({
      method: 'get',
      url: `${TRANSFERS}/${encodeURIComponent(id)}`,
    })
  },
  /** 新建调拨单（转入审批，不含单号，N-1）。 */
  create(payload: CreateStockTransferPayload): Promise<StockTransferDetailRow> {
    return apiRequest<StockTransferDetailRow>({
      method: 'post',
      url: TRANSFERS,
      data: payload,
    })
  },
  /** 发货（已生效 → 调出已发货，后端写调出仓台账 + 在途）。 */
  ship(id: string, payload: ShipStockTransferPayload): Promise<StockTransferDetailRow> {
    return apiRequest<StockTransferDetailRow>({
      method: 'post',
      url: `${TRANSFERS}/${encodeURIComponent(id)}/ship`,
      data: payload,
    })
  },
  /** 收货（调出已发货 → 调入已收货，后端写入调仓台账）。 */
  receive(id: string, payload: ReceiveStockTransferPayload): Promise<StockTransferDetailRow> {
    return apiRequest<StockTransferDetailRow>({
      method: 'post',
      url: `${TRANSFERS}/${encodeURIComponent(id)}/receive`,
      data: payload,
    })
  },
  /** 作废调拨单（未发货可作废）。 */
  void(id: string): Promise<StockTransferRow> {
    return apiRequest<StockTransferRow>({
      method: 'post',
      url: `${TRANSFERS}/${encodeURIComponent(id)}/void`,
    })
  },
}

/* ============================================================
 * 盘点计划（/api/wms/stock-take-plans）
 * ============================================================ */

const STOCK_TAKE_PLANS = '/api/wms/stock-take-plans'

export const stockTakePlanApi = {
  /** 盘点计划扁平列表（范围类型 / 仓库筛选）。 */
  list(
    params: { scopeType?: number; warehouseId?: string } = {},
  ): Promise<StockTakePlanRow[]> {
    return apiRequest<StockTakePlanRow[]>({
      method: 'get',
      url: STOCK_TAKE_PLANS,
      params: toParams({ scopeType: params.scopeType, warehouseId: params.warehouseId }),
    })
  },
  /** 新建盘点计划（不含计划号，N-1）。 */
  create(payload: CreateStockTakePlanPayload): Promise<StockTakePlanRow> {
    return apiRequest<StockTakePlanRow>({
      method: 'post',
      url: STOCK_TAKE_PLANS,
      data: payload,
    })
  },
  /** 生成盘点单（按计划范围取库存快照，返回盘点单详情）。 */
  generate(id: string): Promise<StockTakeDetailRow> {
    return apiRequest<StockTakeDetailRow>({
      method: 'post',
      url: `${STOCK_TAKE_PLANS}/${encodeURIComponent(id)}/generate`,
    })
  },
}

/* ============================================================
 * 盘点单（/api/wms/stock-takes）
 * ============================================================ */

const STOCK_TAKES = '/api/wms/stock-takes'

export const stockTakeApi = {
  /** 盘点单扁平列表（状态 / 仓库筛选）。 */
  list(params: { status?: number; warehouseId?: string } = {}): Promise<StockTakeRow[]> {
    return apiRequest<StockTakeRow[]>({
      method: 'get',
      url: STOCK_TAKES,
      params: toParams({ status: params.status, warehouseId: params.warehouseId }),
    })
  },
  /** 盘点单详情（头 + 明细；盲盘时 bookQty / diffQty 为 null）。 */
  get(id: string): Promise<StockTakeDetailRow> {
    return apiRequest<StockTakeDetailRow>({
      method: 'get',
      url: `${STOCK_TAKES}/${encodeURIComponent(id)}`,
    })
  },
  /** 开始盘点（待盘点 → 初盘中 / 复盘中）。 */
  start(id: string): Promise<StockTakeRow> {
    return apiRequest<StockTakeRow>({
      method: 'post',
      url: `${STOCK_TAKES}/${encodeURIComponent(id)}/start`,
    })
  },
  /** 保存实盘数量。 */
  saveCounts(id: string, payload: SaveStockTakeCountsPayload): Promise<StockTakeDetailRow> {
    return apiRequest<StockTakeDetailRow>({
      method: 'post',
      url: `${STOCK_TAKES}/${encodeURIComponent(id)}/save-counts`,
      data: payload,
    })
  },
  /** 完成盘点（初盘有差异时回传复盘单 Id）。 */
  finish(id: string): Promise<StockTakeFinishResultRow> {
    return apiRequest<StockTakeFinishResultRow>({
      method: 'post',
      url: `${STOCK_TAKES}/${encodeURIComponent(id)}/finish`,
    })
  },
  /** 取消盘点。 */
  cancel(id: string): Promise<StockTakeRow> {
    return apiRequest<StockTakeRow>({
      method: 'post',
      url: `${STOCK_TAKES}/${encodeURIComponent(id)}/cancel`,
    })
  },
}

/* ============================================================
 * 盘盈盘亏（/api/wms/inventory-adjusts）
 * ============================================================ */

const INVENTORY_ADJUSTS = '/api/wms/inventory-adjusts'

export const inventoryAdjustApi = {
  /** 盘盈盘亏单扁平列表（类型 / 状态 / 仓库筛选）。 */
  list(
    params: { type?: number; status?: number; warehouseId?: string } = {},
  ): Promise<InventoryAdjustRow[]> {
    return apiRequest<InventoryAdjustRow[]>({
      method: 'get',
      url: INVENTORY_ADJUSTS,
      params: toParams({
        type: params.type,
        status: params.status,
        warehouseId: params.warehouseId,
      }),
    })
  },
  /** 盘盈盘亏单详情（头 + 明细）。 */
  get(id: string): Promise<InventoryAdjustDetailRow> {
    return apiRequest<InventoryAdjustDetailRow>({
      method: 'get',
      url: `${INVENTORY_ADJUSTS}/${encodeURIComponent(id)}`,
    })
  },
  /** 新建盘盈盘亏草稿（不含单号，N-1；金额由后端估算）。 */
  create(payload: CreateInventoryAdjustPayload): Promise<InventoryAdjustDetailRow> {
    return apiRequest<InventoryAdjustDetailRow>({
      method: 'post',
      url: INVENTORY_ADJUSTS,
      data: payload,
    })
  },
  /** 提交审批（草稿 → 待审批）。 */
  submit(id: string): Promise<InventoryAdjustRow> {
    return apiRequest<InventoryAdjustRow>({
      method: 'post',
      url: `${INVENTORY_ADJUSTS}/${encodeURIComponent(id)}/submit`,
    })
  },
}

/* ============================================================
 * 库存预警（/api/wms/inventory-alerts）
 * ============================================================ */

const INVENTORY_ALERTS = '/api/wms/inventory-alerts'

export const inventoryAlertApi = {
  /** 预警扁平列表（类型 / 状态 / 仓库筛选）。 */
  list(
    params: { alertType?: number; status?: number; warehouseId?: string } = {},
  ): Promise<InventoryAlertRow[]> {
    return apiRequest<InventoryAlertRow[]>({
      method: 'get',
      url: INVENTORY_ALERTS,
      params: toParams({
        alertType: params.alertType,
        status: params.status,
        warehouseId: params.warehouseId,
      }),
    })
  },
  /** 处理预警（待处理 → 已处理，处理说明必填）。 */
  handle(id: string, payload: HandleInventoryAlertPayload): Promise<InventoryAlertRow> {
    return apiRequest<InventoryAlertRow>({
      method: 'post',
      url: `${INVENTORY_ALERTS}/${encodeURIComponent(id)}/handle`,
      data: payload,
    })
  },
  /** 忽略预警（待处理 → 已忽略）。 */
  ignore(id: string): Promise<InventoryAlertRow> {
    return apiRequest<InventoryAlertRow>({
      method: 'post',
      url: `${INVENTORY_ALERTS}/${encodeURIComponent(id)}/ignore`,
    })
  },
}

/* ============================================================
 * 库存命令（/api/wms/stock-commands）
 * ============================================================ */

const STOCK_COMMANDS = '/api/wms/stock-commands'

export const stockCommandApi = {
  /** 人工重推（仅分发失败终态可重推，重复 409 WMS_INVALID_STATE）。 */
  retry(id: string): Promise<StockCommandRow> {
    return apiRequest<StockCommandRow>({
      method: 'post',
      url: `${STOCK_COMMANDS}/${encodeURIComponent(id)}/retry`,
    })
  },
}
