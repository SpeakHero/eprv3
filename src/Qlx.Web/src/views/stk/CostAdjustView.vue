<!--
  所属公司：四川淇利馨集团有限公司
  描述：成本调整——扁平单据列表 + 草稿新建 / 编辑 / 删除 + 提交审批 + 详情抽屉 + 旧均价 / 差额前端预览
  说明：
    1. 单号由后端生成，只读展示，表单无单号输入框（N-1）；
    2. 旧均价取台账当前均价、差额由后端按数量计算，前端预览仅为界面展示（V-1）；
    3. 仓库范围为「全部」时单据 warehouseId 为 null，明细仍逐行指定仓库。
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
import { itemApi, costAdjustApi, stockApi } from '@/api/stk'
import { warehouseApi } from '@/api/wms'
import { useLocalTable } from '@/composables/useLocalTable'
import type {
  CostAdjustDetailRow,
  CostAdjustLinePayload,
  CostAdjustReason,
  CostAdjustRow,
  ItemRow,
  SaveCostAdjustPayload,
  StockBalanceRow,
} from '@/types/stk'
import type { WarehouseRow } from '@/types/wms'
import { formatDateTime } from '@/utils/format'
import {
  COST_ADJUST_REASON_LABELS,
  COST_ADJUST_STATUS_LABELS,
  costAdjustReasonLabel,
  costAdjustStatusLabel,
  costAdjustStatusTone,
  formatCost,
  formatMoney,
  previewDiffAmount,
  toNumberOptions,
  toStringOptions,
} from '@/utils/stk-enums'

/* ============================================================
 * 一、成本调整单列表
 * ============================================================ */

const items = ref<ItemRow[]>([])
const warehouses = ref<WarehouseRow[]>([])
const ledgerRows = ref<StockBalanceRow[]>([])

const itemOptions = computed(() =>
  items.value.map((item) => ({ label: `${item.itemCode} · ${item.name}`, value: item.id })),
)
const warehouseOptions = computed(() =>
  warehouses.value.map((warehouse) => ({ label: warehouse.name, value: warehouse.id })),
)

const searchFields: SearchField[] = [
  {
    prop: 'status',
    label: '状态',
    type: 'select',
    options: toStringOptions(COST_ADJUST_STATUS_LABELS),
    width: 140,
  },
]

const { loading, rows, total, page, pageSize, query, search, reset, refresh, changePage } =
  useLocalTable<CostAdjustRow>((params) =>
    costAdjustApi.list({ status: params.status ? Number(params.status) : undefined }),
  )

function warehouseName(id: string | null): string {
  if (!id) {
    return '全部仓库'
  }
  return warehouses.value.find((warehouse) => warehouse.id === id)?.name ?? '—'
}

function itemLabel(id: string): string {
  const item = items.value.find((row) => row.id === id)
  return item ? `${item.itemCode} · ${item.name}` : '—'
}

const columns: Array<CrudColumn<CostAdjustRow>> = [
  { prop: 'adjNo', label: '调整单号', type: 'code', width: 170, fixed: 'left' },
  {
    prop: 'reasonCode',
    label: '调整原因',
    type: 'enum',
    enumLabels: COST_ADJUST_REASON_LABELS,
    width: 130,
  },
  { prop: 'warehouseId', label: '仓库范围', minWidth: 150 },
  { prop: 'totalDiffAmount', label: '差额合计', width: 140, align: 'right' },
  {
    prop: 'status',
    label: '状态',
    type: 'tag',
    width: 100,
    align: 'center',
    tagText: (row) => costAdjustStatusLabel(row.status),
    tagTone: (row) => costAdjustStatusTone(row.status),
  },
  { prop: 'appliedAt', label: '申请时间', type: 'time', width: 170 },
  { prop: 'remark', label: '备注', minWidth: 180 },
]

/* ============================================================
 * 二、台账索引（旧均价 / 数量 / 差额预览）
 * ============================================================ */

async function loadLedger(): Promise<void> {
  try {
    ledgerRows.value = await stockApi.ledger()
  } catch {
    ledgerRows.value = []
  }
}

function findBalance(itemId: string, warehouseId: string): StockBalanceRow | undefined {
  return ledgerRows.value.find(
    (row) => row.itemId === itemId && row.warehouseId === warehouseId,
  )
}

/* ============================================================
 * 三、草稿新建 / 编辑（自定义弹窗承载动态明细行）
 * ============================================================ */

interface AdjustLineDraft {
  itemId: string
  warehouseId: string
  newAvgCost: number
}

const editOpen = ref(false)
const submitting = ref(false)
const editing = ref<CostAdjustDetailRow | null>(null)
const form = reactive<{
  reasonCode: CostAdjustReason
  scope: string
  remark: string
  lines: AdjustLineDraft[]
}>({
  reasonCode: 1,
  scope: 'all',
  remark: '',
  lines: [],
})

function resetForm(): void {
  form.reasonCode = 1
  form.scope = 'all'
  form.remark = ''
  form.lines = [{ itemId: '', warehouseId: '', newAvgCost: 0 }]
}

function applyScope(): void {
  if (form.scope !== 'all') {
    for (const line of form.lines) {
      line.warehouseId = form.scope
    }
  }
}

function addLine(): void {
  form.lines.push({
    itemId: '',
    warehouseId: form.scope === 'all' ? '' : form.scope,
    newAvgCost: 0,
  })
}

function removeLine(index: number): void {
  form.lines.splice(index, 1)
}

function lineOldAvg(line: AdjustLineDraft): number | null {
  if (!line.itemId || !line.warehouseId) {
    return null
  }
  return findBalance(line.itemId, line.warehouseId)?.avgCost ?? null
}

function lineQty(line: AdjustLineDraft): number {
  if (!line.itemId || !line.warehouseId) {
    return 0
  }
  return findBalance(line.itemId, line.warehouseId)?.qtyOnhand ?? 0
}

function lineDiff(line: AdjustLineDraft): number {
  const oldAvg = lineOldAvg(line)
  if (oldAvg === null) {
    return 0
  }
  return previewDiffAmount(line.newAvgCost, oldAvg, lineQty(line))
}

async function openCreate(): Promise<void> {
  editing.value = null
  resetForm()
  await loadLedger()
  editOpen.value = true
}

async function openEdit(row: CostAdjustRow): Promise<void> {
  try {
    const detail = await costAdjustApi.get(row.id)
    editing.value = detail
    form.reasonCode = detail.header.reasonCode
    form.scope = detail.header.warehouseId ?? 'all'
    form.remark = detail.header.remark ?? ''
    form.lines = detail.lines.map((line) => ({
      itemId: line.itemId,
      warehouseId: line.warehouseId,
      newAvgCost: line.newAvgCost,
    }))
    await loadLedger()
    editOpen.value = true
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : '加载成本调整详情失败')
  }
}

async function onSubmit(): Promise<void> {
  if (form.lines.length === 0) {
    ElMessage.warning('请至少添加一条调整明细')
    return
  }
  const invalid = form.lines.some(
    (line) =>
      !line.itemId ||
      !line.warehouseId ||
      !Number.isFinite(line.newAvgCost) ||
      line.newAvgCost < 0,
  )
  if (invalid) {
    ElMessage.warning('请完整填写明细：商品与仓库必选、新均价不得为负')
    return
  }
  submitting.value = true
  try {
    const lines: CostAdjustLinePayload[] = form.lines.map((line) => ({
      itemId: line.itemId,
      warehouseId: line.warehouseId,
      newAvgCost: line.newAvgCost,
    }))
    const payload: SaveCostAdjustPayload = {
      reasonCode: form.reasonCode,
      warehouseId: form.scope === 'all' ? null : form.scope,
      lines,
      remark: form.remark.trim() || null,
    }
    if (editing.value) {
      await costAdjustApi.update(editing.value.header.id, payload)
      ElMessage.success('成本调整草稿已保存')
    } else {
      await costAdjustApi.create(payload)
      ElMessage.success('成本调整草稿已创建（单号由系统生成）')
    }
    editOpen.value = false
    await refresh()
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : '保存成本调整失败')
  } finally {
    submitting.value = false
  }
}

/* ============================================================
 * 四、删除 / 提交审批 / 详情
 * ============================================================ */

async function onRemove(row: CostAdjustRow): Promise<void> {
  try {
    await ElMessageBox.confirm(`删除成本调整草稿「${row.adjNo}」后不可恢复，是否继续？`, '删除草稿', {
      confirmButtonText: '确认删除',
      cancelButtonText: '取消',
      type: 'warning',
    })
  } catch {
    return
  }
  try {
    await costAdjustApi.remove(row.id)
    await refresh()
    ElMessage.success('成本调整草稿已删除')
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : '删除草稿失败')
  }
}

async function onSubmitApproval(row: CostAdjustRow): Promise<void> {
  try {
    await ElMessageBox.confirm(
      `提交成本调整单「${row.adjNo}」审批，审批通过后按新均价重算库存成本，是否继续？`,
      '提交成本调整审批',
      { confirmButtonText: '提交申请', cancelButtonText: '取消', type: 'warning' },
    )
  } catch {
    return
  }
  try {
    const request = await costAdjustApi.submit(row.id)
    await refresh()
    ElMessage.success(`成本调整申请已提交审批（状态码 ${request.status}）`)
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : '提交审批失败')
  }
}

const detailOpen = ref(false)
const detailLoading = ref(false)
const detail = ref<CostAdjustDetailRow | null>(null)

async function openDetail(row: CostAdjustRow): Promise<void> {
  detailOpen.value = true
  detailLoading.value = true
  try {
    detail.value = await costAdjustApi.get(row.id)
  } catch (error) {
    detail.value = null
    ElMessage.error(error instanceof Error ? error.message : '加载成本调整详情失败')
  } finally {
    detailLoading.value = false
  }
}

/* ============================================================
 * 五、基础下拉
 * ============================================================ */

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

onMounted(async () => {
  await Promise.all([loadOptions(), loadLedger()])
})
</script>

<template>
  <QlxPagePanel title="成本调整" subtitle="草稿 → 提交审批 → 按新均价重算库存成本">
    <template #toolbar>
      <el-button v-permission="'stk.costadjust.action'" type="primary" @click="openCreate">
        <AppIcon name="Plus" :size="14" />
        新增成本调整
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
      :actions-width="260"
      empty-text="暂无成本调整单"
      @page-change="changePage"
    >
      <template #cell-warehouseId="{ row }">{{ warehouseName(row.warehouseId) }}</template>
      <template #cell-totalDiffAmount="{ row }">
        <span class="qlx-numeric">{{ formatMoney(row.totalDiffAmount) }}</span>
      </template>
      <template #cell-appliedAt="{ row }">
        {{ row.appliedAt ? formatDateTime(row.appliedAt) : '—' }}
      </template>
      <template #cell-remark="{ row }">{{ row.remark ?? '—' }}</template>
      <template #actions="{ row }">
        <button type="button" class="cell-link" @click="openDetail(row)">详情</button>
        <template v-if="row.status === 1">
          <el-divider direction="vertical" />
          <button
            v-permission="'stk.costadjust.action'"
            type="button"
            class="cell-link"
            @click="openEdit(row)"
          >
            编辑
          </button>
          <el-divider direction="vertical" />
          <button
            v-permission="'stk.costadjust.action'"
            type="button"
            class="cell-link"
            @click="onSubmitApproval(row)"
          >
            提交审批
          </button>
          <el-divider direction="vertical" />
          <button
            v-permission="'stk.costadjust.action'"
            type="button"
            class="cell-link is-danger"
            @click="onRemove(row)"
          >
            删除
          </button>
        </template>
      </template>
    </QlxDataTable>

    <!-- 新建 / 编辑：动态明细行 + 旧均价 / 差额预览 -->
    <el-dialog
      v-model="editOpen"
      :title="editing ? `编辑成本调整 · ${editing.header.adjNo}` : '新增成本调整草稿'"
      width="980px"
      :close-on-click-modal="false"
      append-to-body
    >
      <el-form label-width="90px">
        <el-row :gutter="16">
          <el-col :span="8">
            <el-form-item label="调整原因" required>
              <el-select v-model="form.reasonCode" style="width: 100%">
                <el-option
                  v-for="option in toNumberOptions(COST_ADJUST_REASON_LABELS)"
                  :key="option.value"
                  :label="option.label"
                  :value="option.value"
                />
              </el-select>
            </el-form-item>
          </el-col>
          <el-col :span="8">
            <el-form-item label="仓库范围">
              <el-select v-model="form.scope" style="width: 100%" @change="applyScope">
                <el-option label="全部仓库" value="all" />
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
            <el-form-item label="备注">
              <el-input v-model="form.remark" maxlength="200" clearable placeholder="可选" />
            </el-form-item>
          </el-col>
        </el-row>

        <div class="adjust-lines">
          <div class="adjust-lines__head">
            <h3>明细（旧均价取台账当前值，差额由后端计算，此处为预览）</h3>
            <el-button size="small" @click="addLine">添加明细</el-button>
          </div>
          <el-table :data="form.lines" size="small" border empty-text="请添加明细">
            <el-table-column label="商品" min-width="230">
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
            <el-table-column label="仓库" width="170">
              <template #default="{ row }">
                <el-select
                  v-model="row.warehouseId"
                  placeholder="选择仓库"
                  filterable
                  style="width: 100%"
                  :disabled="form.scope !== 'all'"
                >
                  <el-option
                    v-for="option in warehouseOptions"
                    :key="option.value"
                    :label="option.label"
                    :value="option.value"
                  />
                </el-select>
              </template>
            </el-table-column>
            <el-table-column label="台账数量" width="120" align="right">
              <template #default="{ row }">
                <span class="qlx-numeric">
                  {{ row.itemId && row.warehouseId ? lineQty(row).toFixed(4) : '—' }}
                </span>
              </template>
            </el-table-column>
            <el-table-column label="旧均价" width="120" align="right">
              <template #default="{ row }">
                <span class="qlx-numeric">{{ formatCost(lineOldAvg(row)) }}</span>
              </template>
            </el-table-column>
            <el-table-column label="新均价" width="160">
              <template #default="{ row }">
                <el-input-number
                  v-model="row.newAvgCost"
                  :min="0"
                  :precision="4"
                  style="width: 100%"
                />
              </template>
            </el-table-column>
            <el-table-column label="差额预览" width="140" align="right">
              <template #default="{ row }">
                <span class="qlx-numeric">{{ formatMoney(lineDiff(row)) }}</span>
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
      :title="detail ? `成本调整详情 · ${detail.header.adjNo}` : '成本调整详情'"
      size="820px"
      append-to-body
    >
      <div v-loading="detailLoading" class="adjust-detail">
        <template v-if="detail">
          <el-descriptions :column="2" border>
            <el-descriptions-item label="调整单号">
              <span class="qlx-numeric">{{ detail.header.adjNo }}</span>
            </el-descriptions-item>
            <el-descriptions-item label="调整原因">
              {{ costAdjustReasonLabel(detail.header.reasonCode) }}
            </el-descriptions-item>
            <el-descriptions-item label="仓库范围">
              {{ warehouseName(detail.header.warehouseId) }}
            </el-descriptions-item>
            <el-descriptions-item label="差额合计">
              <span class="qlx-numeric">{{ formatMoney(detail.header.totalDiffAmount) }}</span>
            </el-descriptions-item>
            <el-descriptions-item label="状态">
              {{ costAdjustStatusLabel(detail.header.status) }}
            </el-descriptions-item>
            <el-descriptions-item label="申请时间">
              {{ detail.header.appliedAt ? formatDateTime(detail.header.appliedAt) : '—' }}
            </el-descriptions-item>
            <el-descriptions-item label="备注" :span="2">
              {{ detail.header.remark ?? '—' }}
            </el-descriptions-item>
          </el-descriptions>

          <h3 class="adjust-detail__title">明细（{{ detail.lines.length }}）</h3>
          <el-table :data="detail.lines" size="small" border empty-text="暂无明细">
            <el-table-column label="商品" min-width="220">
              <template #default="{ row }">{{ itemLabel(row.itemId) }}</template>
            </el-table-column>
            <el-table-column label="仓库" width="150">
              <template #default="{ row }">{{ warehouseName(row.warehouseId) }}</template>
            </el-table-column>
            <el-table-column label="旧均价" width="130" align="right">
              <template #default="{ row }">
                <span class="qlx-numeric">{{ formatCost(row.oldAvgCost) }}</span>
              </template>
            </el-table-column>
            <el-table-column label="新均价" width="130" align="right">
              <template #default="{ row }">
                <span class="qlx-numeric">{{ formatCost(row.newAvgCost) }}</span>
              </template>
            </el-table-column>
            <el-table-column label="差额" width="140" align="right">
              <template #default="{ row }">
                <span class="qlx-numeric">{{ formatMoney(row.diffAmount) }}</span>
              </template>
            </el-table-column>
          </el-table>
        </template>
      </div>
    </el-drawer>
  </QlxPagePanel>
</template>

<style scoped>
.adjust-lines {
  margin-top: var(--qlx-space-2);
}

.adjust-lines__head {
  display: flex;
  margin-bottom: var(--qlx-space-2);
  align-items: center;
  justify-content: space-between;
}

.adjust-lines__head h3,
.adjust-detail__title {
  color: var(--qlx-ink-600);
  font-size: var(--qlx-text-sm);
  font-weight: 600;
}

.adjust-detail__title {
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
