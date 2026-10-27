<!--
  所属公司：四川淇利馨集团有限公司
  描述：文件中心——文件列表 / 上传 / 回收站（规范 支撑功能 04-文件存储中心）
  说明：复用 FileUploader（SYNC/ASYNC 双模式）与 FilePreview 公共组件；
        文件经 IFileStorageService 任务化提交（D22），列表/回收站/还原走 /api/support/files。
-->
<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import QlxPagePanel from '@/components/crud/QlxPagePanel.vue'
import FileUploader from '@/components/file/FileUploader.vue'
import {
  deleteFile,
  fetchFileBlob,
  fetchFileList,
  fetchRecycleList,
  purgeFile,
  restoreFile,
} from '@/api/support'
import { realFileUploadService } from '@/components/file/file-api'
import type { SupportFileMetadata } from '@/types/support'
import { formatDateTime } from '@/utils/format'
import { formatBytes } from '@/components/file/types'

type FileTab = 'list' | 'recycle'

const activeTab = ref<FileTab>('list')

/* ============ 文件列表 ============ */
const files = ref<SupportFileMetadata[]>([])
const loading = ref(false)
const keyword = ref('')
const parentId = ref<string | null>(null)

async function loadFiles(): Promise<void> {
  loading.value = true
  try {
    files.value = await fetchFileList(parentId.value, keyword.value || null)
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : '加载文件列表失败')
  } finally {
    loading.value = false
  }
}

async function handleDelete(file: SupportFileMetadata): Promise<void> {
  try {
    await ElMessageBox.confirm(`确定将「${file.fileName}」移入回收站？`, '删除文件', { type: 'warning' })
    await deleteFile(file.fileId)
    ElMessage.success('已移入回收站')
    void loadFiles()
  } catch {
    /* 用户取消 */
  }
}

/** 触发浏览器下载（带鉴权取 Blob，避免直链丢 Token）。 */
async function handleDownload(file: SupportFileMetadata): Promise<void> {
  try {
    const blob = await fetchFileBlob(file.fileId, 'download')
    const url = URL.createObjectURL(blob)
    const link = document.createElement('a')
    link.href = url
    link.download = file.fileName
    link.click()
    window.setTimeout(() => URL.revokeObjectURL(url), 10_000)
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : '下载失败')
  }
}

/* ============ 在线预览（页内弹窗，避免新窗口 about:blank） ============ */
type PreviewKind = 'image' | 'frame' | 'none'

const previewVisible = ref(false)
const previewKind = ref<PreviewKind>('none')
const previewUrl = ref('')
const previewTitle = ref('')
const previewLoading = ref(false)
let previewObjectUrl = ''

/** 释放预览 Blob URL（关闭弹窗 / 重新预览时调用，避免内存泄漏）。 */
function releasePreviewUrl(): void {
  if (previewObjectUrl) {
    URL.revokeObjectURL(previewObjectUrl)
    previewObjectUrl = ''
  }
  previewUrl.value = ''
}

async function handlePreview(file: SupportFileMetadata): Promise<void> {
  releasePreviewUrl()
  previewTitle.value = file.fileName
  previewKind.value = 'none'
  previewVisible.value = true
  previewLoading.value = true
  try {
    const blob = await fetchFileBlob(file.fileId, 'preview')
    const type = (blob.type || file.contentType || '').toLowerCase()
    previewObjectUrl = URL.createObjectURL(blob)
    previewUrl.value = previewObjectUrl
    previewKind.value = type.startsWith('image/')
      ? 'image'
      : type === 'application/pdf' || type.startsWith('text/') || type === 'application/json'
        ? 'frame'
        : 'none'
  } catch (error) {
    previewVisible.value = false
    releasePreviewUrl()
    ElMessage.error(error instanceof Error ? error.message : '预览失败')
  } finally {
    previewLoading.value = false
  }
}

/** 上传成功后自动刷新列表（E-5）。 */
function handleUploaded(): void {
  if (activeTab.value !== 'list') {
    activeTab.value = 'list'
  }
  void loadFiles()
}

/* ============ 回收站 ============ */
const recycle = ref<SupportFileMetadata[]>([])
const recycleLoading = ref(false)

async function loadRecycle(): Promise<void> {
  recycleLoading.value = true
  try {
    recycle.value = await fetchRecycleList()
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : '加载回收站失败')
  } finally {
    recycleLoading.value = false
  }
}

async function handleRestore(file: SupportFileMetadata): Promise<void> {
  try {
    await restoreFile(file.fileId)
    ElMessage.success('已还原')
    void loadRecycle()
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : '还原失败')
  }
}

/** 回收站彻底删除（不可恢复）。 */
async function handlePurge(file: SupportFileMetadata): Promise<void> {
  try {
    await ElMessageBox.confirm(
      `「${file.fileName}」将被彻底删除且无法恢复，是否继续？`,
      '彻底删除',
      { type: 'error', confirmButtonText: '彻底删除', cancelButtonText: '取消' },
    )
  } catch {
    return
  }
  try {
    await purgeFile(file.fileId)
    ElMessage.success('已彻底删除')
    void loadRecycle()
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : '彻底删除失败')
  }
}

function switchTab(tab: FileTab): void {
  activeTab.value = tab
  if (tab === 'list') void loadFiles()
  if (tab === 'recycle') void loadRecycle()
}

onMounted(() => {
  void loadFiles()
})
</script>

<template>
  <QlxPagePanel
    title="文件中心"
    subtitle="文件上传 / 列表 / 预览 / 回收站——所有文件操作经任务中心统一调度，ACL 权限由服务端判定"
  >
    <el-tabs v-model="activeTab" @tab-change="(name: string) => switchTab(name as FileTab)">
      <!-- 文件列表 -->
      <el-tab-pane label="文件列表" name="list">
        <div class="file-toolbar">
          <div class="file-toolbar__search">
            <el-input
              v-model="keyword"
              placeholder="按文件名搜索"
              clearable
              @keyup.enter="loadFiles"
            />
            <el-button type="primary" plain @click="loadFiles">搜索</el-button>
            <el-button @click="loadFiles">刷新</el-button>
          </div>
          <FileUploader
            v-permission="'support.file.upload'"
            biz-type="support"
            :service="realFileUploadService"
            @uploaded="handleUploaded"
          />
        </div>
        <el-table :data="files" v-loading="loading" stripe class="file-table">
          <el-table-column prop="fileName" label="文件名" min-width="180" show-overflow-tooltip />
          <el-table-column prop="contentType" label="类型" width="150" class-name="col-optional" />
          <el-table-column label="大小" width="100">
            <template #default="{ row }">{{ formatBytes(row.sizeBytes) }}</template>
          </el-table-column>
          <el-table-column prop="bizDomain" label="业务域" width="110" class-name="col-optional" />
          <el-table-column label="受保护" width="90" class-name="col-optional">
            <template #default="{ row }">
              <el-tag v-if="row.isProtected" type="warning" size="small">受保护</el-tag>
            </template>
          </el-table-column>
          <el-table-column label="操作" width="190" fixed="right">
            <template #default="{ row }">
              <el-button
                v-permission="'support.file.download'"
                size="small"
                link
                type="primary"
                @click="handlePreview(row)"
              >
                预览
              </el-button>
              <el-button
                v-permission="'support.file.download'"
                size="small"
                link
                type="primary"
                @click="handleDownload(row)"
              >
                下载
              </el-button>
              <el-button
                v-permission="'support.file.delete'"
                size="small"
                type="danger"
                link
                @click="handleDelete(row)"
              >删除</el-button>
            </template>
          </el-table-column>
        </el-table>
      </el-tab-pane>

      <!-- 回收站 -->
      <el-tab-pane label="回收站" name="recycle">
        <el-table :data="recycle" v-loading="recycleLoading" stripe class="file-table">
          <el-table-column prop="fileName" label="文件名" min-width="180" show-overflow-tooltip />
          <el-table-column prop="contentType" label="类型" width="150" class-name="col-optional" />
          <el-table-column label="大小" width="100">
            <template #default="{ row }">{{ formatBytes(row.sizeBytes) }}</template>
          </el-table-column>
          <el-table-column prop="recycledAt" label="删除时间" width="170" class-name="col-optional">
            <template #default="{ row }">{{ formatDateTime(row.recycledAt) }}</template>
          </el-table-column>
          <el-table-column label="操作" width="170" fixed="right">
            <template #default="{ row }">
              <el-button
                v-permission="'support.file.restore'"
                size="small"
                type="primary"
                link
                @click="handleRestore(row)"
              >还原</el-button>
              <el-button
                v-permission="'support.file.delete'"
                size="small"
                type="danger"
                link
                @click="handlePurge(row)"
              >彻底删除</el-button>
            </template>
          </el-table-column>
        </el-table>
      </el-tab-pane>
    </el-tabs>

    <el-dialog
      v-model="previewVisible"
      :title="previewTitle"
      width="min(92vw, 960px)"
      top="6vh"
      destroy-on-close
      @closed="releasePreviewUrl"
    >
      <div v-loading="previewLoading" class="preview-body">
        <img
          v-if="previewKind === 'image' && previewUrl"
          :src="previewUrl"
          :alt="previewTitle"
          class="preview-image"
        />
        <iframe
          v-else-if="previewKind === 'frame' && previewUrl"
          :src="previewUrl"
          :title="previewTitle"
          class="preview-frame"
        />
        <el-empty v-else-if="!previewLoading" description="该文件类型暂不支持在线预览，请下载后查看" />
      </div>
    </el-dialog>
  </QlxPagePanel>
</template>

<style scoped>
.file-toolbar {
  display: flex;
  flex-wrap: wrap;
  align-items: flex-start;
  gap: var(--qlx-space-3);
  margin-bottom: var(--qlx-space-3);
}

.file-toolbar__search {
  display: flex;
  flex: 1 1 320px;
  align-items: center;
  gap: var(--qlx-space-2);
  min-width: 0;
  max-width: 520px;
}

.file-toolbar__search :deep(.el-input) {
  flex: 1;
  min-width: 0;
}

.file-toolbar :deep(.file-uploader) {
  flex: 1 1 360px;
  min-width: 0;
}

.file-table {
  width: 100%;
}

.preview-body {
  display: flex;
  min-height: 320px;
  align-items: center;
  justify-content: center;
}

.preview-image {
  max-width: 100%;
  max-height: 70vh;
  object-fit: contain;
}

.preview-frame {
  width: 100%;
  height: 70vh;
  border: 0;
}

@media (max-width: 640px) {
  .file-toolbar__search {
    flex-wrap: wrap;
    max-width: none;
  }

  .file-toolbar__search :deep(.el-input) {
    flex-basis: 100%;
  }

  .file-table :deep(.col-optional) {
    display: none;
  }
}
</style>
