<!--
  所属公司：四川淇利馨集团有限公司
  描述：主数据导入弹窗（xlsx 文件模式）——包装 QlxImportDialog + 模板下载 + 行级错误回显
  说明：供应商 / 客户 / 人员 / 商品分类四个页面共用；模板编码经 useMasterTransfer 校验后由父级传入，
        列标题来自后端模板清单（不硬编码）；后端行级失败明细在此表格展示。
-->
<script setup lang="ts">
import { computed } from 'vue'
import QlxImportDialog from '@/components/crud/QlxImportDialog.vue'
import type { ImportResult } from '@/types/bas-master'

const props = withDefaults(
  defineProps<{
    modelValue: boolean
    title: string
    /** 模板列说明（useMasterTransfer.templateHint）。 */
    hint?: string
    /** 导入结果（成功 / 失败行明细）。 */
    result?: ImportResult | null
    submitting?: boolean
    downloading?: boolean
  }>(),
  { hint: '', result: null, submitting: false, downloading: false },
)

const emit = defineEmits<{
  'update:modelValue': [open: boolean]
  'submit-file': [file: File]
  'download-template': []
}>()

const visible = computed({
  get: () => props.modelValue,
  set: (open) => emit('update:modelValue', open),
})
</script>

<template>
  <QlxImportDialog
    v-model="visible"
    :title="title"
    :columns="[]"
    :hint="hint"
    file-mode
    :submitting="submitting"
    @submit-file="(file: File) => emit('submit-file', file)"
  >
    <template #toolbar-extra>
      <el-button size="small" :loading="downloading" @click="emit('download-template')">
        下载导入模板
      </el-button>
    </template>

    <template v-if="result" #result>
      <div class="master-import-result">
        <p class="master-import-result__summary">
          文件共 <span class="qlx-numeric">{{ result.totalRows }}</span> 行，
          成功 <span class="qlx-numeric">{{ result.successRows }}</span> 行，
          失败 <span class="qlx-numeric">{{ result.failedRows }}</span> 行，
          耗时 <span class="qlx-numeric">{{ result.duration }}</span>
        </p>
        <el-table
          v-if="result.errors.length"
          :data="result.errors"
          size="small"
          max-height="200"
          border
        >
          <el-table-column prop="rowNumber" label="行号" width="80" align="center" />
          <el-table-column prop="columnName" label="列" width="140" />
          <el-table-column prop="errorMessage" label="失败原因" min-width="220" />
        </el-table>
      </div>
    </template>
  </QlxImportDialog>
</template>

<style scoped>
.master-import-result {
  margin-top: var(--qlx-space-4);
}

.master-import-result__summary {
  margin-bottom: var(--qlx-space-2);
  color: var(--qlx-ink-600);
  font-size: var(--qlx-text-xs);
}
</style>
