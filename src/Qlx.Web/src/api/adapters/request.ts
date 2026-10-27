/*
 * 所属公司：四川淇利馨集团有限公司
 * 描述：全局统一请求层——ApiResponse 信封解包 + ApiError 错误元数据规范化（全站唯一入口，禁止各自复制）
 * 说明：
 *   1. 统一走 core/http.ts 共享实例（Token 注入 / 401 续期由拦截器承载），禁止私有 axios 实例；
 *      OpenIddict 端点（/connect/*）不经 ApiResponse 包装，portal.ts 认证流程用 unwrapApiResponse 纯函数复用解包逻辑。
 *   2. 错误一律抛 ApiError：message 透传后端文案，code / HTTP status / fieldErrors（字段级校验错误）全量保留，
 *      供页面按 code 分支处理或表单字段旁展示；超时/断网归一为可读文案（TIMEOUT / NETWORK_ERROR）。
 *   3. 对齐后端 Qlx.BuildingBlocks.Api.ApiResponse<T> 契约：
 *      成功 { success:true, message?, data, timestamp }；
 *      失败 { success:false, code, message, errors?, timestamp }（400/422 带 errors）。
 */

import axios, { type AxiosRequestConfig } from 'axios'
import { http } from '@/core/http'

/** 字段级校验错误（对齐后端 FieldError；400/422 校验失败时随 errors 返回）。 */
export interface FieldError {
  fieldName: string
  errorMessage: string
}

/** 后端统一响应包装（对齐 BuildingBlocks ApiResponse<T>）。 */
export interface ApiResponse<T> {
  success: boolean
  code?: string | null
  message?: string | null
  data?: T
  errors?: FieldError[] | null
  timestamp?: string
}

/**
 * 业务请求异常：message 为可读消息（透传后端）；code 为后端业务错误码（如 VALIDATION_ERROR / CONFLICT 自定义码）；
 * status 为 HTTP 状态码；fieldErrors 仅在 400/422 校验失败时携带，供表单字段旁展示。
 */
export class ApiError extends Error {
  readonly code?: string
  readonly status?: number
  readonly fieldErrors?: FieldError[]

  constructor(message: string, code?: string, status?: number, fieldErrors?: FieldError[]) {
    super(message)
    this.name = 'ApiError'
    this.code = code
    this.status = status
    this.fieldErrors = fieldErrors
  }
}

/** 解包 ApiResponse 信封：success=false 抛 ApiError（纯函数，不依赖共享 http 实例）。 */
export function unwrapApiResponse<T>(body: ApiResponse<T>): T {
  if (!body.success) {
    throw new ApiError(body.message ?? body.code ?? '请求失败', body.code ?? undefined, undefined, body.errors ?? undefined)
  }
  return body.data as T
}

/** 任意异常归一为 ApiError：后端 message 优先；超时/断网/无信封裸错误归一为可读文案。 */
export function toApiError(error: unknown): ApiError {
  if (error instanceof ApiError) {
    return error
  }
  if (axios.isAxiosError(error)) {
    const body = error.response?.data as ApiResponse<unknown> | undefined
    const message = body?.message ?? body?.code
    if (typeof message === 'string' && message.length > 0) {
      return new ApiError(message, body?.code ?? undefined, error.response?.status, body?.errors ?? undefined)
    }
    if (error.code === 'ECONNABORTED') {
      return new ApiError('请求超时，请稍后重试', 'TIMEOUT')
    }
    if (!error.response) {
      return new ApiError('无法连接服务，请确认后端服务已启动', 'NETWORK_ERROR')
    }
    return new ApiError(`请求失败：HTTP ${error.response.status}`, `HTTP_${error.response.status}`, error.response.status)
  }
  return new ApiError(error instanceof Error ? error.message : '请求失败')
}

/**
 * 统一请求：解包 ApiResponse.data；success=false 或 HTTP 错误时抛 ApiError（含 code/status/fieldErrors）。
 */
export async function apiRequest<T>(config: AxiosRequestConfig): Promise<T> {
  try {
    const response = await http.request<ApiResponse<T>>(config)
    return unwrapApiResponse(response.data)
  } catch (error) {
    throw toApiError(error)
  }
}

/** 文件下载结果：文件流 + 后端下发文件名（Content-Disposition）。 */
export interface BlobResult {
  blob: Blob
  fileName: string
}

/** 文件响应转 Blob 失败时后端仍返回 ApiResponse JSON，需从 Blob 中解出信封（非 Blob / 非 JSON 返回 null）。 */
export async function parseBlobApiResponse(data: unknown): Promise<ApiResponse<unknown> | null> {
  if (!(data instanceof Blob)) {
    return null
  }
  try {
    const body = JSON.parse(await data.text()) as ApiResponse<unknown>
    return typeof body?.success === 'boolean' ? body : null
  } catch {
    return null
  }
}

/**
 * 下载型请求：保留文件流与后端下发的文件名；失败时从 Blob 中解出后端 message/code 抛 ApiError。
 */
export async function apiRequestBlob(config: AxiosRequestConfig): Promise<BlobResult> {
  try {
    const response = await http.request<Blob>({ ...config, responseType: 'blob' })
    const disposition = String(response.headers?.['content-disposition'] ?? '')
    const utf8Match = /filename\*=UTF-8''([^;]+)/i.exec(disposition)
    const plainMatch = /filename="?([^";]+)"?/i.exec(disposition)
    const fileName = decodeURIComponent(utf8Match?.[1] ?? plainMatch?.[1] ?? 'download.xlsx')
    return { blob: response.data, fileName }
  } catch (error) {
    if (axios.isAxiosError(error)) {
      const body = await parseBlobApiResponse(error.response?.data)
      if (body) {
        throw new ApiError(
          body.message ?? body.code ?? '请求失败',
          body.code ?? undefined,
          error.response?.status,
          body.errors ?? undefined,
        )
      }
      throw toApiError(error)
    }
    throw error
  }
}
