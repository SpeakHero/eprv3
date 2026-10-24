<!--
  所属公司：四川淇利馨集团有限公司
  描述：文件预览——图片 / PDF 内嵌；OFD / Office 占位跳转（预览接口预留）
-->
<script setup lang="ts">
import { computed, ref } from 'vue'
import AppIcon from '@/components/AppIcon.vue'
import { formatBytes } from './types'
import type { FileReferenceInfo } from './types'

const props = defineProps<{
  file: FileReferenceInfo
  /** 预览地址（默认走 SYNC 流式下载端点；mock 场景由服务注入）。 */
  previewUrl?: string
}>()

const visible = ref(false)

type PreviewKind = 'image' | 'pdf' | 'office' | 'unknown'

const kind = computed<PreviewKind>(() => {
  const type = props.file.contentType.toLowerCase()
  if (type.startsWith('image/')) {
    return 'image'
  }
  if (type === 'application/pdf') {
    return 'pdf'
  }
  if (
    type.includes('officedocument') ||
    type.includes('msword') ||
    type.includes('ofd') ||
    /\.(docx?|xlsx?|pptx?|ofd)$/i.test(props.file.fileName)
  ) {
    return 'office'
  }
  return 'unknown'
})

const url = computed(() => props.previewUrl ?? `/api/support/files/${encodeURIComponent(props.file.fileId)}/content`)

const kindLabel = computed(
  () => ({ image: '图片预览', pdf: 'PDF 预览', office: 'Office / OFD 文档', unknown: '二进制文件' })[kind.value],
)
</script>

<template>
  <span class="file-preview">
    <el-button size="small" text type="primary" @click="visible = true">
      <AppIcon name="View" :size="14" /> 预览
    </el-button>

    <el-dialog v-model="visible" :title="file.fileName" width="720px" destroy-on-close>
      <div class="file-preview__meta">
        <span>{{ kindLabel }}</span>
        <span>{{ formatBytes(file.sizeBytes) }}</span>
        <span class="file-preview__hash" :title="file.hash">SHA-256：{{ file.hash.slice(0, 24) }}…</span>
      </div>

      <!-- 图片内嵌 -->
      <div v-if="kind === 'image'" class="file-preview__body">
        <img :src="url" :alt="file.fileName" class="file-preview__image" />
      </div>

      <!-- PDF 内嵌 -->
      <iframe
        v-else-if="kind === 'pdf'"
        :src="url"
        class="file-preview__iframe"
        :title="file.fileName"
      />

      <!-- OFD / Office 占位跳转（接口预留：接入在线预览服务后替换） -->
      <div v-else class="file-preview__placeholder">
        <AppIcon name="Warning" :size="24" />
        <p>该类型暂不支持内嵌预览（{{ kindLabel }}）。</p>
        <el-button type="primary" size="small" tag="a" :href="url" target="_blank">打开原文件</el-button>
      </div>
    </el-dialog>
  </span>
</template>

<style scoped>
.file-preview__meta {
  display: flex;
  gap: var(--qlx-space-4);
  font-size: var(--qlx-text-xs);
  color: var(--qlx-ink-500);
  margin-bottom: var(--qlx-space-3);
}

.file-preview__hash {
  font-family: var(--qlx-font-mono);
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.file-preview__body {
  display: flex;
  justify-content: center;
  background: var(--qlx-canvas);
  border-radius: var(--qlx-radius-sm);
  padding: var(--qlx-space-3);
}

.file-preview__image {
  max-width: 100%;
  max-height: 60vh;
  object-fit: contain;
}

.file-preview__iframe {
  width: 100%;
  height: 60vh;
  border: 1px solid var(--qlx-line);
  border-radius: var(--qlx-radius-sm);
}

.file-preview__placeholder {
  display: grid;
  justify-items: center;
  gap: var(--qlx-space-2);
  padding: var(--qlx-space-10) var(--qlx-space-4);
  background: var(--qlx-canvas);
  border-radius: var(--qlx-radius-sm);
  color: var(--qlx-ink-500);
}

.file-preview__placeholder p {
  margin: 0;
  font-size: var(--qlx-text-sm);
}
</style>
