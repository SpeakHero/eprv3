/*
 * 所属公司：四川淇利馨集团有限公司
 * 描述：审批操作区真实 API 实现（后端联调用；端点按 SupportApprovalService 预留）
 * 说明：与 mockApprovalService 同形（ApprovalServiceApi），响应统一解包 ApiResponse<T>。
 */

import axios from 'axios'
import { http } from '@/core/http'
import type {
  AddSignerRequest,
  ApprovalInstanceInfo,
  ApprovalServiceApi,
  ApprovalUserOption,
  ApproveRequest,
  RejectRequest,
  TransferRequest,
  UrgeRequest,
  WithdrawRequest,
} from './types'

const API_BASE = '/api/support/approvals'

/** 统一响应包装（对齐 ApiResponse<T>）。 */
interface ApiResponse<T> {
  success: boolean
  code?: string | null
  message?: string | null
  data?: T
}

async function request<T>(config: { method: 'get' | 'post'; url: string; data?: unknown }): Promise<T> {
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
      if (typeof body?.message === 'string' && body.message) {
        throw new Error(body.message)
      }
    }
    throw error
  }
}

/** 真实审批服务（待后端 SupportApprovalService 联调）。 */
export const realApprovalService: ApprovalServiceApi = {
  getInstance: (instanceId) =>
    request<ApprovalInstanceInfo>({ method: 'get', url: `${API_BASE}/${encodeURIComponent(instanceId)}` }),
  approve: (payload: ApproveRequest) =>
    request<ApprovalInstanceInfo>({ method: 'post', url: `${API_BASE}/approve`, data: payload }),
  reject: (payload: RejectRequest) =>
    request<ApprovalInstanceInfo>({ method: 'post', url: `${API_BASE}/reject`, data: payload }),
  withdraw: (payload: WithdrawRequest) =>
    request<ApprovalInstanceInfo>({ method: 'post', url: `${API_BASE}/withdraw`, data: payload }),
  addSigner: (payload: AddSignerRequest) =>
    request<ApprovalInstanceInfo>({ method: 'post', url: `${API_BASE}/add-signer`, data: payload }),
  transfer: (payload: TransferRequest) =>
    request<ApprovalInstanceInfo>({ method: 'post', url: `${API_BASE}/transfer`, data: payload }),
  urge: (payload: UrgeRequest) =>
    request<void>({ method: 'post', url: `${API_BASE}/urge`, data: payload }),
  listUsers: () =>
    request<ApprovalUserOption[]>({ method: 'get', url: `${API_BASE}/candidates` }),
}
