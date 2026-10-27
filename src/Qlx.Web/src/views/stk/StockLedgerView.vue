<!--
  所属公司：四川淇利馨集团有限公司
  描述：库存台账——按商品 / 仓库筛选的余额扁平列表 + 可用量展示 + 成本字段权限裁剪 + CSV 导出
  说明：
    1. 后端下发扁平余额行（无行键），前端补展示行键并做展示分页（B-4）；
    2. 可用量 = 现有量 − 冻结量，仅为界面展示，真实校验以服务端为准；
    3. 无 stk.cost.field 权限时隐藏均价 / 金额列与导出列。
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
import type { ItemRow, StockBalanceTableRow } from '@/types/stk'
import type { WarehouseRow } from '@/types/wms'
import type { CsvColumn } from '@/utils/csv-export'
import { exportCsv } from '@/utils/csv-export'
import { availableQty, formatCost, formatMoney, formatQty } from '@/utils/stk-enums'

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
  useLocalTable<StockBalanceTableRow>(
    async (params) => {
      const data = await stockApi.ledger({
        itemId: params.itemId || undefined,
        warehouseId: params.warehouseId || undefined,
      })
      return data.map((row) => ({ ...row, id: `${row.itemId}-${row.warehouseId}` }))
    },
  )

function itemLabel(id: string): string {
  const item = items.value.find((row) => row.id === id)
  return item ? `${item.itemCode} · ${item.name}` : '—'
}

function warehouseName(id: string): string {
  return warehouses.value.find((warehouse) => warehouse.id === id)?.name ?? '—'
}

const columns = computed<Array<CrudColumn<StockBalanceTableRow>>>(() => [
  { prop: 'itemId', label: '商品', minWidth: 220 },
  { prop: 'warehouseId', label: '仓库', minWidth: 160 },
  { prop: 'qtyOnhand', label: '现有量', width: 130, align: 'right' },
  { prop: 'qtyLocked', label: '冻结量', width: 120, align: 'right' },
  { prop: 'available', label: '可用量', width: 130, align: 'right' },
  ...(canViewCost.value
    ? ([
        { prop: 'avgCost', label: '移动平均价', width: 140, align: 'right' },
        { prop: 'amount', label: '库存金额', width: 150, align: 'right' },
      ] as Array<CrudColumn<StockBalanceTableRow>>)
    : []),
  { prop: 'lastTxnAt', label: '最近变动', type: 'time', width: 170 },
])

const csvColumns = computed<Array<CsvColumn<StockBalanceTableRow>>>(() => {
  const base: Array<CsvColumn<StockBalanceTableRow>> = [
    { title: '商品编码', value: (row) => items.value.find((item) => item.id === row.itemId)?.itemCode ?? '' },
    { title: '商品名称', value: (row) => items.value.find((item) => item.id === row.itemId)?.name ?? '' },
    { title: '仓库', value: (row) => warehouseName(row.warehouseId) },
    { title: '现有量', value: (row) => row.qtyOnhand },
    { title: '冻结量', value: (row) => row.qtyLocked },
    { title: '可用量', value: (row) => availableQty(row.qtyOnhand, row.qtyLocked) },
  ]
  if (canViewCost.value) {
    base.push(
      { title: '移动平均价', value: (row) => row.avgCost ?? '' },
      { title: '库存金额', value: (row) => row.amount ?? '' },
    )
  }
  base.push({
    title: '最近变动',
    value: (row) => (row.lastTxnAt ? row.lastTxnAt : ''),
  })
  return base
})

function onExport(): void {
  if (rows.value.length === 0) {
    ElMessage.info('当前无可导出的台账数据')
    return
  }
  const stamp = new Date().toISOString().slice(0, 10)
  exportCsv(`库存台账-${stamp}`, csvColumns.value, rows.value)
  ElMessage.success('库存台账已导出')
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
  <QlxPagePanel title="库存台账" subtitle="按商品 / 仓库查询余额与可用量">
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
      empty-text="暂无台账数据"
      @page-change="changePage"
    >
      <template #cell-itemId="{ row }">{{ itemLabel(row.itemId) }}</template>
      <template #cell-warehouseId="{ row }">{{ warehouseName(row.warehouseId) }}</template>
      <template #cell-qtyOnhand="{ row }">
        <span class="qlx-numeric">{{ formatQty(row.qtyOnhand) }}</span>
      </template>
      <template #cell-qtyLocked="{ row }">
        <span class="qlx-numeric">{{ formatQty(row.qtyLocked) }}</span>
      </template>
      <template #cell-available="{ row }">
        <span class="qlx-numeric is-strong">
          {{ formatQty(availableQty(row.qtyOnhand, row.qtyLocked)) }}
        </span>
      </template>
      <template #cell-avgCost="{ row }">
        <span class="qlx-numeric">{{ formatCost(row.avgCost) }}</span>
      </template>
      <template #cell-amount="{ row }">
        <span class="qlx-numeric">{{ formatMoney(row.amount) }}</span>
      </template>
      <template #cell-lastTxnAt="{ row }">
        {{ row.lastTxnAt ? row.lastTxnAt.replace('T', ' ').slice(0, 19) : '—' }}
      </template>
    </QlxDataTable>
  </QlxPagePanel>
</template>

<style scoped>
.qlx-numeric.is-strong {
  font-weight: 600;
}
</style>
