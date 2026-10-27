<!--
  所属公司：四川淇利馨集团有限公司
  描述：岗位列表（Bas 主数据版）——QlxDataTable + 新建 / 编辑弹窗 + 启停 + 超编审批申请
  说明：受控展示组件；保存 / 启停 / 超编申请意图经事件抛出（确认框与请求由页面处理）；
        编码只读回显（无编码输入框，N-1），状态数值对接（B-4）
-->
<script setup lang="ts">
import { computed, ref } from 'vue'
import QlxDataTable from '@/components/crud/QlxDataTable.vue'
import type { CrudColumn } from '@/components/crud/QlxDataTable.vue'
import QlxFormDialog from '@/components/crud/QlxFormDialog.vue'
import type { FormField, FormFieldOption, FormValue } from '@/components/crud/QlxFormDialog.vue'
import type { BasPositionRow, SaveBasPositionPayload } from '@/types/bas-master'
import { basStatusLabel, basStatusTone } from '@/utils/bas-master-mappers'

const props = withDefaults(
  defineProps<{
    items: BasPositionRow[]
    loading?: boolean
    total?: number
    page?: number
    pageSize?: number
    /** 所属组织下拉选项（层级缩进，页面经 buildOrgOptions 生成）。 */
    orgOptions: FormFieldOption[]
    /** 新建时默认所属组织（页面传入当前选中组织）。 */
    defaultOrgId?: string
    submitting?: boolean
  }>(),
  { loading: false, total: 0, page: 1, pageSize: 10, defaultOrgId: '', submitting: false },
)

const emit = defineEmits<{
  save: [input: SaveBasPositionPayload]
  toggle: [row: BasPositionRow]
  'request-overstaff': [row: BasPositionRow]
  'page-change': [page: number, pageSize: number]
}>()

const columns: Array<CrudColumn<BasPositionRow>> = [
  { prop: 'name', label: '岗位名称', width: 150, fixed: 'left' },
  { prop: 'code', label: '岗位编码', type: 'code', width: 150 },
  { prop: 'orgId', label: '所属组织', minWidth: 160 },
  { prop: 'headcount', label: '编制人数', width: 96, align: 'center' },
  { prop: 'currentCount', label: '在职人数', width: 96, align: 'center' },
  {
    prop: 'status',
    label: '状态',
    type: 'tag',
    width: 90,
    align: 'center',
    tagText: (row) => basStatusLabel(row.status),
    tagTone: (row) => basStatusTone(row.status),
  },
  { prop: 'duty', label: '岗位职责', minWidth: 180 },
  { prop: 'description', label: '岗位说明', minWidth: 180 },
  { prop: 'updatedAt', label: '更新时间', type: 'time', width: 150 },
]

/** 组织 Id → 显示名（选项 label 含缩进，展示时去掉前缀装饰）。 */
function orgLabel(orgId: string): string {
  const option = props.orgOptions.find((o) => String(o.value) === orgId)
  return option ? option.label.replace(/^[　└ ]+/, '') : '—'
}

/** ============ 新建 / 编辑弹窗 ============ */

const dialogOpen = ref(false)
const editing = ref<BasPositionRow | null>(null)

const dialogTitle = computed(() =>
  editing.value ? `编辑岗位 · ${editing.value.name}` : '新增岗位',
)

const fields = computed<FormField[]>(() => [
  {
    prop: 'name',
    label: '岗位名称',
    type: 'input',
    required: true,
    maxlength: 32,
    defaultValue: editing.value?.name ?? '',
    hint: editing.value
      ? `编码 ${editing.value.code} 由系统生成，不可修改`
      : '编码由系统按规则自动生成，无需填写',
  },
  {
    prop: 'orgId',
    label: '所属组织',
    type: 'select',
    required: true,
    filterable: true,
    options: props.orgOptions,
    defaultValue: editing.value?.orgId ?? props.defaultOrgId,
  },
  {
    prop: 'headcount',
    label: '编制人数',
    type: 'number',
    defaultValue: editing.value?.headcount ?? 1,
  },
  {
    prop: 'duty',
    label: '岗位职责',
    type: 'textarea',
    full: true,
    maxlength: 200,
    defaultValue: editing.value?.duty ?? '',
  },
  {
    prop: 'description',
    label: '岗位说明',
    type: 'textarea',
    full: true,
    maxlength: 120,
    defaultValue: editing.value?.description ?? '',
  },
])

function openCreate(): void {
  editing.value = null
  dialogOpen.value = true
}

function openEdit(row: BasPositionRow): void {
  editing.value = row
  dialogOpen.value = true
}

function onSubmit(values: Record<string, FormValue>): void {
  emit('save', {
    id: editing.value?.id ?? null,
    name: String(values.name ?? '').trim(),
    orgId: String(values.orgId ?? ''),
    headcount: Number(values.headcount ?? 0),
    description: String(values.description ?? '').trim() || null,
    duty: String(values.duty ?? '').trim() || null,
  })
}

/** 关闭弹窗（保存成功后由页面调用，失败时保留表单便于修正）。 */
function closeDialog(): void {
  dialogOpen.value = false
}

defineExpose({ openCreate, closeDialog })
</script>

<template>
  <div class="position-table">
    <QlxDataTable
      :items="items"
      :columns="columns"
      :loading="loading"
      :total="total"
      :page="page"
      :page-size="pageSize"
      :actions-width="180"
      empty-text="暂无岗位"
      @page-change="(p: number, s: number) => emit('page-change', p, s)"
    >
      <template #cell-orgId="{ row }">
        {{ orgLabel(row.orgId) }}
      </template>
      <template #cell-headcount="{ row }">
        <span class="qlx-numeric">{{ row.headcount }}</span>
        <span v-if="row.currentCount > row.headcount" class="position-over" title="在职人数超出编制">
          超编
        </span>
      </template>
      <template #actions="{ row }">
        <button
          v-permission="'bas.admin.org.action'"
          type="button"
          class="cell-link"
          @click="openEdit(row)"
        >
          编辑
        </button>
        <el-divider direction="vertical" />
        <button
          v-permission="'bas.admin.org.action'"
          type="button"
          class="cell-link"
          @click="emit('request-overstaff', row)"
        >
          超编申请
        </button>
        <el-divider direction="vertical" />
        <button
          v-permission="'bas.admin.org.action'"
          type="button"
          class="cell-link"
          :class="{ 'is-danger': row.status === 1 }"
          @click="emit('toggle', row)"
        >
          {{ row.status === 1 ? '停用' : '启用' }}
        </button>
      </template>
    </QlxDataTable>

    <QlxFormDialog
      v-model="dialogOpen"
      :title="dialogTitle"
      :fields="fields"
      :submitting="submitting"
      submit-text="保存岗位"
      :width="600"
      class="position-form-dialog"
      @submit="onSubmit"
    />
  </div>
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

.position-over {
  margin-left: 4px;
  color: var(--qlx-danger);
  font-size: var(--qlx-text-xs);
}
</style>

<style>
/* el-dialog 经 teleport 挂载到 body，scoped 不可达，故用专属类全局样式。
   平板竖屏 / 手机（<768px）：弹窗宽度收敛为 min(600px, 92vw)（方案 §9 断点规范） */
@media (max-width: 767px) {
  .position-form-dialog.el-dialog {
    width: min(600px, 92vw);
  }
}
</style>
