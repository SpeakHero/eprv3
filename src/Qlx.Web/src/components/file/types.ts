/*
 * 所属公司：四川淇利馨集团有限公司
 * 描述：文件上传 / 预览类型定义——对齐 Qlx.Abstractions/Files/FileReference 与阶段二方案 §4.24（D22 + S18）
 * 说明：字段名对齐后端 CamelCase JSON 序列化（QlxJsonDefaults）；FileId / 雪花 Id 一律 string。
 */

/** 文件引用（上传返回 / 文件卡片）。 */
export interface FileReferenceInfo {
  fileId: string
  storageKey: string
  fileName: string
  contentType: string
  sizeBytes: number
  /** SHA-256（hex）。 */
  hash: string
  uploadedAt: string
  bizType?: string | null
  bizId?: string | null
  /** 秒传命中时的引用计数（含本次）。 */
  refCount?: number
  /** 是否秒传命中（hash 重复）。 */
  dedupHit?: boolean
}

/** 分片上传初始化请求。 */
export interface ChunkInitRequest {
  fileName: string
  contentType: string
  sizeBytes: number
  chunkSize: number
  bizType?: string | null
  bizId?: string | null
}

/** 分片上传初始化响应（executionId 供 SSE 订阅装配任务）。 */
export interface ChunkInitResponse {
  uploadId: string
  executionId: string
  chunkSize: number
  chunkCount: number
}

/** 上传模式：SYNC ≤100MB 直传；ASYNC 分片。 */
export type UploadMode = 'sync' | 'async'

/** 上传阶段。 */
export type UploadPhase =
  | 'pending' // 待上传
  | 'uploading' // 传输中（SYNC 直传 / ASYNC 分片）
  | 'assembling' // ASYNC 后端装配中（等待 SSE TASK_COMPLETED）
  | 'success'
  | 'failed'

/** 上传任务（文件列表项）。 */
export interface UploadTask {
  id: string
  fileName: string
  sizeBytes: number
  contentType?: string
  mode: UploadMode
  phase: UploadPhase
  /** 0~100。SYNC 来自 XHR progress；ASYNC 来自 SSE TASK_PROGRESS(percent)。 */
  percent: number
  message?: string
  /** ASYNC 模式：分片装配任务执行 ID（SSE 事件按此路由）。 */
  executionId?: string
  result?: FileReferenceInfo
  error?: string
}

/** SYNC 直传大小阈值：100MB（>100MB 自动走 ASYNC）。 */
export const SYNC_MODE_LIMIT_BYTES = 100 * 1024 * 1024

/** ASYNC 分片默认大小：8MB。 */
export const DEFAULT_CHUNK_SIZE = 8 * 1024 * 1024

/** 上传服务接口（mock 与真实实现同形，业务侧可注入切换）。 */
export interface FileUploadService {
  /** SYNC 直传：POST /api/support/files/upload，返回 file_id + 元数据 + SHA-256 + ACL 计算结果。 */
  uploadSync(
    file: File,
    options: { bizType?: string | null; bizId?: string | null; onProgress: (percent: number) => void },
  ): Promise<FileReferenceInfo>

  /** ASYNC 初始化：POST /api/support/files/chunks → uploadId + executionId。 */
  initChunked(request: ChunkInitRequest): Promise<ChunkInitResponse>

  /** ASYNC 分片提交。 */
  uploadChunk(uploadId: string, index: number, chunk: Blob, onProgress: (percent: number) => void): Promise<void>

  /** ASYNC 提交装配任务（后续进度 / 完成经 SSE TASK_PROGRESS / TASK_COMPLETED 推送）。 */
  completeChunked(uploadId: string): Promise<void>

  /** 预览 / 下载地址（SYNC 流式下载）。 */
  previewUrl(fileId: string): string
}

/** 格式化字节数（演示与卡片文案）。 */
export function formatBytes(bytes: number): string {
  if (bytes < 1024) {
    return `${bytes} B`
  }
  if (bytes < 1024 * 1024) {
    return `${(bytes / 1024).toFixed(1)} KB`
  }
  if (bytes < 1024 * 1024 * 1024) {
    return `${(bytes / 1024 / 1024).toFixed(1)} MB`
  }
  return `${(bytes / 1024 / 1024 / 1024).toFixed(2)} GB`
}
