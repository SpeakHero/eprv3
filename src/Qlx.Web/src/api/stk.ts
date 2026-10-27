/*
 * 所属公司：四川淇利馨集团有限公司
 * 描述：商品与库存（Stk 模块）API 封装——商品档案 / 期初建账 / 成本调整 / 库存台账 / 流水 / 批次
 * 说明：
 *   1. 全部经 api/adapters/request.ts 统一客户端（共享 Token 注入 + ApiResponse 解包 + 错误透传）；
 *   2. 端点严格对齐 Qlx.Modules.Stk 各 Controller（路径 / 方法 / 查询参数）；
 *   3. 所有 Id 参数声明为 string（Hashids 混淆串）；新建/更新入参不含编码/单号（N-1）。
 */

import { apiRequest } from '@/api/adapters/request'
import type {
  CostAdjustDetailRow,
  CostAdjustRow,
  CreateItemPayload,
  ItemPriceLogRow,
  ItemRow,
  OpeningBatchDetailRow,
  OpeningBatchRow,
  OpeningWarehouseSummaryRow,
  SaveCostAdjustPayload,
  SaveOpeningPayload,
  StkApprovalRequestRow,
  StockBalanceRow,
  StockBatchRow,
  StockTxnQuery,
  StockTxnRow,
  UpdateItemPayload,
  UpdateItemResult,
} from '@/types/stk'

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

/* ============================================================
 * 商品档案（/api/stk/items）
 * ============================================================ */

const ITEMS = '/api/stk/items'

export const itemApi = {
  /** 商品扁平列表（成本字段按权限裁剪）。 */
  list(): Promise<ItemRow[]> {
    return apiRequest<ItemRow[]>({ method: 'get', url: ITEMS })
  },
  /** 商品详情。 */
  get(id: string): Promise<ItemRow> {
    return apiRequest<ItemRow>({ method: 'get', url: `${ITEMS}/${encodeURIComponent(id)}` })
  },
  /** 商品价格类变更日志。 */
  priceLogs(id: string): Promise<ItemPriceLogRow[]> {
    return apiRequest<ItemPriceLogRow[]>({
      method: 'get',
      url: `${ITEMS}/${encodeURIComponent(id)}/price-logs`,
    })
  },
  /** 提交商品新建申请（审批通过后落库）。 */
  create(payload: CreateItemPayload): Promise<StkApprovalRequestRow> {
    return apiRequest<StkApprovalRequestRow>({ method: 'post', url: ITEMS, data: payload })
  },
  /** 更新商品（关键属性变化转审批挂起）。 */
  update(id: string, payload: UpdateItemPayload): Promise<UpdateItemResult> {
    return apiRequest<UpdateItemResult>({
      method: 'put',
      url: `${ITEMS}/${encodeURIComponent(id)}`,
      data: payload,
    })
  },
  /** 启用 / 停用商品（存在库存时后端拒绝直接停用）。 */
  toggle(id: string): Promise<ItemRow> {
    return apiRequest<ItemRow>({
      method: 'post',
      url: `${ITEMS}/${encodeURIComponent(id)}/toggle`,
    })
  },
  /** 提交商品停用申请。 */
  submitDisable(id: string): Promise<StkApprovalRequestRow> {
    return apiRequest<StkApprovalRequestRow>({
      method: 'post',
      url: `${ITEMS}/${encodeURIComponent(id)}/disable-request`,
    })
  },
}

/* ============================================================
 * 期初建账（/api/stk/openings）
 * ============================================================ */

const OPENINGS = '/api/stk/openings'

export const openingApi = {
  /** 期初批次扁平列表。 */
  list(params: { status?: number; warehouseId?: string } = {}): Promise<OpeningBatchRow[]> {
    return apiRequest<OpeningBatchRow[]>({
      method: 'get',
      url: OPENINGS,
      params: toParams({ status: params.status, warehouseId: params.warehouseId }),
    })
  },
  /** 期初仓库汇总（仅已过账明细）。 */
  summary(): Promise<OpeningWarehouseSummaryRow[]> {
    return apiRequest<OpeningWarehouseSummaryRow[]>({ method: 'get', url: `${OPENINGS}/summary` })
  },
  /** 期初批次详情（表头 + 明细）。 */
  get(id: string): Promise<OpeningBatchDetailRow> {
    return apiRequest<OpeningBatchDetailRow>({
      method: 'get',
      url: `${OPENINGS}/${encodeURIComponent(id)}`,
    })
  },
  /** 新建期初草稿。 */
  create(payload: SaveOpeningPayload): Promise<OpeningBatchDetailRow> {
    return apiRequest<OpeningBatchDetailRow>({ method: 'post', url: OPENINGS, data: payload })
  },
  /** 修改期初草稿（仅草稿可改）。 */
  update(id: string, payload: SaveOpeningPayload): Promise<OpeningBatchDetailRow> {
    return apiRequest<OpeningBatchDetailRow>({
      method: 'put',
      url: `${OPENINGS}/${encodeURIComponent(id)}`,
      data: payload,
    })
  },
  /** 删除期初草稿（仅草稿可删）。 */
  remove(id: string): Promise<unknown> {
    return apiRequest<unknown>({ method: 'delete', url: `${OPENINGS}/${encodeURIComponent(id)}` })
  },
  /** 提交过账审批。 */
  submit(id: string): Promise<StkApprovalRequestRow> {
    return apiRequest<StkApprovalRequestRow>({
      method: 'post',
      url: `${OPENINGS}/${encodeURIComponent(id)}/submit`,
    })
  },
}

/* ============================================================
 * 成本调整（/api/stk/cost-adjusts）
 * ============================================================ */

const COST_ADJUSTS = '/api/stk/cost-adjusts'

export const costAdjustApi = {
  /** 成本调整单扁平列表。 */
  list(params: { status?: number } = {}): Promise<CostAdjustRow[]> {
    return apiRequest<CostAdjustRow[]>({
      method: 'get',
      url: COST_ADJUSTS,
      params: toParams({ status: params.status }),
    })
  },
  /** 成本调整单详情（表头 + 明细）。 */
  get(id: string): Promise<CostAdjustDetailRow> {
    return apiRequest<CostAdjustDetailRow>({
      method: 'get',
      url: `${COST_ADJUSTS}/${encodeURIComponent(id)}`,
    })
  },
  /** 新建成本调整草稿。 */
  create(payload: SaveCostAdjustPayload): Promise<CostAdjustDetailRow> {
    return apiRequest<CostAdjustDetailRow>({ method: 'post', url: COST_ADJUSTS, data: payload })
  },
  /** 修改成本调整草稿（仅草稿可改）。 */
  update(id: string, payload: SaveCostAdjustPayload): Promise<CostAdjustDetailRow> {
    return apiRequest<CostAdjustDetailRow>({
      method: 'put',
      url: `${COST_ADJUSTS}/${encodeURIComponent(id)}`,
      data: payload,
    })
  },
  /** 删除成本调整草稿（仅草稿可删）。 */
  remove(id: string): Promise<unknown> {
    return apiRequest<unknown>({
      method: 'delete',
      url: `${COST_ADJUSTS}/${encodeURIComponent(id)}`,
    })
  },
  /** 提交成本调整审批。 */
  submit(id: string): Promise<StkApprovalRequestRow> {
    return apiRequest<StkApprovalRequestRow>({
      method: 'post',
      url: `${COST_ADJUSTS}/${encodeURIComponent(id)}/submit`,
    })
  },
}

/* ============================================================
 * 库存台账 / 流水 / 批次（/api/stk，只读）
 * ============================================================ */

const LEDGER = '/api/stk/ledger'
const TXNS = '/api/stk/txns'
const BATCHES = '/api/stk/batches'

export const stockApi = {
  /** 库存台账余额列表（成本字段按权限裁剪）。 */
  ledger(params: { itemId?: string; warehouseId?: string } = {}): Promise<StockBalanceRow[]> {
    return apiRequest<StockBalanceRow[]>({
      method: 'get',
      url: LEDGER,
      params: toParams({ itemId: params.itemId, warehouseId: params.warehouseId }),
    })
  },
  /** 出入库流水列表（按 Id 倒序，take 上限 500）。 */
  txns(query: StockTxnQuery = {}): Promise<StockTxnRow[]> {
    return apiRequest<StockTxnRow[]>({
      method: 'get',
      url: TXNS,
      params: toParams({
        itemId: query.itemId,
        warehouseId: query.warehouseId,
        sourceType: query.sourceType,
        from: query.from,
        to: query.to,
        take: query.take,
      }),
    })
  },
  /** 批次库存列表（商品 + 仓库必填，FEFO 顺序由后端返回）。 */
  batches(params: { itemId: string; warehouseId: string }): Promise<StockBatchRow[]> {
    return apiRequest<StockBatchRow[]>({
      method: 'get',
      url: BATCHES,
      params: toParams({ itemId: params.itemId, warehouseId: params.warehouseId }),
    })
  },
}
