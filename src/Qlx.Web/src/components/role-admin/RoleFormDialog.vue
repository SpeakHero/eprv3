<!--
  所属公司：四川淇利馨集团有限公司
  描述：角色新建 / 编辑弹窗——内置编码规则校验按 caps.codePattern，数据范围按 caps.dataScope 显隐
  说明：受控展示组件（包装 QlxFormDialog）；提交时抛出 SaveRoleInput，由页面接线适配层
-->
<script setup lang="ts">
import { computed } from 'vue'
import QlxFormDialog from '@/components/crud/QlxFormDialog.vue'
import type { FormField, FormValue } from '@/components/crud/QlxFormDialog.vue'
import type { RoleAdminCaps } from '@/api/adapters/roleAdmin'
import type { DataScope, RoleItem, SaveRoleInput } from '@/types/org-admin'
import { dataScopeLabels } from '@/types/tenant'

const props = withDefaults(
  defineProps<{
    modelValue: boolean
    /** 编辑中的角色（null = 新建）。 */
    editing: RoleItem | null
    caps: RoleAdminCaps
    submitting?: boolean
  }>(),
  { submitting: false },
)

const emit = defineEmits<{
  'update:modelValue': [open: boolean]
  submit: [input: SaveRoleInput]
}>()

const visible = computed({
  get: () => props.modelValue,
  set: (open) => emit('update:modelValue', open),
})

const title = computed(() =>
  props.editing ? `编辑角色 · ${props.editing.name}` : '新建角色',
)

const fields = computed<FormField[]>(() => {
  const editing = props.editing
  const list: FormField[] = [
    {
      prop: 'name',
      label: '角色名称',
      type: 'input',
      required: true,
      maxlength: 32,
      defaultValue: editing?.name ?? '',
    },
  ]
  if (props.caps.dataScope) {
    list.push({
      prop: 'dataScope',
      label: '数据范围',
      type: 'select',
      required: true,
      full: true,
      options: props.caps.scopeOptions.map((value) => ({ label: dataScopeLabels[value], value })),
      defaultValue: editing?.dataScope ?? 'Self',
      hint: '仅本人 → 本部门 → 本部门及下级 → 全部；数据范围决定可查看的业务数据边界',
    })
  }
  list.push({
    prop: 'description',
    label: '角色描述',
    type: 'textarea',
    full: true,
    maxlength: 120,
    defaultValue: editing?.description ?? '',
  })
  return list
})

function onSubmit(values: Record<string, FormValue>): void {
  emit('submit', {
    name: String(values.name ?? '').trim(),
    description: String(values.description ?? '').trim(),
    dataScope: props.caps.dataScope ? (String(values.dataScope ?? 'Self') as DataScope) : null,
  })
}
</script>

<template>
  <QlxFormDialog
    v-model="visible"
    :title="title"
    :fields="fields"
    :submitting="submitting"
    submit-text="保存角色"
    :width="600"
    class="role-form-dialog"
    @submit="onSubmit"
  />
</template>

<style>
/* el-dialog 经 teleport 挂载到 body，scoped 不可达，故用专属类全局样式。
   平板竖屏 / 手机（<768px）：弹窗宽度收敛为 min(600px, 92vw)（方案 §9 断点规范） */
@media (max-width: 767px) {
  .role-form-dialog.el-dialog {
    width: min(600px, 92vw);
  }
}
</style>
