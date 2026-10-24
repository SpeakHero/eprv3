/*
 * 所属公司：四川淇利馨集团有限公司
 * 描述：SSE Mock 传输——与 FetchSseConnection 同接口，事件由本地总线驱动
 * 说明：演示页 / 文件上传 ASYNC 模式联调使用；事件经 getMockSseBus().emit() 注入，
 *       与真实模式走同一套 handler 路由，业务代码零改动切换。
 */

import type { SseConnectionState, SseEventHandler, SseEventPayload, SseEventType } from './types'
import { SSE_EVENT_TYPES } from './types'

/** Mock 事件 ID 发号器（演示用递增；真实环境为雪花 Id 字符串）。 */
let mockEventSeq = 1000

/** Mock SSE 事件总线：任何模块可注入事件，已连接的 Mock 传输实例即收到分发。 */
class MockSseBus {
  private listeners = new Set<SseEventHandler>()

  subscribe(handler: SseEventHandler): () => void {
    this.listeners.add(handler)
    return () => {
      this.listeners.delete(handler)
    }
  }

  /** 注入一条事件（data 为载荷对象，对齐后端 DataJson 解析结果）。 */
  emit(type: SseEventType, data: unknown): void {
    const payload: SseEventPayload = {
      eventId: String((mockEventSeq += 1)),
      type,
      data,
      occurredAt: new Date().toISOString(),
    }
    this.listeners.forEach((listener) => listener(payload))
  }
}

let busSingleton: MockSseBus | null = null

/** 取 Mock 事件总线单例。 */
export function getMockSseBus(): MockSseBus {
  if (!busSingleton) {
    busSingleton = new MockSseBus()
  }
  return busSingleton
}

/** Mock 传输实例（演示模式下的 SSE 客户端底层）。 */
export class MockSseTransport {
  private readonly onEvent: SseEventHandler
  private readonly onStateChange: (state: SseConnectionState, detail?: string) => void
  private unsubscribe: (() => void) | null = null
  private heartbeatTimer: number | null = null

  constructor(options: {
    onEvent: SseEventHandler
    onStateChange: (state: SseConnectionState, detail?: string) => void
  }) {
    this.onEvent = options.onEvent
    this.onStateChange = options.onStateChange
  }

  connect(): void {
    if (this.unsubscribe) {
      return
    }
    window.setTimeout(() => {
      this.onStateChange('open')
      this.unsubscribe = getMockSseBus().subscribe(this.onEvent)
      // 模拟后端 30s 心跳（注释行不可见，此处仅演示状态提示事件）
      this.heartbeatTimer = window.setInterval(() => {
        this.onStateChange('open', '心跳正常')
      }, 30_000)
    }, 200)
  }

  close(): void {
    if (this.heartbeatTimer !== null) {
      window.clearInterval(this.heartbeatTimer)
      this.heartbeatTimer = null
    }
    this.unsubscribe?.()
    this.unsubscribe = null
    this.onStateChange('closed')
  }
}

/** 事件类型清单（演示页下拉用）。 */
export const mockSseEventTypes: readonly SseEventType[] = SSE_EVENT_TYPES
