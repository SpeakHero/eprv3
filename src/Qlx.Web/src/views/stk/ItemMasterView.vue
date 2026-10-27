<!--
  所属公司：四川淇利馨集团有限公司
  描述：商品档案——扁平列表 + 新建 / 编辑 + 启停（有库存转停用审批）+ 关键属性变更提示 + 价格历史抽屉
  说明：
    1. 商品编码由后端生成，只读展示，表单无编码输入框（N-1）；
    2. 成本类字段按 stk.cost.field 权限裁剪（后端置 null，前端隐藏列 / 表单字段）；
    3. 税率后端为 0~1 小数，表单按百分比录入并换算，展示由前端格式化（B-4）。
-->
<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import AppIcon from '@/components/AppIcon.vue'
import QlxPagePanel from '@/components/crud/QlxPagePanel.vue'
import QlxSearchBar from '@/components/crud/QlxSearchBar.vue'
import type { SearchField } from '@/components/crud/QlxSearchBar.vue'
import QlxDataTable from '@/components/crud/QlxDataTable.vue'
import type { CrudColumn } from '@/components/crud/QlxDataTable.vue'
import QlxFormDialog from '@/components/crud/QlxFormDialog.vue'
import type { FormField, FormValue } from '@/components/crud/QlxFormDialog.vue'
import { categoryApi, unitApi } from '@/api/basMaster'
import { itemApi, stockApi } from '@/api/stk'
import { useLocalTable } from '@/composables/useLocalTable'
import { usePermission } from '@/composables/usePermission'
import type { ItemCategoryRow, UnitRow } from '@/types/bas-master'
import type { CreateItemPayload, ItemPriceLogRow, ItemRow, StockBalanceRow } from '@/types/stk'
import { formatDateTime } from '@/utils/format'
import {
  formatCost,
  formatTaxRate,
  itemStatusLabel,
  itemStatusTone,
  ITEM_STATUS_LABELS,
  priceFieldLabel,
  toStringOptions,
} from '@/utils/stk-enums'

const { hasPermission } = usePermission()
/** 成本字段权限（无权限时后端不下发成本，前端隐藏对应列与表单字段，B-3-1）。 */
const canViewCost = computed(() => hasPermission('stk.cost.field'))

/* ============================================================
 * 一、商品列表（后端全量扁平集合，展示筛选 / 分页在前端）
 * ============================================================ */

const searchFields: SearchField[] = [
  {
    prop: 'keyword',
    label: '关键词',
    type: 'input',
    placeholder: '编码 / 名称 / 规格 / 条码',
    width: 240,
  },
  { prop: 'categoryId', label: '商品分类', type: 'select', width: 180 },
  {
    prop: 'status',
    label: '状态',
    type: 'select',
    options: toStringOptions(ITEM_STATUS_LABELS),
    width: 120,
  },
]

const categories = ref<ItemCategoryRow[]>([])
const units = ref<UnitRow[]>([])
const balanceRows = ref<StockBalanceRow[]>([])

const categoryOptions = computed(() =>
  categories.value.map((category) => ({ label: category.name, value: category.id })),
)
const unitOptions = computed(() => units.value.map((unit) => ({ label: unit.name, value: unit.id })))

const { loading, items, total, page, pageSize, query, search, reset, refresh, changePage } =
  useLocalTable<ItemRow>(() => itemApi.list(), {
    filter: (row, params) => {
      const keyword = (params.keyword ?? '').trim().toLowerCase()
      if (keyword) {
        const haystack = [row.name, row.itemCode, row.spec ?? '', row.barcode ?? '']
          .join(' ')
          .toLowerCase()
        if (!haystack.includes(keyword)) {
          return false
        }
      }
      if (params.categoryId && row.categoryId !== params.categoryId) {
        return false
      }
      if (params.status && Number(params.status) !== row.status) {
        return false
      }
      return true
    },
  })

function categoryName(id: string): string {
  return categories.value.find((category) => category.id === id)?.name ?? '—'
}

function unitLabel(id: string): string {
  const unit = units.value.find((item) => item.id === id)
  return unit ? `${unit.name}（精度 ${unit.precision}）` : '—'
}

const columns = computed<Array<CrudColumn<ItemRow>>>(() => [
  { prop: 'itemCode', label: '商品编码', type: 'code', width: 150, fixed: 'left' },
  { prop: 'name', label: '商品名称', minWidth: 180 },
  { prop: 'spec', label: '规格', minWidth: 140 },
  { prop: 'barcode', label: '条码', minWidth: 150 },
  { prop: 'categoryId', label: '商品分类', minWidth: 150 },
  { prop: 'unitId', label: '计量单位', minWidth: 150 },
  { prop: 'batchEnabled', label: '批次管理', type: 'check', width: 100, align: 'center' },
  { prop: 'expiryEnabled', label: '有效期', type: 'check', width: 100, align: 'center' },
  { prop: 'taxRate', label: '税率', width: 100, align: 'right' },
  ...(canViewCost.value
    ? ([
        { prop: 'refCostPrice', label: '参考成本', width: 120, align: 'right' },
        { prop: 'guidePrice', label: '指导价', width: 120, align: 'right' },
      ] as Array<CrudColumn<ItemRow>>)
    : []),
  {
    prop: 'status',
    label: '状态',
    type: 'tag',
    width: 90,
    align: 'center',
    tagText: (row) => itemStatusLabel(row.status),
    tagTone: (row) => itemStatusTone(row.status),
  },
])

/* ============================================================
 * 二、新建 / 编辑（编码只读，N-1）
 * ============================================================ */

const formOpen = ref(false)
const submitting = ref(false)
const editing = ref<ItemRow | null>(null)

/** 原始税率（0~1）→ 表单百分比输入。 */
function toPercentInput(rate: number | null | undefined): number {
  if (rate === null || rate === undefined) {
    return 0
  }
  return Math.round(rate * 10000) / 100
}

const formFields = computed<FormField[]>(() => {
  const base: FormField[] = [
    {
      prop: 'name',
      label: '商品名称',
      type: 'input',
      required: true,
      maxlength: 80,
      full: true,
      defaultValue: editing.value?.name ?? '',
      hint: editing.value
        ? `编码 ${editing.value.itemCode} 由系统生成，不可修改`
        : '编码由系统按规则自动生成，无需填写',
    },
    {
      prop: 'spec',
      label: '规格',
      type: 'input',
      maxlength: 80,
      defaultValue: editing.value?.spec ?? '',
    },
    {
      prop: 'barcode',
      label: '条码',
      type: 'input',
      maxlength: 64,
      defaultValue: editing.value?.barcode ?? '',
    },
    {
      prop: 'categoryId',
      label: '商品分类',
      type: 'select',
      required: true,
      filterable: true,
      options: categoryOptions.value,
      defaultValue: editing.value?.categoryId ?? '',
    },
    {
      prop: 'unitId',
      label: '计量单位',
      type: 'select',
      required: true,
      filterable: true,
      options: unitOptions.value,
      defaultValue: editing.value?.unitId ?? '',
    },
    {
      prop: 'taxRate',
      label: '税率(%)',
      type: 'number',
      defaultValue: editing.value ? toPercentInput(editing.value.taxRate) : 13,
      hint: '按百分比录入（如 13 表示 13%），提交时换算为后端 0~1 小数',
    },
    {
      prop: 'batchEnabled',
      label: '批次管理',
      type: 'switch',
      defaultValue: editing.value?.batchEnabled ?? false,
    },
    {
      prop: 'expiryEnabled',
      label: '有效期管理',
      type: 'switch',
      defaultValue: editing.value?.expiryEnabled ?? false,
    },
  ]
  if (canViewCost.value) {
    base.push(
      {
        prop: 'refCostPrice',
        label: '参考成本',
        type: 'number',
        defaultValue: editing.value?.refCostPrice ?? 0,
      },
      {
        prop: 'guidePrice',
        label: '指导价',
        type: 'number',
        defaultValue: editing.value?.guidePrice ?? 0,
      },
    )
  }
  return base
})

function openCreate(): void {
  editing.value = null
  formOpen.value = true
}

function openEdit(row: ItemRow): void {
  editing.value = row
  formOpen.value = true
}

async function onSubmit(values: Record<string, FormValue>): Promise<void> {
  submitting.value = true
  try {
    const payload: CreateItemPayload = {
      name: String(values.name ?? '').trim(),
      spec: String(values.spec ?? '').trim() || null,
      barcode: String(values.barcode ?? '').trim() || null,
      categoryId: String(values.categoryId ?? ''),
      unitId: String(values.unitId ?? ''),
      batchEnabled: Boolean(values.batchEnabled),
      expiryEnabled: Boolean(values.expiryEnabled),
      taxRate: Math.round(Number(values.taxRate ?? 0) * 100) / 10000,
      refCostPrice: canViewCost.value
        ? Number(values.refCostPrice ?? 0)
        : (editing.value?.refCostPrice ?? null),
      guidePrice: canViewCost.value
        ? Number(values.guidePrice ?? 0)
        : (editing.value?.guidePrice ?? null),
    }
    if (editing.value) {
      const result = await itemApi.update(editing.value.id, payload)
      if (result.pendingApproval) {
        ElMessage.warning('关键属性变更已提交审批，审批通过后生效')
      } else {
        ElMessage.success('商品信息已保存')
      }
    } else {
      const request = await itemApi.create(payload)
      ElMessage.success(`商品新建申请已提交审批（状态码 ${request.status}）`)
    }
    formOpen.value = false
    await refresh()
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : '保存商品失败')
  } finally {
    submitting.value = false
  }
}

/* ============================================================
 * 三、启停（有库存走停用审批，无库存直接停用）
 * ============================================================ */

/** 有库存商品集合（台账加载失败时按无库存处理，由后端最终校验）。 */
const stockedItemIds = computed(
  () => new Set(balanceRows.value.filter((row) => row.qtyOnhand > 0).map((row) => row.itemId)),
)

async function onToggle(row: ItemRow): Promise<void> {
  const disabling = row.status === 1
  const hasStock = stockedItemIds.value.has(row.id)
  try {
    await ElMessageBox.confirm(
      disabling
        ? hasStock
          ? `商品「${row.name}」存在库存，需提交停用审批，审批通过后生效，是否继续？`
          : `停用商品「${row.name}」后，业务单据不可再选该商品，是否继续？`
        : `启用商品「${row.name}」后恢复业务办理，是否继续？`,
      disabling ? (hasStock ? '提交停用审批' : '停用商品') : '启用商品',
      { confirmButtonText: '确认', cancelButtonText: '取消', type: 'warning' },
    )
  } catch {
    return
  }
  try {
    if (disabling && hasStock) {
      const request = await itemApi.submitDisable(row.id)
      ElMessage.success(`停用申请已提交审批（状态码 ${request.status}）`)
    } else {
      await itemApi.toggle(row.id)
      ElMessage.success(disabling ? '商品已停用' : '商品已启用')
    }
    await refresh()
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : '操作失败')
  }
}

/* ============================================================
 * 四、价格历史抽屉
 * ============================================================ */

const priceOpen = ref(false)
const priceLoading = ref(false)
const priceTarget = ref<ItemRow | null>(null)
const priceLogs = ref<ItemPriceLogRow[]>([])

async function openPriceLogs(row: ItemRow): Promise<void> {
  priceTarget.value = row
  priceOpen.value = true
  priceLoading.value = true
  try {
    priceLogs.value = await itemApi.priceLogs(row.id)
  } catch (error) {
    priceLogs.value = []
    ElMessage.error(error instanceof Error ? error.message : '加载价格历史失败')
  } finally {
    priceLoading.value = false
  }
}

/* ============================================================
 * 五、基础下拉与台账预加载（跨模块 / 独立权限，失败不阻断）
 * ============================================================ */

async function loadOptions(): Promise<void> {
  try {
    categories.value = await categoryApi.list()
  } catch {
    categories.value = []
  }
  try {
    units.value = await unitApi.list()
  } catch {
    units.value = []
  }
  try {
    balanceRows.value = await stockApi.ledger()
  } catch {
    balanceRows.value = []
  }
}

onMounted(loadOptions)
</script>

<template>
  <QlxPagePanel title="商品档案" subtitle="商品编码由系统生成 · 成本字段按权限展示">
    <template #toolbar>
      <el-button v-permission="'stk.item.action'" type="primary" @click="openCreate">
        <AppIcon name="Plus" :size="14" />
        新增商品
      </el-button>
    </template>

    <QlxSearchBar :fields="searchFields" :model-value="query" @search="search" @reset="reset" />

    <QlxDataTable
      :items="items"
      :columns="columns"
      :loading="loading"
      :total="total"
      :page="page"
      :page-size="pageSize"
      :actions-width="220"
      empty-text="暂无商品"
      @page-change="changePage"
    >
      <template #cell-spec="{ row }">{{ row.spec ?? '—' }}</template>
      <template #cell-barcode="{ row }">{{ row.barcode ?? '—' }}</template>
      <template #cell-categoryId="{ row }">{{ categoryName(row.categoryId) }}</template>
      <template #cell-unitId="{ row }">{{ unitLabel(row.unitId) }}</template>
      <template #cell-taxRate="{ row }">
        <span class="qlx-numeric">{{ formatTaxRate(row.taxRate) }}</span>
      </template>
      <template #cell-refCostPrice="{ row }">
        <span class="qlx-numeric">{{ formatCost(row.refCostPrice) }}</span>
      </template>
      <template #cell-guidePrice="{ row }">
        <span class="qlx-numeric">{{ formatCost(row.guidePrice) }}</span>
      </template>
      <template #actions="{ row }">
        <button type="button" class="cell-link" @click="openPriceLogs(row)">价格历史</button>
        <el-divider direction="vertical" />
        <button
          v-permission="'stk.item.action'"
          type="button"
          class="cell-link"
          @click="openEdit(row)"
        >
          编辑
        </button>
        <el-divider direction="vertical" />
        <button
          v-permission="'stk.item.action'"
          type="button"
          class="cell-link"
          :class="{ 'is-danger': row.status === 1 }"
          @click="onToggle(row)"
        >
          {{ row.status === 1 ? '停用' : '启用' }}
        </button>
      </template>
    </QlxDataTable>

    <QlxFormDialog
      v-model="formOpen"
      :title="editing ? `编辑商品 · ${editing.name}` : '新增商品'"
      :fields="formFields"
      :submitting="submitting"
      submit-text="保存商品"
      :width="680"
      class="stk-form-dialog"
      @submit="onSubmit"
    />

    <el-drawer
      v-model="priceOpen"
      :title="priceTarget ? `价格历史 · ${priceTarget.name}` : '价格历史'"
      size="720px"
      append-to-body
    >
      <div v-loading="priceLoading" class="price-history">
        <el-table :data="priceLogs" size="small" border empty-text="暂无价格变更记录">
          <el-table-column label="变更字段" width="120">
            <template #default="{ row }">{{ priceFieldLabel(row.field) }}</template>
          </el-table-column>
          <el-table-column label="原值" min-width="130">
            <template #default="{ row }">{{ row.oldValue ?? '—' }}</template>
          </el-table-column>
          <el-table-column label="新值" min-width="130">
            <template #default="{ row }">{{ row.newValue ?? '—' }}</template>
          </el-table-column>
          <el-table-column label="审批人" width="130">
            <template #default="{ row }">{{ row.approvedBy ?? '—' }}</template>
          </el-table-column>
          <el-table-column label="变更时间" width="170">
            <template #default="{ row }">{{ formatDateTime(row.createdAt) }}</template>
          </el-table-column>
        </el-table>
      </div>
    </el-drawer>
  </QlxPagePanel>
</template>

<style scoped>
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

.price-history {
  min-height: 240px;
}
</style>

<style>
@media (max-width: 767px) {
  .stk-form-dialog.el-dialog {
    width: min(680px, 92vw);
  }
}
</style>
