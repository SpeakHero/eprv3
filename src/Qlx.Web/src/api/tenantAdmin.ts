/*
 * 所属公司：四川淇利馨集团有限公司
 * 描述：租户自管理域 API 封装（过渡壳，T7 删除）
 * 说明：
 *   1. 组织 / 岗位 / 成员 / 角色实现已全部迁入适配层 api/adapters/{orgAdmin,memberAdmin,roleAdmin}.ts，
 *      页面不再消费本文件（T4 起）；此处仅 re-export 适配器工厂保持过渡期兼容。
 *   2. mock 占位（resendMemberInvite / importTenantMembers / togglePositionStatus）已删除，
 *      真实实现见适配层（B-5 / B-6 / B-7 端点）。
 *   3. fetchTenantModules 暂保留本文件（TenantModuleView 仍消费，模块卡片域未纳入三模块重构范围）。
 */

import axios, { type AxiosRequestConfig } from 'axios'
import { http } from '@/core/http'
import type { TenantModuleCard } from '@/types/tenant'

export { createTenantOrgAdminAdapter } from './adapters/orgAdmin'
export { createTenantMemberAdminAdapter } from './adapters/memberAdmin'
export { createTenantRoleAdminAdapter, createPortalRoleAdminAdapter } from './adapters/roleAdmin'

interface ApiResponse<T> {
  success: boolean
  code?: string | null
  message?: string | null
  data?: T
}

interface BackendModuleCard {
  code: string
  name: string
  description: string
  icon?: string | null
  version?: string | null
  licensed: boolean
  licensedAt?: string | null
}

/** 通用请求：错误消息透传后端 message；超时/断网归一为可读文案。 */
async function request<T>(config: AxiosRequestConfig): Promise<T> {
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

/** 已授权模块卡片（TenantModuleView 消费）。 */
export function fetchTenantModules(): Promise<TenantModuleCard[]> {
  // 后端 TenantModuleRow 不含 features 字段，version 可空——前端补默认值
  return request<BackendModuleCard[]>({
    method: 'GET',
    url: '/api/tenant/modules',
  }).then((items) =>
    items.map((m) => ({
      code: m.code,
      name: m.name,
      description: m.description,
      version: m.version ?? '',
      features: [],
      licensed: m.licensed,
      licensedAt: m.licensedAt ?? null,
    })),
  )
}
