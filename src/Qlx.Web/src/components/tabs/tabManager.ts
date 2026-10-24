/*
 * 所属公司：四川淇利馨集团有限公司
 * 描述：多标签页管理核心——标签增删、激活、固定、排序、去重、上限控制、刷新触发。
 *       纯 TypeScript 实现，不依赖 Vue / Pinia，可被任意状态管理方案包装复用。
 */

import type {
  TabCloseGuard,
  TabInput,
  TabItem,
  TabManagerEventHandler,
  TabManagerEventMap,
  TabManagerEventName,
  TabManagerOptions,
} from './types'

/** 事件取消订阅函数 */
export type TabUnsubscribe = () => void

export interface TabManager {
  /** 当前标签列表（只读快照，固定标签恒在前） */
  readonly tabs: readonly TabItem[]
  /** 当前激活标签 id */
  readonly activeId: string | null
  /** 配置快照 */
  readonly options: Required<Omit<TabManagerOptions, 'beforeClose'>> & {
    beforeClose?: TabCloseGuard
  }

  /** 打开标签：dedupe 命中时仅激活并返回已有标签 */
  open(input: TabInput): { tab: TabItem; created: boolean }
  /** 关闭标签（执行关闭校验；返回是否真正关闭） */
  close(id: string, guard?: TabCloseGuard): Promise<boolean>
  /** 激活标签 */
  activate(id: string): boolean
  /** 设置固定状态（自动调整顺序：固定标签前置） */
  setPinned(id: string, pinned: boolean): boolean
  /**
   * 移动标签到目标索引（拖拽排序唯一入口）。
   * 边界自动修正：索引会被钳制到合法区间；
   * 跨区域（fixed ↔ normal）移动时自动切换 pinned 状态并修正目标索引。
   */
  move(id: string, toIndex: number): boolean
  /** 关闭其他标签（固定标签与不可关闭标签保留；逐个执行关闭校验） */
  closeOthers(id: string): Promise<string[]>
  /** 关闭全部可关闭标签（固定标签保留；逐个执行关闭校验） */
  closeAll(): Promise<string[]>
  /** 触发标签刷新：refreshKey 自增 + 触发 tab-refresh 事件 */
  refresh(id: string, reason?: string): boolean
  /** 按 id 查询 */
  getById(id: string): TabItem | undefined

  /** 订阅事件，返回取消订阅函数 */
  on<K extends TabManagerEventName>(
    event: K,
    handler: TabManagerEventHandler<K>,
  ): TabUnsubscribe
}

export function createTabManager(options: TabManagerOptions = {}): TabManager {
  const resolved = {
    maxTabs: options.maxTabs ?? 20,
    dedupe: options.dedupe ?? true,
    closeLastBehavior: options.closeLastBehavior ?? 'allow-empty',
    overflowStrategy: options.overflowStrategy ?? 'reject',
    beforeClose: options.beforeClose,
  }

  let tabs: TabItem[] = []
  let activeId: string | null = null

  const handlers = new Map<TabManagerEventName, Set<(payload: never) => void>>()

  function emit<K extends TabManagerEventName>(event: K, payload: TabManagerEventMap[K]): void {
    const set = handlers.get(event)
    if (set) {
      for (const handler of set) {
        ;(handler as TabManagerEventHandler<K>)(payload)
      }
    }
    // change 事件永远最后触发（视图层只需订阅 change 即可全量同步）
    if (event !== 'change') {
      emitChange()
    }
  }

  function emitChange(): void {
    const set = handlers.get('change')
    if (!set) return
    const payload: TabManagerEventMap['change'] = { tabs: [...tabs], activeId }
    for (const handler of set) {
      ;(handler as TabManagerEventHandler<'change'>)(payload)
    }
  }

  function indexOf(id: string): number {
    return tabs.findIndex((tab) => tab.id === id)
  }

  function pinnedCount(): number {
    let count = 0
    while (count < tabs.length && tabs[count].pinned) count += 1
    return count
  }

  /** 关闭后需要切换激活时，选择邻居：优先右侧，其次左侧 */
  function pickNeighbor(closedIndex: number): TabItem | null {
    return tabs[closedIndex] ?? tabs[closedIndex - 1] ?? null
  }

  const manager: TabManager = {
    get tabs() {
      return tabs
    },
    get activeId() {
      return activeId
    },
    options: resolved,

    open(input) {
      const existing = resolved.dedupe ? tabs.find((tab) => tab.id === input.id) : undefined
      if (existing) {
        manager.activate(existing.id)
        return { tab: existing, created: false }
      }

      // 上限控制
      if (tabs.length >= resolved.maxTabs) {
        if (resolved.overflowStrategy === 'reject') {
          emit('tab-overflow', { input, maxTabs: resolved.maxTabs })
          // 拒绝时不新增；返回一个未入列的占位对象，created=false 供调用方识别
          return { tab: { refreshKey: 0, ...input }, created: false }
        }
        // evict-oldest：驱逐最旧的非固定标签
        const victimIndex = tabs.findIndex((tab) => !tab.pinned)
        if (victimIndex === -1) {
          // 全部固定，无法驱逐，退化为拒绝
          emit('tab-overflow', { input, maxTabs: resolved.maxTabs })
          return { tab: { refreshKey: 0, ...input }, created: false }
        }
        const [victim] = tabs.splice(victimIndex, 1)
        emit('tab-evict', { tab: victim })
        emit('tab-close', { tab: victim, index: victimIndex })
        if (activeId === victim.id) {
          const neighbor = pickNeighbor(victimIndex)
          activeId = neighbor ? neighbor.id : null
          emit('tab-activate', { tab: neighbor, previousId: victim.id })
        }
      }

      const tab: TabItem = { refreshKey: 0, closable: true, ...input }
      if (tab.pinned) {
        // 固定标签必须插入固定区末尾，维持「固定恒在前」不变量
        tabs.splice(pinnedCount(), 0, tab)
      } else {
        tabs.push(tab)
      }
      emit('tab-add', { tab })
      manager.activate(tab.id)
      return { tab, created: true }
    },

    async close(id, guard) {
      const index = indexOf(id)
      if (index === -1) return false
      const tab = tabs[index]

      if (tab.pinned || tab.closable === false) return false

      // 关闭最后一个标签的默认行为
      if (tabs.length === 1 && resolved.closeLastBehavior === 'prevent-close') {
        return false
      }

      const check = guard ?? resolved.beforeClose
      if (check) {
        const allowed = await check(tab)
        if (!allowed) return false
      }

      tabs.splice(index, 1)
      emit('tab-close', { tab, index })

      if (activeId === id) {
        const neighbor = pickNeighbor(index)
        activeId = neighbor ? neighbor.id : null
        emit('tab-activate', { tab: neighbor, previousId: id })
      }
      return true
    },

    activate(id) {
      const tab = tabs.find((item) => item.id === id)
      if (!tab || activeId === id) return false
      const previousId = activeId
      activeId = id
      emit('tab-activate', { tab, previousId })
      return true
    },

    setPinned(id, pinned) {
      const index = indexOf(id)
      if (index === -1) return false
      const tab = tabs[index]
      if (Boolean(tab.pinned) === pinned) return false

      tabs.splice(index, 1)
      tab.pinned = pinned
      if (pinned) {
        tabs.splice(pinnedCount(), 0, tab)
      } else {
        // 取消固定：插入非固定区首位（固定区末尾之后）
        tabs.splice(pinnedCount(), 0, tab)
      }
      emit('tab-pin', { tab, pinned })
      return true
    },

    move(id, toIndex) {
      const fromIndex = indexOf(id)
      if (fromIndex === -1) return false

      const tab = tabs[fromIndex]
      const wasPinned = Boolean(tab.pinned)
      const pinnedTotal = pinnedCount()

      // 边界自动修正 + 跨区域判定：
      // 目标索引落入固定区 [0, pinnedTotal) → 该标签应为固定；
      // 落入非固定区 [pinnedTotal, len) → 该标签应为非固定。
      let targetPinned = wasPinned
      let clamped = toIndex
      if (wasPinned && toIndex >= pinnedTotal) {
        targetPinned = false
      } else if (!wasPinned && toIndex < pinnedTotal) {
        targetPinned = true
      }
      clamped = Math.max(0, Math.min(clamped, tabs.length - 1))

      tabs.splice(fromIndex, 1)
      tabs.splice(clamped, 0, tab)

      if (targetPinned !== wasPinned) {
        tab.pinned = targetPinned
        emit('tab-pin', { tab, pinned: targetPinned })
      }

      if (clamped !== fromIndex || targetPinned !== wasPinned) {
        // 位置变化，或跨区域原位（仅固定状态变化）均视为一次排序变更
        emit('tab-reorder', {
          tabs: [...tabs],
          movedId: id,
          fromIndex,
          toIndex: clamped,
        })
      }
      return true
    },

    async closeOthers(id) {
      const target = tabs.find((tab) => tab.id === id)
      if (!target) return []
      const closableIds = tabs
        .filter((tab) => tab.id !== id && !tab.pinned && tab.closable !== false)
        .map((tab) => tab.id)
      const closed: string[] = []
      for (const closeId of closableIds) {
        if (await manager.close(closeId)) {
          closed.push(closeId)
        }
      }
      // 保证目标标签最终处于激活态
      manager.activate(id)
      return closed
    },

    async closeAll() {
      const closableIds = tabs
        .filter((tab) => !tab.pinned && tab.closable !== false)
        .map((tab) => tab.id)
      const closed: string[] = []
      for (const closeId of closableIds) {
        if (await manager.close(closeId)) {
          closed.push(closeId)
        }
      }
      return closed
    },

    refresh(id, reason) {
      const tab = tabs.find((item) => item.id === id)
      if (!tab) return false
      tab.refreshKey += 1
      emit('tab-refresh', { tab, refreshKey: tab.refreshKey, reason })
      return true
    },

    getById(id) {
      return tabs.find((tab) => tab.id === id)
    },

    on(event, handler) {
      let set = handlers.get(event)
      if (!set) {
        set = new Set()
        handlers.set(event, set)
      }
      set.add(handler as (payload: never) => void)
      return () => {
        set.delete(handler as (payload: never) => void)
      }
    },
  }

  return manager
}
