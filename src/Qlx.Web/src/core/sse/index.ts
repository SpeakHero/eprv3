/*
 * 所属公司：四川淇利馨集团有限公司
 * 描述：SSE 客户端公共出口
 */

export {
  createSseClient,
} from './sse-client'
export type { SseClient } from './sse-client'
export { SSE_EVENT_TYPES, normalizeEventType } from './types'
export type {
  SseEventType,
  SseEventPayload,
  SseConnectionState,
  SseClientOptions,
  SseEventHandler,
} from './types'
export { getMockSseBus } from './sse-mock'
