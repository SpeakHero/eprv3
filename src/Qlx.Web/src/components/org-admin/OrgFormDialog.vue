<!--
  所属公司：四川淇利馨集团有限公司
  描述：组织新建 / 编辑弹窗——上级选项排除自身及下级（内置 collectOrgIds），负责人候选下拉
  说明：受控展示组件（包装 QlxFormDialog）；nodes 为 buildOrgTree 产出的嵌套树，
        提交时抛出 SaveOrgInput，由页面接线适配层
-->
<script setup lang="ts">
import { computed } from 'vue'
import QlxFormDialog from '@/components/crud/QlxFormDialog.vue'
import type { FormField, FormFieldOption, FormValue } from '@/components/crud/QlxFormDialog.vue'
import type { MemberOption, OrgNodeType, SaveOrgInput } from '@/types/org-admin'
import { orgTypeLabels } from '@/types/tenant'
import { buildOrgOptions, collectOrgIds } from './org-admin-utils'
import type { OrgTreeNode } from './org-admin-utils'

const props = withDefaults(
  defineProps<{
    modelValue: boolean
    /** 编辑中的节点（null = 新增下级）。 */
    editing: OrgTreeNode | null
    /** 新增时的默认上级（页面传入当前选中节点）。 */
    defaultParent?: OrgTreeNode | null
    /** 组织树（上级选项数据源）。 */
    nodes: OrgTreeNode[]
    /** 负责人候选。 */
    managerOptions: MemberOption[]
    submitting?: boolean
  }>(),
  { defaultParent: null, submitting: false },
)

const emit = defineEmits<{
  'update:modelValue': [open: boolean]
  submit: [input: SaveOrgInput]
}>()

const visible = computed({
  get: () => props.modelValue,
  set: (open) => emit('update:modelValue', open),
})

const title = computed(() => {
  if (props.editing) {
    return `编辑组织 · ${props.editing.name}`
  }
  // 组织树为空时首个组织必须是公司根节点（上级不可选），标题同步收敛
  if (props.nodes.length === 0) {
    return '新建公司根节点'
  }
  return `新增下级组织${props.defaultParent ? ` · ${props.defaultParent.name}` : ''}`
})

const fields = computed<FormField[]>(() => {
  const editing = props.editing
  // 编辑时上级选项排除自身及全部下级（避免环形层级，与后端校验同规则）
  const excluded = editing ? collectOrgIds(editing) : undefined
  const isRoot = editing?.parentId === null
  // 首个组织（组织树为空）允许无上级，作为公司根节点创建
  const isFirstOrg = !editing && props.nodes.length === 0
  const parentOptions: FormFieldOption[] = [
    { label: '无上级（公司根节点）', value: '' },
    ...buildOrgOptions(props.nodes, excluded),
  ]
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
        ? '公司根节点不可调整上级组织'
        : isFirstOrg
          ? '组织树为空，当前创建的是公司根节点'
          : '上级组织不能选择自身或下级组织',
    },
    {
      prop: 'name',
      label: '组织名称',
      type: 'input',
      required: true,
      maxlength: 40,
      defaultValue: editing?.name ?? '',
    },
    {
      prop: 'type',
      label: '组织类型',
      type: 'select',
      required: true,
      options: Object.entries(orgTypeLabels).map(([value, label]) => ({ label, value })),
      defaultValue: editing?.type ?? 'Department',
    },
    {
      prop: 'managerMemberId',
      label: '负责人',
      type: 'select',
      filterable: true,
      options: [
        { label: '暂不指定', value: '' },
        ...props.managerOptions.map((member) => ({
          label: `${member.name}（${member.account}）`,
          value: member.id,
        })),
      ],
      defaultValue: editing?.managerMemberId ?? '',
    },
    {
      prop: 'sortOrder',
      label: '排序号',
      type: 'number',
      defaultValue: editing?.sortOrder ?? 10,
      hint: '同级组织按排序号升序展示',
    },
  ]
})

function onSubmit(values: Record<string, FormValue>): void {
  emit('submit', {
    parentId: values.parentId ? String(values.parentId) : null,
    name: String(values.name ?? '').trim(),
    type: String(values.type ?? 'Department') as OrgNodeType,
    managerMemberId: values.managerMemberId ? String(values.managerMemberId) : null,
    sortOrder: Number(values.sortOrder ?? 0),
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
