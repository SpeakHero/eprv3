<!--
  所属公司：四川淇利馨集团有限公司
  描述：通用批量导入弹窗——粘贴/选择 CSV/TXT → 逐行解析 → 校验预览 → 提交有效行
  说明：列定义由父级传入（key / 中文名 / 必填 / 正则），导入行交给业务层映射为接口入参；
        后续商品、供应商等批量导入可直接复用，无需重复实现解析与预览。
-->
<script setup lang="ts">
import { computed, ref, watch } from 'vue'

export interface ImportColumn {
  /** 行数据键。 */
  key: string
  label: string
  required?: boolean
  pattern?: RegExp
  patternMessage?: string
  width?: number
}

interface ParsedRow {
  line: number
  values: Record<string, string>
  errors: string[]
}

const props = withDefaults(
  defineProps<{
    modelValue: boolean
    title: string
    columns: ImportColumn[]
    /** 填写说明（弹窗顶部）。 */
    hint?: string
    /** 示例行（textarea 占位提示）。 */
    sample?: string
    submitting?: boolean
  }>(),
  { hint: '', sample: '', submitting: false },
)

const emit = defineEmits<{
  'update:modelValue': [open: boolean]
  submit: [rows: Record<string, string>[]]
}>()

const visible = computed({
  get: () => props.modelValue,
  set: (open) => emit('update:modelValue', open),
})

const raw = ref('')
const fileInput = ref<HTMLInputElement>()

/** 支持逗号 / 中文逗号 / Tab 分隔（兼容 Excel 复制粘贴与 CSV）。 */
const separator = /[\t,，]/

const rows = computed<ParsedRow[]>(() =>
  raw.value
    .split(/\r?\n/)
    .map((line) => line.trim())
    .filter((line) => line.length > 0)
    .map((line, index) => {
      const cells = line.split(separator).map((cell) => cell.trim())
      const values: Record<string, string> = {}
      const errors: string[] = []
      props.columns.forEach((column, columnIndex) => {
        const value = cells[columnIndex] ?? ''
        values[column.key] = value
        if (column.required && !value) {
          errors.push(`${column.label}为空`)
          return
        }
        if (value && column.pattern && !column.pattern.test(value)) {
          errors.push(column.patternMessage ?? `${column.label}格式不正确`)
        }
      })
      if (cells.length > props.columns.length) {
        errors.push('列数超出模板要求')
      }
      return { line: index + 1, values, errors }
    }),
)

const invalidCount = computed(() => rows.value.filter((row) => row.errors.length > 0).length)

/** 有效行（后端按行独立提交，无效行不阻塞其它行导入）。 */
const validRows = computed(() => rows.value.filter((row) => row.errors.length === 0))

const canSubmit = computed(() => validRows.value.length > 0)

function onFileChange(event: Event): void {
  const target = event.target as HTMLInputElement
  const file = target.files?.[0]
  if (!file) {
    return
  }
  const reader = new FileReader()
  reader.onload = () => {
    raw.value = String(reader.result ?? '')
  }
  reader.readAsText(file, 'utf-8')
  target.value = ''
}

function onSubmit(): void {
  if (!canSubmit.value) {
    return
  }
  // 仅提交有效行，并携带原始行号（页面映射入参时用于失败行定位）
  emit(
    'submit',
    validRows.value.map((row) => ({ line: String(row.line), ...row.values })),
  )
}

watch(
  () => props.modelValue,
  (open) => {
    if (open) {
      raw.value = ''
    }
  },
)
</script>

<template>
  <el-dialog
    v-model="visible"
    :title="title"
    width="760"
    :close-on-click-modal="false"
    append-to-body
  >
    <div class="qlx-import">
      <p v-if="hint" class="qlx-import__hint">{{ hint }}</p>

      <div class="qlx-import__toolbar">
        <el-button size="small" @click="fileInput?.click()">选择 CSV / TXT 文件</el-button>
        <!-- 业务侧扩展操作（如模板下载）。 -->
        <slot name="toolbar-extra" />
        <input
          ref="fileInput"
          type="file"
          accept=".csv,.txt,text/plain,text/csv"
          class="qlx-import__file"
          @change="onFileChange"
        />
        <span class="qlx-import__sample">
          每行一条，列顺序：{{ columns.map((column) => column.label).join(' / ') }}
        </span>
      </div>

      <el-input
        v-model="raw"
        type="textarea"
        :rows="7"
        :placeholder="sample || '可直接从 Excel 复制粘贴，支持逗号或制表符分隔'"
        spellcheck="false"
      />

      <div class="qlx-import__preview">
        <header class="qlx-import__preview-head">
          <span>解析预览</span>
          <span class="qlx-numeric">
            共 {{ rows.length }} 行
            <template v-if="invalidCount">，{{ invalidCount }} 行需修正</template>
          </span>
        </header>
        <el-table :data="rows" size="small" max-height="240" border>
          <el-table-column label="行" prop="line" width="56" align="center" />
          <el-table-column
            v-for="column in columns"
            :key="column.key"
            :label="column.label"
            :width="column.width"
            min-width="120"
          >
            <template #default="{ row }">
              <span :class="{ 'is-invalid': row.errors.length > 0 }">
                {{ row.values[column.key] || '—' }}
              </span>
            </template>
          </el-table-column>
          <el-table-column label="校验" width="220">
            <template #default="{ row }">
              <span v-if="row.errors.length" class="qlx-import__error">
                {{ row.errors.join('；') }}
              </span>
              <span v-else class="qlx-import__ok">通过</span>
            </template>
          </el-table-column>
          <template #empty>
            <div class="qlx-import__empty">粘贴或选择文件后，在此预览解析结果</div>
          </template>
        </el-table>
      </div>
    </div>

    <!-- 业务侧结果回显（如导入成功 N 条 / 失败行明细）。 -->
    <slot name="result" />

    <template #footer>
      <el-button @click="visible = false">取消</el-button>
      <el-button type="primary" :loading="submitting" :disabled="!canSubmit" @click="onSubmit">
        导入 {{ validRows.length }} 条
      </el-button>
    </template>
  </el-dialog>
</template>

<style scoped>
.qlx-import__hint {
  margin-bottom: var(--qlx-space-3);
  padding: var(--qlx-space-2) var(--qlx-space-3);
  border-left: 3px solid var(--qlx-gold-500);
  background: var(--qlx-canvas);
  color: var(--qlx-ink-600);
  font-size: var(--qlx-text-xs);
  line-height: 1.7;
}

.qlx-import__toolbar {
  display: flex;
  align-items: center;
  gap: var(--qlx-space-3);
  margin-bottom: var(--qlx-space-2);
}

.qlx-import__file {
  display: none;
}

.qlx-import__sample {
  color: var(--qlx-ink-400);
  font-size: var(--qlx-text-xs);
}

.qlx-import__preview {
  margin-top: var(--qlx-space-4);
}

.qlx-import__preview-head {
  display: flex;
  align-items: baseline;
  justify-content: space-between;
  margin-bottom: var(--qlx-space-2);
  color: var(--qlx-ink-600);
  font-size: var(--qlx-text-sm);
}

.qlx-import__preview-head span:last-child {
  color: var(--qlx-ink-400);
  font-size: var(--qlx-text-xs);
}

.qlx-import__error {
  color: var(--qlx-danger);
  font-size: var(--qlx-text-xs);
}

.qlx-import__ok {
  color: var(--qlx-success);
  font-size: var(--qlx-text-xs);
}

.is-invalid {
  color: var(--qlx-danger);
}

.qlx-import__empty {
  padding: var(--qlx-space-6) 0;
  color: var(--qlx-ink-400);
  font-size: var(--qlx-text-xs);
}
</style>
