/*
 * 所属公司：四川淇利馨集团有限公司
 * 描述：审批操作区真实 API 实现（后端联调用；端点按 SupportApprovalService 预留）
 * 说明：与 mockApprovalService 同形（ApprovalServiceApi），响应统一解包 ApiResponse<T>。
 */

import { apiRequest } from '@/api/adapters/request'
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

/** 真实审批服务（待后端 SupportApprovalService 联调）。 */
export const realApprovalService: ApprovalServiceApi = {
  getInstance: (instanceId) =>
    apiRequest<ApprovalInstanceInfo>({ method: 'get', url: `${API_BASE}/${encodeURIComponent(instanceId)}` }),
  approve: (payload: ApproveRequest) =>
    apiRequest<ApprovalInstanceInfo>({ method: 'post', url: `${API_BASE}/approve`, data: payload }),
  reject: (payload: RejectRequest) =>
    apiRequest<ApprovalInstanceInfo>({ method: 'post', url: `${API_BASE}/reject`, data: payload }),
  withdraw: (payload: WithdrawRequest) =>
    apiRequest<ApprovalInstanceInfo>({ method: 'post', url: `${API_BASE}/withdraw`, data: payload }),
  addSigner: (payload: AddSignerRequest) =>
    apiRequest<ApprovalInstanceInfo>({ method: 'post', url: `${API_BASE}/add-signer`, data: payload }),
  transfer: (payload: TransferRequest) =>
    apiRequest<ApprovalInstanceInfo>({ method: 'post', url: `${API_BASE}/transfer`, data: payload }),
  urge: (payload: UrgeRequest) =>
    apiRequest<void>({ method: 'post', url: `${API_BASE}/urge`, data: payload }),
  listUsers: () =>
    apiRequest<ApprovalUserOption[]>({ method: 'get', url: `${API_BASE}/candidates` }),
}
