/*
 * 所属公司：四川淇利馨集团有限公司
 * 描述：fetch 流式 SSE 连接（单连接职责：连接 / 解析 / 指数重连 / Last-Event-ID 续传 / 心跳检测）
 * 说明：不用原生 EventSource——它无法携带 Authorization 头与 Last-Event-ID 请求头；
 *       后端 SSE 契约要求租户 Token（Authorization 头）与断线续传（Last-Event-ID 头），
 *       故以 fetch + ReadableStream 自行解析 text/event-stream。
 */

import type { SseConnectionState, SseEventHandler } from './types'
import { normalizeEventType } from './types'

/** 重连退避序列（1s→2s→4s→…，上限封顶 30s）。 */
const BACKOFF_STEPS_MS = [1000, 2000, 4000, 8000, 16000, 30000]

export interface SseConnectionOptions {
  url: string
  getToken: () => string
  onEvent: SseEventHandler
  onStateChange: (state: SseConnectionState, detail?: string) => void
  /** 心跳超时毫秒数（默认 45s：后端 30s ping + 网络余量）。 */
  heartbeatTimeoutMs?: number
}

/** 单条 SSE 连接（含自动重连）。close() 后不再重连。 */
export class FetchSseConnection {
  private readonly options: SseConnectionOptions
  private controller: AbortController | null = null
  private reconnectTimer: number | null = null
  private heartbeatTimer: number | null = null
  private backoffIndex = 0
  private closed = false
  private lastEventId = ''

  constructor(options: SseConnectionOptions) {
    this.options = options
  }

  /** 建立连接（幂等：已连接时忽略）。 */
  connect(): void {
    if (this.closed || this.controller) {
      return
    }
    this.options.onStateChange(this.lastEventId ? 'connecting' : 'connecting', '建立连接')
    void this.openStream()
  }

  /** 主动关闭（不再重连）。 */
  close(): void {
    this.closed = true
    this.clearTimers()
    this.controller?.abort()
    this.controller = null
    this.options.onStateChange('closed')
  }

  /** 当前 Last-Event-ID（诊断用）。 */
  get resumeId(): string {
    return this.lastEventId
  }

  private async openStream(): Promise<void> {
    const controller = new AbortController()
    this.controller = controller

    try {
      const token = this.options.getToken()
      const headers: Record<string, string> = { Accept: 'text/event-stream' }
      if (token) {
        headers.Authorization = `Bearer ${token}`
      }
      if (this.lastEventId) {
        // 断线续传：后端按此 ID 之后补发遗漏事件
        headers['Last-Event-ID'] = this.lastEventId
      }

      const response = await fetch(this.options.url, {
        method: 'GET',
        headers,
        signal: controller.signal,
        cache: 'no-store',
      })
      if (!response.ok || !response.body) {
        throw new Error(`SSE 响应异常：HTTP ${response.status}`)
      }

      this.backoffIndex = 0
      this.options.onStateChange('open')
      this.scheduleHeartbeatCheck()
      await this.readStream(response.body, controller.signal)

      // 服务端正常断流：按断线处理进入重连
      this.scheduleReconnect('服务端断开连接')
    } catch (error) {
      if (controller.signal.aborted && this.closed) {
        return
      }
      this.scheduleReconnect(error instanceof Error ? error.message : '连接失败')
    }
  }

  private async readStream(body: ReadableStream<Uint8Array>, signal: AbortSignal): Promise<void> {
    const reader = body.getReader()
    const decoder = new TextDecoder('utf-8')
    let buffer = ''

    while (true) {
      const { done, value } = await reader.read()
      if (done) {
        return
      }
      if (signal.aborted) {
        return
      }
      buffer += decoder.decode(value, { stream: true })
      let boundary = buffer.indexOf('\n\n')
      while (boundary >= 0) {
        const block = buffer.slice(0, boundary)
        buffer = buffer.slice(boundary + 2)
        this.handleBlock(block)
        boundary = buffer.indexOf('\n\n')
      }
    }
  }

  /** 解析一个 event block（id / event / data / : 注释行）。 */
  private handleBlock(block: string): void {
    // 任意到达的字节（含心跳注释行）都重置心跳计时
    this.scheduleHeartbeatCheck()

    const lines = block.split('\n')
    if (lines.some((line) => line.startsWith(':'))) {
      // 心跳 / 注释：仅用于保活
      return
    }

    let eventName = 'message'
    const dataLines: string[] = []
    for (const line of lines) {
      if (line.startsWith('id:')) {
        this.lastEventId = line.slice(3).trim()
      } else if (line.startsWith('event:')) {
        eventName = line.slice(6).trim()
      } else if (line.startsWith('data:')) {
        dataLines.push(line.slice(5).replace(/^ /, ''))
      }
    }
    if (dataLines.length === 0) {
      return
    }

    const type = normalizeEventType(eventName)
    if (!type) {
      return
    }
    const raw = dataLines.join('\n')
    let data: unknown = raw
    try {
      data = JSON.parse(raw)
    } catch {
      // 非 JSON 载荷按原字符串下发
    }

    this.options.onEvent({
      eventId: this.lastEventId,
      type,
      data,
      occurredAt: new Date().toISOString(),
    })
  }

  private scheduleReconnect(reason: string): void {
    if (this.closed) {
      return
    }
    this.controller = null
    this.options.onStateChange('connecting', `${reason}，准备重连`)
    const delay = BACKOFF_STEPS_MS[Math.min(this.backoffIndex, BACKOFF_STEPS_MS.length - 1)]
    this.backoffIndex += 1
    this.reconnectTimer = window.setTimeout(() => {
      this.reconnectTimer = null
      void this.openStream()
    }, delay)
  }

  /** 心跳检测：超时无事件则强制断开重连（走 Last-Event-ID 续传）。 */
  private scheduleHeartbeatCheck(): void {
    if (this.heartbeatTimer !== null) {
      window.clearTimeout(this.heartbeatTimer)
    }
    const timeout = this.options.heartbeatTimeoutMs ?? 45_000
    this.heartbeatTimer = window.setTimeout(() => {
      if (this.closed || !this.controller) {
        return
      }
      this.controller.abort()
      this.controller = null
      this.scheduleReconnect('心跳超时，强制重连')
    }, timeout)
  }

  private clearTimers(): void {
    if (this.reconnectTimer !== null) {
      window.clearTimeout(this.reconnectTimer)
      this.reconnectTimer = null
    }
    if (this.heartbeatTimer !== null) {
      window.clearTimeout(this.heartbeatTimer)
      this.heartbeatTimer = null
    }
  }
}
