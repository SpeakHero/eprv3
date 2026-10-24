<!--
  所属公司：四川淇利馨集团有限公司
  描述：标准示例 · 表单与弹窗——QlxFormDialog 全字段类型 / 声明式校验 / 服务端 422 字段级
        错误透出 / QlxImportDialog 批量导入 的标准用法（mock 驱动）
  说明：
    - QlxFormDialog 适用一切「新建 / 编辑 / 重置 / 配置」类弹窗表单，字段经 fields 配置驱动；
    - 服务端 422 错误透出演示 V-1-3：后端校验失败响应必须可定位到具体字段（fieldErrors）；
    - QlxImportDialog 适用商品、供应商、成员等批量导入场景，列定义与校验规则由父级传入。
-->
<script setup lang="ts">
import { ref } from 'vue'
import { ElMessage } from 'element-plus'
import AppIcon from '@/components/AppIcon.vue'
import QlxFormDialog from '@/components/crud/QlxFormDialog.vue'
import type { FormField, FormValue } from '@/components/crud/QlxFormDialog.vue'
import QlxImportDialog from '@/components/crud/QlxImportDialog.vue'
import type { ImportColumn } from '@/components/crud/QlxImportDialog.vue'
import QlxPagePanel from '@/components/crud/QlxPagePanel.vue'

/** 最近一次提交负载（演示 @submit 输出形态）。 */
const lastPayload = ref('')

/* ============================================================
 * 1. 全字段类型演示
 *    type 支持：input / textarea / select（可 multiple + filterable）/ number / switch；
 *    常用修饰：required 必填、pattern 正则校验、hint 表单项下方灰字提示、
 *             maxlength 限长、disabled 禁用、full 独占整行（默认半宽两列排布）。
 * ============================================================ */
const fullOpen = ref(false)
const fullSubmitting = ref(false)

const fullFields: FormField[] = [
  { prop: 'name', label: '名称', type: 'input', required: true, maxlength: 32, placeholder: '请输入名称' },
  {
    prop: 'phone',
    label: '联系电话',
    type: 'input',
    // pattern：声明式正则校验（blur 触发）；patternMessage 为失败提示
    pattern: /^1[3-9]\d{9}$/,
    patternMessage: '手机号格式不正确',
    hint: '选填；格式 1xx xxxx xxxx',
  },
  {
    prop: 'category',
    label: '类目',
    type: 'select',
    required: true,
    options: [
      { label: '办公用品', value: 'office' },
      { label: '电子设备', value: 'electronics' },
      { label: '办公家具', value: 'furniture' },
    ],
  },
  {
    prop: 'tags',
    label: '标签',
    type: 'select',
    multiple: true, // 多选：payload 中该字段为 string[]
    filterable: true, // 可搜索（选项较多时启用）
    options: [
      { label: '重点管控', value: 'key' },
      { label: '常用', value: 'frequent' },
      { label: '低频', value: 'rare' },
      { label: '季节采购', value: 'seasonal' },
    ],
    hint: '多选 + 可搜索；提交值为 string[]',
  },
  { prop: 'price', label: '单价（元）', type: 'number', required: true },
  { prop: 'enabled', label: '启用', type: 'switch', defaultValue: true, hint: 'switch 型默认 false，可经 defaultValue 置初值' },
  // full: 独占整行（默认半宽两列）；编码类只读回显用 disabled（N-1：编码不可修改）
  { prop: 'code', label: '业务编码', type: 'input', disabled: true, defaultValue: 'GD-10001（后端生成，只读）' },
  { prop: 'remark', label: '备注', type: 'textarea', full: true, maxlength: 200 },
]

async function onFullSubmit(payload: Record<string, FormValue>): Promise<void> {
  fullSubmitting.value = true
  try {
    // 真实场景：await api.create(payload)；此处仅回显负载
    await new Promise((resolve) => setTimeout(resolve, 300))
    lastPayload.value = JSON.stringify(payload, null, 2)
    ElMessage.success('已提交（负载见下方回显）')
    fullOpen.value = false
  } finally {
    fullSubmitting.value = false
  }
}

/* ============================================================
 * 2. 服务端 422 字段级错误透出（V-1-3）
 *    父级持有 fieldErrors（键 = 字段 prop）；提交时后端 422 返回
 *    { field, message } 后写入 fieldErrors，组件自动定位展示；
 *    重新打开弹窗或再次提交前由父级清空。
 * ============================================================ */
const serverOpen = ref(false)
const serverSubmitting = ref(false)
const serverErrors = ref<Record<string, string>>({})

const serverFields: FormField[] = [
  { prop: 'code', label: '供应商编码', type: 'input', required: true, hint: '演示：输入 DUP 触发服务端冲突错误' },
  { prop: 'name', label: '供应商名称', type: 'input', required: true },
]

async function onServerSubmit(payload: Record<string, FormValue>): Promise<void> {
  serverSubmitting.value = true
  serverErrors.value = {} // 再次提交前清空旧错误
  try {
    await new Promise((resolve) => setTimeout(resolve, 300))
    if (String(payload.code).toUpperCase() === 'DUP') {
      // 模拟后端 422：编码冲突 → 字段级错误透出到对应表单项
      serverErrors.value = { code: '编码已存在，请更换（服务端校验）' }
      return
    }
    lastPayload.value = JSON.stringify(payload, null, 2)
    ElMessage.success('校验通过，已提交')
    serverOpen.value = false
  } finally {
    serverSubmitting.value = false
  }
}

/* ============================================================
 * 3. 批量导入（QlxImportDialog 独立演示）
 *    columns 定义列序与校验；弹窗内部完成逐行解析 / 必填与正则校验 / 预览，
 *    @submit 仅收到全部有效的行数组；示例行（sample）会作为 textarea 占位提示。
 * ============================================================ */
const importOpen = ref(false)
const importSubmitting = ref(false)

const importColumns: ImportColumn[] = [
  { key: 'name', label: '名称', required: true },
  { key: 'phone', label: '手机号', required: true, pattern: /^1[3-9]\d{9}$/, patternMessage: '手机号格式不正确' },
  { key: 'email', label: '邮箱', pattern: /^[^\s@]+@[^\s@]+\.[^\s@]+$/, patternMessage: '邮箱格式不正确' },
]

async function onImportSubmit(rows: Record<string, string>[]): Promise<void> {
  importSubmitting.value = true
  try {
    await new Promise((resolve) => setTimeout(resolve, 400))
    lastPayload.value = JSON.stringify(rows, null, 2)
    ElMessage.success(`已导入 ${rows.length} 行（行数据见下方回显）`)
    importOpen.value = false
  } finally {
    importSubmitting.value = false
  }
}
</script>

<template>
  <QlxPagePanel
    title="表单与弹窗示例"
    subtitle="QlxFormDialog 全字段类型 / 声明式校验 / 服务端 422 字段级错误透出 / QlxImportDialog 批量导入"
  >
    <div class="form-demo">
      <!-- 触发区：实际业务中由工具栏或行内按钮打开对应弹窗 -->
      <div class="form-demo__triggers">
        <el-button type="primary" @click="fullOpen = true">
          <AppIcon name="EditPen" :size="14" />
          全字段类型表单
        </el-button>
        <el-button @click="serverOpen = true">
          <AppIcon name="Warning" :size="14" />
          服务端 422 错误透出
        </el-button>
        <el-button @click="importOpen = true">
          <AppIcon name="Upload" :size="14" />
          批量导入弹窗
        </el-button>
      </div>

      <p class="form-demo__hint">
        提交负载 / 导入行将在下方回显，便于核对 @submit 输出形态（真实场景此处替换为接口调用）。
      </p>

      <!-- 负载回显：确认 payload / rows 的数据结构 -->
      <pre v-if="lastPayload" class="form-demo__payload">{{ lastPayload }}</pre>
    </div>

    <!-- 1. 全字段类型 -->
    <QlxFormDialog
      v-model="fullOpen"
      title="全字段类型表单"
      :fields="fullFields"
      :submitting="fullSubmitting"
      :width="640"
      @submit="onFullSubmit"
    />

    <!-- 2. 服务端 422 字段级错误透出：field-errors 由父级持有并随提交清空 -->
    <QlxFormDialog
      v-model="serverOpen"
      title="服务端校验错误透出"
      :fields="serverFields"
      :submitting="serverSubmitting"
      :field-errors="serverErrors"
      :width="480"
      @submit="onServerSubmit"
    />

    <!-- 3. 批量导入：行解析 / 校验 / 预览由组件内部完成 -->
    <QlxImportDialog
      v-model="importOpen"
      title="批量导入成员"
      :columns="importColumns"
      hint="每行一名成员，列顺序：名称 / 手机号 / 邮箱（邮箱选填）；支持逗号 / Tab 分隔。"
      sample="张三,13800138000,zhangsan@example.com"
      :submitting="importSubmitting"
      @submit="onImportSubmit"
    />
  </QlxPagePanel>
</template>

<style scoped>
.form-demo {
  display: flex;
  flex-direction: column;
  gap: var(--qlx-space-3);
}

.form-demo__triggers {
  display: flex;
  flex-wrap: wrap;
  gap: var(--qlx-space-3);
}

.form-demo__hint {
  color: var(--qlx-ink-400);
  font-size: var(--qlx-text-xs);
  line-height: 1.7;
}

.form-demo__payload {
  overflow: auto;
  max-height: 280px;
  padding: var(--qlx-space-3);
  border: 1px solid var(--qlx-line);
  border-radius: var(--qlx-radius-sm);
  background: var(--qlx-canvas);
  color: var(--qlx-ink-700);
  font-family: var(--qlx-font-mono);
  font-size: var(--qlx-text-xs);
  line-height: 1.7;
  white-space: pre-wrap;
}
</style>
