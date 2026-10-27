<!--
  所属公司：四川淇利馨集团有限公司
  描述：数据字典——左侧字典类型 + 右侧字典项，支持类型 / 字典项维护与启停
  说明：
    1. 集团级字典（ownerLevel = 1）对租户侧只读，维护按钮统一禁用并给出说明；
    2. 系统内置项（isSystem）禁止删除，仅允许启停；
    3. 类型编码 / 项编码由后端契约要求用户指定，创建后不可修改（表单仅新增时可填）。
-->
<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import AppIcon from '@/components/AppIcon.vue'
import QlxPagePanel from '@/components/crud/QlxPagePanel.vue'
import QlxDataTable from '@/components/crud/QlxDataTable.vue'
import type { CrudColumn } from '@/components/crud/QlxDataTable.vue'
import QlxFormDialog from '@/components/crud/QlxFormDialog.vue'
import type { FormField, FormValue } from '@/components/crud/QlxFormDialog.vue'
import { dictApi, orgMasterApi } from '@/api/basMaster'
import { buildOrgOptions, buildOrgTree } from '@/components/org-admin/org-admin-utils'
import type {
  DictItemRow,
  DictOwnerLevel,
  DictTypeRow,
  SaveDictItemPayload,
  SaveDictTypePayload,
  UpdateDictItemPayload,
  UpdateDictTypePayload,
} from '@/types/bas-master'
import { ENUM_OPTION_MAPS } from '@/utils/bas-master-mappers'
import {
  basStatusLabel,
  basStatusTone,
  dictOwnerLevelLabel,
  toNumberOptions,
} from '@/utils/bas-master-mappers'
import { formatDateTime } from '@/utils/format'

/** ============ 字典类型（左侧） ============ */

const types = ref<DictTypeRow[]>([])
const typesLoading = ref(false)
const selectedTypeId = ref('')
const keyword = ref('')

const selectedType = computed<DictTypeRow | null>(
  () => types.value.find((type) => type.id === selectedTypeId.value) ?? null,
)

const filteredTypes = computed(() => {
  const text = keyword.value.trim().toLowerCase()
  if (!text) {
    return types.value
  }
  return types.value.filter(
    (type) =>
      type.typeName.toLowerCase().includes(text) || type.typeCode.toLowerCase().includes(text),
  )
})

/** 集团级字典只读（不改变后端契约，仅前端按归属层级禁写）。 */
const readonly = computed(() => selectedType.value?.ownerLevel === 1)

const orgOptions = ref<Array<{ label: string; value: string }>>([])

async function loadTypes(preferId?: string): Promise<void> {
  typesLoading.value = true
  try {
    types.value = await dictApi.listTypes()
    const exists = types.value.some((type) => type.id === selectedTypeId.value)
    selectedTypeId.value = preferId
      ?? (exists ? selectedTypeId.value : (types.value[0]?.id ?? ''))
    await loadItems()
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : '加载字典类型失败')
  } finally {
    typesLoading.value = false
  }
}

function selectType(type: DictTypeRow): void {
  selectedTypeId.value = type.id
  void loadItems()
}

/** ============ 字典项（右侧） ============ */

const items = ref<DictItemRow[]>([])
const itemsLoading = ref(false)

const sortedItems = computed(() => [...items.value].sort((a, b) => a.sortNo - b.sortNo))

async function loadItems(): Promise<void> {
  const type = selectedType.value
  if (!type) {
    items.value = []
    return
  }
  itemsLoading.value = true
  try {
    items.value = await dictApi.listItems(type.typeCode)
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : '加载字典项失败')
  } finally {
    itemsLoading.value = false
  }
}

const itemColumns: Array<CrudColumn<DictItemRow>> = [
  { prop: 'itemCode', label: '项编码', type: 'code', width: 180 },
  { prop: 'itemName', label: '项名称', minWidth: 160 },
  { prop: 'sortNo', label: '排序号', width: 90, align: 'center' },
  {
    prop: 'status',
    label: '状态',
    type: 'tag',
    width: 90,
    align: 'center',
    tagText: (row) => basStatusLabel(row.status),
    tagTone: (row) => basStatusTone(row.status),
  },
  { prop: 'isSystem', label: '系统内置', type: 'check', width: 100, align: 'center' },
  { prop: 'updatedAt', label: '更新时间', type: 'time', width: 160 },
]

/** ============ 类型维护 ============ */

const typeOpen = ref(false)
const typeSubmitting = ref(false)
const editingType = ref<DictTypeRow | null>(null)

const typeFields = computed<FormField[]>(() => {
  if (editingType.value) {
    return [
      {
        prop: 'typeCode',
        label: '类型编码',
        type: 'input',
        disabled: true,
        defaultValue: editingType.value.typeCode,
        hint: '类型编码创建后不可修改',
      },
      { prop: 'typeName', label: '类型名称', type: 'input', required: true, maxlength: 64, defaultValue: editingType.value.typeName },
    ]
  }
  return [
    {
      prop: 'typeCode',
      label: '类型编码',
      type: 'input',
      required: true,
      maxlength: 32,
      pattern: /^[a-z][a-z0-9_.]{1,31}$/i,
      patternMessage: '类型编码以字母开头，可含字母 / 数字 / 下划线 / 点',
      hint: '创建后不可修改，建议使用业务前缀（如 sup.grade）',
    },
    { prop: 'typeName', label: '类型名称', type: 'input', required: true, maxlength: 64 },
    {
      prop: 'ownerLevel',
      label: '归属层级',
      type: 'select',
      required: true,
      options: toNumberOptions(ENUM_OPTION_MAPS.dictOwnerLevel),
      defaultValue: 2,
    },
    {
      prop: 'orgId',
      label: '所属组织',
      type: 'select',
      options: orgOptions.value,
      hint: '子公司级字典须指定所属组织；集团级留空',
    },
  ]
})

function openCreateType(): void {
  editingType.value = null
  typeOpen.value = true
}

function openEditType(type: DictTypeRow): void {
  if (readonly.value) {
    return
  }
  editingType.value = type
  typeOpen.value = true
}

async function onTypeSubmit(values: Record<string, FormValue>): Promise<void> {
  typeSubmitting.value = true
  try {
    if (editingType.value) {
      const payload: UpdateDictTypePayload = { typeName: String(values.typeName ?? '').trim() }
      await dictApi.updateType(editingType.value.id, payload)
      ElMessage.success('字典类型已更新')
      typeOpen.value = false
      await loadTypes(editingType.value.id)
    } else {
      const ownerLevel = Number(values.ownerLevel ?? 2) as DictOwnerLevel
      const payload: SaveDictTypePayload = {
        typeCode: String(values.typeCode ?? '').trim(),
        typeName: String(values.typeName ?? '').trim(),
        ownerLevel,
        orgId: ownerLevel === 2 ? ((values.orgId ? String(values.orgId) : '') || null) : null,
      }
      const created = await dictApi.createType(payload)
      ElMessage.success('字典类型已创建')
      typeOpen.value = false
      await loadTypes(created.id)
    }
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : '保存字典类型失败')
  } finally {
    typeSubmitting.value = false
  }
}

async function toggleType(type: DictTypeRow): Promise<void> {
  try {
    await dictApi.toggleType(type.id)
    ElMessage.success(type.status === 1 ? '字典类型已停用' : '字典类型已启用')
    await loadTypes(type.id)
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : '切换字典类型状态失败')
  }
}

/** ============ 字典项维护 ============ */

const itemOpen = ref(false)
const itemSubmitting = ref(false)
const editingItem = ref<DictItemRow | null>(null)

const itemFields = computed<FormField[]>(() => {
  if (editingItem.value) {
    return [
      {
        prop: 'itemCode',
        label: '项编码',
        type: 'input',
        disabled: true,
        defaultValue: editingItem.value.itemCode,
        hint: '项编码创建后不可修改',
      },
      { prop: 'itemName', label: '项名称', type: 'input', required: true, maxlength: 64, defaultValue: editingItem.value.itemName },
      {
        prop: 'sortNo',
        label: '排序号',
        type: 'number',
        required: true,
        defaultValue: editingItem.value.sortNo,
      },
    ]
  }
  return [
    {
      prop: 'itemCode',
      label: '项编码',
      type: 'input',
      required: true,
      maxlength: 32,
      pattern: /^[0-9A-Za-z_.-]{1,32}$/,
      patternMessage: '项编码可含字母 / 数字 / 下划线 / 短横线 / 点',
      hint: '创建后不可修改',
    },
    { prop: 'itemName', label: '项名称', type: 'input', required: true, maxlength: 64 },
    { prop: 'sortNo', label: '排序号', type: 'number', required: true, defaultValue: 10 },
  ]
})

function openCreateItem(): void {
  if (readonly.value) {
    return
  }
  editingItem.value = null
  itemOpen.value = true
}

function openEditItem(item: DictItemRow): void {
  if (readonly.value) {
    return
  }
  editingItem.value = item
  itemOpen.value = true
}

async function onItemSubmit(values: Record<string, FormValue>): Promise<void> {
  const type = selectedType.value
  if (!type) {
    return
  }
  itemSubmitting.value = true
  try {
    if (editingItem.value) {
      const payload: UpdateDictItemPayload = {
        itemName: String(values.itemName ?? '').trim(),
        sortNo: Number(values.sortNo ?? 10),
      }
      await dictApi.updateItem(editingItem.value.id, payload)
      ElMessage.success('字典项已更新')
    } else {
      const payload: SaveDictItemPayload = {
        itemCode: String(values.itemCode ?? '').trim(),
        itemName: String(values.itemName ?? '').trim(),
        sortNo: Number(values.sortNo ?? 10),
      }
      await dictApi.createItem(type.typeCode, payload)
      ElMessage.success('字典项已创建')
    }
    itemOpen.value = false
    await loadItems()
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : '保存字典项失败')
  } finally {
    itemSubmitting.value = false
  }
}

async function toggleItem(item: DictItemRow): Promise<void> {
  try {
    await dictApi.toggleItem(item.id)
    ElMessage.success(item.status === 1 ? '字典项已停用' : '字典项已启用')
    await loadItems()
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : '切换字典项状态失败')
  }
}

async function deleteItem(item: DictItemRow): Promise<void> {
  try {
    await ElMessageBox.confirm(
      `确认删除字典项「${item.itemName}」？已被业务引用时后端会拒绝删除。`,
      '删除字典项',
      { type: 'warning', confirmButtonText: '删除', cancelButtonText: '取消' },
    )
  } catch {
    return
  }
  try {
    await dictApi.deleteItem(item.id)
    ElMessage.success('字典项已删除')
    await loadItems()
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : '删除字典项失败')
  }
}

onMounted(async () => {
  try {
    const orgs = await orgMasterApi.list()
    orgOptions.value = buildOrgOptions(buildOrgTree(orgs))
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : '加载组织候选失败')
  }
  await loadTypes()
})
</script>

<template>
  <QlxPagePanel title="数据字典" subtitle="集团级 / 子公司级字典维护 · 系统内置项受保护（规范 5.2.4）">
    <template #toolbar>
      <el-button type="primary" @click="openCreateType">
        <AppIcon name="Plus" :size="14" />
        新增字典类型
      </el-button>
    </template>

    <div class="dict-layout">
      <!-- 左：字典类型 -->
      <aside class="dict-types">
        <div class="dict-types__search">
          <el-input v-model="keyword" placeholder="搜索类型名称 / 编码" clearable size="small" />
        </div>
        <div v-loading="typesLoading" class="dict-types__list">
          <button
            v-for="type in filteredTypes"
            :key="type.id"
            type="button"
            class="dict-types__item"
            :class="{ 'is-active': type.id === selectedTypeId }"
            @click="selectType(type)"
          >
            <span class="dict-types__name">{{ type.typeName }}</span>
            <span class="dict-types__meta">
              <span class="qlx-numeric">{{ type.typeCode }}</span>
              <el-tag size="small" :type="type.ownerLevel === 1 ? 'warning' : 'info'">
                {{ dictOwnerLevelLabel(type.ownerLevel) }}
              </el-tag>
              <el-tag size="small" :type="basStatusTone(type.status)">
                {{ basStatusLabel(type.status) }}
              </el-tag>
            </span>
          </button>
          <el-empty v-if="!typesLoading && filteredTypes.length === 0" description="暂无字典类型" :image-size="64" />
        </div>
      </aside>

      <!-- 右：字典项 -->
      <section class="dict-items">
        <template v-if="selectedType">
          <el-alert
            v-if="readonly"
            type="warning"
            :closable="false"
            show-icon
            title="集团级字典对租户只读"
            description="该字典归属集团层级，仅集团管理员可维护；此处可查看项清单。"
            class="dict-items__alert"
          />

          <header class="dict-items__head">
            <div class="dict-items__title">
              <h3>{{ selectedType.typeName }}</h3>
              <span class="qlx-numeric">{{ selectedType.typeCode }}</span>
              <span class="dict-items__updated">更新时间：{{ formatDateTime(selectedType.updatedAt) }}</span>
            </div>
            <div class="dict-items__actions">
              <el-button size="small" :disabled="readonly" @click="openEditType(selectedType)">
                编辑类型
              </el-button>
              <el-button size="small" :disabled="readonly" @click="toggleType(selectedType)">
                {{ selectedType.status === 1 ? '停用类型' : '启用类型' }}
              </el-button>
              <el-button
                type="primary"
                size="small"
                :disabled="readonly"
                @click="openCreateItem"
              >
                新增字典项
              </el-button>
            </div>
          </header>

          <QlxDataTable
            :items="sortedItems"
            :columns="itemColumns"
            :loading="itemsLoading"
            :total="sortedItems.length"
            :page-size="10"
            :actions-width="220"
            empty-text="该字典类型下暂无字典项"
          >
            <template #actions="{ row }">
              <button
                type="button"
                class="cell-link"
                :disabled="readonly"
                @click="openEditItem(row)"
              >
                编辑
              </button>
              <el-divider direction="vertical" />
              <button
                type="button"
                class="cell-link"
                :disabled="readonly"
                @click="toggleItem(row)"
              >
                {{ row.status === 1 ? '停用' : '启用' }}
              </button>
              <template v-if="!row.isSystem">
                <el-divider direction="vertical" />
                <button
                  type="button"
                  class="cell-link is-danger"
                  :disabled="readonly"
                  @click="deleteItem(row)"
                >
                  删除
                </button>
              </template>
            </template>
          </QlxDataTable>
        </template>
        <el-empty v-else description="请选择左侧字典类型" :image-size="96" />
      </section>
    </div>

    <QlxFormDialog
      v-model="typeOpen"
      :title="editingType ? `编辑字典类型 · ${editingType.typeName}` : '新增字典类型'"
      :fields="typeFields"
      :submitting="typeSubmitting"
      submit-text="保存字典类型"
      :width="600"
      class="master-form-dialog"
      @submit="onTypeSubmit"
    />

    <QlxFormDialog
      v-model="itemOpen"
      :title="editingItem ? `编辑字典项 · ${editingItem.itemName}` : '新增字典项'"
      :fields="itemFields"
      :submitting="itemSubmitting"
      submit-text="保存字典项"
      :width="520"
      class="master-form-dialog"
      @submit="onItemSubmit"
    />
  </QlxPagePanel>
</template>

<style scoped>
.dict-layout {
  display: grid;
  grid-template-columns: 300px minmax(0, 1fr);
  gap: var(--qlx-space-4);
  align-items: start;
}

.dict-types {
  border: 1px solid var(--qlx-line);
  border-radius: var(--qlx-radius-md);
  background: var(--qlx-surface);
  overflow: hidden;
}

.dict-types__search {
  padding: var(--qlx-space-3);
  border-bottom: 1px solid var(--qlx-line);
}

.dict-types__list {
  max-height: 620px;
  overflow-y: auto;
  padding: var(--qlx-space-2);
}

.dict-types__item {
  display: grid;
  gap: 4px;
  width: 100%;
  padding: var(--qlx-space-2) var(--qlx-space-3);
  border: 0;
  border-radius: var(--qlx-radius-sm);
  background: none;
  text-align: left;
  cursor: pointer;
}

.dict-types__item:hover {
  background: var(--qlx-canvas);
}

.dict-types__item.is-active {
  background: var(--qlx-brand-50);
}

.dict-types__name {
  color: var(--qlx-ink-800);
  font-size: var(--qlx-text-sm);
  font-weight: 500;
}

.dict-types__meta {
  display: flex;
  align-items: center;
  gap: var(--qlx-space-2);
  color: var(--qlx-ink-400);
  font-size: var(--qlx-text-xs);
}

.dict-items {
  min-width: 0;
}

.dict-items__alert {
  margin-bottom: var(--qlx-space-3);
}

.dict-items__head {
  display: flex;
  align-items: flex-end;
  justify-content: space-between;
  gap: var(--qlx-space-3);
  margin-bottom: var(--qlx-space-3);
  padding-bottom: var(--qlx-space-2);
  border-bottom: 1px solid var(--qlx-line);
}

.dict-items__title {
  display: flex;
  align-items: baseline;
  gap: var(--qlx-space-2);
  flex-wrap: wrap;
}

.dict-items__title h3 {
  color: var(--qlx-brand-900);
  font-family: var(--qlx-font-serif);
  font-size: var(--qlx-text-md);
  font-weight: 600;
}

.dict-items__updated {
  color: var(--qlx-ink-400);
  font-size: var(--qlx-text-xs);
}

.dict-items__actions {
  display: flex;
  gap: var(--qlx-space-2);
  flex-wrap: wrap;
}

.cell-link {
  padding: 0;
  border: 0;
  background: none;
  color: var(--qlx-brand-600);
  font-size: var(--qlx-text-sm);
  cursor: pointer;
}

.cell-link:hover:not(:disabled) {
  text-decoration: underline;
}

.cell-link:disabled {
  color: var(--qlx-ink-300);
  cursor: not-allowed;
}

.cell-link.is-danger {
  color: var(--qlx-danger);
}

@media (max-width: 1023px) {
  .dict-layout {
    grid-template-columns: 1fr;
  }

  .dict-types__list {
    max-height: 260px;
  }
}
</style>

<style>
@media (max-width: 767px) {
  .master-form-dialog.el-dialog {
    width: min(600px, 92vw);
  }
}
</style>
