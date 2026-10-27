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
import { ApiError, apiRequest, parseBlobApiResponse, type ApiResponse } from '@/api/adapters/request'
import type {
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

/* ============================================================
 * 审批中心（/api/support/approvals）
 * ============================================================ */

const APPROVALS = '/api/support/approvals'

/** 当前用户待办列表。 */
export function fetchApprovalTodos(): Promise<ApprovalTaskInfo[]> {
  return apiRequest<ApprovalTaskInfo[]>({ method: 'get', url: `${APPROVALS}/my-todos` })
}

/** 当前用户已办列表。 */
export function fetchApprovalDone(): Promise<ApprovalTaskInfo[]> {
  return apiRequest<ApprovalTaskInfo[]>({ method: 'get', url: `${APPROVALS}/my-done` })
}

/** 当前用户发起的审批列表。 */
export function fetchApprovalInitiated(): Promise<ApprovalInstanceInfo[]> {
  return apiRequest<ApprovalInstanceInfo[]>({ method: 'get', url: `${APPROVALS}/my-initiated` })
}

/** 审批实例详情。 */
export function fetchApprovalInstance(instanceId: string): Promise<ApprovalInstanceInfo> {
  return apiRequest<ApprovalInstanceInfo>({ method: 'get', url: `${APPROVALS}/instance/${encodeURIComponent(instanceId)}` })
}

/** 流程定义摘要列表。 */
export function fetchApprovalFlows(): Promise<SupportFlowSummary[]> {
  return apiRequest<SupportFlowSummary[]>({ method: 'get', url: `${APPROVALS}/flows` })
}

/** 审批通过。 */
export function approveApproval(instanceId: string, comment?: string | null): Promise<void> {
  return apiRequest<void>({ method: 'post', url: `${APPROVALS}/${encodeURIComponent(instanceId)}/approve`, data: { comment } })
}

/** 审批驳回。 */
export function rejectApproval(instanceId: string, reason: string): Promise<void> {
  return apiRequest<void>({ method: 'post', url: `${APPROVALS}/${encodeURIComponent(instanceId)}/reject`, data: { reason } })
}

/** 发起人撤回。 */
export function withdrawApproval(instanceId: string, reason?: string | null): Promise<void> {
  return apiRequest<void>({ method: 'post', url: `${APPROVALS}/${encodeURIComponent(instanceId)}/withdraw`, data: { reason } })
}

/** 催办。 */
export function urgeApproval(instanceId: string, message?: string | null): Promise<void> {
  return apiRequest<void>({ method: 'post', url: `${APPROVALS}/${encodeURIComponent(instanceId)}/urge`, data: { message } })
}

/** 加签。 */
export function addSignApproval(
  instanceId: string,
  taskId: string,
  addSignerUserId: string,
  reason?: string | null,
  isBeforeCurrent = true,
): Promise<void> {
  return apiRequest<void>({
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
  return apiRequest<void>({
    method: 'post',
    url: `${APPROVALS}/${encodeURIComponent(instanceId)}/tasks/${encodeURIComponent(taskId)}/transfer`,
    data: { toUserId, reason },
  })
}

/** 我的委托列表。 */
export function fetchApprovalDelegates(): Promise<ApprovalDelegateInfo[]> {
  return apiRequest<ApprovalDelegateInfo[]>({ method: 'get', url: `${APPROVALS}/delegates` })
}

/** 创建委托。 */
export function createApprovalDelegate(payload: CreateDelegateRequest): Promise<string> {
  return apiRequest<string>({ method: 'post', url: `${APPROVALS}/delegates`, data: payload })
}

/** 撤销委托。 */
export function revokeApprovalDelegate(delegateId: string): Promise<void> {
  return apiRequest<void>({ method: 'post', url: `${APPROVALS}/delegates/${encodeURIComponent(delegateId)}/revoke` })
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
  return apiRequest<SupportPage<SupportInboxItem>>({ method: 'get', url: `${MESSAGES}/inbox`, params })
}

/** 已发送分页。 */
export function fetchMessageSent(params: {
  pageIndex: number
  pageSize: number
  keyword?: string | null
}): Promise<SupportPage<MessageInfo>> {
  return apiRequest<SupportPage<MessageInfo>>({ method: 'get', url: `${MESSAGES}/sent`, params })
}

/** 未读数。 */
export function fetchUnreadCount(): Promise<number> {
  return apiRequest<number>({ method: 'get', url: `${MESSAGES}/unread-count` })
}

/** 标记单条已读。 */
export function markMessageRead(inboxId: string): Promise<void> {
  return apiRequest<void>({ method: 'post', url: `${MESSAGES}/inbox/${encodeURIComponent(inboxId)}/read` })
}

/** 全部已读。 */
export function markAllMessagesRead(): Promise<void> {
  return apiRequest<void>({ method: 'post', url: `${MESSAGES}/read-all` })
}

/** 删除收件箱项。 */
export function deleteInboxItem(inboxId: string): Promise<void> {
  return apiRequest<void>({ method: 'post', url: `${MESSAGES}/inbox/${encodeURIComponent(inboxId)}/delete` })
}

/** 记录全局项状态（READ / ACCEPTED / DELETED）。 */
export function markGlobalItemState(globalId: string, state: string): Promise<void> {
  return apiRequest<void>({ method: 'post', url: `${MESSAGES}/global/${encodeURIComponent(globalId)}/state`, data: { state } })
}

/** 订阅清单。 */
export function fetchSubscriptions(): Promise<SupportSubscriptionItem[]> {
  return apiRequest<SupportSubscriptionItem[]>({ method: 'get', url: `${MESSAGES}/subscriptions` })
}

/** 切换订阅开关。 */
export function toggleSubscription(msgType: string, enabled: boolean): Promise<void> {
  return apiRequest<void>({
    method: 'post',
    url: `${MESSAGES}/subscriptions/${encodeURIComponent(msgType)}/toggle`,
    data: { enabled },
  })
}

/** 渠道发送记录。 */
export function fetchMessageSendLogs(messageId: string): Promise<SupportSendLogItem[]> {
  return apiRequest<SupportSendLogItem[]>({ method: 'get', url: `${MESSAGES}/sent/${encodeURIComponent(messageId)}/send-logs` })
}

/** 人工重试投递。 */
export function retryMessageDelivery(messageId: string): Promise<void> {
  return apiRequest<void>({ method: 'post', url: `${MESSAGES}/sent/${encodeURIComponent(messageId)}/retry` })
}

/* ============================================================
 * 后台任务中心（/api/support/jobs）
 * ============================================================ */

const JOBS = '/api/support/jobs'

/** 触发任务。 */
export function triggerJob(payload: JobTriggerPayload): Promise<JobSubmitResult> {
  return apiRequest<JobSubmitResult>({ method: 'post', url: `${JOBS}/trigger`, data: payload })
}

/** 取消任务。 */
export function cancelJob(executionId: string): Promise<void> {
  return apiRequest<void>({ method: 'post', url: `${JOBS}/${encodeURIComponent(executionId)}/cancel` })
}

/** 查询任务状态。 */
export function fetchJobStatus(executionId: string): Promise<JobSubmitResult> {
  return apiRequest<JobSubmitResult>({ method: 'get', url: `${JOBS}/${encodeURIComponent(executionId)}` })
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
  return apiRequest<SupportPage<JobExecutionItem>>({ method: 'get', url: `${JOBS}/history`, params })
}

/** 执行日志。 */
export function fetchJobLogs(executionId: string): Promise<JobExecutionLogEntry[]> {
  return apiRequest<JobExecutionLogEntry[]>({ method: 'get', url: `${JOBS}/${encodeURIComponent(executionId)}/logs` })
}

/** 任务定义清单。 */
export function fetchJobDefinitions(): Promise<JobDefinitionItem[]> {
  return apiRequest<JobDefinitionItem[]>({ method: 'get', url: `${JOBS}/definitions` })
}

/** 启停任务定义。 */
export function toggleJobDefinition(taskCode: string, enabled: boolean): Promise<void> {
  return apiRequest<void>({
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
  return apiRequest<SupportFileMetadata>({ method: 'get', url: `${FILES}/metadata/${encodeURIComponent(id)}` })
}

/** 目录列表。 */
export function fetchFileList(parentId?: string | null, keyword?: string | null): Promise<SupportFileMetadata[]> {
  const params: Record<string, unknown> = {}
  if (parentId) params.parentId = parentId
  if (keyword) params.keyword = keyword
  return apiRequest<SupportFileMetadata[]>({ method: 'get', url: `${FILES}/list`, params })
}

/** 回收站列表。 */
export function fetchRecycleList(): Promise<SupportFileMetadata[]> {
  return apiRequest<SupportFileMetadata[]>({ method: 'get', url: `${FILES}/recycle/list` })
}

/** 软删进回收站。 */
export function deleteFile(id: string): Promise<SupportFileMetadata> {
  return apiRequest<SupportFileMetadata>({ method: 'delete', url: `${FILES}/${encodeURIComponent(id)}` })
}

/** 从回收站还原。 */
export function restoreFile(id: string): Promise<SupportFileMetadata> {
  return apiRequest<SupportFileMetadata>({ method: 'post', url: `${FILES}/${encodeURIComponent(id)}/restore` })
}

/** 回收站彻底删除（物理清理，不可恢复）。 */
export function purgeFile(id: string): Promise<void> {
  return apiRequest<void>({ method: 'delete', url: `${FILES}/${encodeURIComponent(id)}/purge` })
}

/**
 * 带鉴权读取文件内容（预览 / 下载同一内容流）。
 * 说明：新标签页无法携带 Bearer Token，必须经共享客户端取 Blob 后本地生成对象 URL；
 *       失败时后端仍返回 ApiResponse JSON（Blob），优先透传后端 message。
 */
export async function fetchFileBlob(id: string, mode: 'preview' | 'download'): Promise<Blob> {
  try {
    const response = await http.get(`${FILES}/${mode}/${encodeURIComponent(id)}`, {
      responseType: 'blob',
    })
    return response.data as Blob
  } catch (error) {
    if (axios.isAxiosError(error)) {
      const status = error.response?.status
      const body = await parseBlobApiResponse(error.response?.data)
      if (body) {
        throw new ApiError(body.message ?? body.code ?? '文件读取失败', body.code ?? undefined, status)
      }
      if (status === 403) {
        throw new ApiError('无权访问该文件', 'FORBIDDEN', 403)
      }
      if (!error.response) {
        throw new ApiError('无法连接服务，请确认后端服务已启动', 'NETWORK_ERROR')
      }
      throw new ApiError(`文件读取失败：HTTP ${status ?? '未知'}`, status ? `HTTP_${status}` : undefined, status)
    }
    throw error
  }
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
        // 非 2xx：后端统一错误信封（ApiResponse JSON），透传 message/code/status
        try {
          const errBody = JSON.parse(xhr.responseText) as ApiResponse<unknown>
          reject(new ApiError(errBody.message ?? errBody.code ?? `上传失败：HTTP ${xhr.status}`, errBody.code ?? undefined, xhr.status, errBody.errors ?? undefined))
        } catch {
          reject(new ApiError(`上传失败：HTTP ${xhr.status}`, `HTTP_${xhr.status}`, xhr.status))
        }
        return
      }
      try {
        const body = JSON.parse(xhr.responseText) as ApiResponse<SupportFileUploadResult>
        if (!body.success) {
          reject(new ApiError(body.message ?? '上传失败', body.code ?? undefined, xhr.status, body.errors ?? undefined))
          return
        }
        resolve(body.data as SupportFileUploadResult)
      } catch {
        reject(new ApiError('上传响应不是合法 JSON'))
      }
    }
    xhr.onerror = () => reject(new ApiError('网络异常，上传中断', 'NETWORK_ERROR'))
    xhr.send(formData)
  })
}

/* ============================================================
 * 打印模板（/api/support/printing/templates）
 * ============================================================ */

const PRINTING = '/api/support/printing/templates'

/** 模板清单。 */
export function fetchPrintTemplates(): Promise<PrintTemplateView[]> {
  return apiRequest<PrintTemplateView[]>({ method: 'get', url: PRINTING })
}

/** 按编码查最新版本。 */
export function fetchPrintTemplateByCode(code: string): Promise<PrintTemplateView | null> {
  return apiRequest<PrintTemplateView | null>({ method: 'get', url: `${PRINTING}/code/${encodeURIComponent(code)}` })
}

/** 新建模板。 */
export function createPrintTemplate(payload: PrintTemplateCreatePayload): Promise<PrintTemplateView> {
  return apiRequest<PrintTemplateView>({ method: 'post', url: PRINTING, data: payload })
}

/** 更新模板。 */
export function updatePrintTemplate(id: string, payload: PrintTemplateUpdatePayload): Promise<PrintTemplateView> {
  return apiRequest<PrintTemplateView>({ method: 'put', url: `${PRINTING}/${encodeURIComponent(id)}`, data: payload })
}

/** 启停模板。 */
export function setPrintTemplateStatus(id: string, status: number): Promise<void> {
  return apiRequest<void>({ method: 'post', url: `${PRINTING}/${encodeURIComponent(id)}/status`, data: { status } })
}

/** 渲染模板为 HTML。 */
export function renderPrintTemplate(code: string, variables: Record<string, unknown>): Promise<string> {
  return apiRequest<string>({ method: 'post', url: `${PRINTING}/code/${encodeURIComponent(code)}/render`, data: variables })
}

/* ============================================================
 * 审批候选人（加签 / 转办选人——复用租户成员查询接口）
 * ============================================================ */

export type { ApprovalUserOption }
