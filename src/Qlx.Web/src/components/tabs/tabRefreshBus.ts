/*
 * 所属公司：四川淇利馨集团有限公司
 * 描述：标签页刷新事件总线——全局单例，按标签 id 定向投递刷新信号。
 *       每个标签页内部通过 onTabRefresh 注册自身的刷新逻辑；
 *       外部（管理器 / 右键菜单 / 业务代码）通过 emitTabRefresh 按 id 触发。
 *       纯 TypeScript 实现，与 Vue 解耦，可在任意环境下独立使用与测试。
 */

/** 刷新事件负载 */
export interface TabRefreshPayload {
  /** 标签唯一标识 */
  id: string
  /** 当前刷新计数（管理器 refresh 调用后自增；外部直发时可省略） */
  refreshKey?: number
  /** 刷新原因（如 'context-menu' / 'route-reenter'，便于排查） */
  reason?: string
}

export type TabRefreshHandler = (payload: TabRefreshPayload) => void

/** 通配订阅键：接收所有标签的刷新事件 */
export const TAB_REFRESH_ALL = '*'

const registry = new Map<string, Set<TabRefreshHandler>>()

function handlersOf(id: string): Set<TabRefreshHandler> {
  let set = registry.get(id)
  if (!set) {
    set = new Set()
    registry.set(id, set)
  }
  return set
}

/**
 * 订阅指定标签的刷新事件。
 * @param id 标签唯一标识；传 TAB_REFRESH_ALL 订阅全部标签
 * @returns 取消订阅函数
 */
export function onTabRefresh(id: string, handler: TabRefreshHandler): () => void {
  const set = handlersOf(id)
  set.add(handler)
  return () => {
    set.delete(handler)
    if (set.size === 0 && id !== TAB_REFRESH_ALL) {
      registry.delete(id)
    }
  }
}

/**
 * 触发指定标签的刷新事件（同步投递）。
 * 命中：该 id 的全部订阅者 + 通配订阅者。
 */
export function emitTabRefresh(id: string, payload: Omit<TabRefreshPayload, 'id'> = {}): void {
  const full: TabRefreshPayload = { ...payload, id }
  registry.get(id)?.forEach((handler) => handler(full))
  if (id !== TAB_REFRESH_ALL) {
    registry.get(TAB_REFRESH_ALL)?.forEach((handler) => handler(full))
  }
}

/** 清空指定标签（或全部）的订阅——用于测试与 teardown */
export function clearTabRefreshHandlers(id?: string): void {
  if (id === undefined) {
    registry.clear()
  } else {
    registry.delete(id)
  }
}
