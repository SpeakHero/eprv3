/*
 * 所属公司：四川淇利馨集团有限公司
 * 描述：审批操作区 mock 服务——实例状态机内存模拟（通过/驳回/加签/转办/撤回/催办）
 * 说明：请求 / 响应字段形态与 Qlx.Abstractions.Approvals 契约一致；真实实现见 approval-api.ts。
 */

import type {
  AddSignerRequest,
  ApprovalInstanceInfo,
  ApprovalServiceApi,
  ApprovalTaskInfo,
  ApprovalUserOption,
  ApproveRequest,
  RejectRequest,
  TransferRequest,
  UrgeRequest,
  WithdrawRequest,
} from './types'

const MOCK_DELAY_MS = 120

function delay<T>(value: T): Promise<T> {
  return new Promise((resolve) => window.setTimeout(() => resolve(value), MOCK_DELAY_MS))
}

function nowIso(): string {
  return new Date().toISOString()
}

/** 候选人（userId 为雪花 Id 字符串形态）。 */
export const mockApprovalUsers: ApprovalUserOption[] = [
  { userId: '7351012345678901001', userName: '张启航', departmentName: '财务部' },
  { userId: '7351012345678901002', userName: '李稳', departmentName: '采购部' },
  { userId: '7351012345678901003', userName: '王守正', departmentName: '总经理办公室' },
  { userId: '7351012345678901004', userName: '赵清源', departmentName: '法务部' },
]

/** 当前操作人（演示固定为节点二审批人）。 */
export const MOCK_CURRENT_USER_ID = '7351012345678901001'
export const MOCK_INITIATOR_USER_ID = '7351012345678901002'

/** 内存实例（深拷贝下发，动作后重建）。 */
function seedInstance(): ApprovalInstanceInfo {
  return {
    instanceId: 'apr-7352012345678901234',
    approvalDefinitionCode: 'pur_order_above_50k',
    bizType: 'PurchaseOrder',
    bizId: '7852012345678905678',
    bizNo: 'PO-2026-1018-003',
    title: '采购订单 PO-2026-1018-003 审批（金额 68,000 元）',
    initiatorUserId: MOCK_INITIATOR_USER_ID,
    status: 'Pending',
    startedAt: '2026-10-21T09:12:00+08:00',
    completedAt: null,
    tasks: [
      {
        taskId: 'task-7353012345678902001',
        instanceId: 'apr-7352012345678901234',
        nodeCode: 'node_manager',
        nodeName: '部门经理审批',
        assigneeUserId: MOCK_INITIATOR_USER_ID,
        status: 'Approved',
        createdAt: '2026-10-21T09:12:00+08:00',
        completedAt: '2026-10-21T10:05:00+08:00',
        comment: '预算内，同意。',
      },
      {
        taskId: 'task-7353012345678902002',
        instanceId: 'apr-7352012345678901234',
        nodeCode: 'node_finance',
        nodeName: '财务复核',
        assigneeUserId: MOCK_CURRENT_USER_ID,
        status: 'Pending',
        createdAt: '2026-10-21T10:05:00+08:00',
        completedAt: null,
        comment: null,
      },
      {
        taskId: 'task-7353012345678902003',
        instanceId: 'apr-7352012345678901234',
        nodeCode: 'node_gm',
        nodeName: '总经理审批',
        assigneeUserId: '7351012345678901003',
        status: 'Pending',
        createdAt: '2026-10-21T10:05:00+08:00',
        completedAt: null,
        comment: null,
      },
    ],
  }
}

let instanceState = seedInstance()

function clone(): ApprovalInstanceInfo {
  return {
    ...instanceState,
    tasks: instanceState.tasks.map((task) => ({ ...task })),
  }
}

function userMap(): Map<string, ApprovalUserOption> {
  return new Map(mockApprovalUsers.map((user) => [user.userId, user]))
}

function requireTask(taskId: string): ApprovalTaskInfo {
  const task = instanceState.tasks.find((item) => item.taskId === taskId)
  if (!task) {
    throw new Error(`审批任务不存在：${taskId}`)
  }
  return task
}

function requirePendingTask(taskId: string): ApprovalTaskInfo {
  const task = requireTask(taskId)
  if (task.status !== 'Pending') {
    throw new Error(`任务「${task.nodeName}」当前状态为 ${task.status}，不允许该操作`)
  }
  return task
}

/** 通过：当前任务 Approved；全部节点通过则实例 Approved。 */
function doApprove(request: ApproveRequest): void {
  const task = requirePendingTask(currentPendingOf(request.approverUserId))
  if (task.assigneeUserId !== request.approverUserId) {
    throw new Error('当前用户不是该任务的审批人')
  }
  task.status = 'Approved'
  task.completedAt = nowIso()
  task.comment = request.comment?.trim() || task.comment
  if (instanceState.tasks.every((item) => item.status === 'Approved')) {
    instanceState.status = 'Approved'
    instanceState.completedAt = nowIso()
  }
}

/** 当前用户在流的 Pending 任务（按创建序）。 */
function currentPendingOf(userId: string): string {
  const task = instanceState.tasks.find((item) => item.status === 'Pending' && item.assigneeUserId === userId)
  if (!task) {
    throw new Error('当前用户没有待处理的审批任务')
  }
  return task.taskId
}

/** 驳回：当前任务 Rejected，实例 Rejected（后续 Pending 节点取消）。 */
function doReject(request: RejectRequest): void {
  const taskId = currentPendingOf(request.approverUserId)
  const task = requirePendingTask(taskId)
  task.status = 'Rejected'
  task.completedAt = nowIso()
  task.comment = request.reason
  instanceState.tasks.forEach((item) => {
    if (item.status === 'Pending' && item.taskId !== taskId) {
      item.status = 'Canceled'
    }
  })
  instanceState.status = 'Rejected'
  instanceState.completedAt = nowIso()
}

/** 撤回：发起人动作；实例 Withdrawn，全部 Pending 节点取消。 */
function doWithdraw(request: WithdrawRequest): void {
  if (instanceState.initiatorUserId !== request.initiatorUserId) {
    throw new Error('仅发起人可撤回审批')
  }
  if (instanceState.status !== 'Pending') {
    throw new Error('当前实例状态不允许撤回')
  }
  instanceState.tasks.forEach((item) => {
    if (item.status === 'Pending') {
      item.status = 'Canceled'
      item.completedAt = nowIso()
    }
  })
  instanceState.status = 'Withdrawn'
  instanceState.completedAt = nowIso()
}

/** 加签：在当前任务前插入加签人任务，原任务标记 AddSigned，加签人先审。 */
function doAddSigner(request: AddSignerRequest): void {
  const task = requirePendingTask(request.taskId)
  if (task.assigneeUserId !== request.approverUserId) {
    throw new Error('当前用户不是该任务的审批人')
  }
  const userMapLocal = userMap()
  if (!userMapLocal.has(request.addSignerUserId)) {
    throw new Error('加签人不在候选人清单中')
  }
  task.status = 'AddSigned'
  const signerTask: ApprovalTaskInfo = {
    taskId: `task-73530123456789${String(instanceState.tasks.length + 10).padStart(4, '0')}`,
    instanceId: instanceState.instanceId,
    nodeCode: `${task.nodeCode}_addsign`,
    nodeName: `${task.nodeName}（加签：${userMapLocal.get(request.addSignerUserId)?.userName ?? request.addSignerUserId}）`,
    assigneeUserId: request.addSignerUserId,
    status: 'Pending',
    createdAt: nowIso(),
    completedAt: null,
    comment: request.reason ?? null,
  }
  const index = instanceState.tasks.indexOf(task)
  instanceState.tasks.splice(request.isBeforeCurrent === false ? index + 1 : index, 0, signerTask)
}

/** 转办：当前任务 Transferred，新任务落到被转办人。 */
function doTransfer(request: TransferRequest): void {
  const task = requirePendingTask(request.taskId)
  if (task.assigneeUserId !== request.fromUserId) {
    throw new Error('当前用户不是该任务的审批人')
  }
  const userMapLocal = userMap()
  if (!userMapLocal.has(request.toUserId)) {
    throw new Error('被转办人不在候选人清单中')
  }
  task.status = 'Transferred'
  task.completedAt = nowIso()
  task.comment = request.reason ?? task.comment
  const transferred: ApprovalTaskInfo = {
    taskId: `task-73530123456789${String(instanceState.tasks.length + 10).padStart(4, '0')}`,
    instanceId: instanceState.instanceId,
    nodeCode: task.nodeCode,
    nodeName: `${task.nodeName}（转自 ${userMapLocal.get(request.fromUserId)?.userName ?? request.fromUserId}）`,
    assigneeUserId: request.toUserId,
    status: 'Pending',
    createdAt: nowIso(),
    completedAt: null,
    comment: request.reason ?? null,
  }
  const index = instanceState.tasks.indexOf(task)
  instanceState.tasks.splice(index + 1, 0, transferred)
}

/** Mock 审批服务（动作先执行状态机（同步抛业务错误），再延迟返回最新实例）。 */
export const mockApprovalService: ApprovalServiceApi = {
  async getInstance() {
    return delay(clone())
  },
  async approve(request: ApproveRequest) {
    doApprove(request)
    return delay(clone())
  },
  async reject(request: RejectRequest) {
    doReject(request)
    return delay(clone())
  },
  async withdraw(request: WithdrawRequest) {
    doWithdraw(request)
    return delay(clone())
  },
  async addSigner(request: AddSignerRequest) {
    doAddSigner(request)
    return delay(clone())
  },
  async transfer(request: TransferRequest) {
    doTransfer(request)
    return delay(clone())
  },
  async urge(request: UrgeRequest) {
    await delay(undefined)
    // 真实实现：SendTemplateRequest 走消息中心催办；此处留痕即可
    void request
  },
  async listUsers() {
    return delay(mockApprovalUsers.map((user) => ({ ...user })))
  },
}

/** 重置 mock 实例（演示页重放用）。 */
export function resetMockApprovalInstance(): ApprovalInstanceInfo {
  instanceState = seedInstance()
  return clone()
}
