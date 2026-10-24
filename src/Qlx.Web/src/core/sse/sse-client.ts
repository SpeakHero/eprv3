/*
 * 所属公司：四川淇利馨集团有限公司
 * 描述：SSE 客户端门面——SharedWorker 单连接 + BroadcastChannel 多标签分发（含降级路径）
 * 说明：
 *  - fetch 模式：优先 SharedWorker（全浏览器仅 1 条连接，验收 GetConnectionCountAsync == 1），
 *    事件经 BroadcastChannel('qlx-sse') 广播到全部标签页；SharedWorker 不可用时降级为页内连接。
 *  - mock 模式：页内 MockSseTransport，事件来自 getMockSseBus().emit()（演示 / 无后端联调）。
 *  - 事件按 SseEventType 路由到各自 handler，业务侧只 subscribe，不感知连接细节。
 */

import { FetchSseConnection } from './sse-connection'
import { MockSseTransport } from './sse-mock'
import type { SseClientOptions, SseConnectionState, SseEventHandler, SseEventPayload, SseEventType } from './types'
import { SSE_EVENT_TYPES } from './types'
import type { SseBroadcastMessage } from './sse-worker'

const CHANNEL_NAME = 'qlx-sse'

/** SSE 客户端公开接口。 */
export interface SseClient {
  /** 订阅指定类型事件，返回取消订阅函数。 */
  subscribe(type: SseEventType, handler: SseEventHandler): () => void
  /** 订阅连接状态变化，返回取消订阅函数。 */
  onStateChange(listener: (state: SseConnectionState, detail?: string) => void): () => void
  /** 通知底层连接更新 Token（续期后调用；mock 模式空操作）。 */
  setToken(token: string): void
  /** 主动关闭连接（页面卸载 / 组件销毁时调用）。 */
  close(): void
}

interface DispatchRegistry {
  dispatch(event: SseEventPayload): void
}

function createRegistry(): DispatchRegistry & {
  subscribe(type: SseEventType, handler: SseEventHandler): () => void
  emitState(state: SseConnectionState, detail?: string): void
  onStateChange(listener: (state: SseConnectionState, detail?: string) => void): () => void
} {
  const handlers = new Map<SseEventType, Set<SseEventHandler>>(
    SSE_EVENT_TYPES.map((type) => [type, new Set<SseEventHandler>()] as const),
  )
  const stateListeners = new Set<(state: SseConnectionState, detail?: string) => void>()

  return {
    dispatch(event) {
      handlers.get(event.type)?.forEach((handler) => handler(event))
    },
    subscribe(type, handler) {
      handlers.get(type)?.add(handler)
      return () => {
        handlers.get(type)?.delete(handler)
      }
    },
    emitState(state, detail) {
      stateListeners.forEach((listener) => listener(state, detail))
    },
    onStateChange(listener) {
      stateListeners.add(listener)
      return () => {
        stateListeners.delete(listener)
      }
    },
  }
}

/** 创建 SSE 客户端。同一页面多次创建会各自持有连接，业务侧应模块级单例复用。 */
export function createSseClient(options: SseClientOptions): SseClient {
  const transport = options.transport ?? 'mock'
  const registry = createRegistry()
  let closed = false

  if (transport === 'mock') {
    const mock = new MockSseTransport({
      onEvent: (event) => registry.dispatch(event),
      onStateChange: (state, detail) => registry.emitState(state, detail),
    })
    mock.connect()
    return {
      subscribe: registry.subscribe,
      onStateChange: registry.onStateChange,
      setToken: () => undefined,
      close: () => {
        if (!closed) {
          closed = true
          mock.close()
        }
      },
    }
  }

  const url = options.url ?? '/api/support/sse/stream'
  const heartbeatTimeoutMs = options.heartbeatTimeoutMs
  const token = () => options.getToken?.() ?? ''

  // ---- SharedWorker 路径：单连接 + BroadcastChannel 分发 ----
  if (typeof SharedWorker !== 'undefined') {
    // Vite 静态分析要求 new URL(...) 与构造调用同行，以便打包 worker 资源
    const worker = new SharedWorker(new URL('./sse-worker.ts', import.meta.url), {
      type: 'module',
      name: 'qlx-sse-worker',
    })
    const channel = new BroadcastChannel(CHANNEL_NAME)

    channel.onmessage = (message: MessageEvent) => {
      const payload = message.data as SseBroadcastMessage
      if (payload?.kind === 'event') {
        registry.dispatch(payload.payload)
      } else if (payload?.kind === 'state') {
        registry.emitState(payload.state, payload.detail)
      }
    }

    const post = (command: Record<string, unknown>): void => {
      worker.port.postMessage(command)
    }
    post({ kind: 'token', token: token() })
    post({ kind: 'init', url, heartbeatTimeoutMs })

    return {
      subscribe: registry.subscribe,
      onStateChange: registry.onStateChange,
      setToken: (next) => post({ kind: 'token', token: next }),
      close: () => {
        if (!closed) {
          closed = true
          post({ kind: 'close' })
          channel.close()
          worker.port.close()
        }
      },
    }
  }

  // ---- 降级路径：页内直连（无 SharedWorker 的环境） ----
  const connection = new FetchSseConnection({
    url,
    getToken: token,
    onEvent: (event) => registry.dispatch(event),
    onStateChange: (state, detail) => registry.emitState(state, detail),
    heartbeatTimeoutMs,
  })
  connection.connect()

  return {
    subscribe: registry.subscribe,
    onStateChange: registry.onStateChange,
    setToken: () => undefined,
    close: () => {
      if (!closed) {
        closed = true
        connection.close()
      }
    },
  }
}

export type { SseClientOptions, SseConnectionState, SseEventPayload, SseEventType }
export { SSE_EVENT_TYPES }
export { getMockSseBus } from './sse-mock'
