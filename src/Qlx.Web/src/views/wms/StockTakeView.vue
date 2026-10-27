<!--
  所属公司：四川淇利馨集团有限公司
  描述：库存盘点——盘点计划（新建/生成盘点单）+ 盘点单（开始/录入实盘/完成/取消）+ 盲盘隐藏账面量
  说明：
    1. 计划号 / 盘点单号由后端生成，只读展示，表单无编码框（N-1）；
    2. 盲盘时后端不下发 bookQty / diffQty（字段级安全过滤 B-3），前端不得自行推算账面；
    3. 差异原因校验与后端一致：有差异必须填写原因；盲盘下差异由后端判定，原因建议预先填写；
    4. 初盘有差异时后端自动生成复盘单，完成结果回传 recheckTakeId。
-->
<script setup lang="ts">
import { computed, onMounted, reactive, ref, watch } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import AppIcon from '@/components/AppIcon.vue'
import QlxPagePanel from '@/components/crud/QlxPagePanel.vue'
import QlxSearchBar from '@/components/crud/QlxSearchBar.vue'
import type { SearchField } from '@/components/crud/QlxSearchBar.vue'
import QlxDataTable from '@/components/crud/QlxDataTable.vue'
import type { CrudColumn } from '@/components/crud/QlxDataTable.vue'
import { categoryApi } from '@/api/basMaster'
import { itemApi } from '@/api/stk'
import { stockTakeApi, stockTakePlanApi, warehouseApi } from '@/api/wms'
import { useLocalTable } from '@/composables/useLocalTable'
import type { ItemCategoryRow } from '@/types/bas-master'
import type { ItemRow } from '@/types/stk'
import type {
  CreateStockTakePlanPayload,
  LocationRow,
  SaveStockTakeCountLinePayload,
  SaveStockTakeCountsPayload,
  StockTakeDetailRow,
  StockTakePlanRow,
  StockTakeRow,
  StockTakeScopeType,
  WarehouseRow,
} from '@/types/wms'
import { formatDate, formatDateTime, yesNo } from '@/utils/format'
import { formatQty } from '@/utils/stk-enums'
import {
  STOCK_TAKE_SCOPE_TYPE_LABELS,
  STOCK_TAKE_STATUS_LABELS,
  stockTakeRoundLabel,
  stockTakeRoundTone,
  stockTakeScopeTypeLabel,
  stockTakeScopeTypeTone,
  stockTakeStatusLabel,
  stockTakeStatusTone,
  toNumberOptions,
  toStringOptions,
} from '@/utils/wms-enums'

/* ============================================================
 * 一、基础下拉
 * ============================================================ */

const warehouses = ref<WarehouseRow[]>([])
const items = ref<ItemRow[]>([])
const categories = ref<ItemCategoryRow[]>([])
const warehouseOptions = computed(() =>
  warehouses.value.map((warehouse) => ({ label: warehouse.name, value: warehouse.id })),
)
const categoryOptions = computed(() =>
  categories.value.map((category) => ({ label: category.name, value: category.id })),
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
  try {
    categories.value = await categoryApi.list()
  } catch {
    categories.value = []
  }
}

const activeTab = ref<'plans' | 'takes'>('plans')

/* ============================================================
 * 二、盘点计划
 * ============================================================ */

const planSearchFields = computed<SearchField[]>(() => [
  {
    prop: 'scopeType',
    label: '盘点范围',
    type: 'select',
    options: toStringOptions(STOCK_TAKE_SCOPE_TYPE_LABELS),
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
  loading: planLoading,
  rows: planRows,
  total: planTotal,
  page: planPage,
  pageSize: planPageSize,
  query: planQuery,
  search: searchPlan,
  reset: resetPlan,
  refresh: refreshPlan,
  changePage: changePlanPage,
} = useLocalTable<StockTakePlanRow>((params) =>
  stockTakePlanApi.list({
    scopeType: params.scopeType ? Number(params.scopeType) : undefined,
    warehouseId: params.warehouseId || undefined,
  }),
)

const planColumns: Array<CrudColumn<StockTakePlanRow>> = [
  { prop: 'planNo', label: '计划号', type: 'code', width: 180, fixed: 'left' },
  { prop: 'planDate', label: '盘点日期', width: 130 },
  {
    prop: 'scopeType',
    label: '盘点范围',
    type: 'tag',
    width: 110,
    align: 'center',
    tagText: (row) => stockTakeScopeTypeLabel(row.scopeType),
    tagTone: (row) => stockTakeScopeTypeTone(row.scopeType),
  },
  { prop: 'warehouseId', label: '仓库', minWidth: 160 },
  { prop: 'isBlind', label: '盲盘', width: 90, align: 'center' },
  { prop: 'takeId', label: '盘点单', width: 110, align: 'center' },
  { prop: 'createdAt', label: '创建时间', type: 'time', width: 170 },
  { prop: 'remark', label: '备注', minWidth: 160 },
]

const planOpen = ref(false)
const planSubmitting = ref(false)
const planLocations = ref<LocationRow[]>([])
const planForm = reactive<{
  planDate: string
  scopeType: StockTakeScopeType
  warehouseId: string
  categoryId: string
  locationId: string
  isBlind: boolean
  remark: string
}>({
  planDate: '',
  scopeType: 1,
  warehouseId: '',
  categoryId: '',
  locationId: '',
  isBlind: false,
  remark: '',
})

function resetPlanForm(): void {
  planForm.planDate = formatDate(new Date().toISOString())
  planForm.scopeType = 1
  planForm.warehouseId = ''
  planForm.categoryId = ''
  planForm.locationId = ''
  planForm.isBlind = false
  planForm.remark = ''
  planLocations.value = []
}

async function openCreatePlan(): Promise<void> {
  resetPlanForm()
  planOpen.value = true
}

watch(
  () => planForm.warehouseId,
  async (warehouseId) => {
    planForm.locationId = ''
    planLocations.value = []
    if (!warehouseId) {
      return
    }
    try {
      planLocations.value = await warehouseApi.listLocations(warehouseId)
    } catch {
      planLocations.value = []
    }
  },
)

async function onSubmitPlan(): Promise<void> {
  if (!planForm.planDate) {
    ElMessage.warning('请选择盘点日期')
    return
  }
  if (!planForm.warehouseId) {
    ElMessage.warning('请选择仓库')
    return
  }
  if (planForm.scopeType === 2 && !planForm.categoryId) {
    ElMessage.warning('品类范围必须选择品类')
    return
  }
  if (planForm.scopeType === 3 && !planForm.locationId) {
    ElMessage.warning('库位范围必须选择库位')
    return
  }
  planSubmitting.value = true
  try {
    const payload: CreateStockTakePlanPayload = {
      planDate: planForm.planDate,
      scopeType: planForm.scopeType,
      warehouseId: planForm.warehouseId,
      categoryId: planForm.scopeType === 2 ? planForm.categoryId : null,
      locationId: planForm.scopeType === 3 ? planForm.locationId : null,
      isBlind: planForm.isBlind,
      remark: planForm.remark.trim() || null,
    }
    await stockTakePlanApi.create(payload)
    planOpen.value = false
    await refreshPlan()
    ElMessage.success('盘点计划已创建（计划号由系统生成）')
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : '创建盘点计划失败')
  } finally {
    planSubmitting.value = false
  }
}

async function onGenerate(plan: StockTakePlanRow): Promise<void> {
  try {
    await ElMessageBox.confirm(
      `按计划「${plan.planNo}」生成盘点单并冻结范围内库存，是否继续？`,
      '生成盘点单',
      { confirmButtonText: '生成', cancelButtonText: '返回', type: 'warning' },
    )
  } catch {
    return
  }
  try {
    await stockTakePlanApi.generate(plan.id)
    await Promise.all([refreshPlan(), refreshTake()])
    activeTab.value = 'takes'
    ElMessage.success('盘点单已生成，库存已冻结，请在「盘点单」页签开始盘点')
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : '生成盘点单失败')
  }
}

/* ============================================================
 * 三、盘点单
 * ============================================================ */

const takeSearchFields = computed<SearchField[]>(() => [
  {
    prop: 'status',
    label: '状态',
    type: 'select',
    options: toStringOptions(STOCK_TAKE_STATUS_LABELS),
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
  loading: takeLoading,
  rows: takeRows,
  total: takeTotal,
  page: takePage,
  pageSize: takePageSize,
  query: takeQuery,
  search: searchTake,
  reset: resetTake,
  refresh: refreshTake,
  changePage: changeTakePage,
} = useLocalTable<StockTakeRow>((params) =>
  stockTakeApi.list({
    status: params.status ? Number(params.status) : undefined,
    warehouseId: params.warehouseId || undefined,
  }),
)

const takeColumns: Array<CrudColumn<StockTakeRow>> = [
  { prop: 'takeNo', label: '盘点单号', type: 'code', width: 180, fixed: 'left' },
  { prop: 'warehouseId', label: '仓库', minWidth: 160 },
  {
    prop: 'round',
    label: '轮次',
    type: 'tag',
    width: 100,
    align: 'center',
    tagText: (row) => stockTakeRoundLabel(row.round),
    tagTone: (row) => stockTakeRoundTone(row.round),
  },
  { prop: 'isBlind', label: '盲盘', width: 90, align: 'center' },
  {
    prop: 'status',
    label: '状态',
    type: 'tag',
    width: 110,
    align: 'center',
    tagText: (row) => stockTakeStatusLabel(row.status),
    tagTone: (row) => stockTakeStatusTone(row.status),
  },
  { prop: 'createdAt', label: '创建时间', type: 'time', width: 170 },
  { prop: 'remark', label: '备注', minWidth: 150 },
]

async function onStartTake(row: StockTakeRow): Promise<void> {
  try {
    await stockTakeApi.start(row.id)
    await refreshTake()
    ElMessage.success('盘点单已开始')
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : '开始盘点失败')
  }
}

async function onCancelTake(row: StockTakeRow): Promise<void> {
  try {
    await ElMessageBox.confirm(
      `取消盘点单「${row.takeNo}」将解冻范围内库存，是否继续？`,
      '取消盘点',
      { confirmButtonText: '确认取消', cancelButtonText: '返回', type: 'warning' },
    )
  } catch {
    return
  }
  try {
    await stockTakeApi.cancel(row.id)
    await refreshTake()
    ElMessage.success('盘点单已取消，库存已解冻')
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : '取消盘点失败')
  }
}

/* ============================================================
 * 四、录入实盘（盲盘隐藏账面）
 * ============================================================ */

interface CountLineDraft {
  lineId: string
  lineNo: number
  itemId: string
  batchNo: string | null
  bookQty: number | null
  actualQty: number | undefined
  diffReason: string
}

const countOpen = ref(false)
const countLoading = ref(false)
const countSubmitting = ref(false)
const counting = ref<StockTakeDetailRow | null>(null)
const countLines = ref<CountLineDraft[]>([])

function lineDiff(line: CountLineDraft): number | null {
  if (line.bookQty === null || line.actualQty === undefined) {
    return null
  }
  return line.actualQty - line.bookQty
}

async function openCount(row: StockTakeRow): Promise<void> {
  countOpen.value = true
  countLoading.value = true
  counting.value = null
  countLines.value = []
  try {
    const detail = await stockTakeApi.get(row.id)
    counting.value = detail
    countLines.value = detail.lines.map((line) => ({
      lineId: line.id,
      lineNo: line.lineNo,
      itemId: line.itemId,
      batchNo: line.batchNo,
      bookQty: line.bookQty,
      actualQty: line.actualQty ?? undefined,
      diffReason: line.diffReason ?? '',
    }))
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : '加载盘点详情失败')
  } finally {
    countLoading.value = false
  }
}

function buildCountLines(onlyFilled: boolean): SaveStockTakeCountLinePayload[] | null {
  const selected = onlyFilled
    ? countLines.value.filter((line) => line.actualQty !== undefined)
    : countLines.value
  if (selected.length === 0) {
    ElMessage.warning('请至少录入一行实盘数量')
    return null
  }
  const invalid = selected.some(
    (line) =>
      line.actualQty === undefined ||
      !Number.isFinite(line.actualQty) ||
      line.actualQty < 0 ||
      (lineDiff(line) !== null && lineDiff(line) !== 0 && !line.diffReason.trim()),
  )
  if (invalid) {
    ElMessage.warning('实盘数量不得为负；与账面有差异时必须填写差异原因')
    return null
  }
  return selected.map<SaveStockTakeCountLinePayload>((line) => ({
    lineId: line.lineId,
    actualQty: line.actualQty ?? 0,
    diffReason: line.diffReason.trim() || null,
  }))
}

async function onSubmitCount(): Promise<void> {
  if (!counting.value) {
    return
  }
  const lines = buildCountLines(true)
  if (!lines) {
    return
  }
  countSubmitting.value = true
  try {
    const payload: SaveStockTakeCountsPayload = { lines }
    await stockTakeApi.saveCounts(counting.value.take.id, payload)
    await refreshTake()
    ElMessage.success('实盘数量已保存')
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : '保存实盘失败')
  } finally {
    countSubmitting.value = false
  }
}

async function onFinishCount(): Promise<void> {
  if (!counting.value) {
    return
  }
  const lines = buildCountLines(false)
  if (!lines) {
    return
  }
  countSubmitting.value = true
  try {
    await stockTakeApi.saveCounts(counting.value.take.id, { lines })
    const result = await stockTakeApi.finish(counting.value.take.id)
    countOpen.value = false
    await refreshTake()
    if (result.recheckTakeId) {
      ElMessage.warning('初盘存在差异，已自动生成复盘单，请继续复盘')
    } else {
      ElMessage.success('盘点单已完成，盘盈盘亏草稿已生成')
    }
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : '完成盘点失败')
  } finally {
    countSubmitting.value = false
  }
}

/* ============================================================
 * 五、详情抽屉
 * ============================================================ */

const detailOpen = ref(false)
const detailLoading = ref(false)
const detail = ref<StockTakeDetailRow | null>(null)

async function openDetailById(id: string): Promise<void> {
  detailOpen.value = true
  detailLoading.value = true
  detail.value = null
  try {
    detail.value = await stockTakeApi.get(id)
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : '加载盘点详情失败')
  } finally {
    detailLoading.value = false
  }
}

function openTakeDetail(row: StockTakeRow): void {
  void openDetailById(row.id)
}

function openPlanTake(plan: StockTakePlanRow): void {
  if (plan.takeId) {
    void openDetailById(plan.takeId)
  }
}

onMounted(loadOptions)
</script>

<template>
  <QlxPagePanel title="库存盘点" subtitle="计划 → 生成盘点单（冻结库存）→ 初盘 / 复盘 → 完成生成盘盈亏草稿">
    <template #toolbar>
      <el-button v-permission="'wms.stocktake.action'" type="primary" @click="openCreatePlan">
        <AppIcon name="Plus" :size="14" />
        新增盘点计划
      </el-button>
    </template>

    <el-tabs v-model="activeTab" class="take-tabs">
      <el-tab-pane label="盘点计划" name="plans">
        <QlxSearchBar
          :fields="planSearchFields"
          :model-value="planQuery"
          @search="searchPlan"
          @reset="resetPlan"
        />
        <QlxDataTable
          :items="planRows"
          :columns="planColumns"
          :loading="planLoading"
          :total="planTotal"
          :page="planPage"
          :page-size="planPageSize"
          :actions-width="200"
          empty-text="暂无盘点计划"
          @page-change="changePlanPage"
        >
          <template #cell-planNo="{ row }">
            <span class="qlx-numeric">{{ row.planNo }}</span>
          </template>
          <template #cell-planDate="{ row }">{{ row.planDate }}</template>
          <template #cell-warehouseId="{ row }">{{ warehouseName(row.warehouseId) }}</template>
          <template #cell-isBlind="{ row }">{{ yesNo(row.isBlind) }}</template>
          <template #cell-takeId="{ row }">
            <span v-if="row.takeId" class="task-badge is-done">已生成</span>
            <span v-else class="task-badge">未生成</span>
          </template>
          <template #cell-createdAt="{ row }">{{ formatDateTime(row.createdAt) }}</template>
          <template #cell-remark="{ row }">{{ row.remark ?? '—' }}</template>
          <template #actions="{ row }">
            <button
              v-if="!row.takeId"
              v-permission="'wms.stocktake.action'"
              type="button"
              class="cell-link"
              @click="onGenerate(row)"
            >
              生成盘点单
            </button>
            <button v-else type="button" class="cell-link" @click="openPlanTake(row)">
              查看盘点单
            </button>
          </template>
        </QlxDataTable>
      </el-tab-pane>

      <el-tab-pane label="盘点单" name="takes">
        <QlxSearchBar
          :fields="takeSearchFields"
          :model-value="takeQuery"
          @search="searchTake"
          @reset="resetTake"
        />
        <QlxDataTable
          :items="takeRows"
          :columns="takeColumns"
          :loading="takeLoading"
          :total="takeTotal"
          :page="takePage"
          :page-size="takePageSize"
          :actions-width="290"
          empty-text="暂无盘点单"
          @page-change="changeTakePage"
        >
          <template #cell-takeNo="{ row }">
            <span class="qlx-numeric">{{ row.takeNo }}</span>
          </template>
          <template #cell-warehouseId="{ row }">{{ warehouseName(row.warehouseId) }}</template>
          <template #cell-isBlind="{ row }">{{ yesNo(row.isBlind) }}</template>
          <template #cell-createdAt="{ row }">{{ formatDateTime(row.createdAt) }}</template>
          <template #cell-remark="{ row }">{{ row.remark ?? '—' }}</template>
          <template #actions="{ row }">
            <button type="button" class="cell-link" @click="openTakeDetail(row)">详情</button>
            <template v-if="row.status === 1">
              <el-divider direction="vertical" />
              <button
                v-permission="'wms.stocktake.action'"
                type="button"
                class="cell-link"
                @click="onStartTake(row)"
              >
                开始盘点
              </button>
            </template>
            <template v-if="row.status === 2 || row.status === 4">
              <el-divider direction="vertical" />
              <button
                v-permission="'wms.stocktake.action'"
                type="button"
                class="cell-link"
                @click="openCount(row)"
              >
                录入实盘
              </button>
              <el-divider direction="vertical" />
              <button
                v-permission="'wms.stocktake.action'"
                type="button"
                class="cell-link"
                @click="openCount(row)"
              >
                完成
              </button>
              <el-divider direction="vertical" />
              <button
                v-permission="'wms.stocktake.action'"
                type="button"
                class="cell-link is-danger"
                @click="onCancelTake(row)"
              >
                取消
              </button>
            </template>
          </template>
        </QlxDataTable>
      </el-tab-pane>
    </el-tabs>

    <!-- 新建盘点计划 -->
    <el-dialog
      v-model="planOpen"
      title="新增盘点计划"
      width="720px"
      :close-on-click-modal="false"
      append-to-body
    >
      <el-form label-width="100px">
        <el-row :gutter="16">
          <el-col :span="12">
            <el-form-item label="盘点日期" required>
              <el-date-picker
                v-model="planForm.planDate"
                type="date"
                value-format="YYYY-MM-DD"
                placeholder="选择盘点日期"
                style="width: 100%"
              />
            </el-form-item>
          </el-col>
          <el-col :span="12">
            <el-form-item label="盘点范围" required>
              <el-select v-model="planForm.scopeType" style="width: 100%">
                <el-option
                  v-for="option in toNumberOptions(STOCK_TAKE_SCOPE_TYPE_LABELS)"
                  :key="option.value"
                  :label="option.label"
                  :value="option.value"
                />
              </el-select>
            </el-form-item>
          </el-col>
        </el-row>
        <el-row :gutter="16">
          <el-col :span="12">
            <el-form-item label="仓库" required>
              <el-select v-model="planForm.warehouseId" filterable placeholder="请选择仓库" style="width: 100%">
                <el-option
                  v-for="option in warehouseOptions"
                  :key="option.value"
                  :label="option.label"
                  :value="option.value"
                />
              </el-select>
            </el-form-item>
          </el-col>
          <el-col v-if="planForm.scopeType === 2" :span="12">
            <el-form-item label="品类" required>
              <el-select v-model="planForm.categoryId" filterable placeholder="请选择品类" style="width: 100%">
                <el-option
                  v-for="option in categoryOptions"
                  :key="option.value"
                  :label="option.label"
                  :value="option.value"
                />
              </el-select>
            </el-form-item>
          </el-col>
          <el-col v-if="planForm.scopeType === 3" :span="12">
            <el-form-item label="库位" required>
              <el-select v-model="planForm.locationId" filterable placeholder="请选择库位" style="width: 100%">
                <el-option
                  v-for="location in planLocations"
                  :key="location.id"
                  :label="location.code"
                  :value="location.id"
                />
              </el-select>
            </el-form-item>
          </el-col>
        </el-row>
        <el-form-item label="盲盘">
          <el-switch v-model="planForm.isBlind" />
          <span class="task-hint is-inline">开启后录入界面不显示账面数量</span>
        </el-form-item>
        <el-form-item label="备注">
          <el-input v-model="planForm.remark" maxlength="200" clearable placeholder="可选" />
        </el-form-item>
      </el-form>

      <template #footer>
        <el-button @click="planOpen = false">取消</el-button>
        <el-button type="primary" :loading="planSubmitting" @click="onSubmitPlan">
          创建计划
        </el-button>
      </template>
    </el-dialog>

    <!-- 录入实盘 -->
    <el-dialog
      v-model="countOpen"
      :title="counting ? `录入实盘 · ${counting.take.takeNo}` : '录入实盘'"
      width="980px"
      :close-on-click-modal="false"
      append-to-body
    >
      <div v-loading="countLoading">
        <template v-if="counting">
          <el-descriptions :column="3" border size="small">
            <el-descriptions-item label="仓库">
              {{ warehouseName(counting.take.warehouseId) }}
            </el-descriptions-item>
            <el-descriptions-item label="轮次">
              {{ stockTakeRoundLabel(counting.take.round) }}
            </el-descriptions-item>
            <el-descriptions-item label="盲盘">
              {{ yesNo(counting.take.isBlind) }}
            </el-descriptions-item>
          </el-descriptions>

          <el-table :data="countLines" size="small" border class="task-lines__table">
            <el-table-column prop="lineNo" label="行号" width="70" align="center" />
            <el-table-column label="商品" min-width="210">
              <template #default="{ row }">{{ itemLabel(row.itemId) }}</template>
            </el-table-column>
            <el-table-column label="批次号" width="130">
              <template #default="{ row }">{{ row.batchNo ?? '—' }}</template>
            </el-table-column>
            <el-table-column v-if="!counting.take.isBlind" label="账面数量" width="120" align="right">
              <template #default="{ row }">
                <span class="qlx-numeric">{{ formatQty(row.bookQty) }}</span>
              </template>
            </el-table-column>
            <el-table-column label="实盘数量" width="160">
              <template #default="{ row }">
                <el-input-number v-model="row.actualQty" :min="0" :precision="4" style="width: 100%" />
              </template>
            </el-table-column>
            <el-table-column v-if="!counting.take.isBlind" label="差异" width="110" align="right">
              <template #default="{ row }">
                <span class="qlx-numeric">{{ lineDiff(row) === null ? '—' : formatQty(lineDiff(row)) }}</span>
              </template>
            </el-table-column>
            <el-table-column label="差异原因" min-width="200">
              <template #default="{ row }">
                <el-input
                  v-model="row.diffReason"
                  maxlength="200"
                  :placeholder="
                    counting.take.isBlind
                      ? '盲盘：如存在差异后端将要求原因，建议预先填写'
                      : lineDiff(row) === 0 || lineDiff(row) === null
                        ? '无差异可留空'
                        : '有差异必填'
                  "
                />
              </template>
            </el-table-column>
          </el-table>
          <p class="task-hint">盲盘模式不显示账面数量与差异（后端字段级过滤）；全部明细录入实盘后方可完成。</p>
        </template>
      </div>

      <template #footer>
        <el-button @click="countOpen = false">取消</el-button>
        <el-button :loading="countSubmitting" @click="onSubmitCount">保存实盘</el-button>
        <el-button type="primary" :loading="countSubmitting" @click="onFinishCount">
          保存并完成
        </el-button>
      </template>
    </el-dialog>

    <!-- 详情抽屉 -->
    <el-drawer
      v-model="detailOpen"
      :title="detail ? `盘点详情 · ${detail.take.takeNo}` : '盘点详情'"
      size="820px"
      append-to-body
    >
      <div v-loading="detailLoading" class="task-detail">
        <template v-if="detail">
          <el-descriptions :column="2" border>
            <el-descriptions-item label="盘点单号">
              <span class="qlx-numeric">{{ detail.take.takeNo }}</span>
            </el-descriptions-item>
            <el-descriptions-item label="状态">
              {{ stockTakeStatusLabel(detail.take.status) }}
            </el-descriptions-item>
            <el-descriptions-item label="仓库">
              {{ warehouseName(detail.take.warehouseId) }}
            </el-descriptions-item>
            <el-descriptions-item label="轮次">
              {{ stockTakeRoundLabel(detail.take.round) }}
            </el-descriptions-item>
            <el-descriptions-item label="盲盘">{{ yesNo(detail.take.isBlind) }}</el-descriptions-item>
            <el-descriptions-item label="创建时间">
              {{ formatDateTime(detail.take.createdAt) }}
            </el-descriptions-item>
            <el-descriptions-item label="备注" :span="2">
              {{ detail.take.remark ?? '—' }}
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
            <el-table-column label="账面数量" width="110" align="right">
              <template #default="{ row }">
                <span class="qlx-numeric">{{ row.bookQty === null ? '盲盘' : formatQty(row.bookQty) }}</span>
              </template>
            </el-table-column>
            <el-table-column label="实盘数量" width="110" align="right">
              <template #default="{ row }">
                <span class="qlx-numeric">{{ formatQty(row.actualQty) }}</span>
              </template>
            </el-table-column>
            <el-table-column label="差异" width="100" align="right">
              <template #default="{ row }">
                <span class="qlx-numeric">{{ row.diffQty === null ? '—' : formatQty(row.diffQty) }}</span>
              </template>
            </el-table-column>
            <el-table-column label="差异原因" min-width="150">
              <template #default="{ row }">{{ row.diffReason ?? '—' }}</template>
            </el-table-column>
            <el-table-column label="冻结" width="80" align="center">
              <template #default="{ row }">{{ yesNo(row.frozen) }}</template>
            </el-table-column>
          </el-table>
        </template>
      </div>
    </el-drawer>
  </QlxPagePanel>
</template>

<style scoped>
.take-tabs {
  padding: 0 var(--qlx-space-4);
}

.task-badge {
  display: inline-block;
  padding: 1px 8px;
  border-radius: 10px;
  background: var(--qlx-fill-2);
  color: var(--qlx-ink-400);
  font-size: var(--qlx-text-xs);
}

.task-badge.is-done {
  background: var(--qlx-success-soft);
  color: var(--qlx-success);
}

.task-lines__table {
  margin-top: var(--qlx-space-3);
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

.task-hint.is-inline {
  margin-left: var(--qlx-space-2);
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
