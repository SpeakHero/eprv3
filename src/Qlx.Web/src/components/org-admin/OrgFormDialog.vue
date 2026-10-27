<!--
  所属公司：四川淇利馨集团有限公司
  描述：组织新建 / 编辑弹窗（Bas 主数据版）——上级选项排除自身及下级、四级类型、人员负责人、有效期
  说明：
    1. 受控展示组件（包装 QlxFormDialog）；nodes 为 buildOrgTree 产出的嵌套树；
    2. 编码由后端发号器生成，表单不出现编码输入框，仅在名称字段下方只读回显（N-1）；
    3. 类型 / 负责人以原始值提交（类型数值，负责人为人员 Id），中文映射在公共层（B-4）。
-->
<script setup lang="ts">
import { computed } from 'vue'
import QlxFormDialog from '@/components/crud/QlxFormDialog.vue'
import type { FormField, FormFieldOption, FormValue } from '@/components/crud/QlxFormDialog.vue'
import type { BasOrgType, PersonOption, SaveBasOrgPayload } from '@/types/bas-master'
import { buildOrgOptions, basOrgTypeOptions, collectOrgIds } from './org-admin-utils'
import type { BasOrgTreeNode } from './org-admin-utils'

const props = withDefaults(
  defineProps<{
    modelValue: boolean
    /** 编辑中的节点（null = 新增下级）。 */
    editing: BasOrgTreeNode | null
    /** 新增时的默认上级（页面传入当前选中节点）。 */
    defaultParent?: BasOrgTreeNode | null
    /** 组织树（上级选项数据源）。 */
    nodes: BasOrgTreeNode[]
    /** 负责人候选（人员主数据）。 */
    leaderOptions: PersonOption[]
    submitting?: boolean
  }>(),
  { defaultParent: null, submitting: false },
)

const emit = defineEmits<{
  'update:modelValue': [open: boolean]
  submit: [input: SaveBasOrgPayload]
}>()

const visible = computed({
  get: () => props.modelValue,
  set: (open) => emit('update:modelValue', open),
})

const title = computed(() => {
  if (props.editing) {
    return `编辑组织 · ${props.editing.name}`
  }
  if (props.nodes.length === 0) {
    return '新建集团节点'
  }
  return `新增下级组织${props.defaultParent ? ` · ${props.defaultParent.name}` : ''}`
})

const fields = computed<FormField[]>(() => {
  const editing = props.editing
  // 编辑时上级选项排除自身及全部下级（避免环形层级，与后端校验同规则）
  const excluded = editing ? collectOrgIds(editing) : undefined
  const isRoot = editing?.parentId === null
  // 首个组织（组织树为空）允许无上级，作为集团根节点创建
  const isFirstOrg = !editing && props.nodes.length === 0
  const parentOptions: FormFieldOption[] = [
    { label: '无上级（集团根节点）', value: '' },
    ...buildOrgOptions(props.nodes, excluded),
  ]
  // 编码只读回显（不提供输入框，N-1）
  const codeHint = editing
    ? `编码 ${editing.code} 由系统生成，不可修改`
    : '编码由系统按编码规则自动生成，无需填写'
  return [
    {
      prop: 'parentId',
      label: '上级组织',
      type: 'select',
      required: !isRoot && !isFirstOrg,
      full: true,
      filterable: true,
      options: parentOptions,
      disabled: isRoot || isFirstOrg,
      defaultValue: editing ? (editing.parentId ?? '') : (isFirstOrg ? '' : (props.defaultParent?.id ?? '')),
      hint: isRoot
        ? '集团根节点不可调整上级组织'
        : isFirstOrg
          ? '组织树为空，当前创建的是集团根节点'
          : '上级组织不能选择自身或下级组织',
    },
    {
      prop: 'name',
      label: '组织名称',
      type: 'input',
      required: true,
      maxlength: 40,
      defaultValue: editing?.name ?? '',
      hint: codeHint,
    },
    {
      prop: 'type',
      label: '组织类型',
      type: 'select',
      required: true,
      options: basOrgTypeOptions().map((option) => ({
        label: option.label,
        value: option.value,
      })),
      defaultValue: editing?.type ?? (isFirstOrg ? 1 : 3),
    },
    {
      prop: 'leaderPersonId',
      label: '负责人',
      type: 'select',
      filterable: true,
      options: [
        { label: '暂不指定', value: '' },
        ...props.leaderOptions.map((person) => ({
          label: `${person.name}（${person.code}）`,
          value: person.id,
        })),
      ],
      defaultValue: editing?.leaderPersonId ?? '',
    },
    {
      prop: 'sortOrder',
      label: '排序号',
      type: 'number',
      defaultValue: editing?.sortOrder ?? 10,
      hint: '同级组织按排序号升序展示',
    },
    {
      prop: 'effectDate',
      label: '生效日期',
      type: 'date',
      defaultValue: editing?.effectDate ?? '',
    },
    {
      prop: 'expiryDate',
      label: '失效日期',
      type: 'date',
      defaultValue: editing?.expiryDate ?? '',
      hint: '留空表示长期有效',
    },
  ]
})

function onSubmit(values: Record<string, FormValue>): void {
  emit('submit', {
    parentId: values.parentId ? String(values.parentId) : null,
    name: String(values.name ?? '').trim(),
    type: Number(values.type ?? 3) as BasOrgType,
    leaderPersonId: values.leaderPersonId ? String(values.leaderPersonId) : null,
    sortOrder: Number(values.sortOrder ?? 0),
    effectDate: values.effectDate ? String(values.effectDate) : null,
    expiryDate: values.expiryDate ? String(values.expiryDate) : null,
  })
}
</script>

<template>
  <QlxFormDialog
    v-model="visible"
    :title="title"
    :fields="fields"
    :submitting="submitting"
    submit-text="保存组织"
    :width="600"
    class="org-form-dialog"
    @submit="onSubmit"
  />
</template>

<style>
/* el-dialog 经 teleport 挂载到 body，scoped 不可达，故用专属类全局样式。
   平板竖屏 / 手机（<768px）：弹窗宽度收敛为 min(600px, 92vw)（方案 §9 断点规范） */
@media (max-width: 767px) {
  .org-form-dialog.el-dialog {
    width: min(600px, 92vw);
  }
}
</style>
