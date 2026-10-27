<!--
  所属公司：四川淇利馨集团有限公司
  描述：批次库存——按商品 + 仓库必选查询的批次扁平列表（FEFO 顺序由后端返回，前端不重排）+ CSV 导出
  说明：
    1. 后端要求 itemId / warehouseId 必填，未选齐时前端不发请求；
    2. 批次顺序（先到期先出）由后端下发，前端保持原序展示（B-4）；
    3. 均价 / 金额按 stk.cost.field 权限裁剪。
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
import type { ItemRow, StockBatchTableRow } from '@/types/stk'
import type { WarehouseRow } from '@/types/wms'
import { formatDateOnly } from '@/utils/bas-master-mappers'
import type { CsvColumn } from '@/utils/csv-export'
import { exportCsv } from '@/utils/csv-export'
import { formatCost, formatMoney, formatQty } from '@/utils/stk-enums'

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
])

const { loading, rows, total, page, pageSize, query, search, reset, changePage } =
  useLocalTable<StockBatchTableRow>(async (params) => {
    if (!params.itemId || !params.warehouseId) {
      return []
    }
    const data = await stockApi.batches({
      itemId: params.itemId,
      warehouseId: params.warehouseId,
    })
    return data.map((row, index) => ({
      ...row,
      id: `${row.itemId}-${row.warehouseId}-${row.batchNo}-${index}`,
    }))
  })

/** 查询条件是否已齐备（未选齐不发请求，展示引导空态）。 */
const hasQuery = computed(() => Boolean(query.value.itemId && query.value.warehouseId))
const emptyText = computed(() => (hasQuery.value ? '暂无批次库存' : '请选择商品与仓库后查询'))

function onSearch(nextQuery: Record<string, string>): void {
  if (!nextQuery.itemId || !nextQuery.warehouseId) {
    ElMessage.warning('批次库存查询必须选择商品与仓库')
    return
  }
  void search(nextQuery)
}

function itemLabel(id: string): string {
  const item = items.value.find((row) => row.id === id)
  return item ? `${item.itemCode} · ${item.name}` : '—'
}

function warehouseName(id: string): string {
  return warehouses.value.find((warehouse) => warehouse.id === id)?.name ?? '—'
}

const columns = computed<Array<CrudColumn<StockBatchTableRow>>>(() => [
  { prop: 'batchNo', label: '批次号', type: 'code', width: 180, fixed: 'left' },
  { prop: 'produceDate', label: '生产日期', width: 130 },
  { prop: 'expiryDate', label: '到期日期', width: 130 },
  { prop: 'qty', label: '批次数量', width: 130, align: 'right' },
  ...(canViewCost.value
    ? ([
        { prop: 'avgCost', label: '批次均价', width: 130, align: 'right' },
        { prop: 'amount', label: '批次金额', width: 140, align: 'right' },
      ] as Array<CrudColumn<StockBatchTableRow>>)
    : []),
])

const csvColumns = computed<Array<CsvColumn<StockBatchTableRow>>>(() => {
  const base: Array<CsvColumn<StockBatchTableRow>> = [
    { title: '商品', value: (row) => itemLabel(row.itemId) },
    { title: '仓库', value: (row) => warehouseName(row.warehouseId) },
    { title: '批次号', value: (row) => row.batchNo },
    { title: '生产日期', value: (row) => row.produceDate ?? '' },
    { title: '到期日期', value: (row) => row.expiryDate ?? '' },
    { title: '批次数量', value: (row) => row.qty },
  ]
  if (canViewCost.value) {
    base.push(
      { title: '批次均价', value: (row) => row.avgCost ?? '' },
      { title: '批次金额', value: (row) => row.amount ?? '' },
    )
  }
  return base
})

function onExport(): void {
  if (rows.value.length === 0) {
    ElMessage.info('当前无可导出的批次数据')
    return
  }
  const stamp = new Date().toISOString().slice(0, 10)
  exportCsv(`批次库存-${stamp}`, csvColumns.value, rows.value)
  ElMessage.success('批次库存已导出')
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
  <QlxPagePanel title="批次库存" subtitle="商品 + 仓库必选 · FEFO 顺序由后端返回">
    <template #toolbar>
      <el-button :disabled="rows.length === 0" @click="onExport">
        <AppIcon name="Files" :size="14" />
        导出 CSV
      </el-button>
    </template>

    <QlxSearchBar :fields="searchFields" :model-value="query" @search="onSearch" @reset="reset" />

    <QlxDataTable
      :items="rows"
      :columns="columns"
      :loading="loading"
      :total="total"
      :page="page"
      :page-size="pageSize"
      :empty-text="emptyText"
      @page-change="changePage"
    >
      <template #cell-produceDate="{ row }">{{ formatDateOnly(row.produceDate) }}</template>
      <template #cell-expiryDate="{ row }">{{ formatDateOnly(row.expiryDate) }}</template>
      <template #cell-qty="{ row }">
        <span class="qlx-numeric">{{ formatQty(row.qty) }}</span>
      </template>
      <template #cell-avgCost="{ row }">
        <span class="qlx-numeric">{{ formatCost(row.avgCost) }}</span>
      </template>
      <template #cell-amount="{ row }">
        <span class="qlx-numeric">{{ formatMoney(row.amount) }}</span>
      </template>
    </QlxDataTable>
  </QlxPagePanel>
</template>
