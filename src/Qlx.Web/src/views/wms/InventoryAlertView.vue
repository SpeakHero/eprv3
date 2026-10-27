<!--
  所属公司：四川淇利馨集团有限公司
  描述：库存预警——四类卡片看板（安全库存 / 积压 / 临期 / 在途超期）+ 预警列表 + 处理 / 忽略回填
  说明：
    1. 预警仅由后端扫描任务（wms.alert.scan）生成，前端不触发扫描、不写台账；
    2. 阈值 / 实际值为后端原始数值，单位文案与格式化由前端判定（B-4）；
    3. 只有「待处理」可处理或忽略；处理说明必填且不超过 256 字（与后端校验一致）。
-->
<script setup lang="ts">
import { computed, onMounted, reactive, ref } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import QlxPagePanel from '@/components/crud/QlxPagePanel.vue'
import QlxSearchBar from '@/components/crud/QlxSearchBar.vue'
import type { SearchField } from '@/components/crud/QlxSearchBar.vue'
import QlxDataTable from '@/components/crud/QlxDataTable.vue'
import type { CrudColumn } from '@/components/crud/QlxDataTable.vue'
import { itemApi } from '@/api/stk'
import { inventoryAlertApi, warehouseApi } from '@/api/wms'
import { useLocalTable } from '@/composables/useLocalTable'
import type { ItemRow } from '@/types/stk'
import type {
  InventoryAlertRow,
  InventoryAlertType,
  WarehouseRow,
} from '@/types/wms'
import { exportCsv } from '@/utils/csv-export'
import type { CsvColumn } from '@/utils/csv-export'
import { formatDateTime } from '@/utils/format'
import { formatQty } from '@/utils/stk-enums'
import {
  INVENTORY_ALERT_STATUS_LABELS,
  INVENTORY_ALERT_TYPE_LABELS,
  inventoryAlertStatusLabel,
  inventoryAlertStatusTone,
  inventoryAlertTypeLabel,
  inventoryAlertTypeTone,
  toStringOptions,
} from '@/utils/wms-enums'

/* ============================================================
 * 一、基础下拉
 * ============================================================ */

const warehouses = ref<WarehouseRow[]>([])
const items = ref<ItemRow[]>([])

const warehouseOptions = computed(() =>
  warehouses.value.map((warehouse) => ({ label: warehouse.name, value: warehouse.id })),
)

function warehouseName(id: string): string {
  return warehouses.value.find((warehouse) => warehouse.id === id)?.name ?? '—'
}

function itemLabel(id: string): string {
  const item = items.value.find((row) => row.id === id)
  return item ? `${item.itemCode} · ${item.name}` : '—'
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
 * 二、卡片看板
 * ============================================================ */

const ALERT_TYPES: InventoryAlertType[] = [1, 2, 3, 4]

const searchFields = computed<SearchField[]>(() => [
  {
    prop: 'alertType',
    label: '预警类型',
    type: 'select',
    options: toStringOptions(INVENTORY_ALERT_TYPE_LABELS),
    width: 140,
  },
  {
    prop: 'status',
    label: '状态',
    type: 'select',
    options: toStringOptions(INVENTORY_ALERT_STATUS_LABELS),
    width: 130,
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
  loading,
  rows,
  total,
  page,
  pageSize,
  query,
  search,
  reset,
  refresh,
  changePage,
} = useLocalTable<InventoryAlertRow>((params) =>
  inventoryAlertApi.list({
    alertType: params.alertType ? Number(params.alertType) : undefined,
    status: params.status ? Number(params.status) : undefined,
    warehouseId: params.warehouseId || undefined,
  }),
)

/** 各类型预警条数（按当前查询结果统计，展示层计算 B-4）。 */
const typeCounts = computed<Record<InventoryAlertType, number>>(() => {
  const counts: Record<InventoryAlertType, number> = { 1: 0, 2: 0, 3: 0, 4: 0 }
  for (const row of rows.value) {
    counts[row.alertType] += 1
  }
  return counts
})

/** 点击卡片按类型筛选；再次点击取消该条件。 */
async function onCardClick(type: InventoryAlertType): Promise<void> {
  const next: Record<string, string> = { ...query.value }
  if (next.alertType === String(type)) {
    delete next.alertType
  } else {
    next.alertType = String(type)
  }
  await search(next)
}

/** 阈值 / 实际值单位：临期与在途超期为天，其余为数量。 */
function alertUnit(type: InventoryAlertType): string {
  return type === 3 || type === 4 ? '天' : ''
}

/* ============================================================
 * 三、预警列表
 * ============================================================ */

const columns: Array<CrudColumn<InventoryAlertRow>> = [
  {
    prop: 'alertType',
    label: '预警类型',
    type: 'tag',
    width: 120,
    align: 'center',
    tagText: (row) => inventoryAlertTypeLabel(row.alertType),
    tagTone: (row) => inventoryAlertTypeTone(row.alertType),
  },
  { prop: 'itemId', label: '商品', minWidth: 200 },
  { prop: 'warehouseId', label: '仓库', minWidth: 150 },
  { prop: 'batchNo', label: '批次号', width: 130 },
  { prop: 'threshold', label: '阈值', width: 110, align: 'right' },
  { prop: 'actualValue', label: '实际值', width: 110, align: 'right' },
  { prop: 'alertDate', label: '预警日期', width: 130 },
  {
    prop: 'status',
    label: '状态',
    type: 'tag',
    width: 100,
    align: 'center',
    tagText: (row) => inventoryAlertStatusLabel(row.status),
    tagTone: (row) => inventoryAlertStatusTone(row.status),
  },
  { prop: 'handledNote', label: '处理说明', minWidth: 180 },
  { prop: 'handledAt', label: '处理时间', type: 'time', width: 170 },
]

const csvColumns: Array<CsvColumn<InventoryAlertRow>> = [
  { title: '预警类型', value: (row) => inventoryAlertTypeLabel(row.alertType) },
  { title: '商品', value: (row) => itemLabel(row.itemId) },
  { title: '仓库', value: (row) => warehouseName(row.warehouseId) },
  { title: '批次号', value: (row) => row.batchNo },
  { title: '阈值', value: (row) => `${formatQty(row.threshold)}${alertUnit(row.alertType)}` },
  { title: '实际值', value: (row) => `${formatQty(row.actualValue)}${alertUnit(row.alertType)}` },
  { title: '预警日期', value: (row) => row.alertDate },
  { title: '状态', value: (row) => inventoryAlertStatusLabel(row.status) },
  { title: '处理说明', value: (row) => row.handledNote },
  { title: '处理时间', value: (row) => (row.handledAt ? formatDateTime(row.handledAt) : '') },
]

function onExport(): void {
  exportCsv('库存预警', csvColumns, rows.value)
}

/* ============================================================
 * 四、处理 / 忽略
 * ============================================================ */

const handleOpen = ref(false)
const handleSubmitting = ref(false)
const handling = ref<InventoryAlertRow | null>(null)
const handleForm = reactive({
  handledNote: '',
})

function openHandle(row: InventoryAlertRow): void {
  handling.value = row
  handleForm.handledNote = ''
  handleOpen.value = true
}

async function onHandle(): Promise<void> {
  if (!handling.value) {
    return
  }
  const note = handleForm.handledNote.trim()
  if (!note) {
    ElMessage.warning('请填写处理说明')
    return
  }
  if (note.length > 256) {
    ElMessage.warning('处理说明长度不能超过 256 个字符')
    return
  }
  handleSubmitting.value = true
  try {
    await inventoryAlertApi.handle(handling.value.id, { handledNote: note })
    handleOpen.value = false
    await refresh()
    ElMessage.success('预警已处理')
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : '处理预警失败')
  } finally {
    handleSubmitting.value = false
  }
}

async function onIgnore(row: InventoryAlertRow): Promise<void> {
  try {
    await ElMessageBox.confirm(
      `忽略预警「${inventoryAlertTypeLabel(row.alertType)} · ${itemLabel(row.itemId)}」后不再提示，是否继续？`,
      '忽略预警',
      { confirmButtonText: '忽略', cancelButtonText: '返回', type: 'warning' },
    )
  } catch {
    return
  }
  try {
    await inventoryAlertApi.ignore(row.id)
    await refresh()
    ElMessage.success('预警已忽略')
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : '忽略预警失败')
  }
}

onMounted(loadOptions)
</script>

<template>
  <QlxPagePanel title="库存预警" subtitle="安全库存 · 积压 · 临期 · 在途超期（由后端扫描任务生成，前端仅查询与处理）">
    <template #toolbar>
      <el-button @click="onExport">导出 CSV</el-button>
    </template>

    <div class="alert-cards">
      <button
        v-for="type in ALERT_TYPES"
        :key="type"
        type="button"
        class="alert-card"
        :class="{ 'is-active': query.alertType === String(type) }"
        :data-tone="inventoryAlertTypeTone(type)"
        @click="onCardClick(type)"
      >
        <span class="alert-card__label">{{ inventoryAlertTypeLabel(type) }}</span>
        <span class="alert-card__count">{{ typeCounts[type] }}</span>
        <span class="alert-card__hint">
          {{ type === 3 || type === 4 ? '剩余天数低于阈值' : '库存数量触发阈值' }}
        </span>
      </button>
    </div>

    <QlxSearchBar :fields="searchFields" :model-value="query" @search="search" @reset="reset" />

    <QlxDataTable
      :items="rows"
      :columns="columns"
      :loading="loading"
      :total="total"
      :page="page"
      :page-size="pageSize"
      :actions-width="160"
      empty-text="暂无库存预警"
      @page-change="changePage"
    >
      <template #cell-itemId="{ row }">{{ itemLabel(row.itemId) }}</template>
      <template #cell-warehouseId="{ row }">{{ warehouseName(row.warehouseId) }}</template>
      <template #cell-batchNo="{ row }">{{ row.batchNo || '—' }}</template>
      <template #cell-threshold="{ row }">
        <span class="qlx-numeric">{{ formatQty(row.threshold) }}{{ alertUnit(row.alertType) }}</span>
      </template>
      <template #cell-actualValue="{ row }">
        <span class="qlx-numeric">{{ formatQty(row.actualValue) }}{{ alertUnit(row.alertType) }}</span>
      </template>
      <template #cell-handledNote="{ row }">{{ row.handledNote ?? '—' }}</template>
      <template #cell-handledAt="{ row }">
        {{ row.handledAt ? formatDateTime(row.handledAt) : '—' }}
      </template>
      <template #actions="{ row }">
        <template v-if="row.status === 1">
          <button
            v-permission="'wms.alert.action'"
            type="button"
            class="cell-link"
            @click="openHandle(row)"
          >
            处理
          </button>
          <el-divider direction="vertical" />
          <button
            v-permission="'wms.alert.action'"
            type="button"
            class="cell-link is-danger"
            @click="onIgnore(row)"
          >
            忽略
          </button>
        </template>
        <span v-else class="cell-muted">已闭环</span>
      </template>
    </QlxDataTable>

    <!-- 处理预警 -->
    <el-dialog
      v-model="handleOpen"
      :title="
        handling
          ? `处理预警 · ${inventoryAlertTypeLabel(handling.alertType)}`
          : '处理预警'
      "
      width="560px"
      :close-on-click-modal="false"
      append-to-body
    >
      <el-descriptions v-if="handling" :column="1" border size="small">
        <el-descriptions-item label="商品">{{ itemLabel(handling.itemId) }}</el-descriptions-item>
        <el-descriptions-item label="仓库">{{ warehouseName(handling.warehouseId) }}</el-descriptions-item>
        <el-descriptions-item label="批次号">{{ handling.batchNo || '—' }}</el-descriptions-item>
        <el-descriptions-item label="阈值 / 实际值">
          {{ formatQty(handling.threshold) }}{{ alertUnit(handling.alertType) }} /
          {{ formatQty(handling.actualValue) }}{{ alertUnit(handling.alertType) }}
        </el-descriptions-item>
        <el-descriptions-item label="预警日期">{{ handling.alertDate }}</el-descriptions-item>
      </el-descriptions>

      <el-form label-width="90px" class="handle-form">
        <el-form-item label="处理说明" required>
          <el-input
            v-model="handleForm.handledNote"
            type="textarea"
            :rows="3"
            maxlength="256"
            show-word-limit
            placeholder="如：已补货、已调拨、已报损核实"
          />
        </el-form-item>
      </el-form>

      <template #footer>
        <el-button @click="handleOpen = false">取消</el-button>
        <el-button type="primary" :loading="handleSubmitting" @click="onHandle">确认处理</el-button>
      </template>
    </el-dialog>
  </QlxPagePanel>
</template>

<style scoped>
.alert-cards {
  display: grid;
  grid-template-columns: repeat(4, minmax(0, 1fr));
  gap: var(--qlx-space-3);
  padding: var(--qlx-space-3) var(--qlx-space-4) 0;
}

.alert-card {
  display: flex;
  flex-direction: column;
  gap: 2px;
  padding: var(--qlx-space-3) var(--qlx-space-4);
  border: 1px solid var(--qlx-border);
  border-radius: var(--qlx-radius-md);
  background: var(--qlx-surface);
  text-align: left;
  cursor: pointer;
  transition: border-color 0.15s ease, box-shadow 0.15s ease;
}

.alert-card:hover {
  border-color: var(--qlx-brand-400);
}

.alert-card.is-active {
  border-color: var(--qlx-brand-600);
  box-shadow: 0 0 0 1px var(--qlx-brand-600) inset;
}

.alert-card__label {
  color: var(--qlx-ink-500);
  font-size: var(--qlx-text-sm);
}

.alert-card__count {
  color: var(--qlx-ink-800);
  font-size: 26px;
  font-weight: 600;
  line-height: 1.2;
}

.alert-card__hint {
  color: var(--qlx-ink-400);
  font-size: var(--qlx-text-xs);
}

.alert-card[data-tone='danger'] .alert-card__count {
  color: var(--qlx-danger);
}

.alert-card[data-tone='warning'] .alert-card__count {
  color: var(--qlx-warning, #b45309);
}

.handle-form {
  margin-top: var(--qlx-space-3);
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

.cell-muted {
  color: var(--qlx-ink-400);
  font-size: var(--qlx-text-sm);
}
</style>
