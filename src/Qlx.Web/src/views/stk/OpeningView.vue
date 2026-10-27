<!--
  所属公司：四川淇利馨集团有限公司
  描述：期初建账——扁平批次列表 + 草稿新建 / 编辑 / 删除 + 提交过账审批 + 详情抽屉 + 仓库汇总卡片
  说明：
    1. 期初单号由后端生成，只读展示，表单无单号输入框（N-1）；
    2. 明细金额后端计算，前端仅展示预览（V-1）；后端过滤 status / warehouseId；
    3. 仅草稿（status=1）可编辑 / 删除 / 提交审批。
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
import { itemApi, openingApi } from '@/api/stk'
import { warehouseApi } from '@/api/wms'
import { useLocalTable } from '@/composables/useLocalTable'
import type {
  ItemRow,
  OpeningBatchDetailRow,
  OpeningBatchRow,
  OpeningItemPayload,
  OpeningWarehouseSummaryRow,
  SaveOpeningPayload,
} from '@/types/stk'
import type { WarehouseRow } from '@/types/wms'
import { formatDateTime } from '@/utils/format'
import {
  formatCost,
  formatMoney,
  formatQty,
  openingStatusLabel,
  openingStatusTone,
  OPENING_STATUS_LABELS,
  previewAmount,
  toStringOptions,
} from '@/utils/stk-enums'

/* ============================================================
 * 一、期初批次列表
 * ============================================================ */

const warehouses = ref<WarehouseRow[]>([])
const items = ref<ItemRow[]>([])
const itemOptions = computed(() =>
  items.value.map((item) => ({ label: `${item.itemCode} · ${item.name}`, value: item.id })),
)
const warehouseOptions = computed(() =>
  warehouses.value.map((warehouse) => ({ label: warehouse.name, value: warehouse.id })),
)

const searchFields = computed<SearchField[]>(() => [
  {
    prop: 'status',
    label: '状态',
    type: 'select',
    options: toStringOptions(OPENING_STATUS_LABELS),
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

const { loading, items: rows, total, page, pageSize, query, search, reset, refresh, changePage } =
  useLocalTable<OpeningBatchRow>((params) =>
    openingApi.list({
      status: params.status ? Number(params.status) : undefined,
      warehouseId: params.warehouseId || undefined,
    }),
  )

function warehouseName(id: string): string {
  return warehouses.value.find((warehouse) => warehouse.id === id)?.name ?? '—'
}

const columns: Array<CrudColumn<OpeningBatchRow>> = [
  { prop: 'batchNo', label: '期初单号', type: 'code', minWidth: 190, fixed: 'left' },
  { prop: 'warehouseId', label: '仓库', minWidth: 160 },
  {
    prop: 'status',
    label: '状态',
    type: 'tag',
    width: 100,
    align: 'center',
    tagText: (row) => openingStatusLabel(row.status),
    tagTone: (row) => openingStatusTone(row.status),
  },
  { prop: 'postedAt', label: '过账时间', type: 'time', width: 170 },
  { prop: 'remark', label: '备注', minWidth: 200 },
]

/* ============================================================
 * 二、仓库汇总卡片（仅已过账明细）
 * ============================================================ */

const summaryRows = ref<OpeningWarehouseSummaryRow[]>([])

async function loadSummary(): Promise<void> {
  try {
    summaryRows.value = await openingApi.summary()
  } catch {
    summaryRows.value = []
  }
}

/* ============================================================
 * 三、草稿新建 / 编辑（自定义弹窗承载动态明细行）
 * ============================================================ */

interface OpeningLineDraft {
  itemId: string
  batchNo: string
  qty: number
  unitCost: number
}

const editOpen = ref(false)
const submitting = ref(false)
const editing = ref<OpeningBatchDetailRow | null>(null)
const form = reactive<{ warehouseId: string; remark: string; lines: OpeningLineDraft[] }>({
  warehouseId: '',
  remark: '',
  lines: [],
})

function resetForm(): void {
  form.warehouseId = ''
  form.remark = ''
  form.lines = [{ itemId: '', batchNo: '', qty: 0, unitCost: 0 }]
}

function addLine(): void {
  form.lines.push({ itemId: '', batchNo: '', qty: 0, unitCost: 0 })
}

function removeLine(index: number): void {
  form.lines.splice(index, 1)
}

function lineAmount(line: OpeningLineDraft): number {
  return previewAmount(line.qty, line.unitCost)
}

function openCreate(): void {
  editing.value = null
  resetForm()
  editOpen.value = true
}

async function openEdit(row: OpeningBatchRow): Promise<void> {
  try {
    const detail = await openingApi.get(row.id)
    editing.value = detail
    form.warehouseId = detail.header.warehouseId
    form.remark = detail.header.remark ?? ''
    form.lines = detail.items.map((line) => ({
      itemId: line.itemId,
      batchNo: line.batchNo ?? '',
      qty: line.qty,
      unitCost: line.unitCost,
    }))
    editOpen.value = true
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : '加载期初详情失败')
  }
}

async function onSubmit(): Promise<void> {
  if (!form.warehouseId) {
    ElMessage.warning('请选择仓库')
    return
  }
  if (form.lines.length === 0) {
    ElMessage.warning('请至少添加一条明细')
    return
  }
  const invalid = form.lines.some(
    (line) => !line.itemId || !Number.isFinite(line.qty) || line.qty <= 0 || line.unitCost < 0,
  )
  if (invalid) {
    ElMessage.warning('请完整填写明细：商品必选、数量需大于 0、单价不得为负')
    return
  }
  submitting.value = true
  try {
    const lines: OpeningItemPayload[] = form.lines.map((line) => ({
      itemId: line.itemId,
      batchNo: line.batchNo.trim() || null,
      qty: line.qty,
      unitCost: line.unitCost,
    }))
    const payload: SaveOpeningPayload = {
      warehouseId: form.warehouseId,
      remark: form.remark.trim() || null,
      lines,
    }
    if (editing.value) {
      await openingApi.update(editing.value.header.id, payload)
      ElMessage.success('期初草稿已保存')
    } else {
      await openingApi.create(payload)
      ElMessage.success('期初草稿已创建（单号由系统生成）')
    }
    editOpen.value = false
    await refresh()
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : '保存期初草稿失败')
  } finally {
    submitting.value = false
  }
}

/* ============================================================
 * 四、删除 / 提交审批 / 详情
 * ============================================================ */

async function onRemove(row: OpeningBatchRow): Promise<void> {
  try {
    await ElMessageBox.confirm(`删除期初草稿「${row.batchNo}」后不可恢复，是否继续？`, '删除期初草稿', {
      confirmButtonText: '确认删除',
      cancelButtonText: '取消',
      type: 'warning',
    })
  } catch {
    return
  }
  try {
    await openingApi.remove(row.id)
    await refresh()
    ElMessage.success('期初草稿已删除')
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : '删除期初草稿失败')
  }
}

async function onSubmitApproval(row: OpeningBatchRow): Promise<void> {
  try {
    await ElMessageBox.confirm(
      `提交期初单「${row.batchNo}」过账审批，审批通过后计入库存台账，是否继续？`,
      '提交过账审批',
      { confirmButtonText: '提交申请', cancelButtonText: '取消', type: 'warning' },
    )
  } catch {
    return
  }
  try {
    const request = await openingApi.submit(row.id)
    await refresh()
    ElMessage.success(`过账申请已提交审批（状态码 ${request.status}）`)
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : '提交审批失败')
  }
}

const detailOpen = ref(false)
const detailLoading = ref(false)
const detail = ref<OpeningBatchDetailRow | null>(null)

async function openDetail(row: OpeningBatchRow): Promise<void> {
  detailOpen.value = true
  detailLoading.value = true
  try {
    detail.value = await openingApi.get(row.id)
  } catch (error) {
    detail.value = null
    ElMessage.error(error instanceof Error ? error.message : '加载期初详情失败')
  } finally {
    detailLoading.value = false
  }
}

function itemLabel(id: string): string {
  const item = items.value.find((row) => row.id === id)
  return item ? `${item.itemCode} · ${item.name}` : '—'
}

/* ============================================================
 * 五、基础下拉
 * ============================================================ */

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

onMounted(async () => {
  await Promise.all([loadOptions(), loadSummary()])
})
</script>

<template>
  <QlxPagePanel title="期初建账" subtitle="草稿 → 提交审批 → 过账入账（单号由系统生成）">
    <template #toolbar>
      <el-button v-permission="'stk.opening.action'" type="primary" @click="openCreate">
        <AppIcon name="Plus" :size="14" />
        新增期初
      </el-button>
    </template>

    <div v-if="summaryRows.length" class="opening-summary">
      <article v-for="summary in summaryRows" :key="summary.warehouseId" class="opening-summary__card">
        <p class="opening-summary__label">{{ warehouseName(summary.warehouseId) }}</p>
        <p class="opening-summary__value qlx-numeric">{{ formatQty(summary.totalQty) }}</p>
        <p class="opening-summary__meta qlx-numeric">金额 {{ formatMoney(summary.totalAmount) }}</p>
      </article>
    </div>

    <QlxSearchBar :fields="searchFields" :model-value="query" @search="search" @reset="reset" />

    <QlxDataTable
      :items="rows"
      :columns="columns"
      :loading="loading"
      :total="total"
      :page="page"
      :page-size="pageSize"
      :actions-width="260"
      empty-text="暂无期初建账单"
      @page-change="changePage"
    >
      <template #cell-warehouseId="{ row }">{{ warehouseName(row.warehouseId) }}</template>
      <template #cell-postedAt="{ row }">
        {{ row.postedAt ? formatDateTime(row.postedAt) : '—' }}
      </template>
      <template #cell-remark="{ row }">{{ row.remark ?? '—' }}</template>
      <template #actions="{ row }">
        <button type="button" class="cell-link" @click="openDetail(row)">详情</button>
        <template v-if="row.status === 1">
          <el-divider direction="vertical" />
          <button
            v-permission="'stk.opening.action'"
            type="button"
            class="cell-link"
            @click="openEdit(row)"
          >
            编辑
          </button>
          <el-divider direction="vertical" />
          <button
            v-permission="'stk.opening.action'"
            type="button"
            class="cell-link"
            @click="onSubmitApproval(row)"
          >
            提交审批
          </button>
          <el-divider direction="vertical" />
          <button
            v-permission="'stk.opening.action'"
            type="button"
            class="cell-link is-danger"
            @click="onRemove(row)"
          >
            删除
          </button>
        </template>
      </template>
    </QlxDataTable>

    <!-- 新建 / 编辑：动态明细行 -->
    <el-dialog
      v-model="editOpen"
      :title="editing ? `编辑期初草稿 · ${editing.header.batchNo}` : '新增期初草稿'"
      width="920px"
      :close-on-click-modal="false"
      append-to-body
    >
      <el-form label-width="90px">
        <el-row :gutter="16">
          <el-col :span="12">
            <el-form-item label="仓库" required>
              <el-select
                v-model="form.warehouseId"
                placeholder="请选择仓库"
                filterable
                style="width: 100%"
                :disabled="Boolean(editing)"
              >
                <el-option
                  v-for="option in warehouseOptions"
                  :key="option.value"
                  :label="option.label"
                  :value="option.value"
                />
              </el-select>
            </el-form-item>
          </el-col>
          <el-col :span="12">
            <el-form-item label="备注">
              <el-input v-model="form.remark" maxlength="200" clearable placeholder="可选" />
            </el-form-item>
          </el-col>
        </el-row>

        <div class="opening-lines">
          <div class="opening-lines__head">
            <h3>明细（金额由后端计算，此处为预览）</h3>
            <el-button size="small" @click="addLine">添加明细</el-button>
          </div>
          <el-table :data="form.lines" size="small" border empty-text="请添加明细">
            <el-table-column label="商品" min-width="240">
              <template #default="{ row }">
                <el-select
                  v-model="row.itemId"
                  placeholder="选择商品"
                  filterable
                  style="width: 100%"
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
                <el-input v-model="row.batchNo" maxlength="40" placeholder="非批次商品留空" />
              </template>
            </el-table-column>
            <el-table-column label="数量" width="150">
              <template #default="{ row }">
                <el-input-number v-model="row.qty" :min="0" :precision="4" style="width: 100%" />
              </template>
            </el-table-column>
            <el-table-column label="单价" width="160">
              <template #default="{ row }">
                <el-input-number v-model="row.unitCost" :min="0" :precision="4" style="width: 100%" />
              </template>
            </el-table-column>
            <el-table-column label="金额预览" width="140" align="right">
              <template #default="{ row }">
                <span class="qlx-numeric">{{ formatMoney(lineAmount(row)) }}</span>
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
        <el-button @click="editOpen = false">取消</el-button>
        <el-button type="primary" :loading="submitting" @click="onSubmit">保存草稿</el-button>
      </template>
    </el-dialog>

    <!-- 详情抽屉 -->
    <el-drawer
      v-model="detailOpen"
      :title="detail ? `期初详情 · ${detail.header.batchNo}` : '期初详情'"
      size="760px"
      append-to-body
    >
      <div v-loading="detailLoading" class="opening-detail">
        <template v-if="detail">
          <el-descriptions :column="2" border>
            <el-descriptions-item label="期初单号">
              <span class="qlx-numeric">{{ detail.header.batchNo }}</span>
            </el-descriptions-item>
            <el-descriptions-item label="仓库">
              {{ warehouseName(detail.header.warehouseId) }}
            </el-descriptions-item>
            <el-descriptions-item label="状态">
              {{ openingStatusLabel(detail.header.status) }}
            </el-descriptions-item>
            <el-descriptions-item label="过账时间">
              {{ detail.header.postedAt ? formatDateTime(detail.header.postedAt) : '—' }}
            </el-descriptions-item>
            <el-descriptions-item label="备注" :span="2">
              {{ detail.header.remark ?? '—' }}
            </el-descriptions-item>
          </el-descriptions>

          <h3 class="opening-detail__title">明细（{{ detail.items.length }}）</h3>
          <el-table :data="detail.items" size="small" border empty-text="暂无明细">
            <el-table-column label="商品" min-width="220">
              <template #default="{ row }">{{ itemLabel(row.itemId) }}</template>
            </el-table-column>
            <el-table-column label="批次号" width="140">
              <template #default="{ row }">{{ row.batchNo ?? '—' }}</template>
            </el-table-column>
            <el-table-column label="数量" width="120" align="right">
              <template #default="{ row }">
                <span class="qlx-numeric">{{ formatQty(row.qty) }}</span>
              </template>
            </el-table-column>
            <el-table-column label="单价" width="120" align="right">
              <template #default="{ row }">
                <span class="qlx-numeric">{{ formatCost(row.unitCost) }}</span>
              </template>
            </el-table-column>
            <el-table-column label="金额" width="130" align="right">
              <template #default="{ row }">
                <span class="qlx-numeric">{{ formatMoney(row.amount) }}</span>
              </template>
            </el-table-column>
          </el-table>
        </template>
      </div>
    </el-drawer>
  </QlxPagePanel>
</template>

<style scoped>
.opening-summary {
  display: flex;
  padding: var(--qlx-space-3) var(--qlx-space-4);
  flex-wrap: wrap;
  gap: var(--qlx-space-3);
  border-bottom: 1px solid var(--qlx-line);
}

.opening-summary__card {
  min-width: 190px;
  padding: var(--qlx-space-3) var(--qlx-space-4);
  border: 1px solid var(--qlx-line);
  border-radius: var(--qlx-radius-md);
  background: var(--qlx-brand-50);
}

.opening-summary__label {
  color: var(--qlx-ink-500);
  font-size: var(--qlx-text-xs);
}

.opening-summary__value {
  margin-top: 2px;
  color: var(--qlx-brand-900);
  font-size: var(--qlx-text-xl);
  font-weight: 600;
}

.opening-summary__meta {
  color: var(--qlx-ink-400);
  font-size: var(--qlx-text-xs);
}

.opening-lines {
  margin-top: var(--qlx-space-2);
}

.opening-lines__head {
  display: flex;
  margin-bottom: var(--qlx-space-2);
  align-items: center;
  justify-content: space-between;
}

.opening-lines__head h3,
.opening-detail__title {
  color: var(--qlx-ink-600);
  font-size: var(--qlx-text-sm);
  font-weight: 600;
}

.opening-detail__title {
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
