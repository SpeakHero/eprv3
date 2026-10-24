<!--
  所属公司：四川淇利馨集团有限公司
  描述：打印模板中心——模板清单 / 新建 / 编辑 / 启停 / 渲染预览（规范 支撑功能 2F 打印）
  说明：模板使用 Scriban 占位（{{ var }}）+ qrcode: 占位渲染二维码；
        启用态模板承接渲染；内容变更版本自增；渲染走 /api/support/printing/templates/code/{code}/render。
-->
<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { ElMessage } from 'element-plus'
import QlxPagePanel from '@/components/crud/QlxPagePanel.vue'
import {
  createPrintTemplate,
  fetchPrintTemplates,
  renderPrintTemplate,
  setPrintTemplateStatus,
  updatePrintTemplate,
} from '@/api/support'
import type { PrintTemplateCreatePayload, PrintTemplateUpdatePayload, PrintTemplateView } from '@/types/support'
import { PRINT_TEMPLATE_STATUS_LABELS, PRINT_TEMPLATE_STATUS_TYPE } from '@/types/support'
import { formatDateTime, labelOf } from '@/utils/format'

const templates = ref<PrintTemplateView[]>([])
const loading = ref(false)

async function load(): Promise<void> {
  loading.value = true
  try {
    templates.value = await fetchPrintTemplates()
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : '加载模板失败')
  } finally {
    loading.value = false
  }
}

/* 新建 / 编辑弹窗 */
const dialogVisible = ref(false)
const isEdit = ref(false)
const form = ref<PrintTemplateCreatePayload>({
  templateCode: '',
  name: '',
  bizType: '',
  contentHtml: '',
  css: null,
})
const editingId = ref('')

function openCreate(): void {
  isEdit.value = false
  editingId.value = ''
  form.value = { templateCode: '', name: '', bizType: '', contentHtml: '', css: null }
  dialogVisible.value = true
}

function openEdit(tpl: PrintTemplateView): void {
  isEdit.value = true
  editingId.value = tpl.id
  form.value = {
    templateCode: tpl.templateCode,
    name: tpl.name,
    bizType: tpl.bizType,
    contentHtml: tpl.contentHtml,
    css: tpl.css,
  }
  dialogVisible.value = true
}

async function submit(): Promise<void> {
  if (!form.value.templateCode || !form.value.name || !form.value.contentHtml) {
    ElMessage.warning('请填写编码、名称与模板内容')
    return
  }
  try {
    if (isEdit.value) {
      const payload: PrintTemplateUpdatePayload = {
        name: form.value.name,
        bizType: form.value.bizType,
        contentHtml: form.value.contentHtml,
        css: form.value.css,
      }
      await updatePrintTemplate(editingId.value, payload)
      ElMessage.success('模板已更新')
    } else {
      await createPrintTemplate(form.value)
      ElMessage.success('模板已创建')
    }
    dialogVisible.value = false
    void load()
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : '保存失败')
  }
}

async function handleSetStatus(tpl: PrintTemplateView, status: number): Promise<void> {
  try {
    await setPrintTemplateStatus(tpl.id, status)
    ElMessage.success(status === 1 ? '已启用' : '已停用')
    void load()
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : '操作失败')
  }
}

/* 渲染预览 */
const previewVisible = ref(false)
const previewLoading = ref(false)
const previewHtml = ref('')
const previewCode = ref('')
const previewVars = ref('{}')

function openPreview(tpl: PrintTemplateView): void {
  previewCode.value = tpl.templateCode
  previewVars.value = '{}'
  previewHtml.value = ''
  previewVisible.value = true
}

async function runPreview(): Promise<void> {
  previewLoading.value = true
  try {
    let vars: Record<string, unknown> = {}
    if (previewVars.value.trim()) {
      vars = JSON.parse(previewVars.value) as Record<string, unknown>
    }
    previewHtml.value = await renderPrintTemplate(previewCode.value, vars)
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : '渲染失败')
  } finally {
    previewLoading.value = false
  }
}

onMounted(() => {
  void load()
})
</script>

<template>
  <QlxPagePanel
    title="打印模板中心"
    subtitle="打印模板维护与渲染预览——Scriban 占位 + qrcode: 二维码占位；启用态模板承接业务单据打印"
  >
    <template #toolbar>
      <el-button type="primary" @click="openCreate">新建模板</el-button>
    </template>

    <el-table :data="templates" v-loading="loading" stripe style="width: 100%">
      <el-table-column prop="templateCode" label="模板编码" min-width="180" />
      <el-table-column prop="name" label="名称" min-width="160" />
      <el-table-column prop="bizType" label="业务类型" width="140" />
      <el-table-column label="状态" width="100">
        <template #default="{ row }">
          <el-tag :type="labelOf(PRINT_TEMPLATE_STATUS_TYPE, row.status, 'info')">
            {{ labelOf(PRINT_TEMPLATE_STATUS_LABELS, row.status) }}
          </el-tag>
        </template>
      </el-table-column>
      <el-table-column prop="version" label="版本" width="80" />
      <el-table-column prop="updatedAt" label="更新时间" width="170">
        <template #default="{ row }">{{ formatDateTime(row.updatedAt) }}</template>
      </el-table-column>
      <el-table-column label="操作" width="260" fixed="right">
        <template #default="{ row }">
          <el-button size="small" link type="primary" @click="openPreview(row)">预览</el-button>
          <el-button size="small" link @click="openEdit(row)">编辑</el-button>
          <el-button
            v-if="row.status !== 1"
            size="small"
            type="success"
            link
            @click="handleSetStatus(row, 1)"
          >
            启用
          </el-button>
          <el-button
            v-if="row.status === 1"
            size="small"
            type="warning"
            link
            @click="handleSetStatus(row, 2)"
          >
            停用
          </el-button>
        </template>
      </el-table-column>
    </el-table>
  </QlxPagePanel>

  <!-- 新建 / 编辑弹窗 -->
  <el-dialog v-model="dialogVisible" :title="isEdit ? '编辑模板' : '新建模板'" width="720px">
    <el-form label-width="90px">
      <el-form-item label="模板编码">
        <el-input v-model="form.templateCode" :disabled="isEdit" placeholder="租户内唯一" />
      </el-form-item>
      <el-form-item label="名称">
        <el-input v-model="form.name" />
      </el-form-item>
      <el-form-item label="业务类型">
        <el-input v-model="form.bizType" placeholder="如：pur_order / sal_order" />
      </el-form-item>
      <el-form-item label="模板内容">
        <el-input
          v-model="form.contentHtml"
          type="textarea"
          :rows="10"
          placeholder="使用 {{ var }} 占位，qrcode:var 渲染二维码"
        />
      </el-form-item>
      <el-form-item label="附加样式">
        <el-input v-model="form.css" type="textarea" :rows="3" placeholder="可空" />
      </el-form-item>
    </el-form>
    <template #footer>
      <el-button @click="dialogVisible = false">取消</el-button>
      <el-button type="primary" @click="submit">保存</el-button>
    </template>
  </el-dialog>

  <!-- 渲染预览弹窗 -->
  <el-dialog v-model="previewVisible" title="渲染预览" width="800px">
    <div class="preview-toolbar">
      <span>模板编码：{{ previewCode }}</span>
      <el-input
        v-model="previewVars"
        placeholder='模板变量 JSON，如 {"name": "测试"}'
        style="width: 320px"
      />
      <el-button type="primary" :loading="previewLoading" @click="runPreview">渲染</el-button>
    </div>
    <div class="preview-box">
      <div v-if="previewLoading" class="preview-loading">渲染中…</div>
      <iframe v-else-if="previewHtml" :srcdoc="previewHtml" class="preview-iframe" />
      <p v-else class="preview-empty">点击「渲染」查看模板输出</p>
    </div>
  </el-dialog>
</template>

<style scoped>
.preview-toolbar {
  display: flex;
  align-items: center;
  gap: var(--qlx-space-2);
  margin-bottom: var(--qlx-space-3);
}
.preview-box {
  border: 1px solid var(--qlx-line);
  border-radius: var(--qlx-radius-sm);
  min-height: 400px;
  background: var(--qlx-canvas);
}
.preview-iframe {
  width: 100%;
  min-height: 400px;
  border: 0;
}
.preview-loading,
.preview-empty {
  padding: var(--qlx-space-8);
  text-align: center;
  color: var(--qlx-ink-400);
}
</style>
