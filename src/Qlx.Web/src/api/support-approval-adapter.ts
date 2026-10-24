/*
 * 所属公司：四川淇利馨集团有限公司
 * 描述：审批操作区组件适配层——将后端整数枚举状态转换为组件字符串状态
 * 说明：后端 ApprovalStatus / ApprovalTaskStatus 序列化为整数；
 *       ApprovalActionBar 组件使用字符串状态（'Pending'/'Approved'/...）；
 *       本适配器实现组件 ApprovalServiceApi 接口，内部调用真实 API 并做双向转换。
 */

import {
  addSignApproval,
  approveApproval,
  fetchApprovalInstance,
  rejectApproval,
  transferApproval,
  urgeApproval,
  withdrawApproval,
} from '@/api/support'
import type { ApprovalInstanceInfo, ApprovalTaskInfo } from '@/components/approval/types'
import type {
  AddSignerRequest,
  ApproveRequest,
  ApprovalServiceApi,
  ApprovalUserOption,
  RejectRequest,
  TransferRequest,
  UrgeRequest,
  WithdrawRequest,
} from '@/components/approval/types'
import type {
  ApprovalInstanceInfo as BackendInstance,
  ApprovalTaskInfo as BackendTask,
  ApprovalStatusCode,
  ApprovalTaskStatusCode,
} from '@/types/support'

/** 后端整数状态 → 组件字符串状态。 */
const INSTANCE_STATUS_MAP: Record<ApprovalStatusCode, ApprovalInstanceInfo['status']> = {
  0: 'Draft',
  1: 'Pending',
  2: 'Approved',
  3: 'Rejected',
  4: 'Withdrawn',
  5: 'Terminated',
  6: 'Canceled',
}

const TASK_STATUS_MAP: Record<ApprovalTaskStatusCode, ApprovalTaskInfo['status']> = {
  0: 'Pending',
  1: 'Approved',
  2: 'Rejected',
  3: 'Transferred',
  4: 'AddSigned',
  5: 'Canceled',
}

/** 转换单条任务。 */
function convertTask(task: BackendTask): ApprovalTaskInfo {
  return {
    taskId: task.taskId,
    instanceId: task.instanceId,
    nodeCode: task.nodeCode,
    nodeName: task.nodeName,
    assigneeUserId: task.assigneeUserId,
    status: TASK_STATUS_MAP[task.status] ?? 'Pending',
    createdAt: task.createdAt,
    completedAt: task.completedAt,
    comment: task.comment,
  }
}

/** 转换实例详情。 */
export function convertInstance(instance: BackendInstance): ApprovalInstanceInfo {
  return {
    instanceId: instance.instanceId,
    approvalDefinitionCode: instance.approvalDefinitionCode,
    bizType: instance.bizType,
    bizId: instance.bizId,
    bizNo: instance.bizNo,
    title: instance.title,
    initiatorUserId: instance.initiatorUserId,
    status: INSTANCE_STATUS_MAP[instance.status] ?? 'Pending',
    startedAt: instance.startedAt,
    completedAt: instance.completedAt,
    tasks: instance.tasks.map(convertTask),
  }
}

/**
 * 真实审批服务适配器（供 ApprovalActionBar 注入）。
 * 组件期望字符串状态，后端返回整数，本适配器做转换。
 */
export const realApprovalServiceAdapter: ApprovalServiceApi = {
  getInstance: async (instanceId: string): Promise<ApprovalInstanceInfo> => {
    const backend = await fetchApprovalInstance(instanceId)
    return convertInstance(backend)
  },

  approve: async (request: ApproveRequest): Promise<ApprovalInstanceInfo> => {
    await approveApproval(request.instanceId, request.comment)
    const backend = await fetchApprovalInstance(request.instanceId)
    return convertInstance(backend)
  },

  reject: async (request: RejectRequest): Promise<ApprovalInstanceInfo> => {
    await rejectApproval(request.instanceId, request.reason)
    const backend = await fetchApprovalInstance(request.instanceId)
    return convertInstance(backend)
  },

  withdraw: async (request: WithdrawRequest): Promise<ApprovalInstanceInfo> => {
    await withdrawApproval(request.instanceId, request.reason)
    const backend = await fetchApprovalInstance(request.instanceId)
    return convertInstance(backend)
  },

  addSigner: async (request: AddSignerRequest): Promise<ApprovalInstanceInfo> => {
    await addSignApproval(
      request.instanceId,
      request.taskId,
      request.addSignerUserId,
      request.reason,
      request.isBeforeCurrent,
    )
    const backend = await fetchApprovalInstance(request.instanceId)
    return convertInstance(backend)
  },

  transfer: async (request: TransferRequest): Promise<ApprovalInstanceInfo> => {
    await transferApproval(request.instanceId, request.taskId, request.toUserId, request.reason)
    const backend = await fetchApprovalInstance(request.instanceId)
    return convertInstance(backend)
  },

  urge: async (request: UrgeRequest): Promise<void> => {
    await urgeApproval(request.instanceId, request.message)
  },

  listUsers: async (): Promise<ApprovalUserOption[]> => {
    // 候选人清单复用租户成员查询；当前阶段无专门端点，返回空数组
    // （加签 / 转办选人由组件内部使用，后续接入 /api/tenant/members 候选接口）
    return []
  },
}
