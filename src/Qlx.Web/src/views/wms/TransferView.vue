<!--
  所属公司：四川淇利馨集团有限公司
  描述：物资调拨——调拨单列表 / 新建（转审批）/ 发货 / 收货（差异说明）/ 作废 + 在途库存 Tab + 详情抽屉 + CSV 导出
  说明：
    1. 调拨单号由后端生成，只读展示，新建表单无单号输入框（N-1）；
    2. 在途超期由前端按 expectArrive 与今日比较标记（B-2，后端只下发原始日期）；
    3. 收货数量少于发货数量时必须填写差异说明（与后端校验一致）；
    4. 调拨单新建即进入审批，审批通过后状态变为已生效才可发货（审批由审批中心驱动）。
-->
<script setup lang="ts">
import { computed, onMounted, reactive, ref } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import AppIcon from '@/components/AppIcon.vue'
import QlxPagePanel from '@/components/crud/QlxPagePanel.vue'
import QlxSearchBar from '@/components/crud/QlxSearchBar.vue'
import type { SearchField } from '@/components/crud/QlxSearchBar.vue'
import QlxDataTable from '@/components/crud/QlxDataTable.vue'
import type { CrudColumn } from '@/components/crud/QlxDataTable.vue'
import { itemApi } from '@/api/stk'
import { transferApi, warehouseApi } from '@/api/wms'
import { useLocalTable } from '@/composables/useLocalTable'
import type { ItemRow } from '@/types/stk'
import type {
  CreateStockTransferLinePayload,
  CreateStockTransferPayload,
  InTransitStockRow,
  ReceiveStockTransferLinePayload,
  ReceiveStockTransferPayload,
  ShipStockTransferLinePayload,
  ShipStockTransferPayload,
  StockTransferDetailRow,
  StockTransferRow,
  WarehouseRow,
} from '@/types/wms'
import { exportCsv } from '@/utils/csv-export'
import type { CsvColumn } from '@/utils/csv-export'
import { formatCost, formatQty } from '@/utils/stk-enums'
import { formatDate, formatDateTime } from '@/utils/format'
import {
  IN_TRANSIT_STATUS_LABELS,
  STOCK_TRANSFER_STATUS_LABELS,
  inTransitStatusLabel,
  inTransitStatusTone,
  stockTransferStatusLabel,
  stockTransferStatusTone,
  toStringOptions,
} from '@/utils/wms-enums'

/* ============================================================
 * 一、基础下拉（仓库 / 商品）
 * ============================================================ */

const warehouses = ref<WarehouseRow[]>([])
const items = ref<ItemRow[]>([])
const warehouseOptions = computed(() =>
  warehouses.value.map((warehouse) => ({ label: warehouse.name, value: warehouse.id })),
)
const itemOptions = computed(() =>
  items.value.map((item) => ({ label: `${item.itemCode} · ${item.name}`, value: item.id })),
)

function warehouseName(id: string): string {
  return warehouses.value.find((warehouse) => warehouse.id === id)?.name ?? '—'
}

function itemLabel(id: string): string {
  const item = items.value.find((row) => row.id === id)
  return item ? `${item.itemCode} · ${item.name}` : '—'
}

function itemBatchEnabled(id: string): boolean {
  return items.value.find((row) => row.id === id)?.batchEnabled ?? false
}

async function loadOptions(): Promise<void> {
  try {
    warehouses.value = await warehouseApi.list()
  } catch {
    warehouses.value = []
  }
  try {
    items.value = await itemApi.list()
  } catch {
    items.value = []
  }
}

/* ============================================================
 * 二、调拨单列表
 * ============================================================ */

const activeTab = ref<'transfers' | 'transit'>('transfers')

const searchFields = computed<SearchField[]>(() => [
  {
    prop: 'status',
    label: '状态',
    type: 'select',
    options: toStringOptions(STOCK_TRANSFER_STATUS_LABELS),
    width: 150,
  },
  {
    prop: 'warehouseId',
    label: '仓库',
    type: 'select',
    options: warehouseOptions.value,
    width: 180,
  },
])

const {
  loading: transferLoading,
  rows: transferRows,
  total: transferTotal,
  page: transferPage,
  pageSize: transferPageSize,
  query: transferQuery,
  search: searchTransfer,
  reset: resetTransfer,
  refresh: refreshTransfer,
  changePage: changeTransferPage,
} = useLocalTable<StockTransferRow>((params) =>
  transferApi.list({
    status: params.status ? Number(params.status) : undefined,
    warehouseId: params.warehouseId || undefined,
  }),
)

const transferColumns: Array<CrudColumn<StockTransferRow>> = [
  { prop: 'transferNo', label: '调拨单号', type: 'code', width: 180, fixed: 'left' },
  { prop: 'fromWarehouseId', label: '调出仓', minWidth: 150 },
  { prop: 'toWarehouseId', label: '调入仓', minWidth: 150 },
  { prop: 'inTransitDays', label: '在途天数', width: 100, align: 'right' },
  {
    prop: 'status',
    label: '状态',
    type: 'tag',
    width: 110,
    align: 'center',
    tagText: (row) => stockTransferStatusLabel(row.status),
    tagTone: (row) => stockTransferStatusTone(row.status),
  },
  { prop: 'createdAt', label: '创建时间', type: 'time', width: 170 },
  { prop: 'remark', label: '备注', minWidth: 170 },
]

const transferCsvColumns: Array<CsvColumn<StockTransferRow>> = [
  { title: '调拨单号', value: (row) => row.transferNo },
  { title: '调出仓', value: (row) => warehouseName(row.fromWarehouseId) },
  { title: '调入仓', value: (row) => warehouseName(row.toWarehouseId) },
  { title: '在途天数', value: (row) => row.inTransitDays },
  { title: '状态', value: (row) => stockTransferStatusLabel(row.status) },
  { title: '创建时间', value: (row) => formatDateTime(row.createdAt) },
  { title: '备注', value: (row) => row.remark },
]

function onExportTransfers(): void {
  exportCsv('物资调拨', transferCsvColumns, transferRows.value)
}

/* ============================================================
 * 三、在途库存（超期标记由前端判定）
 * ============================================================ */

const inTransitSearchFields = computed<SearchField[]>(() => [
  {
    prop: 'status',
    label: '在途状态',
    type: 'select',
    options: toStringOptions(IN_TRANSIT_STATUS_LABELS),
    width: 140,
  },
  {
    prop: 'warehouseId',
    label: '仓库',
    type: 'select',
    options: warehouseOptions.value,
    width: 180,
  },
])

const {
  loading: transitLoading,
  rows: transitRows,
  total: transitTotal,
  page: transitPage,
  pageSize: transitPageSize,
  query: transitQuery,
  search: searchTransit,
  reset: resetTransit,
  refresh: refreshTransit,
  changePage: changeTransitPage,
} = useLocalTable<InTransitStockRow>((params) =>
  transferApi.inTransit({
    status: params.status ? Number(params.status) : undefined,
    warehouseId: params.warehouseId || undefined,
  }),
)

/** 预计到货日期早于今日即视为在途超期（仅展示判定，B-2）。 */
function isOverdue(expectArrive: string): boolean {
  return formatDate(expectArrive) < formatDate(new Date().toISOString())
}

const transitColumns: Array<CrudColumn<InTransitStockRow>> = [
  { prop: 'transferNo', label: '调拨单号', type: 'code', width: 170, fixed: 'left' },
  { prop: 'itemId', label: '商品', minWidth: 190 },
  { prop: 'fromWarehouseId', label: '调出仓', minWidth: 130 },
  { prop: 'toWarehouseId', label: '调入仓', minWidth: 130 },
  { prop: 'batchNo', label: '批次号', width: 130 },
  { prop: 'qty', label: '在途数量', type: 'text', width: 120, align: 'right' },
  { prop: 'unitCost', label: '单位成本', type: 'text', width: 120, align: 'right' },
  { prop: 'shippedAt', label: '发货时间', type: 'time', width: 160 },
  {
    prop: 'expectArrive',
    label: '预计到货',
    width: 150,
  },
  {
    prop: 'status',
    label: '在途状态',
    type: 'tag',
    width: 100,
    align: 'center',
    tagText: (row) => inTransitStatusLabel(row.status),
    tagTone: (row) => inTransitStatusTone(row.status),
  },
]

const transitCsvColumns: Array<CsvColumn<InTransitStockRow>> = [
  { title: '调拨单号', value: (row) => row.transferNo },
  { title: '商品', value: (row) => itemLabel(row.itemId) },
  { title: '调出仓', value: (row) => warehouseName(row.fromWarehouseId) },
  { title: '调入仓', value: (row) => warehouseName(row.toWarehouseId) },
  { title: '批次号', value: (row) => row.batchNo },
  { title: '在途数量', value: (row) => formatQty(row.qty) },
  { title: '单位成本', value: (row) => formatCost(row.unitCost) },
  { title: '发货时间', value: (row) => formatDateTime(row.shippedAt) },
  { title: '预计到货', value: (row) => formatDate(row.expectArrive) },
  { title: '是否超期', value: (row) => (isOverdue(row.expectArrive) ? '超期' : '正常') },
  { title: '在途状态', value: (row) => inTransitStatusLabel(row.status) },
]

function onExportTransit(): void {
  exportCsv('在途库存', transitCsvColumns, transitRows.value)
}

/* ============================================================
 * 四、新建调拨单（不含单号，N-1）
 * ============================================================ */

interface TransferLineDraft {
  itemId: string
  qty: number
  batchNo: string
}

const createOpen = ref(false)
const submitting = ref(false)
const form = reactive<{
  fromWarehouseId: string
  toWarehouseId: string
  inTransitDays: number | undefined
  remark: string
  lines: TransferLineDraft[]
}>({ fromWarehouseId: '', toWarehouseId: '', inTransitDays: undefined, remark: '', lines: [] })

function resetForm(): void {
  form.fromWarehouseId = ''
  form.toWarehouseId = ''
  form.inTransitDays = undefined
  form.remark = ''
  form.lines = [{ itemId: '', qty: 1, batchNo: '' }]
}

function openCreate(): void {
  resetForm()
  createOpen.value = true
}

function addLine(): void {
  form.lines.push({ itemId: '', qty: 1, batchNo: '' })
}

function removeLine(index: number): void {
  form.lines.splice(index, 1)
}

async function onSubmitCreate(): Promise<void> {
  if (!form.fromWarehouseId || !form.toWarehouseId) {
    ElMessage.warning('请选择调出仓与调入仓')
    return
  }
  if (form.fromWarehouseId === form.toWarehouseId) {
    ElMessage.warning('调出仓与调入仓不能相同')
    return
  }
  if (
    form.inTransitDays !== undefined &&
    (!Number.isFinite(form.inTransitDays) || form.inTransitDays < 1 || form.inTransitDays > 365)
  ) {
    ElMessage.warning('预计在途天数必须为 1~365')
    return
  }
  if (form.lines.length === 0) {
    ElMessage.warning('请至少添加一条明细')
    return
  }
  const invalid = form.lines.some(
    (line) => !line.itemId || !Number.isFinite(line.qty) || line.qty <= 0,
  )
  if (invalid) {
    ElMessage.warning('请完整填写明细：商品必选、调拨数量需大于 0')
    return
  }
  const missingBatch = form.lines.some(
    (line) => itemBatchEnabled(line.itemId) && !line.batchNo.trim(),
  )
  if (missingBatch) {
    ElMessage.warning('启用批次管理的商品必须填写批次号')
    return
  }
  submitting.value = true
  try {
    const lines: CreateStockTransferLinePayload[] = form.lines.map((line) => ({
      itemId: line.itemId,
      qty: line.qty,
      batchNo: line.batchNo.trim() || null,
    }))
    const payload: CreateStockTransferPayload = {
      fromWarehouseId: form.fromWarehouseId,
      toWarehouseId: form.toWarehouseId,
      inTransitDays: form.inTransitDays ?? null,
      remark: form.remark.trim() || null,
      lines,
    }
    await transferApi.create(payload)
    createOpen.value = false
    await refreshTransfer()
    ElMessage.success('调拨单已创建并提交审批（单号由系统生成）')
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : '创建调拨单失败')
  } finally {
    submitting.value = false
  }
}

/* ============================================================
 * 五、发货 / 收货 / 作废
 * ============================================================ */

interface ShipLineDraft {
  lineId: string
  lineNo: number
  itemId: string
  qty: number
  shipQty: number
  batchNo: string | null
}

const shipOpen = ref(false)
const shipLoading = ref(false)
const shipSubmitting = ref(false)
const shipping = ref<StockTransferDetailRow | null>(null)
const shipLines = ref<ShipLineDraft[]>([])

async function openShip(row: StockTransferRow): Promise<void> {
  shipOpen.value = true
  shipLoading.value = true
  shipping.value = null
  shipLines.value = []
  try {
    const detail = await transferApi.get(row.id)
    shipping.value = detail
    shipLines.value = detail.lines.map((line) => ({
      lineId: line.id,
      lineNo: line.lineNo,
      itemId: line.itemId,
      qty: line.qty,
      shipQty: line.shipQty > 0 ? line.shipQty : line.qty,
      batchNo: line.batchNo,
    }))
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : '加载调拨详情失败')
  } finally {
    shipLoading.value = false
  }
}

async function onSubmitShip(): Promise<void> {
  if (!shipping.value) {
    return
  }
  const invalid = shipLines.value.some(
    (line) => !Number.isFinite(line.shipQty) || line.shipQty < 0 || line.shipQty > line.qty,
  )
  if (invalid) {
    ElMessage.warning('发货数量不得为负且不得超过调拨数量')
    return
  }
  if (!shipLines.value.some((line) => line.shipQty > 0)) {
    ElMessage.warning('至少一行发货数量需大于 0')
    return
  }
  shipSubmitting.value = true
  try {
    const payload: ShipStockTransferPayload = {
      lines: shipLines.value.map<ShipStockTransferLinePayload>((line) => ({
        lineId: line.lineId,
        shipQty: line.shipQty,
      })),
    }
    await transferApi.ship(shipping.value.transfer.id, payload)
    shipOpen.value = false
    await Promise.all([refreshTransfer(), refreshTransit()])
    ElMessage.success('调拨单已发货，明细已转入在途库存')
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : '发货提交失败')
  } finally {
    shipSubmitting.value = false
  }
}

interface ReceiveLineDraft {
  lineId: string
  lineNo: number
  itemId: string
  shipQty: number
  receiveQty: number
  diffNote: string
}

const receiveOpen = ref(false)
const receiveLoading = ref(false)
const receiveSubmitting = ref(false)
const receiving = ref<StockTransferDetailRow | null>(null)
const receiveLines = ref<ReceiveLineDraft[]>([])

async function openReceive(row: StockTransferRow): Promise<void> {
  receiveOpen.value = true
  receiveLoading.value = true
  receiving.value = null
  receiveLines.value = []
  try {
    const detail = await transferApi.get(row.id)
    receiving.value = detail
    receiveLines.value = detail.lines
      .filter((line) => line.shipQty > 0)
      .map((line) => ({
        lineId: line.id,
        lineNo: line.lineNo,
        itemId: line.itemId,
        shipQty: line.shipQty,
        receiveQty: line.shipQty,
        diffNote: line.diffNote ?? '',
      }))
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : '加载调拨详情失败')
  } finally {
    receiveLoading.value = false
  }
}

async function onSubmitReceive(): Promise<void> {
  if (!receiving.value) {
    return
  }
  const invalid = receiveLines.value.some(
    (line) =>
      !Number.isFinite(line.receiveQty) ||
      line.receiveQty < 0 ||
      line.receiveQty > line.shipQty ||
      (line.receiveQty < line.shipQty && !line.diffNote.trim()),
  )
  if (invalid) {
    ElMessage.warning('收货数量不得为负且不得超过发货数量；少于发货数量时必须填写差异说明')
    return
  }
  if (!receiveLines.value.some((line) => line.receiveQty > 0)) {
    ElMessage.warning('至少一行收货数量需大于 0')
    return
  }
  receiveSubmitting.value = true
  try {
    const payload: ReceiveStockTransferPayload = {
      lines: receiveLines.value.map<ReceiveStockTransferLinePayload>((line) => ({
        lineId: line.lineId,
        receiveQty: line.receiveQty,
        diffNote: line.diffNote.trim() || null,
      })),
    }
    await transferApi.receive(receiving.value.transfer.id, payload)
    receiveOpen.value = false
    await Promise.all([refreshTransfer(), refreshTransit()])
    ElMessage.success('调拨单已收货，调入仓库存已入账')
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : '收货提交失败')
  } finally {
    receiveSubmitting.value = false
  }
}

async function onVoid(row: StockTransferRow): Promise<void> {
  try {
    await ElMessageBox.confirm(
      `作废调拨单「${row.transferNo}」后不可恢复，是否继续？`,
      '作废调拨单',
      { confirmButtonText: '确认作废', cancelButtonText: '返回', type: 'warning' },
    )
  } catch {
    return
  }
  try {
    await transferApi.void(row.id)
    await refreshTransfer()
    ElMessage.success('调拨单已作废')
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : '作废调拨单失败')
  }
}

/* ============================================================
 * 六、详情抽屉
 * ============================================================ */

const detailOpen = ref(false)
const detailLoading = ref(false)
const detail = ref<StockTransferDetailRow | null>(null)

async function openDetail(row: StockTransferRow): Promise<void> {
  detailOpen.value = true
  detailLoading.value = true
  detail.value = null
  try {
    detail.value = await transferApi.get(row.id)
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : '加载调拨详情失败')
  } finally {
    detailLoading.value = false
  }
}

onMounted(loadOptions)
</script>

<template>
  <QlxPagePanel title="物资调拨" subtitle="调拨单审批 · 发货 · 收货 · 在途库存跟踪（单号由系统生成）">
    <template #toolbar>
      <el-button v-permission="'wms.transfer.action'" type="primary" @click="openCreate">
        <AppIcon name="Plus" :size="14" />
        新增调拨单
      </el-button>
    </template>

    <el-tabs v-model="activeTab" class="transfer-tabs">
      <el-tab-pane label="调拨单" name="transfers">
        <div class="transfer-pane">
          <el-button class="transfer-pane__export" @click="onExportTransfers">导出 CSV</el-button>
          <QlxSearchBar
            :fields="searchFields"
            :model-value="transferQuery"
            @search="searchTransfer"
            @reset="resetTransfer"
          />
          <QlxDataTable
            :items="transferRows"
            :columns="transferColumns"
            :loading="transferLoading"
            :total="transferTotal"
            :page="transferPage"
            :page-size="transferPageSize"
            :actions-width="250"
            empty-text="暂无调拨单"
            @page-change="changeTransferPage"
          >
            <template #cell-transferNo="{ row }">
              <span class="qlx-numeric">{{ row.transferNo }}</span>
            </template>
            <template #cell-fromWarehouseId="{ row }">
              {{ warehouseName(row.fromWarehouseId) }}
            </template>
            <template #cell-toWarehouseId="{ row }">
              {{ warehouseName(row.toWarehouseId) }}
            </template>
            <template #cell-inTransitDays="{ row }">
              <span class="qlx-numeric">{{ row.inTransitDays }}</span>
            </template>
            <template #cell-createdAt="{ row }">{{ formatDateTime(row.createdAt) }}</template>
            <template #cell-remark="{ row }">{{ row.remark ?? '—' }}</template>
            <template #actions="{ row }">
              <button type="button" class="cell-link" @click="openDetail(row)">详情</button>
              <template v-if="row.status === 2">
                <el-divider direction="vertical" />
                <button
                  v-permission="'wms.transfer.action'"
                  type="button"
                  class="cell-link"
                  @click="openShip(row)"
                >
                  发货
                </button>
              </template>
              <template v-if="row.status === 3">
                <el-divider direction="vertical" />
                <button
                  v-permission="'wms.transfer.action'"
                  type="button"
                  class="cell-link"
                  @click="openReceive(row)"
                >
                  收货
                </button>
              </template>
              <template v-if="row.status === 1 || row.status === 2">
                <el-divider direction="vertical" />
                <button
                  v-permission="'wms.transfer.action'"
                  type="button"
                  class="cell-link is-danger"
                  @click="onVoid(row)"
                >
                  作废
                </button>
              </template>
            </template>
          </QlxDataTable>
        </div>
      </el-tab-pane>

      <el-tab-pane label="在途库存" name="transit">
        <div class="transfer-pane">
          <el-button class="transfer-pane__export" @click="onExportTransit">导出 CSV</el-button>
          <QlxSearchBar
            :fields="inTransitSearchFields"
            :model-value="transitQuery"
            @search="searchTransit"
            @reset="resetTransit"
          />
          <QlxDataTable
            :items="transitRows"
            :columns="transitColumns"
            :loading="transitLoading"
            :total="transitTotal"
            :page="transitPage"
            :page-size="transitPageSize"
            :actions-width="110"
            empty-text="暂无在途库存"
            @page-change="changeTransitPage"
          >
            <template #cell-transferNo="{ row }">
              <span class="qlx-numeric">{{ row.transferNo }}</span>
            </template>
            <template #cell-itemId="{ row }">{{ itemLabel(row.itemId) }}</template>
            <template #cell-fromWarehouseId="{ row }">
              {{ warehouseName(row.fromWarehouseId) }}
            </template>
            <template #cell-toWarehouseId="{ row }">
              {{ warehouseName(row.toWarehouseId) }}
            </template>
            <template #cell-batchNo="{ row }">{{ row.batchNo ?? '—' }}</template>
            <template #cell-qty="{ row }">
              <span class="qlx-numeric">{{ formatQty(row.qty) }}</span>
            </template>
            <template #cell-unitCost="{ row }">
              <span class="qlx-numeric">{{ formatCost(row.unitCost) }}</span>
            </template>
            <template #cell-shippedAt="{ row }">{{ formatDateTime(row.shippedAt) }}</template>
            <template #cell-expectArrive="{ row }">
              <span :class="['qlx-numeric', { 'is-overdue': isOverdue(row.expectArrive) }]">
                {{ formatDate(row.expectArrive) }}
              </span>
              <el-tag v-if="isOverdue(row.expectArrive)" size="small" type="danger" class="overdue-tag">
                超期
              </el-tag>
            </template>
            <template #actions="{ row }">
              <button type="button" class="cell-link" @click="openDetail(row)">查看调拨单</button>
            </template>
          </QlxDataTable>
        </div>
      </el-tab-pane>
    </el-tabs>

    <!-- 新建：动态明细行 -->
    <el-dialog
      v-model="createOpen"
      title="新增调拨单"
      width="900px"
      :close-on-click-modal="false"
      append-to-body
    >
      <el-form label-width="100px">
        <el-row :gutter="16">
          <el-col :span="8">
            <el-form-item label="调出仓" required>
              <el-select v-model="form.fromWarehouseId" filterable placeholder="请选择调出仓" style="width: 100%">
                <el-option
                  v-for="option in warehouseOptions"
                  :key="option.value"
                  :label="option.label"
                  :value="option.value"
                />
              </el-select>
            </el-form-item>
          </el-col>
          <el-col :span="8">
            <el-form-item label="调入仓" required>
              <el-select v-model="form.toWarehouseId" filterable placeholder="请选择调入仓" style="width: 100%">
                <el-option
                  v-for="option in warehouseOptions"
                  :key="option.value"
                  :label="option.label"
                  :value="option.value"
                />
              </el-select>
            </el-form-item>
          </el-col>
          <el-col :span="8">
            <el-form-item label="预计在途天数">
              <el-input-number
                v-model="form.inTransitDays"
                :min="1"
                :max="365"
                :precision="0"
                placeholder="留空取系统参数"
                style="width: 100%"
              />
            </el-form-item>
          </el-col>
        </el-row>
        <el-form-item label="备注">
          <el-input v-model="form.remark" maxlength="200" clearable placeholder="可选" />
        </el-form-item>

        <div class="task-lines">
          <div class="task-lines__head">
            <h3>明细</h3>
            <el-button size="small" @click="addLine">添加明细</el-button>
          </div>
          <el-table :data="form.lines" size="small" border empty-text="请添加明细">
            <el-table-column label="商品" min-width="260">
              <template #default="{ row }">
                <el-select v-model="row.itemId" placeholder="选择商品" filterable style="width: 100%">
                  <el-option
                    v-for="option in itemOptions"
                    :key="option.value"
                    :label="option.label"
                    :value="option.value"
                  />
                </el-select>
              </template>
            </el-table-column>
            <el-table-column label="调拨数量" width="150">
              <template #default="{ row }">
                <el-input-number v-model="row.qty" :min="0" :precision="4" style="width: 100%" />
              </template>
            </el-table-column>
            <el-table-column label="批次号" width="170">
              <template #default="{ row }">
                <el-input v-model="row.batchNo" maxlength="64" placeholder="非批次商品留空" />
              </template>
            </el-table-column>
            <el-table-column label="操作" width="80" align="center">
              <template #default="{ $index }">
                <button type="button" class="cell-link is-danger" @click="removeLine($index)">
                  移除
                </button>
              </template>
            </el-table-column>
          </el-table>
        </div>
      </el-form>

      <template #footer>
        <el-button @click="createOpen = false">取消</el-button>
        <el-button type="primary" :loading="submitting" @click="onSubmitCreate">
          创建并提交审批
        </el-button>
      </template>
    </el-dialog>

    <!-- 发货 -->
    <el-dialog
      v-model="shipOpen"
      :title="shipping ? `发货 · ${shipping.transfer.transferNo}` : '发货'"
      width="880px"
      :close-on-click-modal="false"
      append-to-body
    >
      <div v-loading="shipLoading">
        <template v-if="shipping">
          <el-descriptions :column="3" border size="small">
            <el-descriptions-item label="调出仓">
              {{ warehouseName(shipping.transfer.fromWarehouseId) }}
            </el-descriptions-item>
            <el-descriptions-item label="调入仓">
              {{ warehouseName(shipping.transfer.toWarehouseId) }}
            </el-descriptions-item>
            <el-descriptions-item label="预计在途天数">
              {{ shipping.transfer.inTransitDays }}
            </el-descriptions-item>
          </el-descriptions>

          <el-table :data="shipLines" size="small" border class="task-lines__table">
            <el-table-column prop="lineNo" label="行号" width="70" align="center" />
            <el-table-column label="商品" min-width="220">
              <template #default="{ row }">{{ itemLabel(row.itemId) }}</template>
            </el-table-column>
            <el-table-column label="调拨数量" width="120" align="right">
              <template #default="{ row }">
                <span class="qlx-numeric">{{ formatQty(row.qty) }}</span>
              </template>
            </el-table-column>
            <el-table-column label="本次发货" width="160">
              <template #default="{ row }">
                <el-input-number v-model="row.shipQty" :min="0" :precision="4" style="width: 100%" />
              </template>
            </el-table-column>
            <el-table-column label="批次号" width="150">
              <template #default="{ row }">{{ row.batchNo ?? '—' }}</template>
            </el-table-column>
          </el-table>
        </template>
      </div>

      <template #footer>
        <el-button @click="shipOpen = false">取消</el-button>
        <el-button type="primary" :loading="shipSubmitting" @click="onSubmitShip">
          确认发货
        </el-button>
      </template>
    </el-dialog>

    <!-- 收货（差异说明） -->
    <el-dialog
      v-model="receiveOpen"
      :title="receiving ? `收货 · ${receiving.transfer.transferNo}` : '收货'"
      width="920px"
      :close-on-click-modal="false"
      append-to-body
    >
      <div v-loading="receiveLoading">
        <template v-if="receiving">
          <el-descriptions :column="3" border size="small">
            <el-descriptions-item label="调出仓">
              {{ warehouseName(receiving.transfer.fromWarehouseId) }}
            </el-descriptions-item>
            <el-descriptions-item label="调入仓">
              {{ warehouseName(receiving.transfer.toWarehouseId) }}
            </el-descriptions-item>
            <el-descriptions-item label="状态">
              {{ stockTransferStatusLabel(receiving.transfer.status) }}
            </el-descriptions-item>
          </el-descriptions>

          <el-table :data="receiveLines" size="small" border class="task-lines__table">
            <el-table-column prop="lineNo" label="行号" width="70" align="center" />
            <el-table-column label="商品" min-width="200">
              <template #default="{ row }">{{ itemLabel(row.itemId) }}</template>
            </el-table-column>
            <el-table-column label="发货数量" width="110" align="right">
              <template #default="{ row }">
                <span class="qlx-numeric">{{ formatQty(row.shipQty) }}</span>
              </template>
            </el-table-column>
            <el-table-column label="收货数量" width="150">
              <template #default="{ row }">
                <el-input-number v-model="row.receiveQty" :min="0" :precision="4" style="width: 100%" />
              </template>
            </el-table-column>
            <el-table-column label="差异说明" min-width="200">
              <template #default="{ row }">
                <el-input
                  v-model="row.diffNote"
                  maxlength="200"
                  :placeholder="row.receiveQty < row.shipQty ? '少于发货数量必填' : '可选'"
                />
              </template>
            </el-table-column>
          </el-table>
          <p class="task-hint">收货数量少于发货数量时必须填写差异说明（后端强校验）。</p>
        </template>
      </div>

      <template #footer>
        <el-button @click="receiveOpen = false">取消</el-button>
        <el-button type="primary" :loading="receiveSubmitting" @click="onSubmitReceive">
          确认收货
        </el-button>
      </template>
    </el-dialog>

    <!-- 详情抽屉 -->
    <el-drawer
      v-model="detailOpen"
      :title="detail ? `调拨详情 · ${detail.transfer.transferNo}` : '调拨详情'"
      size="820px"
      append-to-body
    >
      <div v-loading="detailLoading" class="task-detail">
        <template v-if="detail">
          <el-descriptions :column="2" border>
            <el-descriptions-item label="调拨单号">
              <span class="qlx-numeric">{{ detail.transfer.transferNo }}</span>
            </el-descriptions-item>
            <el-descriptions-item label="状态">
              {{ stockTransferStatusLabel(detail.transfer.status) }}
            </el-descriptions-item>
            <el-descriptions-item label="调出仓">
              {{ warehouseName(detail.transfer.fromWarehouseId) }}
            </el-descriptions-item>
            <el-descriptions-item label="调入仓">
              {{ warehouseName(detail.transfer.toWarehouseId) }}
            </el-descriptions-item>
            <el-descriptions-item label="预计在途天数">
              {{ detail.transfer.inTransitDays }}
            </el-descriptions-item>
            <el-descriptions-item label="创建时间">
              {{ formatDateTime(detail.transfer.createdAt) }}
            </el-descriptions-item>
            <el-descriptions-item label="备注" :span="2">
              {{ detail.transfer.remark ?? '—' }}
            </el-descriptions-item>
          </el-descriptions>

          <h3 class="task-detail__title">明细（{{ detail.lines.length }}）</h3>
          <el-table :data="detail.lines" size="small" border empty-text="暂无明细">
            <el-table-column prop="lineNo" label="行号" width="70" align="center" />
            <el-table-column label="商品" min-width="210">
              <template #default="{ row }">{{ itemLabel(row.itemId) }}</template>
            </el-table-column>
            <el-table-column label="调拨数量" width="110" align="right">
              <template #default="{ row }">
                <span class="qlx-numeric">{{ formatQty(row.qty) }}</span>
              </template>
            </el-table-column>
            <el-table-column label="发货数量" width="110" align="right">
              <template #default="{ row }">
                <span class="qlx-numeric">{{ formatQty(row.shipQty) }}</span>
              </template>
            </el-table-column>
            <el-table-column label="收货数量" width="110" align="right">
              <template #default="{ row }">
                <span class="qlx-numeric">{{ formatQty(row.receiveQty) }}</span>
              </template>
            </el-table-column>
            <el-table-column label="批次号" width="130">
              <template #default="{ row }">{{ row.batchNo ?? '—' }}</template>
            </el-table-column>
            <el-table-column label="差异说明" min-width="150">
              <template #default="{ row }">{{ row.diffNote ?? '—' }}</template>
            </el-table-column>
          </el-table>
        </template>
      </div>
    </el-drawer>
  </QlxPagePanel>
</template>

<style scoped>
.transfer-tabs {
  padding: 0 var(--qlx-space-4);
}

.transfer-pane {
  position: relative;
}

.transfer-pane__export {
  margin: var(--qlx-space-4) 0 0 var(--qlx-space-4);
}

.is-overdue {
  color: var(--qlx-danger);
}

.overdue-tag {
  margin-left: var(--qlx-space-2);
}

.task-lines {
  margin-top: var(--qlx-space-2);
}

.task-lines__head {
  display: flex;
  margin-bottom: var(--qlx-space-2);
  align-items: center;
  justify-content: space-between;
}

.task-lines__head h3,
.task-detail__title {
  color: var(--qlx-ink-600);
  font-size: var(--qlx-text-sm);
  font-weight: 600;
}

.task-lines__table {
  margin-top: var(--qlx-space-3);
}

.task-detail__title {
  margin: var(--qlx-space-4) 0 var(--qlx-space-2);
}

.task-hint {
  margin-top: var(--qlx-space-2);
  color: var(--qlx-ink-400);
  font-size: var(--qlx-text-xs);
}

.cell-link {
  padding: 0;
  border: 0;
  background: none;
  color: var(--qlx-brand-600);
  font-size: var(--qlx-text-sm);
  cursor: pointer;
}

.cell-link:hover {
  text-decoration: underline;
}

.cell-link.is-danger {
  color: var(--qlx-danger);
}
</style>
