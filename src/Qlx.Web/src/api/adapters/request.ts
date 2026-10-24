/*
 * 所属公司：四川淇利馨集团有限公司
 * 描述：适配层公共请求助手——统一 ApiResponse 解包 + 错误消息归一（三模块适配器共用，禁止各自复制）
 * 说明：
 *   1. 统一走 core/http.ts 共享实例（Token 注入 / 401 续期由拦截器承载），禁止私有 axios 实例。
 *   2. 错误消息一律透传后端 message；超时/断网归一为可读文案（沿用原 tenantAdmin.ts request 做法）。
 *   3. 仅服务适配层内部，不对外暴露给页面/组件。
 */

import axios, { type AxiosRequestConfig } from 'axios'
import { http } from '@/core/http'

/** 后端统一响应包装（对齐 BuildingBlocks ApiResponse<T>）。 */
export interface ApiResponse<T> {
  success: boolean
  code?: string | null
  message?: string | null
  data?: T
}

/**
 * 统一请求：解包 ApiResponse.data；success=false 或 HTTP 错误时抛出后端 message。
 */
export async function apiRequest<T>(config: AxiosRequestConfig): Promise<T> {
  try {
    const response = await http.request<ApiResponse<T>>(config)
    const body = response.data
    if (!body.success) {
      throw new Error(body.message ?? body.code ?? '请求失败')
    }
    return body.data as T
  } catch (error) {
    if (axios.isAxiosError(error)) {
      const body = error.response?.data as ApiResponse<unknown> | undefined
      const message = body?.message ?? body?.code
      if (typeof message === 'string' && message.length > 0) {
        throw new Error(message)
      }
      if (error.code === 'ECONNABORTED') {
        throw new Error('请求超时，请稍后重试')
      }
      if (!error.response) {
        throw new Error('无法连接服务，请确认后端服务已启动')
      }
    }
    throw error
  }
}
