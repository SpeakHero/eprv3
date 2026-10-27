<!--
  所属公司：四川淇利馨集团有限公司
  描述：盘盈盘亏——单据列表 + 新建（盘盈 / 盘亏）+ 明细 + 提交审批 + 状态流转
  说明：
    1. 单据号由后端生成，只读展示，表单无单号输入框（N-1）；
    2. 金额由后端按「当前均价 → 参考成本 → 0」估算，前端只做展示格式化（B-2）；
    3. 提交审批后由后端在审批终态同事务入队库存命令（A-17），前端不判金额、不写台账；
    4. 批次启用商品必须填写批次号（与后端校验一致）。
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
import { inventoryAdjustApi, warehouseApi } from '@/api/wms'
import { useLocalTable } from '@/composables/useLocalTable'
import type { ItemRow } from '@/types/stk'
import type {
  CreateInventoryAdjustLinePayload,
  CreateInventoryAdjustPayload,
  InventoryAdjustDetailRow,
  InventoryAdjustRow,
  InventoryAdjustType,
  WarehouseRow,
} from '@/types/wms'
import { exportCsv } from '@/utils/csv-export'
import type { CsvColumn } from '@/utils/csv-export'
import { formatDateTime } from '@/utils/format'
import { formatCost, formatMoney, formatQty } from '@/utils/stk-enums'
import {
  INVENTORY_ADJUST_STATUS_LABELS,
  INVENTORY_ADJUST_TYPE_LABELS,
  inventoryAdjustStatusLabel,
  inventoryAdjustStatusTone,
  inventoryAdjustTypeLabel,
  inventoryAdjustTypeTone,
  toNumberOptions,
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

function batchEnabled(itemId: string): boolean {
  return items.value.find((row) => row.id === itemId)?.batchEnabled ?? false
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
 * 二、单据列表
 * ============================================================ */

const searchFields = computed<SearchField[]>(() => [
  {
    prop: 'type',
    label: '类型',
    type: 'select',
    options: toStringOptions(INVENTORY_ADJUST_TYPE_LABELS),
    width: 120,
  },
  {
    prop: 'status',
    label: '状态',
    type: 'select',
    options: toStringOptions(INVENTORY_ADJUST_STATUS_LABELS),
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
} = useLocalTable<InventoryAdjustRow>((params) =>
  inventoryAdjustApi.list({
    type: params.type ? Number(params.type) : undefined,
    status: params.status ? Number(params.status) : undefined,
    warehouseId: params.warehouseId || undefined,
  }),
)

const columns: Array<CrudColumn<InventoryAdjustRow>> = [
  { prop: 'adjustNo', label: '单据号', type: 'code', width: 180, fixed: 'left' },
  {
    prop: 'type',
    label: '类型',
    type: 'tag',
    width: 90,
    align: 'center',
    tagText: (row) => inventoryAdjustTypeLabel(row.type),
    tagTone: (row) => inventoryAdjustTypeTone(row.type),
  },
  { prop: 'warehouseId', label: '仓库', minWidth: 160 },
  { prop: 'reason', label: '调整原因', minWidth: 200 },
  { prop: 'totalAmount', label: '估算金额', width: 140, align: 'right' },
  {
    prop: 'status',
    label: '状态',
    type: 'tag',
    width: 110,
    align: 'center',
    tagText: (row) => inventoryAdjustStatusLabel(row.status),
    tagTone: (row) => inventoryAdjustStatusTone(row.status),
  },
  { prop: 'createdAt', label: '创建时间', type: 'time', width: 170 },
]

const csvColumns: Array<CsvColumn<InventoryAdjustRow>> = [
  { title: '单据号', value: (row) => row.adjustNo },
  { title: '类型', value: (row) => inventoryAdjustTypeLabel(row.type) },
  { title: '仓库', value: (row) => warehouseName(row.warehouseId) },
  { title: '调整原因', value: (row) => row.reason },
  { title: '估算金额', value: (row) => formatMoney(row.totalAmount) },
  { title: '状态', value: (row) => inventoryAdjustStatusLabel(row.status) },
  { title: '创建时间', value: (row) => formatDateTime(row.createdAt) },
]

function onExport(): void {
  exportCsv('盘盈盘亏', csvColumns, rows.value)
}

/* ============================================================
 * 三、新建单据（不含单号，N-1）
 * ============================================================ */

interface AdjustLineDraft {
  itemId: string
  batchNo: string
  qty: number | undefined
  remark: string
}

const createOpen = ref(false)
const submitting = ref(false)
const form = reactive<{
  type: InventoryAdjustType
  warehouseId: string
  reason: string
  remark: string
}>({
  type: 1,
  warehouseId: '',
  reason: '',
  remark: '',
})
const lineDrafts = ref<AdjustLineDraft[]>([])

function newLineDraft(): AdjustLineDraft {
  return { itemId: '', batchNo: '', qty: undefined, remark: '' }
}

function resetForm(): void {
  form.type = 1
  form.warehouseId = ''
  form.reason = ''
  form.remark = ''
  lineDrafts.value = [newLineDraft()]
}

function openCreate(): void {
  resetForm()
  createOpen.value = true
}

function addLine(): void {
  lineDrafts.value.push(newLineDraft())
}

function removeLine(index: number): void {
  lineDrafts.value.splice(index, 1)
}

function onItemChange(line: AdjustLineDraft): void {
  if (!batchEnabled(line.itemId)) {
    line.batchNo = ''
  }
}

function buildLines(): CreateInventoryAdjustLinePayload[] | null {
  if (lineDrafts.value.length === 0) {
    ElMessage.warning('请至少添加一行明细')
    return null
  }
  const lines: CreateInventoryAdjustLinePayload[] = []
  for (let index = 0; index < lineDrafts.value.length; index++) {
    const line = lineDrafts.value[index]
    const lineNo = index + 1
    if (!line.itemId) {
      ElMessage.warning(`第 ${lineNo} 行请选择商品`)
      return null
    }
    if (line.qty === undefined || !Number.isFinite(line.qty) || line.qty <= 0) {
      ElMessage.warning(`第 ${lineNo} 行数量必须大于 0`)
      return null
    }
    const batchNo = line.batchNo.trim()
    if (batchEnabled(line.itemId) && !batchNo) {
      ElMessage.warning(`第 ${lineNo} 行商品启用批次管理，必须填写批次号`)
      return null
    }
    lines.push({
      itemId: line.itemId,
      batchNo: batchNo || null,
      qty: line.qty,
      remark: line.remark.trim() || null,
    })
  }
  return lines
}

async function onCreate(): Promise<void> {
  if (!form.warehouseId) {
    ElMessage.warning('请选择仓库')
    return
  }
  const reason = form.reason.trim()
  if (!reason) {
    ElMessage.warning('请填写调整原因')
    return
  }
  if (reason.length > 256) {
    ElMessage.warning('调整原因长度不能超过 256 个字符')
    return
  }
  const lines = buildLines()
  if (!lines) {
    return
  }
  submitting.value = true
  try {
    const payload: CreateInventoryAdjustPayload = {
      type: form.type,
      warehouseId: form.warehouseId,
      reason,
      remark: form.remark.trim() || null,
      lines,
    }
    await inventoryAdjustApi.create(payload)
    createOpen.value = false
    await refresh()
    ElMessage.success('盘盈盘亏草稿已创建（金额由后端按当前均价估算）')
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : '创建盘盈盘亏单失败')
  } finally {
    submitting.value = false
  }
}

async function onSubmit(row: InventoryAdjustRow): Promise<void> {
  try {
    await ElMessageBox.confirm(
      `提交单据「${row.adjustNo}」将发起审批，审批通过后由系统写入库存台账，是否继续？`,
      '提交审批',
      { confirmButtonText: '提交', cancelButtonText: '返回', type: 'warning' },
    )
  } catch {
    return
  }
  try {
    await inventoryAdjustApi.submit(row.id)
    await refresh()
    ElMessage.success('已提交审批，审批通过后自动写库存台账')
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : '提交审批失败')
  }
}

/* ============================================================
 * 四、详情抽屉
 * ============================================================ */

const detailOpen = ref(false)
const detailLoading = ref(false)
const detail = ref<InventoryAdjustDetailRow | null>(null)

async function openDetail(row: InventoryAdjustRow): Promise<void> {
  detailOpen.value = true
  detailLoading.value = true
  detail.value = null
  try {
    detail.value = await inventoryAdjustApi.get(row.id)
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : '加载单据详情失败')
  } finally {
    detailLoading.value = false
  }
}

onMounted(loadOptions)
</script>

<template>
  <QlxPagePanel title="盘盈盘亏" subtitle="手工建单（金额由后端估算）→ 提交审批 → 审批通过后写库存台账（单号由系统生成）">
    <template #toolbar>
      <el-button v-permission="'wms.adjust.action'" type="primary" @click="openCreate">
        <AppIcon name="Plus" :size="14" />
        新增盘盈盘亏
      </el-button>
      <el-button @click="onExport">导出 CSV</el-button>
    </template>

    <QlxSearchBar :fields="searchFields" :model-value="query" @search="search" @reset="reset" />

    <QlxDataTable
      :items="rows"
      :columns="columns"
      :loading="loading"
      :total="total"
      :page="page"
      :page-size="pageSize"
      :actions-width="200"
      empty-text="暂无盘盈盘亏单据"
      @page-change="changePage"
    >
      <template #cell-adjustNo="{ row }">
        <span class="qlx-numeric">{{ row.adjustNo }}</span>
      </template>
      <template #cell-warehouseId="{ row }">{{ warehouseName(row.warehouseId) }}</template>
      <template #cell-totalAmount="{ row }">
        <span class="qlx-numeric">{{ formatMoney(row.totalAmount) }}</span>
      </template>
      <template #cell-createdAt="{ row }">{{ formatDateTime(row.createdAt) }}</template>
      <template #actions="{ row }">
        <button type="button" class="cell-link" @click="openDetail(row)">详情</button>
        <template v-if="row.status === 1">
          <el-divider direction="vertical" />
          <button
            v-permission="'wms.adjust.action'"
            type="button"
            class="cell-link"
            @click="onSubmit(row)"
          >
            提交审批
          </button>
        </template>
      </template>
    </QlxDataTable>

    <!-- 新建盘盈盘亏 -->
    <el-dialog
      v-model="createOpen"
      title="新增盘盈盘亏"
      width="980px"
      :close-on-click-modal="false"
      append-to-body
    >
      <el-form label-width="100px">
        <el-row :gutter="16">
          <el-col :span="8">
            <el-form-item label="类型" required>
              <el-select v-model="form.type" style="width: 100%">
                <el-option
                  v-for="option in toNumberOptions(INVENTORY_ADJUST_TYPE_LABELS)"
                  :key="option.value"
                  :label="option.label"
                  :value="option.value"
                />
              </el-select>
            </el-form-item>
          </el-col>
          <el-col :span="16">
            <el-form-item label="仓库" required>
              <el-select v-model="form.warehouseId" filterable placeholder="请选择仓库" style="width: 100%">
                <el-option
                  v-for="option in warehouseOptions"
                  :key="option.value"
                  :label="option.label"
                  :value="option.value"
                />
              </el-select>
            </el-form-item>
          </el-col>
        </el-row>
        <el-form-item label="调整原因" required>
          <el-input v-model="form.reason" maxlength="256" show-word-limit clearable placeholder="如：盘点差异、报损核实" />
        </el-form-item>
        <el-form-item label="备注">
          <el-input v-model="form.remark" maxlength="256" clearable placeholder="可选" />
        </el-form-item>
      </el-form>

      <div class="line-header">
        <span class="line-header__title">明细（{{ lineDrafts.length }}）</span>
        <el-button size="small" @click="addLine">
          <AppIcon name="Plus" :size="12" />
          添加明细
        </el-button>
      </div>

      <el-table :data="lineDrafts" size="small" border empty-text="请添加明细">
        <el-table-column label="#" width="50" align="center">
          <template #default="{ $index }">{{ $index + 1 }}</template>
        </el-table-column>
        <el-table-column label="商品" min-width="220">
          <template #default="{ row }">
            <el-select
              v-model="row.itemId"
              filterable
              placeholder="请选择商品"
              style="width: 100%"
              @change="onItemChange(row)"
            >
              <el-option
                v-for="option in itemOptions"
                :key="option.value"
                :label="option.label"
                :value="option.value"
              />
            </el-select>
          </template>
        </el-table-column>
        <el-table-column label="批次号" width="150">
          <template #default="{ row }">
            <el-input
              v-model="row.batchNo"
              maxlength="64"
              :disabled="!batchEnabled(row.itemId)"
              :placeholder="batchEnabled(row.itemId) ? '批次必填' : '该商品未启用批次'"
            />
          </template>
        </el-table-column>
        <el-table-column label="数量" width="160">
          <template #default="{ row }">
            <el-input-number v-model="row.qty" :min="0" :precision="4" style="width: 100%" />
          </template>
        </el-table-column>
        <el-table-column label="行备注" min-width="180">
          <template #default="{ row }">
            <el-input v-model="row.remark" maxlength="256" placeholder="可选" />
          </template>
        </el-table-column>
        <el-table-column label="操作" width="80" align="center">
          <template #default="{ $index }">
            <button type="button" class="cell-link is-danger" @click="removeLine($index)">删除</button>
          </template>
        </el-table-column>
      </el-table>
      <p class="task-hint">数量必须大于 0；启用批次管理的商品必须填写批次号；单据金额由后端按当前均价估算。</p>

      <template #footer>
        <el-button @click="createOpen = false">取消</el-button>
        <el-button type="primary" :loading="submitting" @click="onCreate">创建草稿</el-button>
      </template>
    </el-dialog>

    <!-- 详情抽屉 -->
    <el-drawer
      v-model="detailOpen"
      :title="detail ? `盘盈盘亏详情 · ${detail.adjust.adjustNo}` : '盘盈盘亏详情'"
      size="820px"
      append-to-body
    >
      <div v-loading="detailLoading" class="task-detail">
        <template v-if="detail">
          <el-descriptions :column="2" border>
            <el-descriptions-item label="单据号">
              <span class="qlx-numeric">{{ detail.adjust.adjustNo }}</span>
            </el-descriptions-item>
            <el-descriptions-item label="状态">
              {{ inventoryAdjustStatusLabel(detail.adjust.status) }}
            </el-descriptions-item>
            <el-descriptions-item label="类型">
              {{ inventoryAdjustTypeLabel(detail.adjust.type) }}
            </el-descriptions-item>
            <el-descriptions-item label="仓库">
              {{ warehouseName(detail.adjust.warehouseId) }}
            </el-descriptions-item>
            <el-descriptions-item label="估算金额">
              <span class="qlx-numeric">{{ formatMoney(detail.adjust.totalAmount) }}</span>
            </el-descriptions-item>
            <el-descriptions-item label="来源">
              {{ detail.adjust.takeId ? '盘点自动生成' : '手工建单' }}
            </el-descriptions-item>
            <el-descriptions-item label="调整原因" :span="2">
              {{ detail.adjust.reason }}
            </el-descriptions-item>
            <el-descriptions-item label="备注" :span="2">
              {{ detail.adjust.remark ?? '—' }}
            </el-descriptions-item>
            <el-descriptions-item label="创建时间" :span="2">
              {{ formatDateTime(detail.adjust.createdAt) }}
            </el-descriptions-item>
          </el-descriptions>

          <h3 class="task-detail__title">明细（{{ detail.lines.length }}）</h3>
          <el-table :data="detail.lines" size="small" border empty-text="暂无明细">
            <el-table-column prop="lineNo" label="行号" width="70" align="center" />
            <el-table-column label="商品" min-width="210">
              <template #default="{ row }">{{ itemLabel(row.itemId) }}</template>
            </el-table-column>
            <el-table-column label="批次号" width="130">
              <template #default="{ row }">{{ row.batchNo ?? '—' }}</template>
            </el-table-column>
            <el-table-column label="数量" width="110" align="right">
              <template #default="{ row }">
                <span class="qlx-numeric">{{ formatQty(row.qty) }}</span>
              </template>
            </el-table-column>
            <el-table-column label="单位成本" width="120" align="right">
              <template #default="{ row }">
                <span class="qlx-numeric">{{ formatCost(row.unitCost) }}</span>
              </template>
            </el-table-column>
            <el-table-column label="行备注" min-width="160">
              <template #default="{ row }">{{ row.remark ?? '—' }}</template>
            </el-table-column>
          </el-table>
        </template>
      </div>
    </el-drawer>
  </QlxPagePanel>
</template>

<style scoped>
.line-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  margin: var(--qlx-space-3) 0 var(--qlx-space-2);
}

.line-header__title {
  color: var(--qlx-ink-600);
  font-size: var(--qlx-text-sm);
  font-weight: 600;
}

.task-detail__title {
  margin: var(--qlx-space-4) 0 var(--qlx-space-2);
  color: var(--qlx-ink-600);
  font-size: var(--qlx-text-sm);
  font-weight: 600;
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
