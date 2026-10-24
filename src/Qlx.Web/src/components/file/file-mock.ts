/*
 * 所属公司：四川淇利馨集团有限公司
 * 描述：文件上传 mock 服务——SYNC 直传（含秒传）/ ASYNC 分片 + SSE 装配事件，全流程内存模拟
 * 说明：事件经 getMockSseBus 注入（TASK_PROGRESS / TASK_COMPLETED，task_code=file.chunk.assemble），
 *       与 SSE 客户端演示共用同一事件总线，联调形态与真实后端一致。
 */

import { getMockSseBus } from '@/core/sse'
import type {
  ChunkInitRequest,
  ChunkInitResponse,
  FileReferenceInfo,
  FileUploadService,
} from './types'
import { DEFAULT_CHUNK_SIZE } from './types'

/** 模拟网络延迟基数（ms）。 */
const MOCK_LATENCY_MS = 60

function sleep(ms: number): Promise<void> {
  return new Promise((resolve) => window.setTimeout(resolve, ms))
}

/** 伪 SHA-256（演示用；真实环境由后端流式计算并返回 hex）。 */
function mockSha256(file: { name: string; size: number; lastModified: number }): string {
  let hash = 0x811c9dc5
  const basis = `${file.name}:${file.size}:${file.lastModified}`
  for (let index = 0; index < basis.length; index++) {
    hash ^= basis.charCodeAt(index)
    hash = Math.imul(hash, 0x01000193) >>> 0
  }
  return `3a7f${hash.toString(16).padStart(8, '0')}${file.size.toString(16).padStart(12, '0')}c95d01e6b2a4f8`
}

/** 内存文件库（hash → 引用），模拟秒传与引用计数。 */
const fileStore = new Map<string, FileReferenceInfo & { refCount: number }>()
let mockSeq = 9000

function nextId(prefix: string): string {
  mockSeq += 1
  return `${prefix}-${String(mockSeq).padStart(6, '0')}`
}

function storeReference(reference: FileReferenceInfo): FileReferenceInfo {
  const existing = fileStore.get(reference.hash)
  if (existing) {
    existing.refCount += 1
    return { ...existing, dedupHit: true }
  }
  const stored = { ...reference, refCount: 1 }
  fileStore.set(reference.hash, stored)
  return { ...stored, dedupHit: false }
}

/** SYNC 直传（≤100MB）：XHR progress 模拟 + 秒传检测。 */
async function mockUploadSync(
  file: File,
  options: { bizType?: string | null; bizId?: string | null; onProgress: (percent: number) => void },
): Promise<FileReferenceInfo> {
  const steps = 10
  for (let step = 1; step <= steps; step++) {
    await sleep(MOCK_LATENCY_MS)
    options.onProgress(Math.round((step / steps) * 100))
  }
  const hash = mockSha256(file)
  const stored = storeReference({
    fileId: nextId('fil'),
    storageKey: `tenant-shared/2026/10/${hash}.bin`,
    fileName: file.name,
    contentType: file.type || 'application/octet-stream',
    sizeBytes: file.size,
    hash,
    uploadedAt: new Date().toISOString(),
    bizType: options.bizType ?? null,
    bizId: options.bizId ?? null,
  })
  return stored
}

/** ASYNC 状态（uploadId → 分片上下文）。 */
interface ChunkContext {
  request: ChunkInitRequest
  receivedChunks: Set<number>
  executionId: string
}

const chunkContexts = new Map<string, ChunkContext>()

async function mockInitChunked(request: ChunkInitRequest): Promise<ChunkInitResponse> {
  await sleep(MOCK_LATENCY_MS * 2)
  const chunkSize = request.chunkSize || DEFAULT_CHUNK_SIZE
  const chunkCount = Math.max(1, Math.ceil(request.sizeBytes / chunkSize))
  const uploadId = nextId('upl')
  const executionId = nextId('exec')
  chunkContexts.set(uploadId, { request: { ...request, chunkSize }, receivedChunks: new Set(), executionId })
  return { uploadId, executionId, chunkSize, chunkCount }
}

async function mockUploadChunk(
  uploadId: string,
  index: number,
  _chunk: Blob,
  onProgress: (percent: number) => void,
): Promise<void> {
  const context = chunkContexts.get(uploadId)
  if (!context) {
    throw new Error(`分片会话不存在：${uploadId}`)
  }
  await sleep(MOCK_LATENCY_MS)
  context.receivedChunks.add(index)
  onProgress(100)
}

/** 装配完成（后端 Hangfire 任务 file.chunk.assemble）：SSE 推 TASK_PROGRESS → TASK_COMPLETED。 */
async function mockCompleteChunked(uploadId: string): Promise<void> {
  const context = chunkContexts.get(uploadId)
  if (!context) {
    throw new Error(`分片会话不存在：${uploadId}`)
  }
  const bus = getMockSseBus()
  const total = context.request.sizeBytes
  const hash = mockSha256({
    name: context.request.fileName,
    size: total,
    lastModified: uploadId.length,
  })

  // 模拟后端装配进度：30 / 70 / 100
  const percents = [30, 70, 100]
  for (const percent of percents) {
    await sleep(500)
    bus.emit('TaskProgress', {
      executionId: context.executionId,
      taskCode: 'file.chunk.assemble',
      percent,
      message: percent === 100 ? '装配完成' : '分片装配中',
    })
  }

  const reference = storeReference({
    fileId: nextId('fil'),
    storageKey: `tenant-shared/2026/10/${hash}.bin`,
    fileName: context.request.fileName,
    contentType: context.request.contentType,
    sizeBytes: total,
    hash,
    uploadedAt: new Date().toISOString(),
    bizType: context.request.bizType ?? null,
    bizId: context.request.bizId ?? null,
  })

  await sleep(200)
  // TASK_COMPLETED：data.result 含 fileId + 元数据 + ACL（对齐 §4.25 验收 6）
  bus.emit('TaskCompleted', {
    executionId: context.executionId,
    taskCode: 'file.chunk.assemble',
    result: { ...reference },
  })
  chunkContexts.delete(uploadId)
}

/** Mock 上传服务（演示 / 无后端联调）。 */
export const mockFileUploadService: FileUploadService = {
  uploadSync: mockUploadSync,
  initChunked: mockInitChunked,
  uploadChunk: mockUploadChunk,
  completeChunked: mockCompleteChunked,
  previewUrl: (fileId) => `#/file-preview/${encodeURIComponent(fileId)}`,
}
