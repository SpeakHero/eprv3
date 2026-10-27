<!--
  所属公司：四川淇利馨集团有限公司
  描述：出入库流水——多条件筛选（商品 / 仓库 / 来源类型 / 日期区间）只读列表 + CSV 导出
  说明：
    1. 流水不可变，页面不提供任何写操作；
    2. 日期区间在提交前转为 ISO 时间（后端 DateTimeOffset），单次最多 200 条（后端上限 500）；
    3. 来源类型 / 方向以数值对接，中文映射在前端公共层（B-4）。
-->
<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { ElMessage } from 'element-plus'
import AppIcon from '@/components/AppIcon.vue'
import QlxPagePanel from '@/components/crud/QlxPagePanel.vue'
import QlxSearchBar from '@/components/crud/QlxSearchBar.vue'
import type { SearchField } from '@/components/crud/QlxSearchBar.vue'
import QlxDataTable from '@/components/crud/QlxDataTable.vue'
import type { CrudColumn } from '@/components/crud/QlxDataTable.vue'
import { itemApi, stockApi } from '@/api/stk'
import { warehouseApi } from '@/api/wms'
import { useLocalTable } from '@/composables/useLocalTable'
import { usePermission } from '@/composables/usePermission'
import type { ItemRow, StockTxnRow } from '@/types/stk'
import type { WarehouseRow } from '@/types/wms'
import type { CsvColumn } from '@/utils/csv-export'
import { exportCsv } from '@/utils/csv-export'
import {
  formatCost,
  formatMoney,
  formatQty,
  STOCK_SOURCE_TYPE_LABELS,
  stockDirectionLabel,
  stockDirectionTone,
  stockSourceTypeLabel,
  toStringOptions,
} from '@/utils/stk-enums'

const { hasPermission } = usePermission()
const canViewCost = computed(() => hasPermission('stk.cost.field'))

const items = ref<ItemRow[]>([])
const warehouses = ref<WarehouseRow[]>([])

const itemOptions = computed(() =>
  items.value.map((item) => ({ label: `${item.itemCode} · ${item.name}`, value: item.id })),
)
const warehouseOptions = computed(() =>
  warehouses.value.map((warehouse) => ({ label: warehouse.name, value: warehouse.id })),
)

const searchFields = computed<SearchField[]>(() => [
  { prop: 'itemId', label: '商品', type: 'select', options: itemOptions.value, width: 240 },
  { prop: 'warehouseId', label: '仓库', type: 'select', options: warehouseOptions.value, width: 180 },
  {
    prop: 'sourceType',
    label: '来源类型',
    type: 'select',
    options: toStringOptions(STOCK_SOURCE_TYPE_LABELS),
    width: 170,
  },
  { prop: 'from', label: '开始日期', type: 'date', width: 160 },
  { prop: 'to', label: '结束日期', type: 'date', width: 160 },
])

/** 单页取数上限（后端默认 200 / 上限 500，流水页固定 200）。 */
const TAKE = 200

const { loading, rows, total, page, pageSize, query, search, reset, changePage } =
  useLocalTable<StockTxnRow>(async (params) => {
    const fromDate = params.from ? new Date(`${params.from}T00:00:00`) : null
    const toDate = params.to ? new Date(`${params.to}T23:59:59`) : null
    return stockApi.txns({
      itemId: params.itemId || undefined,
      warehouseId: params.warehouseId || undefined,
      sourceType: params.sourceType ? Number(params.sourceType) : undefined,
      from: fromDate && !Number.isNaN(fromDate.getTime()) ? fromDate.toISOString() : undefined,
      to: toDate && !Number.isNaN(toDate.getTime()) ? toDate.toISOString() : undefined,
      take: TAKE,
    })
  })

function itemLabel(id: string): string {
  const item = items.value.find((row) => row.id === id)
  return item ? `${item.itemCode} · ${item.name}` : '—'
}

function warehouseName(id: string): string {
  return warehouses.value.find((warehouse) => warehouse.id === id)?.name ?? '—'
}

const columns = computed<Array<CrudColumn<StockTxnRow>>>(() => [
  { prop: 'txnNo', label: '流水号', type: 'code', width: 170, fixed: 'left' },
  { prop: 'occurredAt', label: '发生时间', type: 'time', width: 170 },
  { prop: 'sourceType', label: '来源类型', width: 130 },
  {
    prop: 'direction',
    label: '方向',
    type: 'tag',
    width: 90,
    align: 'center',
    tagText: (row) => stockDirectionLabel(row.direction),
    tagTone: (row) => stockDirectionTone(row.direction),
  },
  { prop: 'sourceDocNo', label: '来源单号', minWidth: 160 },
  { prop: 'itemId', label: '商品', minWidth: 210 },
  { prop: 'warehouseId', label: '仓库', minWidth: 140 },
  { prop: 'batchNo', label: '批次号', width: 130 },
  { prop: 'qty', label: '数量', width: 120, align: 'right' },
  ...(canViewCost.value
    ? ([
        { prop: 'unitCost', label: '单价', width: 120, align: 'right' },
        { prop: 'amount', label: '金额', width: 130, align: 'right' },
      ] as Array<CrudColumn<StockTxnRow>>)
    : []),
  { prop: 'balanceQtyAfter', label: '结存数量', width: 120, align: 'right' },
  ...(canViewCost.value
    ? ([
        { prop: 'balanceAmountAfter', label: '结存金额', width: 130, align: 'right' },
      ] as Array<CrudColumn<StockTxnRow>>)
    : []),
  { prop: 'operatorId', label: '操作人', width: 140 },
])

const csvColumns = computed<Array<CsvColumn<StockTxnRow>>>(() => {
  const base: Array<CsvColumn<StockTxnRow>> = [
    { title: '流水号', value: (row) => row.txnNo },
    { title: '发生时间', value: (row) => row.occurredAt },
    { title: '来源类型', value: (row) => stockSourceTypeLabel(row.sourceType) },
    { title: '方向', value: (row) => stockDirectionLabel(row.direction) },
    { title: '来源单号', value: (row) => row.sourceDocNo },
    { title: '来源行号', value: (row) => row.sourceLineNo },
    { title: '商品', value: (row) => itemLabel(row.itemId) },
    { title: '仓库', value: (row) => warehouseName(row.warehouseId) },
    { title: '批次号', value: (row) => row.batchNo ?? '' },
    { title: '数量', value: (row) => row.qty },
  ]
  if (canViewCost.value) {
    base.push(
      { title: '单价', value: (row) => row.unitCost ?? '' },
      { title: '金额', value: (row) => row.amount ?? '' },
    )
  }
  base.push({ title: '结存数量', value: (row) => row.balanceQtyAfter })
  if (canViewCost.value) {
    base.push({ title: '结存金额', value: (row) => row.balanceAmountAfter ?? '' })
  }
  base.push({ title: '操作人', value: (row) => row.operatorId ?? '' })
  return base
})

function onExport(): void {
  if (rows.value.length === 0) {
    ElMessage.info('当前无可导出的流水数据')
    return
  }
  const stamp = new Date().toISOString().slice(0, 10)
  exportCsv(`出入库流水-${stamp}`, csvColumns.value, rows.value)
  ElMessage.success('出入库流水已导出')
}

async function loadOptions(): Promise<void> {
  try {
    items.value = await itemApi.list()
  } catch {
    items.value = []
  }
  try {
    warehouses.value = await warehouseApi.list()
  } catch {
    warehouses.value = []
  }
}

onMounted(loadOptions)
</script>

<template>
  <QlxPagePanel title="出入库流水" subtitle="只读流水 · 单次查询最多 200 条">
    <template #toolbar>
      <el-button :disabled="rows.length === 0" @click="onExport">
        <AppIcon name="Files" :size="14" />
        导出 CSV
      </el-button>
    </template>

    <QlxSearchBar :fields="searchFields" :model-value="query" @search="search" @reset="reset" />

    <QlxDataTable
      :items="rows"
      :columns="columns"
      :loading="loading"
      :total="total"
      :page="page"
      :page-size="pageSize"
      empty-text="暂无流水数据"
      @page-change="changePage"
    >
      <template #cell-sourceType="{ row }">{{ stockSourceTypeLabel(row.sourceType) }}</template>
      <template #cell-sourceDocNo="{ row }">{{ row.sourceDocNo || '—' }}</template>
      <template #cell-itemId="{ row }">{{ itemLabel(row.itemId) }}</template>
      <template #cell-warehouseId="{ row }">{{ warehouseName(row.warehouseId) }}</template>
      <template #cell-batchNo="{ row }">{{ row.batchNo ?? '—' }}</template>
      <template #cell-qty="{ row }">
        <span class="qlx-numeric">{{ formatQty(row.qty) }}</span>
      </template>
      <template #cell-unitCost="{ row }">
        <span class="qlx-numeric">{{ formatCost(row.unitCost) }}</span>
      </template>
      <template #cell-amount="{ row }">
        <span class="qlx-numeric">{{ formatMoney(row.amount) }}</span>
      </template>
      <template #cell-balanceQtyAfter="{ row }">
        <span class="qlx-numeric">{{ formatQty(row.balanceQtyAfter) }}</span>
      </template>
      <template #cell-balanceAmountAfter="{ row }">
        <span class="qlx-numeric">{{ formatMoney(row.balanceAmountAfter) }}</span>
      </template>
      <template #cell-operatorId="{ row }">{{ row.operatorId ?? '—' }}</template>
    </QlxDataTable>
  </QlxPagePanel>
</template>
