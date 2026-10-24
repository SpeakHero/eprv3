/*
 * 所属公司：四川淇利馨集团有限公司
 * 描述：审批操作区类型定义——对齐 Qlx.Abstractions/Approvals 契约（请求 / 详情 / 状态）
 * 说明：Id（instanceId / taskId / userId）一律 string（雪花 Id 精度红线）；
 *       状态用契约枚举名承载（真实 API 层做数值 ↔ 名称映射）。
 */

/** 审批实例状态（ApprovalStatus）。 */
export type ApprovalStatus =
  | 'Draft'
  | 'Pending'
  | 'Approved'
  | 'Rejected'
  | 'Withdrawn'
  | 'Terminated'
  | 'Canceled'

/** 审批任务状态（ApprovalTaskStatus）。 */
export type ApprovalTaskStatus =
  | 'Pending'
  | 'Approved'
  | 'Rejected'
  | 'Transferred'
  | 'AddSigned'
  | 'Canceled'

/** 审批任务（ApprovalTaskInfo）。 */
export interface ApprovalTaskInfo {
  taskId: string
  instanceId: string
  nodeCode: string
  nodeName: string
  assigneeUserId: string
  status: ApprovalTaskStatus
  createdAt: string
  completedAt?: string | null
  comment?: string | null
}

/** 审批实例详情（ApprovalInstanceInfo）。 */
export interface ApprovalInstanceInfo {
  instanceId: string
  approvalDefinitionCode: string
  bizType: string
  bizId: string
  bizNo: string
  title: string
  initiatorUserId: string
  status: ApprovalStatus
  startedAt: string
  completedAt?: string | null
  tasks: ApprovalTaskInfo[]
}

/** 审批通过（ApproveRequest）。 */
export interface ApproveRequest {
  instanceId: string
  approverUserId: string
  comment?: string | null
}

/** 审批驳回（RejectRequest，reason 必填）。 */
export interface RejectRequest {
  instanceId: string
  approverUserId: string
  reason: string
}

/** 撤回（WithdrawRequest，发起人操作）。 */
export interface WithdrawRequest {
  instanceId: string
  initiatorUserId: string
  reason?: string | null
}

/** 加签（AddSignerRequest：加签人须先审批，原审批人才能继续）。 */
export interface AddSignerRequest {
  instanceId: string
  taskId: string
  approverUserId: string
  addSignerUserId: string
  reason?: string | null
  isBeforeCurrent?: boolean
}

/** 转办（TransferRequest）。 */
export interface TransferRequest {
  instanceId: string
  taskId: string
  fromUserId: string
  toUserId: string
  reason?: string | null
}

/** 催办（UrgeRequest）。 */
export interface UrgeRequest {
  instanceId: string
  requestorUserId: string
  message?: string | null
}

/** 审批服务接口（mock / 真实同形）。 */
export interface ApprovalServiceApi {
  getInstance(instanceId: string): Promise<ApprovalInstanceInfo>
  approve(request: ApproveRequest): Promise<ApprovalInstanceInfo>
  reject(request: RejectRequest): Promise<ApprovalInstanceInfo>
  withdraw(request: WithdrawRequest): Promise<ApprovalInstanceInfo>
  addSigner(request: AddSignerRequest): Promise<ApprovalInstanceInfo>
  transfer(request: TransferRequest): Promise<ApprovalInstanceInfo>
  urge(request: UrgeRequest): Promise<void>
  /** 候选人清单（加签 / 转办选人）。 */
  listUsers(): Promise<ApprovalUserOption[]>
}

/** 审批候选人。 */
export interface ApprovalUserOption {
  userId: string
  userName: string
  departmentName?: string
}

/** 实例状态 → 中文标签。 */
export const APPROVAL_STATUS_LABELS: Record<ApprovalStatus, string> = {
  Draft: '草稿',
  Pending: '审批中',
  Approved: '已通过',
  Rejected: '已驳回',
  Withdrawn: '已撤回',
  Terminated: '已终止',
  Canceled: '已取消',
}

/** 任务状态 → 中文标签。 */
export const APPROVAL_TASK_STATUS_LABELS: Record<ApprovalTaskStatus, string> = {
  Pending: '待处理',
  Approved: '已通过',
  Rejected: '已驳回',
  Transferred: '已转办',
  AddSigned: '已加签',
  Canceled: '已取消',
}
