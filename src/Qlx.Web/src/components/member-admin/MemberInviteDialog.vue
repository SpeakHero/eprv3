<!--
  所属公司：四川淇利馨集团有限公司
  描述：邀请成员弹窗——账号（手机号 / 邮箱）/ 姓名 / 组织下拉 / 角色多选
  说明：受控展示组件（包装 QlxFormDialog）；提交抛出 InviteMemberInput，由页面接线适配层
-->
<script setup lang="ts">
import { computed } from 'vue'
import { ElMessage } from 'element-plus'
import QlxFormDialog from '@/components/crud/QlxFormDialog.vue'
import type { FormField, FormFieldOption, FormValue } from '@/components/crud/QlxFormDialog.vue'
import type { InviteMemberInput } from '@/types/org-admin'

const props = withDefaults(
  defineProps<{
    modelValue: boolean
    /** 组织下拉选项（层级缩进，页面经 buildOrgOptions 生成）。 */
    orgOptions: FormFieldOption[]
    /** 角色多选选项。 */
    roleOptions: FormFieldOption[]
    /** 默认所属组织（页面传入当前筛选组织）。 */
    defaultOrgId?: string
    submitting?: boolean
  }>(),
  { defaultOrgId: '', submitting: false },
)

const emit = defineEmits<{
  'update:modelValue': [open: boolean]
  submit: [input: InviteMemberInput]
}>()

const visible = computed({
  get: () => props.modelValue,
  set: (open) => emit('update:modelValue', open),
})

const fields = computed<FormField[]>(() => [
  {
    prop: 'account',
    label: '手机号 / 邮箱',
    type: 'input',
    required: true,
    full: true,
    maxlength: 64,
    pattern: /^(1\d{10}|[^\s@]+@[^\s@]+\.[^\s@]+)$/,
    patternMessage: '请输入有效的手机号或邮箱',
    hint: '用于匹配既有自然人；未注册时创建待激活邀请记录',
  },
  {
    prop: 'displayName',
    label: '姓名（本企业内）',
    type: 'input',
    required: true,
    maxlength: 32,
  },
  {
    prop: 'orgId',
    label: '所属组织',
    type: 'select',
    required: true,
    filterable: true,
    options: props.orgOptions,
    defaultValue: props.defaultOrgId,
  },
  {
    prop: 'roleCodes',
    label: '初始角色',
    type: 'select',
    multiple: true,
    filterable: true,
    full: true,
    options: props.roleOptions,
    hint: '可多选；角色决定功能权限与数据范围',
  },
])

function onSubmit(values: Record<string, FormValue>): void {
  const roleCodes = Array.isArray(values.roleCodes) ? values.roleCodes.map(String) : []
  if (!roleCodes.length) {
    ElMessage.warning('请至少选择一个初始角色')
    return
  }
  emit('submit', {
    account: String(values.account ?? '').trim(),
    displayName: String(values.displayName ?? '').trim(),
    orgId: String(values.orgId ?? ''),
    roleCodes,
  })
}
</script>

<template>
  <QlxFormDialog
    v-model="visible"
    title="邀请成员"
    :fields="fields"
    :submitting="submitting"
    submit-text="发送邀请"
    :width="600"
    class="member-invite-dialog"
    @submit="onSubmit"
  />
</template>

<style>
/* el-dialog 经 teleport 挂载到 body，scoped 不可达，故用专属类全局样式。
   平板竖屏 / 手机（<768px）：弹窗宽度收敛为 min(600px, 92vw)（方案 §9 断点规范） */
@media (max-width: 767px) {
  .member-invite-dialog.el-dialog {
    width: min(600px, 92vw);
  }
}
</style>
