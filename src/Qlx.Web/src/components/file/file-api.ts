/*
 * 所属公司：四川淇利馨集团有限公司
 * 描述：文件上传真实 API 实现（可切换真实 base URL；SYNC 走 XHR progress，ASYNC 走分片 + SSE）
 * 说明：当前后端联调端点以阶段二方案 §4.24 为准；字段同时兼容 file_id / fileId 两种返回形态。
 */

import { loadSession } from '@/core/session'
import type {
  ChunkInitRequest,
  FileReferenceInfo,
  FileUploadService,
} from './types'
import { DEFAULT_CHUNK_SIZE } from './types'

const API_BASE = '/api/support/files'

/** 兼容 fileId / file_id 的响应归一化。 */
export function normalizeFileReference(raw: Record<string, unknown>): FileReferenceInfo {
  const pick = (source: Record<string, unknown>, camel: string, snake: string): unknown =>
    source[camel] ?? source[snake]
  const result = raw.result as Record<string, unknown> | undefined
  const source = result ?? raw
  return {
    fileId: String(pick(source, 'fileId', 'file_id') ?? ''),
    storageKey: String(source.storageKey ?? source.storage_key ?? ''),
    fileName: String(source.fileName ?? source.file_name ?? ''),
    contentType: String(source.contentType ?? source.content_type ?? 'application/octet-stream'),
    sizeBytes: Number(source.sizeBytes ?? source.size_bytes ?? 0),
    hash: String(source.hash ?? source.sha256 ?? ''),
    uploadedAt: String(source.uploadedAt ?? source.uploaded_at ?? new Date().toISOString()),
    bizType: (source.bizType ?? source.biz_type ?? null) as string | null,
    bizId: (source.bizId ?? source.biz_id ?? null) as string | null,
    refCount: (source.refCount ?? source.ref_count ?? undefined) as number | undefined,
    dedupHit: Boolean(source.dedupHit ?? source.dedup_hit ?? false),
  }
}

/** 取租户 Token（SSE / 上传均需租户上下文）。 */
function tenantToken(): string {
  return loadSession().tenantToken
}

/** XHR 上传（progress 事件 → 百分比回调）。 */
function xhrUpload(
  url: string,
  formData: FormData,
  onProgress: (percent: number) => void,
): Promise<Record<string, unknown>> {
  return new Promise((resolve, reject) => {
    const xhr = new XMLHttpRequest()
    xhr.open('POST', url)
    const token = tenantToken()
    if (token) {
      xhr.setRequestHeader('Authorization', `Bearer ${token}`)
    }
    xhr.upload.onprogress = (event) => {
      if (event.lengthComputable) {
        onProgress(Math.round((event.loaded / event.total) * 100))
      }
    }
    xhr.onload = () => {
      if (xhr.status < 200 || xhr.status >= 300) {
        reject(new Error(`上传失败：HTTP ${xhr.status}`))
        return
      }
      try {
        resolve(JSON.parse(xhr.responseText) as Record<string, unknown>)
      } catch {
        reject(new Error('上传响应不是合法 JSON'))
      }
    }
    xhr.onerror = () => reject(new Error('网络异常，上传中断'))
    xhr.send(formData)
  })
}

/** 真实上传服务（后端联调用；结构化端点与 mock 同形）。 */
export const realFileUploadService: FileUploadService = {
  async uploadSync(file, options) {
    const formData = new FormData()
    formData.append('file', file)
    if (options.bizType) {
      formData.append('bizType', options.bizType)
    }
    if (options.bizId) {
      formData.append('bizId', options.bizId)
    }
    const body = await xhrUpload(`${API_BASE}/upload`, formData, options.onProgress)
    const payload = (body.data ?? body) as Record<string, unknown>
    return normalizeFileReference(payload)
  },

  async initChunked(request: ChunkInitRequest) {
    const token = tenantToken()
    const response = await fetch(`${API_BASE}/chunks`, {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
        ...(token ? { Authorization: `Bearer ${token}` } : {}),
      },
      body: JSON.stringify({ ...request, chunkSize: request.chunkSize ?? DEFAULT_CHUNK_SIZE }),
    })
    if (!response.ok) {
      throw new Error(`分片初始化失败：HTTP ${response.status}`)
    }
    const body = (await response.json()) as { data?: Record<string, unknown> }
    const payload = (body.data ?? {}) as Record<string, unknown>
    return {
      uploadId: String(payload.uploadId ?? payload.upload_id ?? ''),
      executionId: String(payload.executionId ?? payload.execution_id ?? ''),
      chunkSize: Number(payload.chunkSize ?? payload.chunk_size ?? request.chunkSize),
      chunkCount: Number(payload.chunkCount ?? payload.chunk_count ?? 0),
    }
  },

  async uploadChunk(uploadId, index, chunk, onProgress) {
    const formData = new FormData()
    formData.append('chunk', chunk)
    formData.append('index', String(index))
    await xhrUpload(`${API_BASE}/chunks/${encodeURIComponent(uploadId)}`, formData, onProgress)
  },

  async completeChunked(uploadId) {
    const token = tenantToken()
    const response = await fetch(`${API_BASE}/chunks/${encodeURIComponent(uploadId)}/complete`, {
      method: 'POST',
      headers: token ? { Authorization: `Bearer ${token}` } : undefined,
    })
    if (!response.ok) {
      throw new Error(`提交装配任务失败：HTTP ${response.status}`)
    }
  },

  previewUrl(fileId: string) {
    return `${API_BASE}/${encodeURIComponent(fileId)}/content`
  },
}
