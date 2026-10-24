/*
 * 所属公司：四川淇利馨集团有限公司
 * 描述：支撑功能域统一 API 封装（审批 / 消息 / 任务 / 文件 / 打印）
 * 说明：
 *   1. 全部使用 core/http.ts 共享客户端（自动注入租户 Token + 401 静默续期）；
 *   2. 响应统一解包 ApiResponse<T>；业务失败 / 网络异常抛出可读消息；
 *   3. 端点对齐 Qlx.Modules.Support 各 Controller（路径 / 方法 / 参数）；
 *   4. 所有 Id 参数声明为 string（Hashids 混淆 / 雪花精度）。
 */

import axios from 'axios'
import { http } from '@/core/http'
import { loadSession } from '@/core/session'
import type {
  ApiResponse,
  ApprovalDelegateInfo,
  ApprovalInstanceInfo,
  ApprovalTaskInfo,
  ApprovalUserOption,
  CreateDelegateRequest,
  JobDefinitionItem,
  JobExecutionItem,
  JobExecutionLogEntry,
  JobSubmitResult,
  JobTriggerPayload,
  MessageInfo,
  PrintTemplateCreatePayload,
  PrintTemplateUpdatePayload,
  PrintTemplateView,
  SupportFileMetadata,
  SupportFileUploadResult,
  SupportFlowSummary,
  SupportInboxItem,
  SupportPage,
  SupportSendLogItem,
  SupportSubscriptionItem,
} from '@/types/support'

/** 发起请求并解包 ApiResponse；业务失败 / 网络异常统一抛出可读消息。 */
async function request<T>(config: {
  method: 'get' | 'post' | 'put' | 'delete'
  url: string
  data?: unknown
  params?: Record<string, unknown>
}): Promise<T> {
  try {
    const response = await http.request<ApiResponse<T>>({
      method: config.method,
      url: config.url,
      data: config.data,
      params: config.params,
    })
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

/* ============================================================
 * 审批中心（/api/support/approvals）
 * ============================================================ */

const APPROVALS = '/api/support/approvals'

/** 当前用户待办列表。 */
export function fetchApprovalTodos(): Promise<ApprovalTaskInfo[]> {
  return request<ApprovalTaskInfo[]>({ method: 'get', url: `${APPROVALS}/my-todos` })
}

/** 当前用户已办列表。 */
export function fetchApprovalDone(): Promise<ApprovalTaskInfo[]> {
  return request<ApprovalTaskInfo[]>({ method: 'get', url: `${APPROVALS}/my-done` })
}

/** 当前用户发起的审批列表。 */
export function fetchApprovalInitiated(): Promise<ApprovalInstanceInfo[]> {
  return request<ApprovalInstanceInfo[]>({ method: 'get', url: `${APPROVALS}/my-initiated` })
}

/** 审批实例详情。 */
export function fetchApprovalInstance(instanceId: string): Promise<ApprovalInstanceInfo> {
  return request<ApprovalInstanceInfo>({ method: 'get', url: `${APPROVALS}/instance/${encodeURIComponent(instanceId)}` })
}

/** 流程定义摘要列表。 */
export function fetchApprovalFlows(): Promise<SupportFlowSummary[]> {
  return request<SupportFlowSummary[]>({ method: 'get', url: `${APPROVALS}/flows` })
}

/** 审批通过。 */
export function approveApproval(instanceId: string, comment?: string | null): Promise<void> {
  return request<void>({ method: 'post', url: `${APPROVALS}/${encodeURIComponent(instanceId)}/approve`, data: { comment } })
}

/** 审批驳回。 */
export function rejectApproval(instanceId: string, reason: string): Promise<void> {
  return request<void>({ method: 'post', url: `${APPROVALS}/${encodeURIComponent(instanceId)}/reject`, data: { reason } })
}

/** 发起人撤回。 */
export function withdrawApproval(instanceId: string, reason?: string | null): Promise<void> {
  return request<void>({ method: 'post', url: `${APPROVALS}/${encodeURIComponent(instanceId)}/withdraw`, data: { reason } })
}

/** 催办。 */
export function urgeApproval(instanceId: string, message?: string | null): Promise<void> {
  return request<void>({ method: 'post', url: `${APPROVALS}/${encodeURIComponent(instanceId)}/urge`, data: { message } })
}

/** 加签。 */
export function addSignApproval(
  instanceId: string,
  taskId: string,
  addSignerUserId: string,
  reason?: string | null,
  isBeforeCurrent = true,
): Promise<void> {
  return request<void>({
    method: 'post',
    url: `${APPROVALS}/${encodeURIComponent(instanceId)}/tasks/${encodeURIComponent(taskId)}/add-sign`,
    data: { addSignerUserId, reason, isBeforeCurrent },
  })
}

/** 转办。 */
export function transferApproval(
  instanceId: string,
  taskId: string,
  toUserId: string,
  reason?: string | null,
): Promise<void> {
  return request<void>({
    method: 'post',
    url: `${APPROVALS}/${encodeURIComponent(instanceId)}/tasks/${encodeURIComponent(taskId)}/transfer`,
    data: { toUserId, reason },
  })
}

/** 我的委托列表。 */
export function fetchApprovalDelegates(): Promise<ApprovalDelegateInfo[]> {
  return request<ApprovalDelegateInfo[]>({ method: 'get', url: `${APPROVALS}/delegates` })
}

/** 创建委托。 */
export function createApprovalDelegate(payload: CreateDelegateRequest): Promise<string> {
  return request<string>({ method: 'post', url: `${APPROVALS}/delegates`, data: payload })
}

/** 撤销委托。 */
export function revokeApprovalDelegate(delegateId: string): Promise<void> {
  return request<void>({ method: 'post', url: `${APPROVALS}/delegates/${encodeURIComponent(delegateId)}/revoke` })
}

/* ============================================================
 * 信息中心（/api/support/messages）
 * ============================================================ */

const MESSAGES = '/api/support/messages'

/** 收件箱分页。 */
export function fetchMessageInbox(params: {
  pageIndex: number
  pageSize: number
  keyword?: string | null
  unreadOnly?: boolean
}): Promise<SupportPage<SupportInboxItem>> {
  return request<SupportPage<SupportInboxItem>>({ method: 'get', url: `${MESSAGES}/inbox`, params })
}

/** 已发送分页。 */
export function fetchMessageSent(params: {
  pageIndex: number
  pageSize: number
  keyword?: string | null
}): Promise<SupportPage<MessageInfo>> {
  return request<SupportPage<MessageInfo>>({ method: 'get', url: `${MESSAGES}/sent`, params })
}

/** 未读数。 */
export function fetchUnreadCount(): Promise<number> {
  return request<number>({ method: 'get', url: `${MESSAGES}/unread-count` })
}

/** 标记单条已读。 */
export function markMessageRead(inboxId: string): Promise<void> {
  return request<void>({ method: 'post', url: `${MESSAGES}/inbox/${encodeURIComponent(inboxId)}/read` })
}

/** 全部已读。 */
export function markAllMessagesRead(): Promise<void> {
  return request<void>({ method: 'post', url: `${MESSAGES}/read-all` })
}

/** 删除收件箱项。 */
export function deleteInboxItem(inboxId: string): Promise<void> {
  return request<void>({ method: 'post', url: `${MESSAGES}/inbox/${encodeURIComponent(inboxId)}/delete` })
}

/** 记录全局项状态（READ / ACCEPTED / DELETED）。 */
export function markGlobalItemState(globalId: string, state: string): Promise<void> {
  return request<void>({ method: 'post', url: `${MESSAGES}/global/${encodeURIComponent(globalId)}/state`, data: { state } })
}

/** 订阅清单。 */
export function fetchSubscriptions(): Promise<SupportSubscriptionItem[]> {
  return request<SupportSubscriptionItem[]>({ method: 'get', url: `${MESSAGES}/subscriptions` })
}

/** 切换订阅开关。 */
export function toggleSubscription(msgType: string, enabled: boolean): Promise<void> {
  return request<void>({
    method: 'post',
    url: `${MESSAGES}/subscriptions/${encodeURIComponent(msgType)}/toggle`,
    data: { enabled },
  })
}

/** 渠道发送记录。 */
export function fetchMessageSendLogs(messageId: string): Promise<SupportSendLogItem[]> {
  return request<SupportSendLogItem[]>({ method: 'get', url: `${MESSAGES}/sent/${encodeURIComponent(messageId)}/send-logs` })
}

/** 人工重试投递。 */
export function retryMessageDelivery(messageId: string): Promise<void> {
  return request<void>({ method: 'post', url: `${MESSAGES}/sent/${encodeURIComponent(messageId)}/retry` })
}

/* ============================================================
 * 后台任务中心（/api/support/jobs）
 * ============================================================ */

const JOBS = '/api/support/jobs'

/** 触发任务。 */
export function triggerJob(payload: JobTriggerPayload): Promise<JobSubmitResult> {
  return request<JobSubmitResult>({ method: 'post', url: `${JOBS}/trigger`, data: payload })
}

/** 取消任务。 */
export function cancelJob(executionId: string): Promise<void> {
  return request<void>({ method: 'post', url: `${JOBS}/${encodeURIComponent(executionId)}/cancel` })
}

/** 查询任务状态。 */
export function fetchJobStatus(executionId: string): Promise<JobSubmitResult> {
  return request<JobSubmitResult>({ method: 'get', url: `${JOBS}/${encodeURIComponent(executionId)}` })
}

/** 执行历史分页。 */
export function fetchJobHistory(params: {
  taskCode?: string | null
  status?: string | null
  from?: string | null
  to?: string | null
  pageIndex: number
  pageSize: number
}): Promise<SupportPage<JobExecutionItem>> {
  return request<SupportPage<JobExecutionItem>>({ method: 'get', url: `${JOBS}/history`, params })
}

/** 执行日志。 */
export function fetchJobLogs(executionId: string): Promise<JobExecutionLogEntry[]> {
  return request<JobExecutionLogEntry[]>({ method: 'get', url: `${JOBS}/${encodeURIComponent(executionId)}/logs` })
}

/** 任务定义清单。 */
export function fetchJobDefinitions(): Promise<JobDefinitionItem[]> {
  return request<JobDefinitionItem[]>({ method: 'get', url: `${JOBS}/definitions` })
}

/** 启停任务定义。 */
export function toggleJobDefinition(taskCode: string, enabled: boolean): Promise<void> {
  return request<void>({
    method: 'post',
    url: `${JOBS}/definitions/${encodeURIComponent(taskCode)}/toggle`,
    data: { enabled },
  })
}

/* ============================================================
 * 文件中心（/api/support/files）
 * ============================================================ */

const FILES = '/api/support/files'

/** 查询文件元数据。 */
export function fetchFileMetadata(id: string): Promise<SupportFileMetadata> {
  return request<SupportFileMetadata>({ method: 'get', url: `${FILES}/metadata/${encodeURIComponent(id)}` })
}

/** 目录列表。 */
export function fetchFileList(parentId?: string | null, keyword?: string | null): Promise<SupportFileMetadata[]> {
  const params: Record<string, unknown> = {}
  if (parentId) params.parentId = parentId
  if (keyword) params.keyword = keyword
  return request<SupportFileMetadata[]>({ method: 'get', url: `${FILES}/list`, params })
}

/** 回收站列表。 */
export function fetchRecycleList(): Promise<SupportFileMetadata[]> {
  return request<SupportFileMetadata[]>({ method: 'get', url: `${FILES}/recycle/list` })
}

/** 软删进回收站。 */
export function deleteFile(id: string): Promise<SupportFileMetadata> {
  return request<SupportFileMetadata>({ method: 'delete', url: `${FILES}/${encodeURIComponent(id)}` })
}

/** 从回收站还原。 */
export function restoreFile(id: string): Promise<SupportFileMetadata> {
  return request<SupportFileMetadata>({ method: 'post', url: `${FILES}/${encodeURIComponent(id)}/restore` })
}

/** 上传文件（XHR，支持 progress）。 */
export async function uploadFile(
  file: File,
  options: { bizModule?: string | null; docType?: string | null; onProgress?: (percent: number) => void } = {},
): Promise<SupportFileUploadResult> {
  const formData = new FormData()
  formData.append('file', file)
  if (options.bizModule) formData.append('bizModule', options.bizModule)
  if (options.docType) formData.append('docType', options.docType)

  return new Promise<SupportFileUploadResult>((resolve, reject) => {
    const xhr = new XMLHttpRequest()
    const token = loadSession().tenantToken
    xhr.open('POST', `${import.meta.env.VITE_API_BASE_URL ?? ''}${FILES}/upload`)
    if (token) xhr.setRequestHeader('Authorization', `Bearer ${token}`)
    xhr.upload.onprogress = (event) => {
      if (event.lengthComputable && options.onProgress) {
        options.onProgress(Math.round((event.loaded / event.total) * 100))
      }
    }
    xhr.onload = () => {
      if (xhr.status < 200 || xhr.status >= 300) {
        reject(new Error(`上传失败：HTTP ${xhr.status}`))
        return
      }
      try {
        const body = JSON.parse(xhr.responseText) as ApiResponse<SupportFileUploadResult>
        if (!body.success) {
          reject(new Error(body.message ?? '上传失败'))
          return
        }
        resolve(body.data as SupportFileUploadResult)
      } catch {
        reject(new Error('上传响应不是合法 JSON'))
      }
    }
    xhr.onerror = () => reject(new Error('网络异常，上传中断'))
    xhr.send(formData)
  })
}

/* ============================================================
 * 打印模板（/api/support/printing/templates）
 * ============================================================ */

const PRINTING = '/api/support/printing/templates'

/** 模板清单。 */
export function fetchPrintTemplates(): Promise<PrintTemplateView[]> {
  return request<PrintTemplateView[]>({ method: 'get', url: PRINTING })
}

/** 按编码查最新版本。 */
export function fetchPrintTemplateByCode(code: string): Promise<PrintTemplateView | null> {
  return request<PrintTemplateView | null>({ method: 'get', url: `${PRINTING}/code/${encodeURIComponent(code)}` })
}

/** 新建模板。 */
export function createPrintTemplate(payload: PrintTemplateCreatePayload): Promise<PrintTemplateView> {
  return request<PrintTemplateView>({ method: 'post', url: PRINTING, data: payload })
}

/** 更新模板。 */
export function updatePrintTemplate(id: string, payload: PrintTemplateUpdatePayload): Promise<PrintTemplateView> {
  return request<PrintTemplateView>({ method: 'put', url: `${PRINTING}/${encodeURIComponent(id)}`, data: payload })
}

/** 启停模板。 */
export function setPrintTemplateStatus(id: string, status: number): Promise<void> {
  return request<void>({ method: 'post', url: `${PRINTING}/${encodeURIComponent(id)}/status`, data: { status } })
}

/** 渲染模板为 HTML。 */
export function renderPrintTemplate(code: string, variables: Record<string, unknown>): Promise<string> {
  return request<string>({ method: 'post', url: `${PRINTING}/code/${encodeURIComponent(code)}/render`, data: variables })
}

/* ============================================================
 * 审批候选人（加签 / 转办选人——复用租户成员查询接口）
 * ============================================================ */

export type { ApprovalUserOption }
