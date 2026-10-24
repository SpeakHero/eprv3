/*
 * 所属公司：四川淇利馨集团有限公司
 * 描述：SSE 事件契约（对齐 Qlx.Abstractions/Sse/ISseConnectionManager.cs）
 * 说明：SseEvent.EventId 为 long（雪花 Id），JS 侧一律按 string 承载（2^53-1 精度红线）；
 *       DataJson 为载荷 JSON 字符串，路由到具体 handler 前由客户端解析为对象。
 */

/** SSE 事件类型（与后端 SseEventType 枚举一一对应）。 */
export const SSE_EVENT_TYPES = [
  'MessageCreated',
  'TaskProgress',
  'TaskCompleted',
  'TaskFailed',
  'ApprovalPending',
  'ApprovalCompleted',
  'GlobalPublish',
  'SystemAlert',
  'FileUploadCompleted',
] as const

export type SseEventType = (typeof SSE_EVENT_TYPES)[number]

/** SSE 事件载荷（eventId 字符串化，避免雪花 Id 精度丢失）。 */
export interface SseEventPayload {
  eventId: string
  type: SseEventType
  /** DataJson 解析结果（解析失败时为原始字符串）。 */
  data: unknown
  /** ISO 8601 时间。 */
  occurredAt: string
}

/** 连接状态。 */
export type SseConnectionState =
  | 'idle' // 未连接
  | 'connecting' // 连接中 / 重连等待
  | 'open' // 已连接
  | 'closed' // 已关闭（主动）

/** 客户端配置。 */
export interface SseClientOptions {
  /** SSE 端点（真实模式，如 /api/support/sse/stream）。 */
  url?: string
  /** 取 Token 函数（SSE 必须租户 Token，走 Authorization 头）。 */
  getToken?: () => string
  /** 传输模式：mock（演示 / 无后端）或 fetch（真实 fetch 流解析）。默认 mock。 */
  transport?: 'mock' | 'fetch'
  /** 心跳超时毫秒数（后端 30s ping，客户端 45s 无任何事件视为断线）。 */
  heartbeatTimeoutMs?: number
}

/** 事件处理器。 */
export type SseEventHandler = (event: SseEventPayload) => void

/** 判定事件类型名（兼容大小写与枚举数值）。 */
export function normalizeEventType(raw: string): SseEventType | null {
  const found = SSE_EVENT_TYPES.find((name) => name.toLowerCase() === raw.toLowerCase())
  return found ?? null
}
