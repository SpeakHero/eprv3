<!--
  所属公司：四川淇利馨集团有限公司
  描述：文件上传（双模式）——SYNC ≤100MB 直传（XHR progress）；ASYNC >100MB 或手动分片
        （POST chunks → execution_id → SSE TASK_PROGRESS / TASK_COMPLETED(task_code=file.chunk.assemble)）
  秒传：hash 重复命中显示引用计数提示；预览卡片见 FilePreview
-->
<script setup lang="ts">
import { onMounted, onUnmounted, ref } from 'vue'
import AppIcon from '@/components/AppIcon.vue'
import { mockFileUploadService } from './file-mock'
import { formatBytes, SYNC_MODE_LIMIT_BYTES } from './types'
import type { FileUploadService, UploadTask } from './types'
import type { SseClient, SseEventPayload } from '@/core/sse'
import { createSseClient } from '@/core/sse'

const props = defineProps<{
  /** 上传服务（默认 mock；联调注入 realFileUploadService）。 */
  service?: FileUploadService
  /** SSE 客户端（ASYNC 进度 / 完成事件；默认内部创建 mock 传输客户端）。 */
  sseClient?: SseClient | null
  bizType?: string
}>()

const service = () => props.service ?? mockFileUploadService

const tasks = ref<UploadTask[]>([])
const forceAsync = ref(false)
const dragOver = ref(false)
const fileInput = ref<HTMLInputElement | null>(null)
const unsubscribers: Array<() => void> = []
let ownedClient: SseClient | null = null
let taskSeq = 0

function sse(): SseClient {
  if (props.sseClient) {
    return props.sseClient
  }
  if (!ownedClient) {
    ownedClient = createSseClient({ transport: 'mock' })
  }
  return ownedClient
}

onMounted(() => {
  const client = sse()
  unsubscribers.push(
    client.subscribe('TaskProgress', (event) => onSseEvent(event)),
    client.subscribe('TaskCompleted', (event) => onSseEvent(event)),
  )
})

onUnmounted(() => {
  unsubscribers.forEach((unsubscribe) => unsubscribe())
  ownedClient?.close()
})

// ASYNC 模式 SSE 事件：按 executionId 路由到任务
function onSseEvent(event: SseEventPayload): void {
  const data = event.data as { executionId?: string; taskCode?: string; percent?: number; result?: Record<string, unknown> } | null
  if (!data?.executionId) {
    return
  }
  const task = tasks.value.find((item) => item.executionId === data.executionId)
  if (!task) {
    return
  }
  if (event.type === 'TaskProgress' && typeof data.percent === 'number') {
    task.percent = data.percent
    task.message = '服务端装配中'
    return
  }
  if (event.type === 'TaskCompleted' && data.taskCode === 'file.chunk.assemble' && data.result) {
    task.phase = 'success'
    task.percent = 100
    task.message = '上传完成（SSE 装配回执）'
    task.result = {
      fileId: String(data.result.fileId ?? data.result.file_id ?? ''),
      storageKey: String(data.result.storageKey ?? data.result.storage_key ?? ''),
      fileName: task.fileName,
      contentType: task.contentType ?? 'application/octet-stream',
      sizeBytes: task.sizeBytes,
      hash: String(data.result.hash ?? ''),
      uploadedAt: new Date().toISOString(),
      refCount: (data.result.refCount ?? data.result.ref_count ?? undefined) as number | undefined,
      dedupHit: Boolean(data.result.dedupHit ?? data.result.dedup_hit ?? false),
    }
  }
}

function pickFiles(): void {
  fileInput.value?.click()
}

function onFilesChosen(event: Event): void {
  const input = event.target as HTMLInputElement
  if (input.files) {
    enqueue([...input.files])
  }
  input.value = ''
}

function onDrop(event: DragEvent): void {
  dragOver.value = false
  if (event.dataTransfer?.files) {
    enqueue([...event.dataTransfer.files])
  }
}

/** 入队并按双模式规则分发。 */
function enqueue(files: File[]): void {
  files.forEach((file) => {
    const mode: UploadTask['mode'] = forceAsync.value || file.size > SYNC_MODE_LIMIT_BYTES ? 'async' : 'sync'
    const task: UploadTask & { executionId?: string; contentType?: string } = {
      id: `task-${String(taskSeq += 1)}`,
      fileName: file.name,
      sizeBytes: file.size,
      contentType: file.type || 'application/octet-stream',
      mode,
      phase: 'uploading',
      percent: 0,
      message: mode === 'sync' ? '直传中（SYNC）' : '分片上传中（ASYNC）',
    }
    tasks.value.unshift(task)
    void runUpload(task, file)
  })
}

async function runUpload(task: UploadTask & { executionId?: string; contentType?: string }, file: File): Promise<void> {
  try {
    if (task.mode === 'sync') {
      const result = await service().uploadSync(file, {
        bizType: props.bizType ?? null,
        bizId: null,
        onProgress: (percent) => {
          task.percent = percent
        },
      })
      task.phase = 'success'
      task.percent = 100
      task.message = result.dedupHit ? `秒传命中 · 引用计数 ${result.refCount ?? 1}` : '上传成功（SHA-256 已校验）'
      task.result = result
      return
    }

    // ASYNC：初始化 → 分片 → 提交装配 → SSE 等待
    const chunkSize = 8 * 1024 * 1024
    const init = await service().initChunked({
      fileName: file.name,
      contentType: task.contentType ?? 'application/octet-stream',
      sizeBytes: file.size,
      chunkSize,
      bizType: props.bizType ?? null,
      bizId: null,
    })
    task.executionId = init.executionId
    task.message = `分片提交完成 · ${init.chunkCount} 片 · 等待装配`

    for (let index = 0; index < init.chunkCount; index++) {
      const start = index * init.chunkSize
      const chunk = file.slice(start, Math.min(start + init.chunkSize, file.size))
      await service().uploadChunk(init.uploadId, index, chunk, () => {
        task.percent = Math.round(((index + 1) / init.chunkCount) * 100)
      })
    }

    task.phase = 'assembling'
    task.message = '分片提交完毕 · 等待 SSE 装配回执'
    await service().completeChunked(init.uploadId)
  } catch (error) {
    task.phase = 'failed'
    task.error = error instanceof Error ? error.message : '上传失败'
  }
}

function phaseTag(task: UploadTask): { label: string; type: 'info' | 'warning' | 'success' | 'danger' | 'primary' } {
  switch (task.phase) {
    case 'uploading':
      return { label: task.mode === 'sync' ? '直传中' : '分片上传中', type: 'warning' }
    case 'assembling':
      return { label: '装配中', type: 'primary' }
    case 'success':
      return { label: '成功', type: 'success' }
    default:
      return { label: '失败', type: 'danger' }
  }
}
</script>

<template>
  <div class="file-uploader">
    <div
      class="file-uploader__drop"
      :class="{ 'file-uploader__drop--over': dragOver }"
      @click="pickFiles"
      @dragover.prevent="dragOver = true"
      @dragleave="dragOver = false"
      @drop.prevent="onDrop"
    >
      <AppIcon name="UploadFilled" :size="28" />
      <p class="file-uploader__drop-text">点击或拖拽文件到此处上传</p>
      <p class="file-uploader__drop-hint">
        ≤ {{ formatBytes(SYNC_MODE_LIMIT_BYTES) }} 自动直传（SYNC）；超过自动分片（ASYNC）
      </p>
    </div>
    <input ref="fileInput" type="file" multiple class="file-uploader__input" @change="onFilesChosen" />

    <label class="file-uploader__force">
      <el-checkbox v-model="forceAsync">强制分片上传（ASYNC）</el-checkbox>
    </label>

    <div v-if="tasks.length" class="file-uploader__list">
      <div v-for="task in tasks" :key="task.id" class="file-task">
        <div class="file-task__row">
          <AppIcon name="Document" :size="16" />
          <span class="file-task__name" :title="task.fileName">{{ task.fileName }}</span>
          <span class="file-task__size">{{ formatBytes(task.sizeBytes) }}</span>
          <el-tag size="small" :type="task.mode === 'sync' ? 'info' : 'warning'">
            {{ task.mode === 'sync' ? 'SYNC' : 'ASYNC' }}
          </el-tag>
          <el-tag size="small" :type="phaseTag(task).type">{{ phaseTag(task).label }}</el-tag>
        </div>
        <el-progress
          :percentage="task.percent"
          :status="task.phase === 'failed' ? 'exception' : task.phase === 'success' ? 'success' : undefined"
          :stroke-width="6"
        />
        <p class="file-task__message">
          <template v-if="task.error">✕ {{ task.error }}</template>
          <template v-else>{{ task.message }}</template>
        </p>
        <div v-if="task.result" class="file-task__card">
          <span class="file-task__card-item">file_id：<code>{{ task.result.fileId }}</code></span>
          <span class="file-task__card-item">SHA-256：<code :title="task.result.hash">{{ task.result.hash.slice(0, 20) }}…</code></span>
          <span v-if="task.result.dedupHit" class="file-task__card-item file-task__card-item--dedup">
            秒传命中 · 引用计数 {{ task.result.refCount }}
          </span>
        </div>
      </div>
    </div>
  </div>
</template>

<style scoped>
.file-uploader {
  background: var(--qlx-surface);
  border: 1px solid var(--qlx-line);
  border-radius: var(--qlx-radius-lg);
  padding: var(--qlx-space-4);
  display: grid;
  gap: var(--qlx-space-3);
}

.file-uploader__drop {
  border: 1px dashed var(--qlx-ink-300);
  border-radius: var(--qlx-radius-md);
  padding: var(--qlx-space-8) var(--qlx-space-4);
  text-align: center;
  cursor: pointer;
  color: var(--qlx-ink-500);
  background: var(--qlx-canvas);
  transition: border-color var(--qlx-duration-fast) var(--qlx-ease-out),
    background var(--qlx-duration-fast) var(--qlx-ease-out);
}

.file-uploader__drop--over {
  border-color: var(--qlx-brand-500);
  background: var(--qlx-brand-50);
}

.file-uploader__drop-text {
  margin: var(--qlx-space-2) 0 var(--qlx-space-1);
  font-size: var(--qlx-text-base);
  color: var(--qlx-ink-700);
}

.file-uploader__drop-hint {
  margin: 0;
  font-size: var(--qlx-text-xs);
  color: var(--qlx-ink-400);
}

.file-uploader__input {
  display: none;
}

.file-uploader__force {
  display: inline-flex;
}

.file-uploader__list {
  display: grid;
  gap: var(--qlx-space-3);
}

.file-task {
  border: 1px solid var(--qlx-line);
  border-radius: var(--qlx-radius-md);
  padding: var(--qlx-space-3);
  display: grid;
  gap: var(--qlx-space-2);
}

.file-task__row {
  display: flex;
  align-items: center;
  gap: var(--qlx-space-2);
  flex-wrap: wrap;
}

.file-task__name {
  font-size: var(--qlx-text-sm);
  color: var(--qlx-ink-800);
  max-width: 280px;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.file-task__size {
  font-size: var(--qlx-text-xs);
  color: var(--qlx-ink-400);
  font-family: var(--qlx-font-numeric);
}

.file-task__message {
  margin: 0;
  font-size: var(--qlx-text-xs);
  color: var(--qlx-ink-500);
}

.file-task__card {
  display: flex;
  flex-wrap: wrap;
  gap: var(--qlx-space-2) var(--qlx-space-4);
  background: var(--qlx-success-soft);
  border-radius: var(--qlx-radius-sm);
  padding: var(--qlx-space-2) var(--qlx-space-3);
}

.file-task__card-item {
  font-size: var(--qlx-text-xs);
  color: var(--qlx-ink-700);
}

.file-task__card-item code {
  font-family: var(--qlx-font-mono);
  color: var(--qlx-brand-700);
}

.file-task__card-item--dedup {
  color: var(--qlx-gold-600);
  font-weight: 600;
}
</style>
