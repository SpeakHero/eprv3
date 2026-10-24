/*
 * 所属公司：四川淇利馨集团有限公司
 * 描述：SSE SharedWorker 脚本——浏览器级单连接（3 标签页合并为 1 条连接）
 * 说明：经 BroadcastChannel 向全部标签页广播事件与状态；端口仅承载 token / init / close 控制指令。
 *       本文件在 SharedWorker 全局作用域运行，不引用 DOM 类型。
 */

/// <reference lib="webworker" />
import { FetchSseConnection } from './sse-connection'
import type { SseConnectionState, SseEventPayload } from './types'

const CHANNEL_NAME = 'qlx-sse'

/** 广播消息（worker → 全部标签页）。 */
export type SseBroadcastMessage =
  | { kind: 'event'; payload: SseEventPayload }
  | { kind: 'state'; state: SseConnectionState; detail?: string }

/** 标签页 → worker 控制指令。 */
interface WorkerCommand {
  kind: 'token' | 'init' | 'close'
  url?: string
  token?: string
  heartbeatTimeoutMs?: number
}

interface WorkerScope {
  onconnect: ((event: MessageEvent) => void) | null
}

const ctx = self as unknown as WorkerScope
const channel: BroadcastChannel | null =
  typeof BroadcastChannel !== 'undefined' ? new BroadcastChannel(CHANNEL_NAME) : null

let connection: FetchSseConnection | null = null
let currentToken = ''
let refCount = 0

function broadcast(message: SseBroadcastMessage): void {
  channel?.postMessage(message)
}

function ensureConnection(url: string, heartbeatTimeoutMs?: number): void {
  if (connection) {
    connection.connect()
    return
  }
  connection = new FetchSseConnection({
    url,
    getToken: () => currentToken,
    onEvent: (payload) => broadcast({ kind: 'event', payload }),
    onStateChange: (state, detail) => broadcast({ kind: 'state', state, detail }),
    heartbeatTimeoutMs,
  })
  connection.connect()
}

ctx.onconnect = (event: MessageEvent) => {
  const port = event.ports[0] as MessagePort
  refCount += 1

  port.onmessage = (message: MessageEvent) => {
    const command = message.data as WorkerCommand
    if (command.kind === 'token' && typeof command.token === 'string') {
      // Token 续期 / 首次注入（SharedWorker 作用域读不到标签页 localStorage）
      currentToken = command.token
    } else if (command.kind === 'init' && command.url) {
      if (typeof command.token === 'string') {
        currentToken = command.token
      }
      ensureConnection(command.url, command.heartbeatTimeoutMs)
    } else if (command.kind === 'close') {
      refCount = Math.max(0, refCount - 1)
      // 全部标签页退出后释放连接
      if (refCount === 0) {
        connection?.close()
        connection = null
      }
    }
  }
  port.start()
}
