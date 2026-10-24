<!--
  所属公司：四川淇利馨集团有限公司
  描述：成员批量导入弹窗——包装 QlxImportDialog + 模板下载 + 结果回显（成功 N / 失败行明细表）
  说明：受控展示组件；模板为纯前端 CSV 生成（Blob 下载，无 API）；提交抛出 ImportRow[]，
        导入结果经 result prop 回显（#result 插槽），关闭时由页面重置 result
-->
<script setup lang="ts">
import { computed } from 'vue'
import QlxImportDialog from '@/components/crud/QlxImportDialog.vue'
import type { ImportColumn } from '@/components/crud/QlxImportDialog.vue'
import type { ImportResult, ImportRow } from '@/types/org-admin'
import { mapImportRows } from './member-admin-utils'

/** 导入列定义（与后端 ImportMemberRow 契约一致：按名称匹配组织 / 角色）。 */
const importColumns: ImportColumn[] = [
  { key: 'displayName', label: '姓名', required: true, width: 130 },
  {
    key: 'account',
    label: '手机号或邮箱',
    required: true,
    width: 190,
    pattern: /^(1\d{10}|[^\s@]+@[^\s@]+\.[^\s@]+)$/,
    patternMessage: '手机号或邮箱格式不正确',
  },
  { key: 'orgName', label: '所属组织', required: true, width: 160 },
  { key: 'roleName', label: '角色名称', required: true, width: 140 },
]

/** CSV 模板（首行表头 + 一行示例）。 */
const TEMPLATE_CSV = '姓名,手机号或邮箱,所属组织,角色名称\n张三,13800000019,采购部,采购专员\n'

const props = withDefaults(
  defineProps<{
    modelValue: boolean
    submitting?: boolean
    /** 最近一次导入结果（非空时展示回显区；页面在关闭 / 重新打开时重置为 null）。 */
    result?: ImportResult | null
  }>(),
  { submitting: false, result: null },
)

const emit = defineEmits<{
  'update:modelValue': [open: boolean]
  submit: [rows: ImportRow[]]
}>()

const visible = computed({
  get: () => props.modelValue,
  set: (open) => emit('update:modelValue', open),
})

/** 下载 CSV 模板（纯前端生成，含 BOM 保证 Excel 中文不乱码）。 */
function downloadTemplate(): void {
  const blob = new Blob([`﻿${TEMPLATE_CSV}`], { type: 'text/csv;charset=utf-8' })
  const url = URL.createObjectURL(blob)
  const anchor = document.createElement('a')
  anchor.href = url
  anchor.download = '成员导入模板.csv'
  anchor.click()
  URL.revokeObjectURL(url)
}

function onSubmit(rows: Record<string, string>[]): void {
  emit('submit', mapImportRows(rows))
}
</script>

<template>
  <QlxImportDialog
    v-model="visible"
    title="批量导入成员"
    :columns="importColumns"
    :submitting="submitting"
    hint="按「姓名 / 手机号或邮箱 / 所属组织名称 / 角色名称」逐行填写；组织与角色需与本企业已维护数据完全一致，导入后统一进入待激活状态。"
    sample="张三,13800000019,采购部,采购专员"
    @submit="onSubmit"
  >
    <template #toolbar-extra>
      <el-button size="small" text type="primary" @click="downloadTemplate">下载模板</el-button>
    </template>

    <template v-if="result" #result>
      <div class="import-result">
        <el-alert
          :type="result.failed.length ? 'warning' : 'success'"
          :closable="false"
          :title="`成功邀请 ${result.invited} 人${result.failed.length ? `；失败 ${result.failed.length} 行` : ''}`"
          show-icon
        />
        <el-table v-if="result.failed.length" :data="result.failed" size="small" border max-height="200">
          <el-table-column prop="line" label="行号" width="80" align="center" />
          <el-table-column prop="reason" label="失败原因" min-width="220" />
        </el-table>
      </div>
    </template>
  </QlxImportDialog>
</template>

<style scoped>
.import-result {
  margin-top: var(--qlx-space-4);
}

.import-result :deep(.el-table) {
  margin-top: var(--qlx-space-2);
}
</style>
