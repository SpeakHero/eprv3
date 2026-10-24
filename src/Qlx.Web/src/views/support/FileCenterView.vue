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
  fetchFileList,
  fetchRecycleList,
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

function fileHref(id: string): string {
  return `${import.meta.env.VITE_API_BASE_URL ?? ''}/api/support/files/preview/${encodeURIComponent(id)}`
}

function handlePreview(fileId: string): void {
  window.open(fileHref(fileId), '_blank')
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
          <el-input
            v-model="keyword"
            placeholder="按文件名搜索"
            clearable
            style="width: 260px"
            @keyup.enter="loadFiles"
          />
          <el-button type="primary" plain @click="loadFiles">搜索</el-button>
          <el-button @click="loadFiles">刷新</el-button>
          <FileUploader biz-type="support" :service="realFileUploadService" />
        </div>
        <el-table :data="files" v-loading="loading" stripe style="width: 100%">
          <el-table-column prop="fileName" label="文件名" min-width="220" show-overflow-tooltip />
          <el-table-column prop="contentType" label="类型" width="160" />
          <el-table-column label="大小" width="120">
            <template #default="{ row }">{{ formatBytes(row.sizeBytes) }}</template>
          </el-table-column>
          <el-table-column prop="bizDomain" label="业务域" width="120" />
          <el-table-column label="受保护" width="90">
            <template #default="{ row }">
              <el-tag v-if="row.isProtected" type="warning" size="small">受保护</el-tag>
            </template>
          </el-table-column>
          <el-table-column label="操作" width="180" fixed="right">
            <template #default="{ row }">
              <el-button size="small" link type="primary" @click="handlePreview(row.fileId)">
                预览
              </el-button>
              <el-button size="small" type="danger" link @click="handleDelete(row)">删除</el-button>
            </template>
          </el-table-column>
        </el-table>
      </el-tab-pane>

      <!-- 回收站 -->
      <el-tab-pane label="回收站" name="recycle">
        <el-table :data="recycle" v-loading="recycleLoading" stripe style="width: 100%">
          <el-table-column prop="fileName" label="文件名" min-width="220" show-overflow-tooltip />
          <el-table-column prop="contentType" label="类型" width="160" />
          <el-table-column label="大小" width="120">
            <template #default="{ row }">{{ formatBytes(row.sizeBytes) }}</template>
          </el-table-column>
          <el-table-column prop="recycledAt" label="删除时间" width="170">
            <template #default="{ row }">{{ formatDateTime(row.recycledAt) }}</template>
          </el-table-column>
          <el-table-column label="操作" width="120" fixed="right">
            <template #default="{ row }">
              <el-button size="small" type="primary" link @click="handleRestore(row)">还原</el-button>
            </template>
          </el-table-column>
        </el-table>
      </el-tab-pane>
    </el-tabs>
  </QlxPagePanel>
</template>

<style scoped>
.file-toolbar {
  display: flex;
  align-items: center;
  gap: var(--qlx-space-2);
  margin-bottom: var(--qlx-space-3);
}
</style>
