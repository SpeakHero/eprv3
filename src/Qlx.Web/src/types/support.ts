/*
 * 所属公司：四川淇利馨集团有限公司
 * 描述：支撑功能域（审批 / 消息 / 任务 / 文件 / 打印）统一类型定义
 * 说明：对齐 Qlx.Modules.Support 各 Service 契约与 Controller 端点；
 *       Id（雪花 long）一律 string（Hashids 混淆 / 2^53-1 精度红线）；
 *       C# 枚举在 JSON 中默认序列化为整数，前端统一以 number 承载并提供标签映射。
 */

/* ============================================================
 * 通用
 * ============================================================ */

/** 分页结果（对齐后端各 Service 的分页契约，字段名按实际返回）。 */
export interface SupportPage<T> {
  items: T[]
  total: number
  pageIndex: number
  pageSize: number
}

/* ============================================================
 * 审批中心（/api/support/approvals）
 * ============================================================ */

/** 审批实例状态（ApprovalStatus 枚举整数）。 */
export type ApprovalStatusCode = 0 | 1 | 2 | 3 | 4 | 5 | 6

/** 审批任务状态（ApprovalTaskStatus 枚举整数）。 */
export type ApprovalTaskStatusCode = 0 | 1 | 2 | 3 | 4 | 5

/** 审批任务（ApprovalTaskInfo）。 */
export interface ApprovalTaskInfo {
  taskId: string
  instanceId: string
  nodeCode: string
  nodeName: string
  assigneeUserId: string
  status: ApprovalTaskStatusCode
  createdAt: string
  completedAt: string | null
  comment: string | null
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
  status: ApprovalStatusCode
  startedAt: string
  completedAt: string | null
  tasks: ApprovalTaskInfo[]
}

/** 审批委托（ApprovalDelegateInfo）。 */
export interface ApprovalDelegateInfo {
  delegateId: string
  delegatorUserId: string
  delegateeUserId: string
  startAt: string
  endAt: string
  reason: string | null
}

/** 流程定义摘要（SupportFlowSummary）。 */
export interface SupportFlowSummary {
  defCode: string
  defName: string
  bizType: string
  version: number
  status: string
  orgScopeJson: string
  nodeCount: number
  branchCount: number
  updatedAt: string
}

/** 创建委托请求（SupportCreateDelegateRequest）。 */
export interface CreateDelegateRequest {
  agentUserId: string
  bizTypes: string[] | null
  startAt: string
  endAt: string
  reason: string | null
}

/** 审批候选人。 */
export interface ApprovalUserOption {
  userId: string
  userName: string
  departmentName: string | null
}

/* ============================================================
 * 信息中心（/api/support/messages）
 * ============================================================ */

/** 消息渠道（MessageChannel 枚举整数）。 */
export type MessageChannelCode = 0 | 1 | 2 | 3

/** 消息优先级（MessagePriority 枚举整数）。 */
export type MessagePriorityCode = 0 | 1 | 2

/** 消息状态（MessageStatus 枚举整数）。 */
export type MessageStatusCode = 0 | 1 | 2 | 3 | 4 | 5 | 6

/** 发送日志状态（MessageSendLogStatus 枚举整数）。 */
export type MessageSendLogStatusCode = 0 | 1 | 2

/** 收件箱合并项（SupportInboxItem）。 */
export interface SupportInboxItem {
  messageId: string
  inboxId: string | null
  msgType: string
  subject: string
  content: string
  priority: MessagePriorityCode
  status: MessageStatusCode
  senderUserId: string
  createdAt: string
  readAt: string | null
  bizType: string | null
  bizId: string | null
  bizNo: string | null
  isGlobal: boolean
  forceAccept: boolean
}

/** 已发送消息项（MessageInfo）。 */
export interface MessageInfo {
  messageId: string
  inboxId: string | null
  subject: string
  content: string
  channel: MessageChannelCode
  status: MessageStatusCode
  priority: MessagePriorityCode
  senderUserId: string
  recipientUserId: string
  createdAt: string
  readAt: string | null
  bizRef: { bizType: string; bizId: string; bizNo: string | null } | null
}

/** 渠道发送记录（SupportSendLogItem）。 */
export interface SupportSendLogItem {
  messageId: string
  channel: MessageChannelCode
  recipient: string
  status: MessageSendLogStatusCode
  failReason: string | null
  retryCount: number
  sentAt: string | null
  createdAt: string
}

/** 订阅配置项（SupportSubscriptionItem）。 */
export interface SupportSubscriptionItem {
  msgType: string
  channelsMask: number
  locked: boolean
  enabled: boolean
}

/* ============================================================
 * 后台任务中心（/api/support/jobs）
 * ============================================================ */

/** 任务执行状态（JobExecutionStatus，后端以字符串返回）。 */
export type JobStatusName =
  | 'Pending'
  | 'Queued'
  | 'Running'
  | 'Success'
  | 'Failed'
  | 'Timeout'
  | 'Canceled'

/** 任务提交结果（JobSubmitResult）。 */
export interface JobSubmitResult {
  executionId: string
  mode: 'sync' | 'async'
  status: JobStatusName
  createdAt: string
  completedResult: {
    resultJson: unknown
    stdout: string | null
    stderr: string | null
    executionDurationMs: number
    isSuccess: boolean
    errorMessage: string | null
  } | null
}

/** 任务执行历史项（JobExecutionItem）。 */
export interface JobExecutionItem {
  executionId: string
  taskCode: string
  mode: string
  status: JobStatusName
  retryCount: number
  triggerSource: string
  initiatorUserId: string | null
  queuedAt: string
  startedAt: string | null
  finishedAt: string | null
  durationMs: number | null
  error: string | null
}

/** 任务定义项（JobDefinitionItem）。 */
export interface JobDefinitionItem {
  taskCode: string
  name: string
  moduleCode: string
  defaultCron: string
  cronOverride: string | null
  effectiveCron: string
  defaultMode: string
  enabled: boolean
  paused: boolean
  timeoutSeconds: number
  nextRunAt: string | null
}

/** 任务日志条目（JobExecutionLogEntry）。 */
export interface JobExecutionLogEntry {
  executionId: string
  logAt: string
  level: 'Debug' | 'Info' | 'Warning' | 'Error'
  message: string
  detail: string | null
  stackTrace: string | null
}

/** 触发任务入参。 */
export interface JobTriggerPayload {
  taskCode: string
  mode?: 'sync' | 'async' | null
  parameters?: Record<string, unknown> | null
  idempotencyKey?: string | null
  timeoutSeconds?: number | null
}

/* ============================================================
 * 文件中心（/api/support/files）
 * ============================================================ */

/** 文件上传结果（SupportFileUploadResult）。 */
export interface SupportFileUploadResult {
  fileId: string
  storageKey: string
  fileName: string
  contentType: string
  sizeBytes: number
  hash: string
  path: string
  bizDomain: string
  deduplicated: boolean
  warning: string | null
}

/** 文件元数据（SupportFileMetadata）。 */
export interface SupportFileMetadata {
  fileId: string
  fileName: string
  contentType: string
  sizeBytes: number
  hash: string | null
  path: string
  bizDomain: string
  currentVersion: number
  ownerId: string
  status: string
  isProtected: boolean
  recycledAt: string | null
}

/* ============================================================
 * 打印模板（/api/support/printing/templates）
 * ============================================================ */

/** 打印模板状态（PrintTemplateStatus 枚举整数）。 */
export type PrintTemplateStatusCode = 0 | 1 | 2

/** 打印模板视图（PrintTemplateView）。 */
export interface PrintTemplateView {
  id: string
  templateCode: string
  name: string
  bizType: string
  contentHtml: string
  css: string | null
  status: PrintTemplateStatusCode
  version: number
  createdAt: string
  updatedAt: string
}

/** 创建打印模板入参。 */
export interface PrintTemplateCreatePayload {
  templateCode: string
  name: string
  bizType: string
  contentHtml: string
  css: string | null
}

/** 更新打印模板入参。 */
export interface PrintTemplateUpdatePayload {
  name: string
  bizType: string
  contentHtml: string
  css: string | null
}

/* ============================================================
 * 枚举标签映射（供列表 / 详情展示复用）
 * ============================================================ */

export const APPROVAL_STATUS_LABELS: Record<ApprovalStatusCode, string> = {
  0: '草稿',
  1: '审批中',
  2: '已通过',
  3: '已驳回',
  4: '已撤回',
  5: '已终止',
  6: '已取消',
}

export const APPROVAL_STATUS_TYPE: Record<ApprovalStatusCode, string> = {
  0: 'info',
  1: 'warning',
  2: 'success',
  3: 'danger',
  4: 'info',
  5: 'info',
  6: 'info',
}

export const APPROVAL_TASK_STATUS_LABELS: Record<ApprovalTaskStatusCode, string> = {
  0: '待处理',
  1: '已通过',
  2: '已驳回',
  3: '已转办',
  4: '已加签',
  5: '已撤销',
}

export const APPROVAL_TASK_STATUS_TYPE: Record<ApprovalTaskStatusCode, string> = {
  0: 'warning',
  1: 'success',
  2: 'danger',
  3: 'info',
  4: 'info',
  5: 'info',
}

export const MESSAGE_CHANNEL_LABELS: Record<MessageChannelCode, string> = {
  0: '站内信',
  1: '邮件',
  2: '短信',
  3: 'App 推送',
}

export const MESSAGE_PRIORITY_LABELS: Record<MessagePriorityCode, string> = {
  0: '普通',
  1: '高',
  2: '紧急',
}

export const MESSAGE_PRIORITY_TYPE: Record<MessagePriorityCode, string> = {
  0: 'info',
  1: 'warning',
  2: 'danger',
}

export const MESSAGE_STATUS_LABELS: Record<MessageStatusCode, string> = {
  0: '草稿',
  1: '已入队',
  2: '已发送',
  3: '已送达',
  4: '已读',
  5: '已归档',
  6: '发送失败',
}

export const MESSAGE_SEND_LOG_STATUS_LABELS: Record<MessageSendLogStatusCode, string> = {
  0: '待发送',
  1: '成功',
  2: '失败',
}

export const JOB_STATUS_LABELS: Record<JobStatusName, string> = {
  Pending: '已登记',
  Queued: '已入队',
  Running: '执行中',
  Success: '成功',
  Failed: '失败',
  Timeout: '超时',
  Canceled: '已取消',
}

export const JOB_STATUS_TYPE: Record<JobStatusName, string> = {
  Pending: 'info',
  Queued: 'info',
  Running: 'warning',
  Success: 'success',
  Failed: 'danger',
  Timeout: 'danger',
  Canceled: 'info',
}

export const PRINT_TEMPLATE_STATUS_LABELS: Record<PrintTemplateStatusCode, string> = {
  0: '草稿',
  1: '启用',
  2: '停用',
}

export const PRINT_TEMPLATE_STATUS_TYPE: Record<PrintTemplateStatusCode, string> = {
  0: 'info',
  1: 'success',
  2: 'warning',
}
