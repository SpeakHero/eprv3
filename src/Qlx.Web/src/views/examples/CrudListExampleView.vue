<!--
  所属公司：四川淇利馨集团有限公司
  描述：标准示例 · 列表页——查询区 / 数据表格 / 分页 / 工具栏按钮 / 新建编辑弹窗 / 删除确认 /
        批量导入 / 详情查看 的完整标准用法（mock 数据驱动）
  说明：本页是所有「列表型业务页面」的复制起点。接入真实后端时，仅需把
        useCrudTable(fetchGoodsDemoList) 换成 @/api 下同签名函数，并把 create/update/remove
        换成真实接口调用，模板无需改动（参考 PortalUserListView → @/api/admin）。
  组件适用场景：
    QlxPagePanel     页面骨架：标题 + 描述 + 右上角工具按钮 + 内容插槽
    QlxSearchBar     查询区：配置驱动 input / select，受控于 useCrudTable 的 query
    QlxDataTable     数据表格：配置驱动列 + 内置分页器 + #cell-{prop} 自定义单元格 + #actions 操作列
    QlxFormDialog    新建 / 编辑 / 重置类弹窗：配置驱动字段 + 校验 + 提交态
    QlxImportDialog  批量导入：粘贴 / 选择 CSV 逐行解析校验后提交
    useCrudTable     列表页状态机：loading / items / total / page / query / search / reset / refresh / changePage
-->
<script setup lang="ts">
import { computed, ref } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import AppIcon from '@/components/AppIcon.vue'
import QlxDataTable from '@/components/crud/QlxDataTable.vue'
import type { CrudColumn } from '@/components/crud/QlxDataTable.vue'
import QlxDescriptionList from '@/components/crud/QlxDescriptionList.vue'
import type { DescriptionItem } from '@/components/crud/QlxDescriptionList.vue'
import QlxFormDialog from '@/components/crud/QlxFormDialog.vue'
import type { FormField, FormValue } from '@/components/crud/QlxFormDialog.vue'
import QlxImportDialog from '@/components/crud/QlxImportDialog.vue'
import type { ImportColumn } from '@/components/crud/QlxImportDialog.vue'
import QlxPagePanel from '@/components/crud/QlxPagePanel.vue'
import QlxSearchBar from '@/components/crud/QlxSearchBar.vue'
import type { SearchField } from '@/components/crud/QlxSearchBar.vue'
import { useCrudTable } from '@/composables/useCrudTable'
import { formatDateTime } from '@/utils/format'
import {
  createGoodsDemo,
  fetchGoodsDemoList,
  goodsCategoryLabels,
  goodsStatusLabels,
  removeGoodsDemo,
  toggleGoodsDemoStatus,
  updateGoodsDemo,
} from './mock/goodsDemo'
import type { DemoGoods } from './mock/goodsDemo'

/* ============================================================
 * 1. 查询区（QlxSearchBar）
 *    fields 为配置数组：type 'input' | 'select'；查询条件经
 *    @search 交给 useCrudTable.search（自动回第 1 页），@reset 清空重查。
 *    枚举下拉选项统一由「枚举映射字典」转换生成（B-2：映射在前端维护）。
 * ============================================================ */
const searchFields: SearchField[] = [
  { prop: 'keyword', label: '关键词', type: 'input', placeholder: '编码 / 名称', width: 240 },
  {
    prop: 'category',
    label: '类目',
    type: 'select',
    options: Object.entries(goodsCategoryLabels).map(([value, label]) => ({ value, label })),
  },
  {
    prop: 'status',
    label: '状态',
    type: 'select',
    options: Object.entries(goodsStatusLabels).map(([value, label]) => ({ value, label })),
  },
]

/* ============================================================
 * 2. 数据表格（QlxDataTable）
 *    columns 为配置数组，常用 type：
 *      text  默认文本（空值自动显示 —）
 *      enum  枚举映射（enumLabels）
 *      tag   状态标签（tagText + tagTone 动态色调）
 *      code  可复制编码（点击复制，适合业务编码 / 单号）
 *      time  时间格式化（内部走 utils/format.formatDateTime）
 *    自定义单元格用 #cell-{prop} 插槽；操作列用 #actions 插槽（fixed right）。
 * ============================================================ */
const columns: Array<CrudColumn<DemoGoods>> = [
  { prop: 'code', label: '商品编码', type: 'code', width: 120, fixed: 'left' },
  { prop: 'name', label: '商品名称', minWidth: 160 },
  { prop: 'category', label: '类目', type: 'enum', enumLabels: goodsCategoryLabels, width: 110 },
  { prop: 'price', label: '单价（元）', width: 110, align: 'right' },
  { prop: 'stock', label: '库存', width: 90, align: 'right' },
  {
    prop: 'status',
    label: '状态',
    type: 'tag',
    width: 90,
    align: 'center',
    tagText: (row) => goodsStatusLabels[row.status],
    tagTone: (row) => (row.status === 'OnShelf' ? 'success' : 'info'),
  },
  { prop: 'updatedAt', label: '更新时间', type: 'time', width: 160 },
]

/* ============================================================
 * 3. 列表状态机（useCrudTable）
 *    fetcher 签名与真实 REST 一致： (query & PagedQuery) => Promise<PagedResult<T>>。
 *    search 变更条件回第 1 页；changePage 保持条件翻页；refresh 保持条件重查当前页
 *   （新建/编辑/删除/导入成功后调用 refresh 刷新）。
 * ============================================================ */
const { loading, items, total, page, pageSize, query, search, reset, refresh, changePage } =
  useCrudTable<DemoGoods>(fetchGoodsDemoList)

/* ============================================================
 * 4. 新建 / 编辑弹窗（QlxFormDialog）
 *    fields 配置驱动：input / textarea / select / number / switch；
 *    required + pattern 声明式校验；父级监听 @submit 拿 payload 调接口并自行关闭。
 *    编码字段不进表单（N-1：后端生成、只读回显），编辑时经 defaultValue 回填。
 * ============================================================ */
const formOpen = ref(false)
const formSubmitting = ref(false)
/** 当前编辑目标（null 表示新建）。 */
const editingRow = ref<DemoGoods | null>(null)

/** 编辑时把行数据映射为字段默认值；新建时给业务默认值。 */
const formFields = computed<FormField[]>(() => [
  {
    prop: 'name',
    label: '商品名称',
    type: 'input',
    required: true,
    maxlength: 32,
    defaultValue: editingRow.value?.name ?? '',
  },
  {
    prop: 'category',
    label: '类目',
    type: 'select',
    required: true,
    options: Object.entries(goodsCategoryLabels).map(([value, label]) => ({ value, label })),
    defaultValue: editingRow.value?.category ?? 'office',
  },
  {
    prop: 'price',
    label: '单价（元）',
    type: 'number',
    required: true,
    defaultValue: editingRow.value?.price ?? 0,
  },
  {
    prop: 'stock',
    label: '库存',
    type: 'number',
    required: true,
    defaultValue: editingRow.value?.stock ?? 0,
  },
  {
    prop: 'remark',
    label: '备注',
    type: 'textarea',
    full: true,
    maxlength: 200,
    defaultValue: editingRow.value?.remark ?? '',
  },
])

function openCreate(): void {
  editingRow.value = null
  formOpen.value = true
}

function openEdit(row: DemoGoods): void {
  editingRow.value = row
  formOpen.value = true
}

async function onFormSubmit(payload: Record<string, FormValue>): Promise<void> {
  formSubmitting.value = true
  try {
    const input = {
      name: String(payload.name),
      category: payload.category as DemoGoods['category'],
      price: Number(payload.price),
      stock: Number(payload.stock),
      remark: String(payload.remark ?? ''),
    }
    if (editingRow.value) {
      await updateGoodsDemo(editingRow.value.id, input)
      ElMessage.success(`已保存「${input.name}」`)
    } else {
      const created = await createGoodsDemo(input)
      ElMessage.success(`已创建「${created.name}」，编码 ${created.code}（后端自动生成）`)
    }
    formOpen.value = false
    await refresh()
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : '保存失败')
  } finally {
    formSubmitting.value = false
  }
}

/* ============================================================
 * 5. 行内操作：上下架切换 / 删除确认
 *    危险操作必须 ElMessageBox 二次确认；成功后 refresh 刷新列表。
 * ============================================================ */
async function onToggleStatus(row: DemoGoods): Promise<void> {
  try {
    await toggleGoodsDemoStatus(row.id)
    ElMessage.success(row.status === 'OnShelf' ? '已下架' : '已上架')
    await refresh()
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : '操作失败')
  }
}

async function onRemove(row: DemoGoods): Promise<void> {
  try {
    await ElMessageBox.confirm(
      `删除「${row.name}（${row.code}）」后不可恢复，是否继续？`,
      '删除商品',
      { confirmButtonText: '确认删除', cancelButtonText: '取消', type: 'warning' },
    )
  } catch {
    return // 用户取消
  }
  try {
    await removeGoodsDemo(row.id)
    ElMessage.success('已删除')
    await refresh()
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : '删除失败')
  }
}

/* ============================================================
 * 6. 批量导入（QlxImportDialog）
 *    columns 定义列序（key / 中文名 / 必填 / 正则）；弹窗内部完成逐行解析与校验预览，
 *    @submit 只收到「有效行」数组，父级映射为接口入参后提交。
 * ============================================================ */
const importOpen = ref(false)
const importSubmitting = ref(false)

const importColumns: ImportColumn[] = [
  { key: 'name', label: '商品名称', required: true },
  {
    key: 'category',
    label: '类目',
    required: true,
    pattern: /^(office|electronics|furniture|consumable)$/,
    patternMessage: '类目须为 office / electronics / furniture / consumable 之一',
  },
  { key: 'price', label: '单价', required: true, pattern: /^\d+(\.\d{1,2})?$/, patternMessage: '单价须为数字（最多两位小数）' },
  { key: 'stock', label: '库存', pattern: /^\d+$/, patternMessage: '库存须为非负整数' },
]

async function onImportSubmit(rows: Record<string, string>[]): Promise<void> {
  importSubmitting.value = true
  try {
    for (const row of rows) {
      // 真实场景：rows.map(...) 为接口入参后一次性 POST /batch；示例逐条写入 mock 库
      await createGoodsDemo({
        name: row.name,
        category: row.category as DemoGoods['category'],
        price: Number(row.price),
        stock: Number(row.stock || 0),
        remark: '批量导入',
      })
    }
    ElMessage.success(`已导入 ${rows.length} 条商品`)
    importOpen.value = false
    await refresh()
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : '导入失败')
  } finally {
    importSubmitting.value = false
  }
}

/* ============================================================
 * 7. 详情查看（el-dialog + QlxDescriptionList）
 *    描述列表为配置驱动键值对，复杂值用 #item-{label} 插槽自定义。
 * ============================================================ */
const detailOpen = ref(false)
const detailRow = ref<DemoGoods | null>(null)

const detailItems = computed<DescriptionItem[]>(() => {
  const row = detailRow.value
  if (!row) {
    return []
  }
  return [
    { label: '商品编码', value: row.code },
    { label: '商品名称', value: row.name },
    { label: '类目', value: goodsCategoryLabels[row.category] },
    { label: '单价（元）', value: String(row.price) },
    { label: '库存', value: String(row.stock) },
    { label: '状态', value: goodsStatusLabels[row.status] },
    { label: '创建时间', value: formatDateTime(row.createdAt) },
    { label: '更新时间', value: formatDateTime(row.updatedAt) },
    { label: '备注', value: row.remark || '—', full: true },
  ]
})

function openDetail(row: DemoGoods): void {
  detailRow.value = row
  detailOpen.value = true
}
</script>

<template>
  <!-- 页面骨架：标题 + 描述 + 右上角工具按钮（toolbar 插槽） + 内容默认插槽 -->
  <QlxPagePanel
    title="标准列表页示例"
    subtitle="查询区 + 表格 + 分页 + 操作按钮 + 新建/编辑弹窗 + 删除确认 + 批量导入 + 详情（mock 数据）"
  >
    <template #toolbar>
      <!-- 工具栏按钮规范：主操作 type="primary"，其余默认；图标经 AppIcon -->
      <el-button type="primary" @click="openCreate">
        <AppIcon name="Plus" :size="14" />
        新增商品
      </el-button>
      <el-button @click="importOpen = true">
        <AppIcon name="Upload" :size="14" />
        批量导入
      </el-button>
      <el-button :loading="loading" @click="refresh">
        <AppIcon name="Refresh" :size="14" />
        刷新
      </el-button>
    </template>

    <!-- 查询区：受控于 useCrudTable.query，@search / @reset 直连状态机 -->
    <QlxSearchBar :fields="searchFields" :model-value="query" @search="search" @reset="reset" />

    <!-- 数据表格：items/columns/loading/total/page/pageSize 全部来自 useCrudTable；
         @page-change 直连 changePage（翻页 / 改页大小） -->
    <QlxDataTable
      :items="items"
      :columns="columns"
      :loading="loading"
      :total="total"
      :page="page"
      :page-size="pageSize"
      :actions-width="240"
      empty-text="暂无符合条件的商品"
      @page-change="changePage"
    >
      <!-- 操作列：文字链样式 .cell-link；危险操作加 .is-danger；链接类操作用 <a> 新窗口打开 -->
      <template #actions="{ row }">
        <button type="button" class="cell-link" @click="openDetail(row)">详情</button>
        <el-divider direction="vertical" />
        <button type="button" class="cell-link" @click="openEdit(row)">编辑</button>
        <el-divider direction="vertical" />
        <button type="button" class="cell-link" @click="onToggleStatus(row)">
          {{ row.status === 'OnShelf' ? '下架' : '上架' }}
        </button>
        <el-divider direction="vertical" />
        <button type="button" class="cell-link is-danger" @click="onRemove(row)">删除</button>
      </template>
    </QlxDataTable>

    <!-- 新建 / 编辑弹窗：v-model 控制开关，fields 配置驱动，@submit 拿 payload 调接口 -->
    <QlxFormDialog
      v-model="formOpen"
      :title="editingRow ? `编辑商品 · ${editingRow.code}` : '新增商品'"
      :fields="formFields"
      :submitting="formSubmitting"
      :submit-text="editingRow ? '保存' : '创建'"
      @submit="onFormSubmit"
    />

    <!-- 批量导入弹窗：逐行解析校验后，@submit 仅收到有效行 -->
    <QlxImportDialog
      v-model="importOpen"
      title="批量导入商品"
      :columns="importColumns"
      hint="每行一条商品，列顺序与模板一致；支持逗号 / Tab 分隔（可直接从 Excel 粘贴）。"
      sample="复印纸 A4,office,25.50,200"
      :submitting="importSubmitting"
      @submit="onImportSubmit"
    />

    <!-- 详情弹窗：el-dialog + QlxDescriptionList（详情页头部亦可复用该组件） -->
    <el-dialog v-model="detailOpen" title="商品详情" :width="640">
      <QlxDescriptionList v-if="detailRow" :items="detailItems" :column="2" />
    </el-dialog>
  </QlxPagePanel>
</template>

<style scoped>
/* 行内操作文字链：与 PortalUserListView 等既有页面同款样式（后续可在公共层抽取复用） */
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
