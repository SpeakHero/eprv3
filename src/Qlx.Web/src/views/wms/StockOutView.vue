<!--
  所属公司：四川淇利馨集团有限公司
  描述：出库作业——任务池筛选 + 新建 + 拣货（FEFO 建议）/ 复核 / 完成（大额转审批）/ 取消 + 详情抽屉 + CSV 导出
  说明：
    1. 作业单号由后端生成，只读展示，新建表单无单号输入框（N-1）；
    2. FEFO 建议由后端按批次效期升序求值，前端仅做展示与应用（B-4）；
    3. 复核数量与拣货数量必须一致（后端强校验 409），故复核弹窗只读回显拣货量；
    4. 完成时后端按金额阈值返回 approvalRequired，前端仅提示审批挂起，不做金额判定（B-2）。
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
import { stockOutApi, warehouseApi } from '@/api/wms'
import { useLocalTable } from '@/composables/useLocalTable'
import type { ItemRow } from '@/types/stk'
import type {
  CheckStockOutLinePayload,
  CheckStockOutTaskPayload,
  CreateStockOutLinePayload,
  CreateStockOutTaskPayload,
  LocationRow,
  PickStockOutLinePayload,
  PickStockOutTaskPayload,
  StockOutFefoSuggestionRow,
  StockOutSourceType,
  StockOutTaskDetailRow,
  StockOutTaskRow,
  WarehouseRow,
} from '@/types/wms'
import { exportCsv } from '@/utils/csv-export'
import type { CsvColumn } from '@/utils/csv-export'
import { formatMoney, formatQty } from '@/utils/stk-enums'
import { formatDate, formatDateTime } from '@/utils/format'
import {
  STOCK_OUT_SOURCE_TYPE_LABELS,
  STOCK_OUT_TASK_STATUS_LABELS,
  stockOutSourceTypeLabel,
  stockOutSourceTypeTone,
  stockOutTaskStatusLabel,
  stockOutTaskStatusTone,
  toNumberOptions,
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
 * 二、任务池列表
 * ============================================================ */

const searchFields = computed<SearchField[]>(() => [
  {
    prop: 'status',
    label: '状态',
    type: 'select',
    options: toStringOptions(STOCK_OUT_TASK_STATUS_LABELS),
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
    options: toStringOptions(STOCK_OUT_SOURCE_TYPE_LABELS),
    width: 150,
  },
])

const { loading, rows, total, page, pageSize, query, search, reset, refresh, changePage } =
  useLocalTable<StockOutTaskRow>((params) =>
    stockOutApi.list({
      status: params.status ? Number(params.status) : undefined,
      warehouseId: params.warehouseId || undefined,
      sourceType: params.sourceType ? Number(params.sourceType) : undefined,
    }),
  )

const columns: Array<CrudColumn<StockOutTaskRow>> = [
  { prop: 'taskNo', label: '作业单号', type: 'code', width: 180, fixed: 'left' },
  {
    prop: 'sourceType',
    label: '来源类型',
    type: 'tag',
    width: 110,
    align: 'center',
    tagText: (row) => stockOutSourceTypeLabel(row.sourceType),
    tagTone: (row) => stockOutSourceTypeTone(row.sourceType),
  },
  { prop: 'sourceDocNo', label: '来源单号', minWidth: 150 },
  { prop: 'warehouseId', label: '仓库', minWidth: 150 },
  {
    prop: 'status',
    label: '状态',
    type: 'tag',
    width: 100,
    align: 'center',
    tagText: (row) => stockOutTaskStatusLabel(row.status),
    tagTone: (row) => stockOutTaskStatusTone(row.status),
  },
  { prop: 'totalAmount', label: '出库金额', type: 'text', width: 130, align: 'right' },
  { prop: 'handoverTo', label: '交接人', width: 110 },
  { prop: 'createdAt', label: '创建时间', type: 'time', width: 170 },
]

const csvColumns: Array<CsvColumn<StockOutTaskRow>> = [
  { title: '作业单号', value: (row) => row.taskNo },
  { title: '来源类型', value: (row) => stockOutSourceTypeLabel(row.sourceType) },
  { title: '来源单号', value: (row) => row.sourceDocNo },
  { title: '仓库', value: (row) => warehouseName(row.warehouseId) },
  { title: '状态', value: (row) => stockOutTaskStatusLabel(row.status) },
  { title: '出库金额', value: (row) => formatMoney(row.totalAmount) },
  { title: '交接人', value: (row) => row.handoverTo },
  { title: '创建时间', value: (row) => formatDateTime(row.createdAt) },
  { title: '备注', value: (row) => row.remark },
]

function onExport(): void {
  exportCsv('出库作业', csvColumns, rows.value)
}

/* ============================================================
 * 三、新建出库作业（不含单号，N-1）
 * ============================================================ */

interface StockOutLineDraft {
  itemId: string
  applyQty: number
  batchNo: string
}

const createOpen = ref(false)
const submitting = ref(false)
const form = reactive<{
  sourceType: StockOutSourceType
  sourceDocNo: string
  warehouseId: string
  remark: string
  lines: StockOutLineDraft[]
}>({ sourceType: 1, sourceDocNo: '', warehouseId: '', remark: '', lines: [] })

function resetForm(): void {
  form.sourceType = 1
  form.sourceDocNo = ''
  form.warehouseId = ''
  form.remark = ''
  form.lines = [{ itemId: '', applyQty: 1, batchNo: '' }]
}

function openCreate(): void {
  resetForm()
  createOpen.value = true
}

function addLine(): void {
  form.lines.push({ itemId: '', applyQty: 1, batchNo: '' })
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
    (line) => !line.itemId || !Number.isFinite(line.applyQty) || line.applyQty <= 0,
  )
  if (invalid) {
    ElMessage.warning('请完整填写明细：商品必选、申请数量需大于 0')
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
    const lines: CreateStockOutLinePayload[] = form.lines.map((line) => ({
      itemId: line.itemId,
      applyQty: line.applyQty,
      locationId: null,
      batchNo: line.batchNo.trim() || null,
    }))
    const payload: CreateStockOutTaskPayload = {
      sourceType: form.sourceType,
      sourceDocNo: form.sourceDocNo.trim(),
      warehouseId: form.warehouseId,
      remark: form.remark.trim() || null,
      lines,
    }
    await stockOutApi.create(payload)
    createOpen.value = false
    await refresh()
    ElMessage.success('出库作业已创建（单号由系统生成）')
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : '创建出库作业失败')
  } finally {
    submitting.value = false
  }
}

/* ============================================================
 * 四、拣货（FEFO 建议）
 * ============================================================ */

interface PickLineDraft {
  lineId: string
  lineNo: number
  itemId: string
  applyQty: number
  pickQty: number
  locationId: string
  batchNo: string
}

const pickOpen = ref(false)
const pickLoading = ref(false)
const pickSubmitting = ref(false)
const picking = ref<StockOutTaskDetailRow | null>(null)
const pickLocations = ref<LocationRow[]>([])
const pickLines = ref<PickLineDraft[]>([])

async function openPick(row: StockOutTaskRow): Promise<void> {
  pickOpen.value = true
  pickLoading.value = true
  picking.value = null
  pickLocations.value = []
  pickLines.value = []
  try {
    const detail = await stockOutApi.get(row.id)
    picking.value = detail
    pickLines.value = detail.lines.map((line) => ({
      lineId: line.id,
      lineNo: line.lineNo,
      itemId: line.itemId,
      applyQty: line.applyQty,
      pickQty: line.pickQty > 0 ? line.pickQty : line.applyQty,
      locationId: line.locationId ?? '',
      batchNo: line.batchNo ?? '',
    }))
    try {
      pickLocations.value = await warehouseApi.listLocations(detail.task.warehouseId)
    } catch {
      pickLocations.value = []
    }
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : '加载出库详情失败')
  } finally {
    pickLoading.value = false
  }
}

function suggestionsForLine(lineNo: number): StockOutFefoSuggestionRow[] {
  return picking.value?.fefoSuggestions.filter((row) => row.lineNo === lineNo) ?? []
}

/** 应用该行首条 FEFO 建议：批次取最早效期，拣货量取申请量与批内可用量的较小值。 */
function applyFefo(line: PickLineDraft): void {
  const suggestion = suggestionsForLine(line.lineNo)[0]
  if (!suggestion) {
    ElMessage.warning('该明细暂无批次库存建议')
    return
  }
  line.batchNo = suggestion.batchNo
  line.pickQty = Math.min(line.applyQty, suggestion.qty)
}

async function onSubmitPick(): Promise<void> {
  if (!picking.value) {
    return
  }
  const invalid = pickLines.value.some(
    (line) =>
      !line.locationId ||
      !Number.isFinite(line.pickQty) ||
      line.pickQty < 0 ||
      line.pickQty > line.applyQty,
  )
  if (invalid) {
    ElMessage.warning('请完整填写：库位必选、实拣数量不得为负且不得超过申请数量')
    return
  }
  const missingBatch = pickLines.value.some(
    (line) => line.pickQty > 0 && itemBatchEnabled(line.itemId) && !line.batchNo.trim(),
  )
  if (missingBatch) {
    ElMessage.warning('启用批次管理的商品在实拣数量大于 0 时必须填写批次号')
    return
  }
  pickSubmitting.value = true
  try {
    const payload: PickStockOutTaskPayload = {
      lines: pickLines.value.map<PickStockOutLinePayload>((line) => ({
        lineId: line.lineId,
        pickQty: line.pickQty,
        locationId: line.locationId,
        batchNo: line.batchNo.trim() || null,
      })),
    }
    await stockOutApi.pick(picking.value.task.id, payload)
    pickOpen.value = false
    await refresh()
    ElMessage.success('拣货已完成，等待复核')
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : '拣货提交失败')
  } finally {
    pickSubmitting.value = false
  }
}

/* ============================================================
 * 五、复核（数量锁定为拣货量）
 * ============================================================ */

const checkOpen = ref(false)
const checkLoading = ref(false)
const checkSubmitting = ref(false)
const checking = ref<StockOutTaskDetailRow | null>(null)
const handoverTo = ref('')

async function openCheck(row: StockOutTaskRow): Promise<void> {
  checkOpen.value = true
  checkLoading.value = true
  checking.value = null
  handoverTo.value = ''
  try {
    const detail = await stockOutApi.get(row.id)
    checking.value = detail
    handoverTo.value = detail.task.handoverTo ?? ''
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : '加载出库详情失败')
  } finally {
    checkLoading.value = false
  }
}

async function onSubmitCheck(): Promise<void> {
  if (!checking.value) {
    return
  }
  if (!handoverTo.value.trim()) {
    ElMessage.warning('请填写交接人')
    return
  }
  checkSubmitting.value = true
  try {
    const payload: CheckStockOutTaskPayload = {
      handoverTo: handoverTo.value.trim(),
      lines: checking.value.lines.map<CheckStockOutLinePayload>((line) => ({
        lineId: line.id,
        checkQty: line.pickQty,
      })),
    }
    await stockOutApi.check(checking.value.task.id, payload)
    checkOpen.value = false
    await refresh()
    ElMessage.success('复核已通过，可执行完成出库')
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : '复核提交失败')
  } finally {
    checkSubmitting.value = false
  }
}

/* ============================================================
 * 六、完成（大额转审批） / 取消
 * ============================================================ */

async function onComplete(row: StockOutTaskRow): Promise<void> {
  try {
    await ElMessageBox.confirm(
      `完成出库作业「${row.taskNo}」后库存将扣减，是否继续？`,
      '完成出库',
      { confirmButtonText: '确认完成', cancelButtonText: '返回', type: 'warning' },
    )
  } catch {
    return
  }
  try {
    const result = await stockOutApi.complete(row.id)
    await refresh()
    if (result.approvalRequired) {
      ElMessage.warning(
        `出库金额 ${formatMoney(result.totalAmount)} 超出阈值，已转大额审批，请等待审批结果`,
      )
      return
    }
    ElMessage.success(`出库已完成，出库金额 ${formatMoney(result.totalAmount)}`)
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : '完成出库失败')
  }
}

async function onCancel(row: StockOutTaskRow): Promise<void> {
  try {
    await ElMessageBox.confirm(
      `取消出库作业「${row.taskNo}」后不可继续作业，是否继续？`,
      '取消出库作业',
      { confirmButtonText: '确认取消', cancelButtonText: '返回', type: 'warning' },
    )
  } catch {
    return
  }
  try {
    await stockOutApi.cancel(row.id)
    await refresh()
    ElMessage.success('出库作业已取消')
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : '取消出库作业失败')
  }
}

/* ============================================================
 * 七、详情抽屉
 * ============================================================ */

const detailOpen = ref(false)
const detailLoading = ref(false)
const detail = ref<StockOutTaskDetailRow | null>(null)

async function openDetail(row: StockOutTaskRow): Promise<void> {
  detailOpen.value = true
  detailLoading.value = true
  detail.value = null
  try {
    detail.value = await stockOutApi.get(row.id)
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : '加载出库详情失败')
  } finally {
    detailLoading.value = false
  }
}

onMounted(loadOptions)
</script>

<template>
  <QlxPagePanel title="出库作业" subtitle="任务池 · FEFO 拣货 · 复核交接 · 完成后扣减库存（单号由系统生成）">
    <template #toolbar>
      <el-button v-permission="'wms.outbound.action'" type="primary" @click="openCreate">
        <AppIcon name="Plus" :size="14" />
        新增出库作业
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
      empty-text="暂无出库作业"
      @page-change="changePage"
    >
      <template #cell-taskNo="{ row }">
        <span class="qlx-numeric">{{ row.taskNo }}</span>
      </template>
      <template #cell-warehouseId="{ row }">{{ warehouseName(row.warehouseId) }}</template>
      <template #cell-totalAmount="{ row }">
        <span class="qlx-numeric">{{ formatMoney(row.totalAmount) }}</span>
      </template>
      <template #cell-handoverTo="{ row }">{{ row.handoverTo ?? '—' }}</template>
      <template #cell-createdAt="{ row }">{{ formatDateTime(row.createdAt) }}</template>
      <template #actions="{ row }">
        <button type="button" class="cell-link" @click="openDetail(row)">详情</button>
        <template v-if="row.status === 1">
          <el-divider direction="vertical" />
          <button
            v-permission="'wms.outbound.action'"
            type="button"
            class="cell-link"
            @click="openPick(row)"
          >
            拣货
          </button>
        </template>
        <template v-if="row.status === 2">
          <el-divider direction="vertical" />
          <button
            v-permission="'wms.outbound.action'"
            type="button"
            class="cell-link"
            @click="openCheck(row)"
          >
            复核
          </button>
        </template>
        <template v-if="row.status === 3">
          <el-divider direction="vertical" />
          <button
            v-permission="'wms.outbound.action'"
            type="button"
            class="cell-link"
            @click="onComplete(row)"
          >
            完成
          </button>
        </template>
        <template v-if="row.status === 1 || row.status === 2 || row.status === 3">
          <el-divider direction="vertical" />
          <button
            v-permission="'wms.outbound.action'"
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
      title="新增出库作业"
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
                  v-for="option in toNumberOptions(STOCK_OUT_SOURCE_TYPE_LABELS)"
                  :key="option.value"
                  :label="option.label"
                  :value="option.value"
                />
              </el-select>
            </el-form-item>
          </el-col>
          <el-col :span="8">
            <el-form-item label="来源单号" required>
              <el-input v-model="form.sourceDocNo" maxlength="32" placeholder="销售单 / 退货单等" />
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
            <h3>明细（库位在拣货时指定）</h3>
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
            <el-table-column label="申请数量" width="150">
              <template #default="{ row }">
                <el-input-number v-model="row.applyQty" :min="0" :precision="4" style="width: 100%" />
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
        <el-button type="primary" :loading="submitting" @click="onSubmitCreate">创建作业</el-button>
      </template>
    </el-dialog>

    <!-- 拣货：FEFO 建议 -->
    <el-dialog
      v-model="pickOpen"
      :title="picking ? `拣货 · ${picking.task.taskNo}` : '拣货'"
      width="1080px"
      :close-on-click-modal="false"
      append-to-body
    >
      <div v-loading="pickLoading">
        <template v-if="picking">
          <el-descriptions :column="3" border size="small">
            <el-descriptions-item label="来源类型">
              {{ stockOutSourceTypeLabel(picking.task.sourceType) }}
            </el-descriptions-item>
            <el-descriptions-item label="来源单号">
              <span class="qlx-numeric">{{ picking.task.sourceDocNo }}</span>
            </el-descriptions-item>
            <el-descriptions-item label="仓库">
              {{ warehouseName(picking.task.warehouseId) }}
            </el-descriptions-item>
          </el-descriptions>

          <el-table :data="pickLines" size="small" border class="task-lines__table">
            <el-table-column label="商品" min-width="200">
              <template #default="{ row }">{{ itemLabel(row.itemId) }}</template>
            </el-table-column>
            <el-table-column label="申请数量" width="110" align="right">
              <template #default="{ row }">
                <span class="qlx-numeric">{{ formatQty(row.applyQty) }}</span>
              </template>
            </el-table-column>
            <el-table-column label="实拣数量" width="150">
              <template #default="{ row }">
                <el-input-number v-model="row.pickQty" :min="0" :precision="4" style="width: 100%" />
              </template>
            </el-table-column>
            <el-table-column label="库位" width="180">
              <template #default="{ row }">
                <el-select v-model="row.locationId" placeholder="选择库位" filterable style="width: 100%">
                  <el-option
                    v-for="location in pickLocations"
                    :key="location.id"
                    :label="location.code"
                    :value="location.id"
                  />
                </el-select>
              </template>
            </el-table-column>
            <el-table-column label="批次号" width="170">
              <template #default="{ row }">
                <el-input v-model="row.batchNo" maxlength="64" placeholder="非批次商品留空" />
              </template>
            </el-table-column>
            <el-table-column label="FEFO 建议" width="150">
              <template #default="{ row }">
                <button
                  v-if="suggestionsForLine(row.lineNo).length"
                  type="button"
                  class="cell-link"
                  @click="applyFefo(row)"
                >
                  取最早效期
                </button>
                <span v-else class="task-muted">无批次库存</span>
              </template>
            </el-table-column>
          </el-table>

          <h3 class="task-detail__title">FEFO 批次建议（效期升序，由后端求值）</h3>
          <el-table
            :data="picking.fefoSuggestions"
            size="small"
            border
            empty-text="暂无批次库存建议"
          >
            <el-table-column prop="lineNo" label="行号" width="70" align="center" />
            <el-table-column label="商品" min-width="200">
              <template #default="{ row }">{{ itemLabel(row.itemId) }}</template>
            </el-table-column>
            <el-table-column label="批次号" width="160">
              <template #default="{ row }">{{ row.batchNo }}</template>
            </el-table-column>
            <el-table-column label="有效期" width="120">
              <template #default="{ row }">
                {{ row.expiryDate ? formatDate(row.expiryDate) : '—' }}
              </template>
            </el-table-column>
            <el-table-column label="批内可用" width="120" align="right">
              <template #default="{ row }">
                <span class="qlx-numeric">{{ formatQty(row.qty) }}</span>
              </template>
            </el-table-column>
          </el-table>
        </template>
      </div>

      <template #footer>
        <el-button @click="pickOpen = false">取消</el-button>
        <el-button type="primary" :loading="pickSubmitting" @click="onSubmitPick">
          确认拣货
        </el-button>
      </template>
    </el-dialog>

    <!-- 复核：数量锁定为拣货量 -->
    <el-dialog
      v-model="checkOpen"
      :title="checking ? `复核 · ${checking.task.taskNo}` : '复核'"
      width="860px"
      :close-on-click-modal="false"
      append-to-body
    >
      <div v-loading="checkLoading">
        <template v-if="checking">
          <el-form label-width="90px">
            <el-form-item label="交接人" required>
              <el-input v-model="handoverTo" maxlength="64" placeholder="填写交接人" />
            </el-form-item>
          </el-form>

          <el-table :data="checking.lines" size="small" border>
            <el-table-column prop="lineNo" label="行号" width="70" align="center" />
            <el-table-column label="商品" min-width="220">
              <template #default="{ row }">{{ itemLabel(row.itemId) }}</template>
            </el-table-column>
            <el-table-column label="申请数量" width="120" align="right">
              <template #default="{ row }">
                <span class="qlx-numeric">{{ formatQty(row.applyQty) }}</span>
              </template>
            </el-table-column>
            <el-table-column label="拣货数量" width="120" align="right">
              <template #default="{ row }">
                <span class="qlx-numeric">{{ formatQty(row.pickQty) }}</span>
              </template>
            </el-table-column>
            <el-table-column label="复核数量" width="120" align="right">
              <template #default="{ row }">
                <span class="qlx-numeric">{{ formatQty(row.pickQty) }}</span>
              </template>
            </el-table-column>
            <el-table-column label="批次号" width="150">
              <template #default="{ row }">{{ row.batchNo ?? '—' }}</template>
            </el-table-column>
          </el-table>
          <p class="task-hint">复核数量与拣货数量一致方可提交（后端强校验）。</p>
        </template>
      </div>

      <template #footer>
        <el-button @click="checkOpen = false">取消</el-button>
        <el-button type="primary" :loading="checkSubmitting" @click="onSubmitCheck">
          确认复核
        </el-button>
      </template>
    </el-dialog>

    <!-- 详情抽屉 -->
    <el-drawer
      v-model="detailOpen"
      :title="detail ? `出库详情 · ${detail.task.taskNo}` : '出库详情'"
      size="860px"
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
              {{ stockOutSourceTypeLabel(detail.task.sourceType) }}
            </el-descriptions-item>
            <el-descriptions-item label="仓库">
              {{ warehouseName(detail.task.warehouseId) }}
            </el-descriptions-item>
            <el-descriptions-item label="状态">
              {{ stockOutTaskStatusLabel(detail.task.status) }}
            </el-descriptions-item>
            <el-descriptions-item label="出库金额">
              <span class="qlx-numeric">{{ formatMoney(detail.task.totalAmount) }}</span>
            </el-descriptions-item>
            <el-descriptions-item label="拣货人">{{ detail.task.pickerId ?? '—' }}</el-descriptions-item>
            <el-descriptions-item label="复核人">{{ detail.task.checkerId ?? '—' }}</el-descriptions-item>
            <el-descriptions-item label="交接人">{{ detail.task.handoverTo ?? '—' }}</el-descriptions-item>
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
            <el-table-column label="申请数量" width="110" align="right">
              <template #default="{ row }">
                <span class="qlx-numeric">{{ formatQty(row.applyQty) }}</span>
              </template>
            </el-table-column>
            <el-table-column label="拣货数量" width="110" align="right">
              <template #default="{ row }">
                <span class="qlx-numeric">{{ formatQty(row.pickQty) }}</span>
              </template>
            </el-table-column>
            <el-table-column label="复核数量" width="110" align="right">
              <template #default="{ row }">
                <span class="qlx-numeric">{{ formatQty(row.checkQty) }}</span>
              </template>
            </el-table-column>
            <el-table-column label="批次号" width="140">
              <template #default="{ row }">{{ row.batchNo ?? '—' }}</template>
            </el-table-column>
          </el-table>

          <h3 class="task-detail__title">FEFO 批次建议（{{ detail.fefoSuggestions.length }}）</h3>
          <el-table :data="detail.fefoSuggestions" size="small" border empty-text="暂无建议">
            <el-table-column prop="lineNo" label="行号" width="70" align="center" />
            <el-table-column label="商品" min-width="200">
              <template #default="{ row }">{{ itemLabel(row.itemId) }}</template>
            </el-table-column>
            <el-table-column label="批次号" width="150">
              <template #default="{ row }">{{ row.batchNo }}</template>
            </el-table-column>
            <el-table-column label="有效期" width="110">
              <template #default="{ row }">
                {{ row.expiryDate ? formatDate(row.expiryDate) : '—' }}
              </template>
            </el-table-column>
            <el-table-column label="批内可用" width="110" align="right">
              <template #default="{ row }">
                <span class="qlx-numeric">{{ formatQty(row.qty) }}</span>
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

.task-hint {
  margin-top: var(--qlx-space-2);
  color: var(--qlx-ink-400);
  font-size: var(--qlx-text-xs);
}

.task-muted {
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
