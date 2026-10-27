<!--
  所属公司：四川淇利馨集团有限公司
  描述：商品分类主数据——左分类树（前端组装）+ 右分类详情 / 表单 + 导入导出
  说明：
    1. 后端只下发扁平分类列表，建树 / 层级缩进 / 上级排除在前端公共层（B-2-1 / B-4）；
    2. 编码由后端层级连续编码生成，只读展示，表单无编码输入框（N-1）；
    3. 导入导出模板编码经后端清单校验（bas.category.import / .export）。
-->
<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import AppIcon from '@/components/AppIcon.vue'
import QlxPagePanel from '@/components/crud/QlxPagePanel.vue'
import QlxTreePanel from '@/components/crud/QlxTreePanel.vue'
import QlxFormDialog from '@/components/crud/QlxFormDialog.vue'
import type { FormField, FormValue } from '@/components/crud/QlxFormDialog.vue'
import MasterImportDialog from '@/components/master/MasterImportDialog.vue'
import { categoryApi, IMPORT_TEMPLATE_CODES, unitApi } from '@/api/basMaster'
import { useMasterTransfer } from '@/composables/useMasterTransfer'
import type { ItemCategoryRow, UnitGroupRow } from '@/types/bas-master'
import {
  basStatusLabel,
  basStatusTone,
  buildCategoryOptions,
  buildCategoryTree,
  collectCategoryIds,
  findCategoryNode,
} from '@/utils/bas-master-mappers'
import { formatDateTime } from '@/utils/format'

const flatCategories = ref<ItemCategoryRow[]>([])
const loading = ref(false)
const selectedId = ref('')
const unitGroups = ref<UnitGroupRow[]>([])

const tree = computed(() => buildCategoryTree(flatCategories.value))
const selected = computed(() =>
  selectedId.value ? findCategoryNode(tree.value, selectedId.value) : null,
)

const unitGroupName = computed<string>(() => {
  const id = selected.value?.unitGroupId
  if (!id) {
    return '未指定'
  }
  return unitGroups.value.find((group) => group.id === id)?.name ?? '—'
})

const parentName = computed<string>(() => {
  const parentId = selected.value?.parentId
  if (!parentId) {
    return '无（顶级分类）'
  }
  return findCategoryNode(tree.value, parentId)?.name ?? '—'
})

async function load(): Promise<void> {
  loading.value = true
  try {
    flatCategories.value = await categoryApi.list()
    const target = selectedId.value
    selectedId.value = findCategoryNode(tree.value, target)?.id ?? (tree.value[0]?.id ?? '')
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : '加载商品分类失败')
  } finally {
    loading.value = false
  }
}

async function loadUnitGroups(): Promise<void> {
  try {
    unitGroups.value = await unitApi.listGroups()
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : '加载计量单位组失败')
  }
}

/** ============ 新建 / 编辑（编码只读，N-1） ============ */

const dialogOpen = ref(false)
const submitting = ref(false)
const editing = ref<ItemCategoryRow | null>(null)
const defaultParentId = ref('')

const dialogTitle = computed(() =>
  editing.value ? `编辑分类 · ${editing.value.name}` : '新增商品分类',
)

const fields = computed<FormField[]>(() => {
  const editingRow = editing.value
  const excluded = editingRow && selected.value ? collectCategoryIds(selected.value) : undefined
  return [
    {
      prop: 'parentId',
      label: '上级分类',
      type: 'select',
      full: true,
      filterable: true,
      disabled: Boolean(editingRow),
      options: [
        { label: '无（顶级分类）', value: '' },
        ...buildCategoryOptions(tree.value, excluded).map((option) => ({
          label: option.label,
          value: option.value,
        })),
      ],
      defaultValue: editingRow ? (editingRow.parentId ?? '') : defaultParentId.value,
      hint: editingRow ? '分类编码与层级创建后不可变更（可由后端层级连续编码推导）' : '编码由后端按层级连续编码自动生成',
    },
    {
      prop: 'name',
      label: '分类名称',
      type: 'input',
      required: true,
      maxlength: 40,
      defaultValue: editingRow?.name ?? '',
    },
    {
      prop: 'unitGroupId',
      label: '默认单位组',
      type: 'select',
      filterable: true,
      options: [
        { label: '暂不指定', value: '' },
        ...unitGroups.value.map((group) => ({ label: group.name, value: group.id })),
      ],
      defaultValue: editingRow?.unitGroupId ?? '',
    },
  ]
})

function openCreate(parentId = ''): void {
  editing.value = null
  defaultParentId.value = parentId
  dialogOpen.value = true
}

function openEdit(): void {
  if (!selected.value) {
    return
  }
  editing.value = selected.value
  dialogOpen.value = true
}

async function onSubmit(values: Record<string, FormValue>): Promise<void> {
  const editingRow = editing.value
  submitting.value = true
  try {
    const unitGroupId = values.unitGroupId ? String(values.unitGroupId) : null
    if (editingRow) {
      const saved = await categoryApi.update(editingRow.id, {
        name: String(values.name ?? '').trim(),
        unitGroupId,
      })
      await load()
      selectedId.value = saved.id
      ElMessage.success('分类已保存')
    } else {
      const saved = await categoryApi.create({
        name: String(values.name ?? '').trim(),
        parentId: values.parentId ? String(values.parentId) : null,
        unitGroupId,
      })
      await load()
      selectedId.value = saved.id
      ElMessage.success('分类已创建')
    }
    dialogOpen.value = false
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : '保存分类失败')
  } finally {
    submitting.value = false
  }
}

async function onToggle(): Promise<void> {
  const row = selected.value
  if (!row) {
    return
  }
  const disabling = row.status === 1
  try {
    await ElMessageBox.confirm(
      disabling
        ? `停用分类「${row.name}」后，业务单据不再可选该分类（历史数据保留），是否继续？`
        : `启用分类「${row.name}」后恢复可选状态，是否继续？`,
      disabling ? '停用分类' : '启用分类',
      { confirmButtonText: disabling ? '确认停用' : '确认启用', cancelButtonText: '取消', type: 'warning' },
    )
  } catch {
    return
  }
  try {
    await categoryApi.toggle(row.id)
    await load()
    ElMessage.success(disabling ? '分类已停用' : '分类已启用')
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : '操作失败')
  }
}

/** 提交分类停用审批（审批通过后生效）。 */
async function onSubmitDisable(): Promise<void> {
  const row = selected.value
  if (!row) {
    return
  }
  try {
    await ElMessageBox.confirm(
      `将提交「${row.name}」的停用审批申请，审批通过后生效，是否继续？`,
      '提交停用审批',
      { confirmButtonText: '提交申请', cancelButtonText: '取消', type: 'warning' },
    )
  } catch {
    return
  }
  try {
    const request = await categoryApi.submitDisable(row.id)
    ElMessage.success(`停用申请已提交（状态码 ${request.status}）`)
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : '提交停用申请失败')
  }
}

/** ============ 导入导出 ============ */

const {
  importOpen: importDialogOpen,
  importing: importSubmitting,
  exporting,
  downloading,
  importResult,
  templateHint,
  openImport,
  downloadTemplate,
  exportFile,
  submitImportFile,
} = useMasterTransfer({
  templateCode: IMPORT_TEMPLATE_CODES.category,
  onImported: load,
})

onMounted(async () => {
  await Promise.all([load(), loadUnitGroups()])
})
</script>

<template>
  <QlxPagePanel title="商品分类" subtitle="层级分类编码 + 默认计量单位组（规范 5.2.1）">
    <template #toolbar>
      <el-button v-permission="'bas.category.action'" type="primary" @click="openCreate()">
        <AppIcon name="Plus" :size="14" />
        新增分类
      </el-button>
      <el-button :loading="downloading" @click="downloadTemplate()">
        <AppIcon name="Document" :size="14" />
        下载模板
      </el-button>
      <el-button @click="openImport()">
        <AppIcon name="UploadFilled" :size="14" />
        导入
      </el-button>
      <el-button :loading="exporting" @click="exportFile()">
        <AppIcon name="Files" :size="14" />
        导出
      </el-button>
    </template>

    <div class="category-layout">
      <QlxTreePanel
        :title="'分类树'"
        :data="tree"
        :loading="loading"
        :selected-key="selectedId"
        search-placeholder="搜索分类名称"
        empty-text="暂无分类，请先创建顶级分类"
        @select="(id: string) => (selectedId = id)"
      >
        <template #node="{ data }">
          <span class="category-node" :class="{ 'is-off': data.status === 2 }">
            <AppIcon name="Collection" :size="13" class="category-node__icon" />
            <span class="category-node__name">{{ data.name }}</span>
            <span class="category-node__code qlx-numeric">{{ data.code }}</span>
          </span>
        </template>
        <template #footer>
          <el-button v-permission="'bas.category.action'" size="small" @click="openCreate()">
            新增顶级分类
          </el-button>
        </template>
      </QlxTreePanel>

      <section v-if="selected" class="category-detail">
        <header class="category-detail__head">
          <div>
            <h2>{{ selected.name }}</h2>
            <p class="qlx-numeric">{{ selected.code }} · 第 {{ selected.levelNo }} 级</p>
          </div>
          <div class="category-detail__actions">
            <el-tag :type="basStatusTone(selected.status)" size="small" effect="light">
              {{ basStatusLabel(selected.status) }}
            </el-tag>
            <el-button
              v-permission="'bas.category.action'"
              size="small"
              @click="openCreate(selected.id)"
            >
              新增子分类
            </el-button>
            <el-button v-permission="'bas.category.action'" size="small" @click="openEdit">
              编辑
            </el-button>
            <el-button v-permission="'bas.category.action'" size="small" plain @click="onSubmitDisable">
              停用审批
            </el-button>
            <el-button
              v-permission="'bas.category.action'"
              size="small"
              :type="selected.status === 1 ? 'danger' : 'primary'"
              plain
              @click="onToggle"
            >
              {{ selected.status === 1 ? '停用' : '启用' }}
            </el-button>
          </div>
        </header>

        <el-descriptions :column="2" border>
          <el-descriptions-item label="分类编码">
            <span class="qlx-numeric">{{ selected.code }}</span>
          </el-descriptions-item>
          <el-descriptions-item label="层级">第 {{ selected.levelNo }} 级</el-descriptions-item>
          <el-descriptions-item label="上级分类">{{ parentName }}</el-descriptions-item>
          <el-descriptions-item label="默认单位组">{{ unitGroupName }}</el-descriptions-item>
          <el-descriptions-item label="状态">{{ basStatusLabel(selected.status) }}</el-descriptions-item>
          <el-descriptions-item label="更新时间">{{ formatDateTime(selected.updatedAt) }}</el-descriptions-item>
        </el-descriptions>

        <div class="category-detail__children">
          <h3>下级分类（{{ selected.children.length }}）</h3>
          <div v-if="selected.children.length" class="category-chips">
            <button
              v-for="child in selected.children"
              :key="child.id"
              type="button"
              class="category-chip"
              :class="{ 'is-off': child.status === 2 }"
              @click="selectedId = child.id"
            >
              {{ child.name }}
              <em class="qlx-numeric">{{ child.code }}</em>
            </button>
          </div>
          <p v-else class="category-detail__empty">暂无下级分类。</p>
        </div>

        <p class="category-detail__note">
          分类编码按层级连续生成（父编码 + 序号），创建后不可修改；停用不影响历史单据引用。
        </p>
      </section>

      <section v-else class="category-detail category-detail--empty">
        <el-empty description="请选择左侧分类查看详情" />
      </section>
    </div>

    <QlxFormDialog
      v-model="dialogOpen"
      :title="dialogTitle"
      :fields="fields"
      :submitting="submitting"
      submit-text="保存分类"
      :width="600"
      class="category-form-dialog"
      @submit="onSubmit"
    />

    <MasterImportDialog
      v-model="importDialogOpen"
      title="导入商品分类"
      :hint="templateHint"
      :result="importResult"
      :submitting="importSubmitting"
      :downloading="downloading"
      @submit-file="submitImportFile"
      @download-template="downloadTemplate"
    />
  </QlxPagePanel>
</template>

<style scoped>
.category-layout {
  display: grid;
  gap: var(--qlx-space-4);
  grid-template-columns: 288px minmax(0, 1fr);
  align-items: start;
}

.category-node {
  display: inline-flex;
  min-width: 0;
  flex: 1;
  align-items: center;
  gap: 6px;
}

.category-node__icon {
  color: var(--qlx-brand-600);
}

.category-node.is-off {
  color: var(--qlx-ink-300);
}

.category-node__name {
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.category-node__code {
  margin-left: auto;
  color: var(--qlx-ink-400);
  font-size: var(--qlx-text-xs);
}

.category-detail {
  padding: var(--qlx-space-4);
  border: 1px solid var(--qlx-line);
  border-radius: var(--qlx-radius-md);
  background: var(--qlx-surface);
}

.category-detail--empty {
  display: flex;
  min-height: 420px;
  align-items: center;
  justify-content: center;
}

.category-detail__head {
  display: flex;
  margin-bottom: var(--qlx-space-4);
  align-items: flex-start;
  justify-content: space-between;
  gap: var(--qlx-space-4);
}

.category-detail__head h2 {
  color: var(--qlx-brand-900);
  font-family: var(--qlx-font-serif);
  font-size: var(--qlx-text-lg);
  font-weight: 600;
  letter-spacing: 0.04em;
}

.category-detail__head p {
  margin-top: 2px;
  color: var(--qlx-ink-400);
  font-size: var(--qlx-text-xs);
}

.category-detail__actions {
  display: flex;
  flex: 0 0 auto;
  flex-wrap: wrap;
  align-items: center;
  gap: var(--qlx-space-2);
}

.category-detail__children {
  margin-top: var(--qlx-space-5);
  padding-top: var(--qlx-space-4);
  border-top: 1px dashed var(--qlx-ink-200);
}

.category-detail__children h3 {
  margin-bottom: var(--qlx-space-3);
  color: var(--qlx-ink-600);
  font-size: var(--qlx-text-sm);
  font-weight: 600;
}

.category-chips {
  display: flex;
  flex-wrap: wrap;
  gap: var(--qlx-space-2);
}

.category-chip {
  display: inline-flex;
  padding: 5px 10px;
  border: 1px solid var(--qlx-line);
  border-radius: var(--qlx-radius-full);
  background: var(--qlx-brand-50);
  color: var(--qlx-brand-700);
  font-size: var(--qlx-text-sm);
  align-items: center;
  gap: 6px;
  cursor: pointer;
}

.category-chip.is-off {
  background: var(--qlx-canvas);
  color: var(--qlx-ink-400);
}

.category-chip em {
  color: var(--qlx-ink-400);
  font-size: var(--qlx-text-xs);
  font-style: normal;
}

.category-detail__empty,
.category-detail__note {
  color: var(--qlx-ink-400);
  font-size: var(--qlx-text-xs);
  line-height: 1.7;
}

.category-detail__note {
  margin-top: var(--qlx-space-4);
  padding-left: var(--qlx-space-3);
  border-left: 2px solid var(--qlx-gold-400);
}

@media (max-width: 1199px) {
  .category-layout {
    grid-template-columns: 220px minmax(0, 1fr);
  }
}

@media (max-width: 767px) {
  .category-layout {
    grid-template-columns: 1fr;
  }
}
</style>

<style>
@media (max-width: 767px) {
  .category-form-dialog.el-dialog {
    width: min(600px, 92vw);
  }
}
</style>
