<!--
  所属公司：四川淇利馨集团有限公司
  描述：入库作业——任务池筛选 + 新建 + 开始 / 完成（清点确认）/ 取消 + 详情抽屉 + CSV 导出
  说明：
    1. 作业单号由后端生成，只读展示，新建表单无单号输入框（N-1）；
    2. 完成后由后端写库存台账（前端只提交清点 / 确认数量与库位批次，V-1）；
    3. 枚举中文与色调统一取 utils/wms-enums.ts（B-4），CSV 导出复用 utils/csv-export.ts。
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
import { stockInApi, warehouseApi } from '@/api/wms'
import { useLocalTable } from '@/composables/useLocalTable'
import type { ItemRow } from '@/types/stk'
import type {
  CompleteStockInLinePayload,
  CompleteStockInTaskPayload,
  CreateStockInLinePayload,
  CreateStockInTaskPayload,
  LocationRow,
  StockInSourceType,
  StockInTaskDetailRow,
  StockInTaskRow,
  WarehouseRow,
} from '@/types/wms'
import { exportCsv } from '@/utils/csv-export'
import type { CsvColumn } from '@/utils/csv-export'
import { formatCost, formatQty } from '@/utils/stk-enums'
import { formatDateTime } from '@/utils/format'
import {
  STOCK_IN_SOURCE_TYPE_LABELS,
  STOCK_IN_TASK_STATUS_LABELS,
  stockInSourceTypeLabel,
  stockInSourceTypeTone,
  stockInTaskStatusLabel,
  stockInTaskStatusTone,
  toNumberOptions,
  toStringOptions,
} from '@/utils/wms-enums'

/* ============================================================
 * 一、基础下拉（仓库 / 商品 / 库位）
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

function locationLabel(locationId: string | null): string {
  if (!locationId) {
    return '—'
  }
  return formLocations.value.find((location) => location.id === locationId)?.code ?? '—'
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
 * 二、任务池列表（backend 按状态 / 仓库 / 来源筛选）
 * ============================================================ */

const searchFields = computed<SearchField[]>(() => [
  {
    prop: 'status',
    label: '状态',
    type: 'select',
    options: toStringOptions(STOCK_IN_TASK_STATUS_LABELS),
    width: 140,
  },
  {
    prop: 'warehouseId',
    label: '仓库',
    type: 'select',
    options: warehouseOptions.value,
    width: 180,
  },
  {
    prop: 'sourceType',
    label: '来源',
    type: 'select',
    options: toStringOptions(STOCK_IN_SOURCE_TYPE_LABELS),
    width: 150,
  },
])

const { loading, rows, total, page, pageSize, query, search, reset, refresh, changePage } =
  useLocalTable<StockInTaskRow>((params) =>
    stockInApi.list({
      status: params.status ? Number(params.status) : undefined,
      warehouseId: params.warehouseId || undefined,
      sourceType: params.sourceType ? Number(params.sourceType) : undefined,
    }),
  )

const columns: Array<CrudColumn<StockInTaskRow>> = [
  { prop: 'taskNo', label: '作业单号', type: 'code', width: 180, fixed: 'left' },
  {
    prop: 'sourceType',
    label: '来源类型',
    type: 'tag',
    width: 120,
    align: 'center',
    tagText: (row) => stockInSourceTypeLabel(row.sourceType),
    tagTone: (row) => stockInSourceTypeTone(row.sourceType),
  },
  { prop: 'sourceDocNo', label: '来源单号', minWidth: 150 },
  { prop: 'warehouseId', label: '仓库', minWidth: 160 },
  {
    prop: 'status',
    label: '状态',
    type: 'tag',
    width: 100,
    align: 'center',
    tagText: (row) => stockInTaskStatusLabel(row.status),
    tagTone: (row) => stockInTaskStatusTone(row.status),
  },
  { prop: 'createdAt', label: '创建时间', type: 'time', width: 170 },
  { prop: 'remark', label: '备注', minWidth: 180 },
]

const csvColumns: Array<CsvColumn<StockInTaskRow>> = [
  { title: '作业单号', value: (row) => row.taskNo },
  { title: '来源类型', value: (row) => stockInSourceTypeLabel(row.sourceType) },
  { title: '来源单号', value: (row) => row.sourceDocNo },
  { title: '仓库', value: (row) => warehouseName(row.warehouseId) },
  { title: '状态', value: (row) => stockInTaskStatusLabel(row.status) },
  { title: '创建时间', value: (row) => formatDateTime(row.createdAt) },
  { title: '备注', value: (row) => row.remark },
]

function onExport(): void {
  exportCsv('入库作业', csvColumns, rows.value)
}

/* ============================================================
 * 三、新建入库作业（不含单号，N-1）
 * ============================================================ */

interface StockInLineDraft {
  itemId: string
  expectQty: number
  batchNo: string
  unitCost: number | undefined
}

const createOpen = ref(false)
const submitting = ref(false)
const form = reactive<{
  sourceType: StockInSourceType
  sourceDocNo: string
  warehouseId: string
  remark: string
  lines: StockInLineDraft[]
}>({ sourceType: 1, sourceDocNo: '', warehouseId: '', remark: '', lines: [] })

function resetForm(): void {
  form.sourceType = 1
  form.sourceDocNo = ''
  form.warehouseId = ''
  form.remark = ''
  form.lines = [{ itemId: '', expectQty: 1, batchNo: '', unitCost: undefined }]
}

function openCreate(): void {
  resetForm()
  createOpen.value = true
}

function addLine(): void {
  form.lines.push({ itemId: '', expectQty: 1, batchNo: '', unitCost: undefined })
}

function removeLine(index: number): void {
  form.lines.splice(index, 1)
}

async function onSubmitCreate(): Promise<void> {
  if (!form.warehouseId) {
    ElMessage.warning('请选择仓库')
    return
  }
  if (!form.sourceDocNo.trim()) {
    ElMessage.warning('请填写来源单号')
    return
  }
  if (form.lines.length === 0) {
    ElMessage.warning('请至少添加一条明细')
    return
  }
  const invalid = form.lines.some(
    (line) => !line.itemId || !Number.isFinite(line.expectQty) || line.expectQty <= 0,
  )
  if (invalid) {
    ElMessage.warning('请完整填写明细：商品必选、预期数量需大于 0')
    return
  }
  submitting.value = true
  try {
    const lines: CreateStockInLinePayload[] = form.lines.map((line) => ({
      itemId: line.itemId,
      expectQty: line.expectQty,
      locationId: null,
      batchNo: line.batchNo.trim() || null,
      unitCost: line.unitCost ?? null,
    }))
    const payload: CreateStockInTaskPayload = {
      sourceType: form.sourceType,
      sourceDocNo: form.sourceDocNo.trim(),
      warehouseId: form.warehouseId,
      remark: form.remark.trim() || null,
      lines,
    }
    await stockInApi.create(payload)
    createOpen.value = false
    await refresh()
    ElMessage.success('入库作业已创建（单号由系统生成）')
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : '创建入库作业失败')
  } finally {
    submitting.value = false
  }
}

/* ============================================================
 * 四、开始 / 完成 / 取消
 * ============================================================ */

async function onStart(row: StockInTaskRow): Promise<void> {
  try {
    await stockInApi.start(row.id)
    await refresh()
    ElMessage.success('入库作业已开始')
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : '开始作业失败')
  }
}

const completeOpen = ref(false)
const completeLoading = ref(false)
const completeSubmitting = ref(false)
const completing = ref<StockInTaskDetailRow | null>(null)
const formLocations = ref<LocationRow[]>([])

interface CompleteLineDraft {
  lineId: string
  countQty: number
  confirmQty: number
  locationId: string
  batchNo: string
  unitCost: number | undefined
}

const completeLines = ref<CompleteLineDraft[]>([])

async function openComplete(row: StockInTaskRow): Promise<void> {
  completeOpen.value = true
  completeLoading.value = true
  completing.value = null
  formLocations.value = []
  try {
    const detail = await stockInApi.get(row.id)
    completing.value = detail
    completeLines.value = detail.lines.map((line) => ({
      lineId: line.id,
      countQty: line.expectQty,
      confirmQty: line.expectQty,
      locationId: line.locationId ?? '',
      batchNo: line.batchNo ?? '',
      unitCost: line.unitCost ?? undefined,
    }))
    try {
      formLocations.value = await warehouseApi.listLocations(detail.task.warehouseId)
    } catch {
      formLocations.value = []
    }
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : '加载入库详情失败')
  } finally {
    completeLoading.value = false
  }
}

async function onSubmitComplete(): Promise<void> {
  if (!completing.value) {
    return
  }
  const invalid = completeLines.value.some(
    (line) =>
      !line.locationId ||
      !Number.isFinite(line.countQty) ||
      line.countQty < 0 ||
      line.confirmQty < 0 ||
      line.confirmQty > line.countQty,
  )
  if (invalid) {
    ElMessage.warning('请完整填写：库位必选、清点数量不得为负、确认数量不得超过清点数量')
    return
  }
  completeSubmitting.value = true
  try {
    const payload: CompleteStockInTaskPayload = {
      lines: completeLines.value.map<CompleteStockInLinePayload>((line) => ({
        lineId: line.lineId,
        countQty: line.countQty,
        confirmQty: line.confirmQty,
        locationId: line.locationId,
        batchNo: line.batchNo.trim() || null,
        unitCost: line.unitCost ?? null,
      })),
    }
    await stockInApi.complete(completing.value.task.id, payload)
    completeOpen.value = false
    await refresh()
    ElMessage.success('入库作业已完成，库存已入账')
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : '完成入库作业失败')
  } finally {
    completeSubmitting.value = false
  }
}

async function onCancel(row: StockInTaskRow): Promise<void> {
  try {
    await ElMessageBox.confirm(`取消入库作业「${row.taskNo}」后不可继续作业，是否继续？`, '取消入库作业', {
      confirmButtonText: '确认取消',
      cancelButtonText: '返回',
      type: 'warning',
    })
  } catch {
    return
  }
  try {
    await stockInApi.cancel(row.id)
    await refresh()
    ElMessage.success('入库作业已取消')
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : '取消入库作业失败')
  }
}

/* ============================================================
 * 五、详情抽屉
 * ============================================================ */

const detailOpen = ref(false)
const detailLoading = ref(false)
const detail = ref<StockInTaskDetailRow | null>(null)

async function openDetail(row: StockInTaskRow): Promise<void> {
  detailOpen.value = true
  detailLoading.value = true
  detail.value = null
  try {
    detail.value = await stockInApi.get(row.id)
    formLocations.value = await warehouseApi.listLocations(detail.value.task.warehouseId)
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : '加载入库详情失败')
  } finally {
    detailLoading.value = false
  }
}

onMounted(loadOptions)
</script>

<template>
  <QlxPagePanel title="入库作业" subtitle="任务池 · 清点确认 · 完成后计入库存台账（单号由系统生成）">
    <template #toolbar>
      <el-button v-permission="'wms.inbound.action'" type="primary" @click="openCreate">
        <AppIcon name="Plus" :size="14" />
        新增入库作业
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
      :actions-width="260"
      empty-text="暂无入库作业"
      @page-change="changePage"
    >
      <template #cell-taskNo="{ row }">
        <span class="qlx-numeric">{{ row.taskNo }}</span>
      </template>
      <template #cell-warehouseId="{ row }">{{ warehouseName(row.warehouseId) }}</template>
      <template #cell-createdAt="{ row }">{{ formatDateTime(row.createdAt) }}</template>
      <template #cell-remark="{ row }">{{ row.remark ?? '—' }}</template>
      <template #actions="{ row }">
        <button type="button" class="cell-link" @click="openDetail(row)">详情</button>
        <template v-if="row.status === 1">
          <el-divider direction="vertical" />
          <button
            v-permission="'wms.inbound.action'"
            type="button"
            class="cell-link"
            @click="onStart(row)"
          >
            开始
          </button>
        </template>
        <template v-if="row.status === 2">
          <el-divider direction="vertical" />
          <button
            v-permission="'wms.inbound.action'"
            type="button"
            class="cell-link"
            @click="openComplete(row)"
          >
            完成
          </button>
        </template>
        <template v-if="row.status === 1 || row.status === 2">
          <el-divider direction="vertical" />
          <button
            v-permission="'wms.inbound.action'"
            type="button"
            class="cell-link is-danger"
            @click="onCancel(row)"
          >
            取消
          </button>
        </template>
      </template>
    </QlxDataTable>

    <!-- 新建：动态明细行 -->
    <el-dialog
      v-model="createOpen"
      title="新增入库作业"
      width="900px"
      :close-on-click-modal="false"
      append-to-body
    >
      <el-form label-width="90px">
        <el-row :gutter="16">
          <el-col :span="8">
            <el-form-item label="来源类型" required>
              <el-select v-model="form.sourceType" style="width: 100%">
                <el-option
                  v-for="option in toNumberOptions(STOCK_IN_SOURCE_TYPE_LABELS)"
                  :key="option.value"
                  :label="option.label"
                  :value="option.value"
                />
              </el-select>
            </el-form-item>
          </el-col>
          <el-col :span="8">
            <el-form-item label="来源单号" required>
              <el-input v-model="form.sourceDocNo" maxlength="40" placeholder="采购单 / 退货单 / 调拨单号" />
            </el-form-item>
          </el-col>
          <el-col :span="8">
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
        <el-form-item label="备注">
          <el-input v-model="form.remark" maxlength="200" clearable placeholder="可选" />
        </el-form-item>

        <div class="task-lines">
          <div class="task-lines__head">
            <h3>明细（库位在完成时指定）</h3>
            <el-button size="small" @click="addLine">添加明细</el-button>
          </div>
          <el-table :data="form.lines" size="small" border empty-text="请添加明细">
            <el-table-column label="商品" min-width="240">
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
            <el-table-column label="预期数量" width="150">
              <template #default="{ row }">
                <el-input-number v-model="row.expectQty" :min="0" :precision="4" style="width: 100%" />
              </template>
            </el-table-column>
            <el-table-column label="批次号" width="160">
              <template #default="{ row }">
                <el-input v-model="row.batchNo" maxlength="40" placeholder="非批次商品留空" />
              </template>
            </el-table-column>
            <el-table-column label="入库单价" width="150">
              <template #default="{ row }">
                <el-input-number v-model="row.unitCost" :min="0" :precision="4" style="width: 100%" />
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
        <el-button type="primary" :loading="submitting" @click="onSubmitCreate">创建作业</el-button>
      </template>
    </el-dialog>

    <!-- 完成：清点 / 确认 / 库位 -->
    <el-dialog
      v-model="completeOpen"
      :title="completing ? `完成入库 · ${completing.task.taskNo}` : '完成入库'"
      width="980px"
      :close-on-click-modal="false"
      append-to-body
    >
      <div v-loading="completeLoading">
        <template v-if="completing">
          <el-descriptions :column="3" border size="small">
            <el-descriptions-item label="来源类型">
              {{ stockInSourceTypeLabel(completing.task.sourceType) }}
            </el-descriptions-item>
            <el-descriptions-item label="来源单号">
              <span class="qlx-numeric">{{ completing.task.sourceDocNo }}</span>
            </el-descriptions-item>
            <el-descriptions-item label="仓库">
              {{ warehouseName(completing.task.warehouseId) }}
            </el-descriptions-item>
          </el-descriptions>

          <el-table :data="completeLines" size="small" border class="task-lines__table">
            <el-table-column label="商品" min-width="200">
              <template #default="{ row }">
                {{ itemLabel(completing.lines.find((line) => line.id === row.lineId)?.itemId ?? '') }}
              </template>
            </el-table-column>
            <el-table-column label="清点数量" width="150">
              <template #default="{ row }">
                <el-input-number
                  v-model="row.countQty"
                  :min="0"
                  :precision="4"
                  style="width: 100%"
                  @change="row.confirmQty = row.countQty"
                />
              </template>
            </el-table-column>
            <el-table-column label="确认数量" width="150">
              <template #default="{ row }">
                <el-input-number v-model="row.confirmQty" :min="0" :precision="4" style="width: 100%" />
              </template>
            </el-table-column>
            <el-table-column label="库位" width="180">
              <template #default="{ row }">
                <el-select v-model="row.locationId" placeholder="选择库位" filterable style="width: 100%">
                  <el-option
                    v-for="location in formLocations"
                    :key="location.id"
                    :label="location.code"
                    :value="location.id"
                  />
                </el-select>
              </template>
            </el-table-column>
            <el-table-column label="批次号" width="150">
              <template #default="{ row }">
                <el-input v-model="row.batchNo" maxlength="40" placeholder="非批次商品留空" />
              </template>
            </el-table-column>
            <el-table-column label="入库单价" width="140">
              <template #default="{ row }">
                <el-input-number v-model="row.unitCost" :min="0" :precision="4" style="width: 100%" />
              </template>
            </el-table-column>
          </el-table>
        </template>
      </div>

      <template #footer>
        <el-button @click="completeOpen = false">取消</el-button>
        <el-button type="primary" :loading="completeSubmitting" @click="onSubmitComplete">
          确认完成并入账
        </el-button>
      </template>
    </el-dialog>

    <!-- 详情抽屉 -->
    <el-drawer
      v-model="detailOpen"
      :title="detail ? `入库详情 · ${detail.task.taskNo}` : '入库详情'"
      size="820px"
      append-to-body
    >
      <div v-loading="detailLoading" class="task-detail">
        <template v-if="detail">
          <el-descriptions :column="2" border>
            <el-descriptions-item label="作业单号">
              <span class="qlx-numeric">{{ detail.task.taskNo }}</span>
            </el-descriptions-item>
            <el-descriptions-item label="来源单号">
              <span class="qlx-numeric">{{ detail.task.sourceDocNo }}</span>
            </el-descriptions-item>
            <el-descriptions-item label="来源类型">
              {{ stockInSourceTypeLabel(detail.task.sourceType) }}
            </el-descriptions-item>
            <el-descriptions-item label="仓库">
              {{ warehouseName(detail.task.warehouseId) }}
            </el-descriptions-item>
            <el-descriptions-item label="状态">
              {{ stockInTaskStatusLabel(detail.task.status) }}
            </el-descriptions-item>
            <el-descriptions-item label="创建时间">
              {{ formatDateTime(detail.task.createdAt) }}
            </el-descriptions-item>
            <el-descriptions-item label="备注" :span="2">
              {{ detail.task.remark ?? '—' }}
            </el-descriptions-item>
          </el-descriptions>

          <h3 class="task-detail__title">明细（{{ detail.lines.length }}）</h3>
          <el-table :data="detail.lines" size="small" border empty-text="暂无明细">
            <el-table-column prop="lineNo" label="行号" width="70" align="center" />
            <el-table-column label="商品" min-width="220">
              <template #default="{ row }">{{ itemLabel(row.itemId) }}</template>
            </el-table-column>
            <el-table-column label="预期数量" width="120" align="right">
              <template #default="{ row }">
                <span class="qlx-numeric">{{ formatQty(row.expectQty) }}</span>
              </template>
            </el-table-column>
            <el-table-column label="清点数量" width="120" align="right">
              <template #default="{ row }">
                <span class="qlx-numeric">{{ formatQty(row.countQty) }}</span>
              </template>
            </el-table-column>
            <el-table-column label="确认数量" width="120" align="right">
              <template #default="{ row }">
                <span class="qlx-numeric">{{ formatQty(row.confirmQty) }}</span>
              </template>
            </el-table-column>
            <el-table-column label="库位" width="120">
              <template #default="{ row }">
                {{ locationLabel(row.locationId) }}
              </template>
            </el-table-column>
            <el-table-column label="批次号" width="130">
              <template #default="{ row }">{{ row.batchNo ?? '—' }}</template>
            </el-table-column>
            <el-table-column label="单价" width="110" align="right">
              <template #default="{ row }">
                <span class="qlx-numeric">{{ row.unitCost === null ? '—' : formatCost(row.unitCost) }}</span>
              </template>
            </el-table-column>
          </el-table>
        </template>
      </div>
    </el-drawer>
  </QlxPagePanel>
</template>

<style scoped>
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
